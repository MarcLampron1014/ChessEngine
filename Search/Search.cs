using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ChessEngine
{
    public static partial class Search
    {
        private const int MateScore = 100000;
        private const int Infinity = 1_000_000;
        private const int MaxPly = 128;
        private const int DefaultHashSizeMB = 64;
        private const int TimeCheckInterval = 2047;

        private static readonly TranspositionTable _tt = new TranspositionTable(DefaultHashSizeMB);
        private static TimeManager? _timeManager;
        private static Stopwatch? _sw;
        private static int _hardTimeLimit;
        private static volatile bool _stopRequested;
        private static SearchResult _lastResult;

        public static void RequestStop() => _stopRequested = true;
        public static SearchResult GetLastResult()
        {
            lock (_resultLock) { return _lastResult; }
        }

        private static readonly ThreadLocal<Move[,]> _killerMovesTls = new ThreadLocal<Move[,]>(() => new Move[MaxPly, 2]);
        private static readonly ThreadLocal<int[,]> _historyTableTls = new ThreadLocal<int[,]>(() => new int[13, 64]);
        private static readonly ThreadLocal<Move[,]> _counterMovesTls = new ThreadLocal<Move[,]>(() => new Move[13, 64]);
        private static readonly ThreadLocal<int[,,,]> _continuationHistoryTls = new ThreadLocal<int[,,,]>(() => new int[13, 64, 13, 64]);
        private static readonly ThreadLocal<int[,,]> _captureHistoryTls = new ThreadLocal<int[,,]>(() => new int[13, 64, 7]);
        private static readonly ThreadLocal<Move> _previousMoveTls = new ThreadLocal<Move>(() => default);
        private static readonly ThreadLocal<Move[][]> _moveStacksTls = new ThreadLocal<Move[][]>(() =>
        {
            var stacks = new Move[MaxPly][];
            for (int i = 0; i < MaxPly; i++)
            {
                stacks[i] = new Move[256];
            }
            return stacks;
        });
        private static readonly ThreadLocal<int[]> _moveScoresTls = new ThreadLocal<int[]>(() => new int[256]);

        private static int _searchGeneration;
        private static readonly ThreadLocal<int> _mySearchGeneration = new ThreadLocal<int>(() => -1);

        private static long _nodeCount;

        private static long _nullMoveAttempts;
        private static long _nullMoveCutoffs;
        private static long _ttCutoffs;
        private static long _betaCutoffs;
        private static long _lmrResearches;

        private static readonly int[] LMPThresholds = { 0, 10, 14, 18, 22, 26, 30 };
        private const int TriedQuietsMax = 64;
        [ThreadStatic]
        private static Move[]? _triedQuietsBuffer;
        private static Move[] TriedQuietsBuffer => _triedQuietsBuffer ??= new Move[TriedQuietsMax];

        private static readonly object _resultLock = new object();
        private static int _bestDepthReached;
        private static Move _bestMoveOverall;
        private static int _bestScoreOverall;

        private static Move[,] Killers => _killerMovesTls.Value!;
        private static int[,] History => _historyTableTls.Value!;
        private static Move[,] Counters => _counterMovesTls.Value!;
        private static int[,,,] ContinuationHistory => _continuationHistoryTls.Value!;
        private static int[,,] CaptureHistory => _captureHistoryTls.Value!;
        private static Move PreviousMove { get => _previousMoveTls.Value; set => _previousMoveTls.Value = value; }
        private static Move[][] MoveStacks => _moveStacksTls.Value!;
        private static int[] MoveScores => _moveScoresTls.Value!;

        private static void EnsureSearchTablesCurrent()
        {
            if (_mySearchGeneration.Value != _searchGeneration)
            {
                Array.Clear(Killers, 0, Killers.Length);
                for (int i = 0; i < History.GetLength(0); i++)
                {
                    for (int j = 0; j < History.GetLength(1); j++)
                    {
                        History[i, j] /= 2;
                    }
                }
                Array.Clear(Counters, 0, Counters.Length);
                var ch = ContinuationHistory;
                for (int a = 0; a < ch.GetLength(0); a++)
                    for (int b = 0; b < ch.GetLength(1); b++)
                        for (int c = 0; c < ch.GetLength(2); c++)
                            for (int d = 0; d < ch.GetLength(3); d++)
                                ch[a, b, c, d] /= 2;
                for (int i = 0; i < CaptureHistory.GetLength(0); i++)
                {
                    for (int j = 0; j < CaptureHistory.GetLength(1); j++)
                    {
                        for (int k = 0; k < CaptureHistory.GetLength(2); k++)
                        {
                            CaptureHistory[i, j, k] /= 2;
                        }
                    }
                }
                PreviousMove = default;
                _mySearchGeneration.Value = _searchGeneration;
            }
        }

        private static Move GetPonderMove(Board board, Move ourBestMove)
        {
            if (ourBestMove.From == ourBestMove.To)
            {
                return default;
            }
            board.MakeMove(ourBestMove);
            Move ponder = _tt.GetTTMove(board.ZobristHash);
            board.UndoMove(ourBestMove);
            if (ponder.From == ponder.To)
            {
                return default;
            }
            board.MakeMove(ourBestMove);
            var legal = MoveGenerator.GenerateLegalMoves(board);
            board.UndoMove(ourBestMove);
            foreach (var m in legal)
            {
                if (m.From == ponder.From && m.To == ponder.To && m.Promotion == ponder.Promotion)
                {
                    return m;
                }
            }
            return default;
        }

        public static void SetHashSize(int sizeMB) => _tt.Resize(sizeMB);
        public static void ClearHash() => _tt.Clear();

        private static void ClearSearchTables()
        {
            _searchGeneration++;
            _nullMoveAttempts = 0;
            _nullMoveCutoffs = 0;
            _ttCutoffs = 0;
            _betaCutoffs = 0;
            _lmrResearches = 0;
        }

        private static void PrintDiagnostics()
        {
            long attempts = _nullMoveAttempts;
            long cutoffs = _nullMoveCutoffs;
            double ratio = attempts > 0 ? (100.0 * cutoffs / attempts) : 0;
            Console.Error.WriteLine($"Diagnostics: nullAttempts={attempts} nullCutoffs={cutoffs} (ratio {ratio:F1}%) ttCutoffs={_ttCutoffs} betaCutoffs={_betaCutoffs} lmrResearches={_lmrResearches}");
        }

        public static SearchResult FindBestMove(Board board, TimeManager timeManager, int maxDepth = 64, int numThreads = 1)
        {
            if (maxDepth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDepth));
            }
            if (numThreads <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(numThreads));
            }

            string rootFen = Fen.Generate(board);
            try
            {
                _timeManager = timeManager;
                _sw = Stopwatch.StartNew();
                _hardTimeLimit = timeManager.MaxTimeMs;
                _stopRequested = false;
                _nodeCount = 0;
                ClearSearchTables();

                List<Move> rootMoves = MoveGenerator.GenerateLegalMoves(board);
                if (rootMoves.Count == 0)
                {
                    return new SearchResult(default, 0, 0);
                }

                if (numThreads <= 1)
                {
                    EnsureSearchTablesCurrent();
                    return FindBestMoveSingleThread(board, rootFen, rootMoves, maxDepth, timeManager);
                }

                _bestDepthReached = 0;
                _bestMoveOverall = rootMoves[0];
                _bestScoreOverall = 0;
                _lastResult = new SearchResult(_bestMoveOverall, _bestScoreOverall, 0, default);

                var workerTasks = new Task[numThreads - 1];
                for (int t = 0; t < workerTasks.Length; t++)
                {
                    workerTasks[t] = Task.Run(() => SearchThreadBody(rootFen, rootMoves, maxDepth, timeManager));
                }

                try
                {
                    SearchThreadBody(rootFen, rootMoves, maxDepth, timeManager);
                }
                catch (SearchTimeoutException) { }

                _stopRequested = true;
                Task.WaitAll(workerTasks, 5000);

                _timeManager = null;
                PrintDiagnostics();
                Fen.Load(board, rootFen);
                Move ponder = GetPonderMove(board, _bestMoveOverall);
                return new SearchResult(_bestMoveOverall, _bestScoreOverall, _bestDepthReached, ponder);
            }
            finally
            {
                Fen.Load(board, rootFen);
            }
        }

        private static void SearchThreadBody(string rootFen, List<Move> rootMoves, int maxDepth, TimeManager? timeManager)
        {
            EnsureSearchTablesCurrent();
            var myBoard = new Board();
            Fen.Load(myBoard, rootFen);

            Move myBestMove = rootMoves[0];
            int myBestScore = 0;
            int alpha = -Infinity;
            int beta = Infinity;

            for (int depth = 1; depth <= maxDepth; depth++)
            {
                try
                {
                    int delta = depth >= 6 ? 25 : (depth >= 4 ? 50 : 25);
                    if (depth >= 4)
                    {
                        alpha = myBestScore - delta;
                        beta = myBestScore + delta;
                    }

                    while (true)
                    {
                        var (bestMoveAtDepth, bestScoreAtDepth) = SearchRoot(myBoard, depth, alpha, beta, myBestMove);

                        if (bestScoreAtDepth <= alpha)
                        {
                            alpha = Math.Max(-Infinity, alpha - delta);
                            delta *= 2;
                        }
                        else if (bestScoreAtDepth >= beta)
                        {
                            beta = Math.Min(Infinity, beta + delta);
                            delta *= 2;
                        }
                        else
                        {
                            myBestMove = bestMoveAtDepth;
                            myBestScore = bestScoreAtDepth;
                            break;
                        }
                    }

                    bool shouldStop = false;
                    lock (_resultLock)
                    {
                        if (depth >= _bestDepthReached)
                        {
                            _bestDepthReached = depth;
                            _bestMoveOverall = myBestMove;
                            _bestScoreOverall = myBestScore;
                            Move ponder = GetPonderMove(myBoard, myBestMove);
                            _lastResult = new SearchResult(_bestMoveOverall, _bestScoreOverall, depth, ponder);
                            SendUciInfo(depth, _bestScoreOverall, _bestMoveOverall, _nodeCount, _sw!.ElapsedMilliseconds);
                            timeManager?.OnIterationComplete(depth, myBestScore);
                        }
                        if (timeManager != null)
                            shouldStop = timeManager.ShouldStop(_sw!.ElapsedMilliseconds);
                    }

                    if (shouldStop)
                    {
                        break;
                    }
                }
                catch (SearchTimeoutException)
                {
                    break;
                }
            }
        }

        private static SearchResult FindBestMoveSingleThread(Board board, string rootFen, List<Move> rootMoves, int maxDepth, TimeManager timeManager)
        {
            EnsureSearchTablesCurrent();
            Move bestMoveOverall = rootMoves[0];
            int bestScoreOverall = 0;
            int depthReached = 0;
            int alpha = -Infinity;
            int beta = Infinity;

            for (int depth = 1; depth <= maxDepth; depth++)
            {
                try
                {
                    int delta = depth >= 6 ? 25 : (depth >= 4 ? 50 : 25);
                    if (depth >= 4)
                    {
                        alpha = bestScoreOverall - delta;
                        beta = bestScoreOverall + delta;
                    }

                    while (true)
                    {
                        var (bestMoveAtDepth, bestScoreAtDepth) = SearchRoot(board, depth, alpha, beta, bestMoveOverall);

                        if (bestScoreAtDepth <= alpha)
                        {
                            alpha = Math.Max(-Infinity, alpha - delta);
                            delta *= 2;
                        }
                        else if (bestScoreAtDepth >= beta)
                        {
                            beta = Math.Min(Infinity, beta + delta);
                            delta *= 2;
                        }
                        else
                        {
                            bestMoveOverall = bestMoveAtDepth;
                            bestScoreOverall = bestScoreAtDepth;
                            depthReached = depth;
                            break;
                        }
                    }

                    Move ponder = GetPonderMove(board, bestMoveOverall);
                    _lastResult = new SearchResult(bestMoveOverall, bestScoreOverall, depthReached, ponder);
                    SendUciInfo(depth, bestScoreOverall, bestMoveOverall, _nodeCount, _sw!.ElapsedMilliseconds);
                    timeManager.OnIterationComplete(depth, bestScoreOverall);

                    if (timeManager.ShouldStop(_sw.ElapsedMilliseconds))
                    {
                        break;
                    }
                }
                catch (SearchTimeoutException)
                {
                    break;
                }
            }

            _timeManager = null;
            PrintDiagnostics();
            Move finalPonder = GetPonderMove(board, bestMoveOverall);
            return new SearchResult(bestMoveOverall, bestScoreOverall, depthReached, finalPonder);
        }

        public static SearchResult FindBestMove(Board board, int timeMs, int maxDepth = 64)
        {
            if (timeMs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timeMs));
            }
            if (maxDepth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDepth));
            }

            string rootFen = Fen.Generate(board);
            try
            {
                _timeManager = null;
                _sw = Stopwatch.StartNew();
                _hardTimeLimit = timeMs;
                _stopRequested = false;
                _nodeCount = 0;
                ClearSearchTables();
                EnsureSearchTablesCurrent();

                List<Move> rootMoves = MoveGenerator.GenerateLegalMoves(board);
                if (rootMoves.Count == 0)
                {
                    return new SearchResult(default, 0, 0);
                }

                Move bestMoveOverall = rootMoves[0];
                int bestScoreOverall = 0;
                int depthReached = 0;

                int alpha = -Infinity;
                int beta = Infinity;

                for (int depth = 1; depth <= maxDepth; depth++)
                {
                    try
                    {
                        int delta = depth >= 6 ? 25 : (depth >= 4 ? 50 : 25);
                        if (depth >= 4)
                        {
                            alpha = bestScoreOverall - delta;
                            beta = bestScoreOverall + delta;
                        }

                        while (true)
                        {
                            var (bestMoveAtDepth, bestScoreAtDepth) = SearchRoot(board, depth, alpha, beta, bestMoveOverall);

                            if (bestScoreAtDepth <= alpha)
                            {
                                alpha = Math.Max(-Infinity, alpha - delta);
                                delta *= 2;
                            }
                            else if (bestScoreAtDepth >= beta)
                            {
                                beta = Math.Min(Infinity, beta + delta);
                                delta *= 2;
                            }
                            else
                            {
                                bestMoveOverall = bestMoveAtDepth;
                                bestScoreOverall = bestScoreAtDepth;
                                depthReached = depth;
                                break;
                            }
                        }

                        Move ponder = GetPonderMove(board, bestMoveOverall);
                        _lastResult = new SearchResult(bestMoveOverall, bestScoreOverall, depthReached, ponder);
                        SendUciInfo(depth, bestScoreOverall, bestMoveOverall, _nodeCount, _sw.ElapsedMilliseconds);
                    }
                    catch (SearchTimeoutException)
                    {
                        break;
                    }
                }

                Move finalPonder = GetPonderMove(board, bestMoveOverall);
                _lastResult = new SearchResult(bestMoveOverall, bestScoreOverall, depthReached, finalPonder);
                PrintDiagnostics();
                return _lastResult;
            }
            finally
            {
                Fen.Load(board, rootFen);
            }
        }

        private static (Move bestMove, int bestScore) SearchRoot(Board board, int depth, int alpha, int beta, Move previousBestMove = default)
        {
            CheckTime();

            Move[] moves = MoveStacks[0];
            int moveCount = MoveGenerator.GenerateLegalMoves(board, moves);
            if (moveCount == 0)
            {
                bool stmInCheck = board.IsKingInCheck(board.WhiteToMove);
                return (default, stmInCheck ? -MateScore : 0);
            }

            Move ttMove = _tt.GetTTMove(board.ZobristHash);
            OrderMoves(board, moves, moveCount, ttMove, 0);

            if (previousBestMove.From != previousBestMove.To || previousBestMove.Promotion != Piece.Empty)
            {
                for (int i = 1; i < moveCount; i++)
                {
                    if (MovesEqual(moves[i], previousBestMove))
                    {
                        (moves[0], moves[i]) = (moves[i], moves[0]);
                        (MoveScores[0], MoveScores[i]) = (MoveScores[i], MoveScores[0]);
                        break;
                    }
                }
            }

            Move bestMove = moves[0];
            int bestScore = -Infinity;
            int originalAlpha = alpha;

            for (int i = 0; i < moveCount; i++)
            {
                Move move = moves[i];
                board.MakeMove(move);

                int score;
                if (i == 0)
                {
                    score = -AlphaBeta(board, depth - 1, -beta, -alpha, 1, false);
                }
                else
                {
                    score = -AlphaBeta(board, depth - 1, -alpha - 1, -alpha, 1, false);
                    if (score > alpha && score < beta)
                    {
                        score = -AlphaBeta(board, depth - 1, -beta, -alpha, 1, false);
                    }
                }

                board.UndoMove(move);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMove = move;
                }

                if (score > alpha)
                {
                    alpha = score;
                }

                if (score >= beta)
                {
                    break;
                }
            }

            TTFlag flag = bestScore <= originalAlpha ? TTFlag.Alpha :
                          bestScore >= beta ? TTFlag.Beta : TTFlag.Exact;
            _tt.Store(board.ZobristHash, depth, bestScore, flag, bestMove, 0);
            return (bestMove, bestScore);
        }

        private static bool HasNonPawnMaterial(Board board)
        {
            if (board.WhiteToMove)
            {
                return (board.WN | board.WB | board.WR | board.WQ) != 0;
            }
            else
            {
                return (board.BN | board.BB | board.BR | board.BQ) != 0;
            }
        }

        private static void CheckTime()
        {
            Interlocked.Increment(ref _nodeCount);

            if ((_nodeCount & TimeCheckInterval) != 0)
            {
                return;
            }

            if (_sw == null)
            {
                return;
            }
            if (_stopRequested)
            {
                throw new SearchTimeoutException();
            }

            long elapsed = _sw.ElapsedMilliseconds;
            if (_timeManager != null)
            {
                if (_timeManager.MustStop(elapsed))
                {
                    throw new SearchTimeoutException();
                }
            }
            else
            {
                if (elapsed >= _hardTimeLimit)
                {
                    throw new SearchTimeoutException();
                }
            }
        }

        private static void SendUciInfo(int depth, int score, Move bestMove, long nodes, long elapsedMs)
        {
            long nps = elapsedMs > 0 ? (nodes * 1000) / elapsedMs : nodes;
            int hashfull = _tt.Hashfull();

            string scoreStr;
            if (score > MateScore - 1000)
            {
                int mateIn = (MateScore - score + 1) / 2;
                scoreStr = $"mate {mateIn}";
            }
            else if (score < -MateScore + 1000)
            {
                int mateIn = -(MateScore + score + 1) / 2;
                scoreStr = $"mate {mateIn}";
            }
            else
            {
                scoreStr = $"cp {score}";
            }

            string moveStr = bestMove.From != bestMove.To ? bestMove.ToString() : "0000";
            string info = $"info depth {depth} score {scoreStr} nodes {nodes} nps {nps} hashfull {hashfull} time {elapsedMs} pv {moveStr}";
            
            Console.WriteLine(info);
            Console.Out.Flush();
        }
    }
}
