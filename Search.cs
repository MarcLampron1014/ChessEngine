using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ChessEngine
{
    public static class Search
    {
        private const int MateScore = 100000;
        private const int Infinity = 1_000_000;
        private const int MaxPly = 128;

        private static readonly TranspositionTable _tt = new TranspositionTable(64);
        private static TimeManager? _timeManager;
        private static Stopwatch? _sw;
        private static int _hardTimeLimit;
        private static volatile bool _stopRequested;
        private static SearchResult _lastResult;

        public static void RequestStop() => _stopRequested = true;
        public static SearchResult GetLastResult() => _lastResult;

        // Per-thread search state (for Lazy SMP)
        private static readonly ThreadLocal<Move[,]> _killerMovesTls = new ThreadLocal<Move[,]>(() => new Move[MaxPly, 2]);
        private static readonly ThreadLocal<int[,]> _historyTableTls = new ThreadLocal<int[,]>(() => new int[13, 64]);
        private static readonly ThreadLocal<Move[,]> _counterMovesTls = new ThreadLocal<Move[,]>(() => new Move[13, 64]);
        private static readonly ThreadLocal<Move> _previousMoveTls = new ThreadLocal<Move>(() => default);
        private static readonly ThreadLocal<Move[][]> _moveStacksTls = new ThreadLocal<Move[][]>(() =>
        {
            var stacks = new Move[MaxPly][];
            for (int i = 0; i < MaxPly; i++)
                stacks[i] = new Move[256];
            return stacks;
        });
        private static readonly ThreadLocal<int[]> _moveScoresTls = new ThreadLocal<int[]>(() => new int[256]);

        // Generation counter so ClearSearchTables invalidates all thread caches
        private static int _searchGeneration;
        private static readonly ThreadLocal<int> _mySearchGeneration = new ThreadLocal<int>(() => -1);

        // Node counter for time check optimization and UCI info (shared, atomic)
        private static long _nodeCount;

        // Diagnostic counters for search efficiency analysis
        private static long _nullMoveAttempts;
        private static long _nullMoveCutoffs;
        private static long _ttCutoffs;
        private static long _betaCutoffs;
        private static long _lmrResearches;

        // Late Move Pruning thresholds by depth (reduced pruning for accuracy)
        private static readonly int[] LMPThresholds = { 0, 10, 14, 18, 22, 26, 30 };

        // Shared best result for multi-threaded search
        private static readonly object _resultLock = new object();
        private static int _bestDepthReached;
        private static Move _bestMoveOverall;
        private static int _bestScoreOverall;

        // Thread-local accessors for search state
        private static Move[,] Killers => _killerMovesTls.Value!;
        private static int[,] History => _historyTableTls.Value!;
        private static Move[,] Counters => _counterMovesTls.Value!;
        private static Move PreviousMove { get => _previousMoveTls.Value; set => _previousMoveTls.Value = value; }
        private static Move[][] MoveStacks => _moveStacksTls.Value!;
        private static int[] MoveScores => _moveScoresTls.Value!;

        private static void EnsureSearchTablesCurrent()
        {
            if (_mySearchGeneration.Value != _searchGeneration)
            {
                Array.Clear(Killers, 0, Killers.Length);
                for (int i = 0; i < History.GetLength(0); i++)
                    for (int j = 0; j < History.GetLength(1); j++)
                        History[i, j] /= 2;
                Array.Clear(Counters, 0, Counters.Length);
                PreviousMove = default;
                _mySearchGeneration.Value = _searchGeneration;
            }
        }

        public sealed class SearchTimeoutException : Exception { }

        /// <summary>
        /// MovePicker generates moves in stages for optimal search efficiency.
        /// Stages: TT Move -> Captures -> Killers -> Quiet moves
        /// </summary>
        private struct MovePicker
        {
            private enum Stage : byte { TTMove, GenerateMoves, GoodCaptures, Killers, Counters, BadCaptures, Quiets, Done }

            private Stage _stage;
            private readonly Board _board;
            private readonly Move _ttMove;
            private readonly int _ply;
            private Move[] _moves;
            private int _moveCount;
            private int _currentIdx;
            private int _captureEnd;  // Index where captures end and quiets begin
            private int _badCaptureStart; // Index where bad captures are moved

            public MovePicker(Board board, Move[] moveBuffer, Move ttMove, int ply)
            {
                _board = board;
                _ttMove = ttMove;
                _ply = ply;
                _moves = moveBuffer;
                _moveCount = 0;
                _currentIdx = 0;
                _captureEnd = 0;
                _badCaptureStart = 0;
                _stage = ttMove.From != ttMove.To ? Stage.TTMove : Stage.GenerateMoves;
            }

            public bool NextMove(out Move move, out int moveIndex)
            {
                while (_stage != Stage.Done)
                {
                    switch (_stage)
                    {
                        case Stage.TTMove:
                            _stage = Stage.GenerateMoves;
                            // Check if TT move is legal
                            if (IsLegalMove(_board, _ttMove))
                            {
                                move = _ttMove;
                                moveIndex = 0;
                                return true;
                            }
                            continue;

                        case Stage.GenerateMoves:
                            GenerateAndScoreMoves();
                            _stage = Stage.GoodCaptures;
                            continue;

                        case Stage.GoodCaptures:
                            while (_currentIdx < _captureEnd)
                            {
                                int bestIdx = SelectBest(_currentIdx, _captureEnd);
                                move = _moves[bestIdx];
                                
                                // Skip TT move (already searched)
                                if (MovesEqual(move, _ttMove))
                                {
                                    SwapMoves(bestIdx, _currentIdx);
                                    _currentIdx++;
                                    continue;
                                }

                                // Check SEE for this capture
                                if (SEE(_board, move) < 0)
                                {
                                    // Bad capture - move to end
                                    SwapMoves(bestIdx, --_captureEnd);
                                    continue;
                                }

                                SwapMoves(bestIdx, _currentIdx);
                                moveIndex = _currentIdx++;
                                return true;
                            }
                            _badCaptureStart = _captureEnd;
                            _stage = Stage.Killers;
                            continue;

                        case Stage.Killers:
                            _stage = Stage.Counters;
                            // Try killer moves (if they're legal quiet moves in our list)
                            if (_ply < MaxPly)
                            {
                                for (int k = 0; k < 2; k++)
                                {
                                    Move killer = Killers[_ply, k];
                                    if (killer.From == killer.To) continue;
                                    if (MovesEqual(killer, _ttMove)) continue;
                                    if (killer.IsCapture) continue;

                                    // Find killer in quiet moves
                                    for (int i = _captureEnd; i < _moveCount; i++)
                                    {
                                        if (MovesEqual(_moves[i], killer))
                                        {
                                            move = _moves[i];
                                            moveIndex = i;
                                            SwapMoves(i, _captureEnd);
                                            _captureEnd++; // Advance past killer
                                            return true;
                                        }
                                    }
                                }
                            }
                            continue;

                        case Stage.Counters:
                            _stage = Stage.BadCaptures;
                            // Try countermove
                            if (PreviousMove.From != PreviousMove.To)
                            {
                                Piece prevPiece = _board.PieceAt(PreviousMove.To);
                                if (prevPiece != Piece.Empty)
                                {
                                    Move counter = Counters[(int)prevPiece, PreviousMove.To];
                                    if (counter.From != counter.To && !MovesEqual(counter, _ttMove) && !counter.IsCapture)
                                    {
                                        for (int i = _captureEnd; i < _moveCount; i++)
                                        {
                                            if (MovesEqual(_moves[i], counter))
                                            {
                                                move = _moves[i];
                                                moveIndex = i;
                                                SwapMoves(i, _captureEnd);
                                                _captureEnd++;
                                                return true;
                                            }
                                        }
                                    }
                                }
                            }
                            continue;

                        case Stage.BadCaptures:
                            // Return bad captures that were filtered out earlier
                            while (_badCaptureStart < _moveCount && _moves[_badCaptureStart].IsCapture)
                            {
                                move = _moves[_badCaptureStart];
                                if (!MovesEqual(move, _ttMove))
                                {
                                    moveIndex = _badCaptureStart++;
                                    return true;
                                }
                                _badCaptureStart++;
                            }
                            _stage = Stage.Quiets;
                            _currentIdx = _captureEnd;
                            continue;

                        case Stage.Quiets:
                            while (_currentIdx < _moveCount)
                            {
                                int bestIdx = SelectBest(_currentIdx, _moveCount);
                                move = _moves[bestIdx];
                                
                                if (MovesEqual(move, _ttMove) || 
                                    (_ply < MaxPly && (MovesEqual(move, Killers[_ply, 0]) || MovesEqual(move, Killers[_ply, 1]))))
                                {
                                    SwapMoves(bestIdx, _currentIdx);
                                    _currentIdx++;
                                    continue;
                                }

                                // Skip countermove if already returned
                                if (PreviousMove.From != PreviousMove.To)
                                {
                                    Piece prevPiece = _board.PieceAt(PreviousMove.To);
                                    if (prevPiece != Piece.Empty && MovesEqual(move, Counters[(int)prevPiece, PreviousMove.To]))
                                    {
                                        SwapMoves(bestIdx, _currentIdx);
                                        _currentIdx++;
                                        continue;
                                    }
                                }

                                SwapMoves(bestIdx, _currentIdx);
                                moveIndex = _currentIdx++;
                                return true;
                            }
                            _stage = Stage.Done;
                            continue;
                    }
                }

                move = default;
                moveIndex = -1;
                return false;
            }

            private void GenerateAndScoreMoves()
            {
                _moveCount = MoveGenerator.GenerateLegalMoves(_board, _moves);
                
                // Partition into captures and quiets, score moves
                int captureIdx = 0;
                int quietIdx = _moveCount - 1;

                while (captureIdx <= quietIdx)
                {
                    if (_moves[captureIdx].IsCapture || _moves[captureIdx].IsPromotion)
                    {
                        MoveScores[captureIdx] = ScoreMoveInternal(_moves[captureIdx]);
                        captureIdx++;
                    }
                    else
                    {
                        // Swap with a quiet from the end
                        (_moves[captureIdx], _moves[quietIdx]) = (_moves[quietIdx], _moves[captureIdx]);
                        MoveScores[quietIdx] = ScoreMoveInternal(_moves[quietIdx]);
                        quietIdx--;
                    }
                }

                _captureEnd = captureIdx;
                _currentIdx = 0;

                // Score remaining quiets  
                for (int i = _captureEnd; i < _moveCount; i++)
                {
                    MoveScores[i] = ScoreMoveInternal(_moves[i]);
                }
            }

            private int ScoreMoveInternal(Move move)
            {
                int score = 0;

                if (move.IsPromotion)
                    score += 1_000_000 + Evaluator.GetPieceValue(move.Promotion);

                if (move.IsCapture)
                {
                    Piece attacker = _board.PieceAt(move.From);
                    Piece victim = move.IsEnPassant
                        ? (_board.WhiteToMove ? Piece.BP : Piece.WP)
                        : _board.PieceAt(move.To);

                    int victimValue = Evaluator.GetPieceValue(victim);
                    int attackerValue = Evaluator.GetPieceValue(attacker);
                    score += 500_000 + (victimValue * 10) - attackerValue;
                }
                else
                {
                    Piece piece = _board.PieceAt(move.From);
                    if (piece != Piece.Empty)
                        score += History[(int)piece, move.To];
                }

                if (move.IsCastling)
                    score += 50;

                return score;
            }

            private int SelectBest(int start, int end)
            {
                int bestIdx = start;
                int bestScore = MoveScores[start];
                for (int i = start + 1; i < end; i++)
                {
                    if (MoveScores[i] > bestScore)
                    {
                        bestScore = MoveScores[i];
                        bestIdx = i;
                    }
                }
                return bestIdx;
            }

            private void SwapMoves(int a, int b)
            {
                if (a != b)
                {
                    (_moves[a], _moves[b]) = (_moves[b], _moves[a]);
                    (MoveScores[a], MoveScores[b]) = (MoveScores[b], MoveScores[a]);
                }
            }

            private static bool IsLegalMove(Board board, Move move)
            {
                // Quick validation - check if piece exists at from square
                Piece p = board.PieceAt(move.From);
                if (p == Piece.Empty) return false;
                
                bool white = board.WhiteToMove;
                bool isPieceWhite = (int)p <= 6;
                if (white != isPieceWhite) return false;

                // Make and check legality
                board.MakeMove(move);
                bool legal = !board.IsKingInCheck(white);
                board.UndoMove(move);
                return legal;
            }

            public int TotalMoveCount => _moveCount;
        }

        public readonly struct SearchResult
        {
            public readonly Move BestMove;
            public readonly int BestScore;
            public readonly int DepthReached;
            public readonly Move PonderMove;

            public SearchResult(Move bestMove, int bestScore, int depthReached, Move ponderMove = default)
            {
                BestMove = bestMove;
                BestScore = bestScore;
                DepthReached = depthReached;
                PonderMove = ponderMove;
            }
        }

        /// <summary>
        /// Gets the opponent's expected best reply (ponder move) from the TT after our best move.
        /// </summary>
        private static Move GetPonderMove(Board board, Move ourBestMove)
        {
            if (ourBestMove.From == ourBestMove.To)
                return default;
            board.MakeMove(ourBestMove);
            Move ponder = _tt.GetTTMove(board.ZobristHash);
            board.UndoMove(ourBestMove);
            if (ponder.From == ponder.To)
                return default;
            board.MakeMove(ourBestMove);
            var legal = MoveGenerator.GenerateLegalMoves(board);
            board.UndoMove(ourBestMove);
            foreach (var m in legal)
                if (m.From == ponder.From && m.To == ponder.To && m.Promotion == ponder.Promotion)
                    return m;
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
                throw new ArgumentOutOfRangeException(nameof(maxDepth));
            if (numThreads <= 0)
                throw new ArgumentOutOfRangeException(nameof(numThreads));

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
                    return new SearchResult(default, 0, 0);

                if (numThreads <= 1)
                {
                    EnsureSearchTablesCurrent();
                    return FindBestMoveSingleThread(board, rootFen, rootMoves, maxDepth, timeManager);
                }

                // Multi-threaded Lazy SMP (Task.Run uses ThreadPool - avoids ThreadLocal accumulation)
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
                        }
                    }

                    timeManager?.OnIterationComplete(depth, myBestScore);

                    if (timeManager != null && timeManager.ShouldStop(_sw!.ElapsedMilliseconds))
                        break;
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
                        break;
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
                throw new ArgumentOutOfRangeException(nameof(timeMs));
            if (maxDepth <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxDepth));

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
                    return new SearchResult(default, 0, 0);

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
                // PVS: search first move with full window, rest with null window
                if (i == 0)
                {
                    score = -AlphaBeta(board, depth - 1, -beta, -alpha, 1, false);
                }
                else
                {
                    // Null window search
                    score = -AlphaBeta(board, depth - 1, -alpha - 1, -alpha, 1, false);
                    // Re-search with full window if it fails high
                    if (score > alpha && score < beta)
                        score = -AlphaBeta(board, depth - 1, -beta, -alpha, 1, false);
                }

                board.UndoMove(move);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMove = move;
                }

                if (score > alpha)
                    alpha = score;

                if (score >= beta)
                    break;
            }

            TTFlag flag = bestScore <= originalAlpha ? TTFlag.Alpha :
                          bestScore >= beta ? TTFlag.Beta : TTFlag.Exact;
            _tt.Store(board.ZobristHash, depth, bestScore, flag, bestMove, 0);
            return (bestMove, bestScore);
        }

        private static int AlphaBeta(Board board, int depth, int alpha, int beta, int ply, bool isNullMove, Move excludeMove = default)
        {
            CheckTime();

            // Check for draws: repetition, fifty-move rule, insufficient material
            if (ply > 0)
            {
                if (board.IsRepetition() || board.IsFiftyMoveRule())
                    return 0;
                if (Evaluator.IsInsufficientMaterial(board))
                    return 0;
            }

            // Check extension: extend search when in check (capped to avoid tree explosion)
            bool inCheck = board.IsKingInCheck(board.WhiteToMove);
            if (inCheck && depth <= 8)
                depth++;

            if (depth <= 0)
                return Quiesce(board, alpha, beta, ply);

            // Determine if this is a PV node
            bool isPV = beta - alpha > 1;

            if (_tt.Probe(board.ZobristHash, depth, alpha, beta, ply, out int ttScore, out Move ttMove))
            {
                Interlocked.Increment(ref _ttCutoffs);
                return ttScore;
            }

            int quickEval = Evaluator.QuickEvaluate(board);
            // Use full eval when depth >= 3 for null move and futility decisions
            int staticEval = depth >= 3 ? Evaluator.Evaluate(board) : quickEval;

            // When losing by more than this we don't prune so we search for a hold
            const int LosingMargin = 200;

            // Razor and RFP disabled for evaluation accuracy

            // Null move pruning: depth >= 3, HasNonPawnMaterial excludes K+P zugzwang
            if (!isNullMove && !inCheck && depth >= 3 && HasNonPawnMaterial(board))
            {
                Interlocked.Increment(ref _nullMoveAttempts);
                int R = 3 + depth / 5;
                R = Math.Min(R, depth - 1);

                board.MakeNullMove();
                int nullScore = -AlphaBeta(board, depth - R, -beta, -beta + 1, ply + 1, true);
                board.UndoNullMove();

                if (nullScore >= beta)
                {
                    // In endgame, verify null move cutoff to avoid zugzwang false positives
                    if (board.Phase <= 12 && depth >= 4)
                    {
                        board.MakeNullMove();
                        int verifyScore = -AlphaBeta(board, depth - 2, -beta, -alpha, ply + 1, true);
                        board.UndoNullMove();
                        if (verifyScore < beta)
                            goto SkipNullMoveCutoff;
                    }
                    Interlocked.Increment(ref _nullMoveCutoffs);
                    return beta;
                }
            }
            SkipNullMoveCutoff:

            // Internal Iterative Deepening (IID)
            // If we don't have a TT move at high depth, do a shallow search first
            if (ttMove.From == ttMove.To && depth >= 6 && isPV)
            {
                AlphaBeta(board, depth - 4, alpha, beta, ply, false);
                ttMove = _tt.GetTTMove(board.ZobristHash);
            }

            // Singular extension disabled for evaluation accuracy
            int singularExtension = 0;
            bool singularSearchInProgress = excludeMove.From != excludeMove.To;

            int originalAlpha = alpha;
            Move bestMove = default;
            int movesSearched = 0;

            // Improving: side to move's position is already good (conversion: don't over-prune)
            const int ImprovingMargin = 50;
            bool improving = staticEval >= alpha - ImprovingMargin;

            // Losing: we need to find a hold (draw/defense), so don't over-prune
            bool losing = staticEval < alpha - LosingMargin;

            // Futility: increased margins for accuracy; skip when losing so we don't prune the saving move
            int[] futilityMargins = { 0, 200, 400, 600, 900 };
            bool canFutilityPrune = !inCheck && !isPV && !losing && depth <= 4 && depth < futilityMargins.Length && staticEval + futilityMargins[depth] < alpha;
            bool canReverseFutilityPrune = false;  // disabled: was pruning winning quiet moves

            Move prevMove = PreviousMove;
            var picker = new MovePicker(board, MoveStacks[ply], ttMove, ply);

            while (picker.NextMove(out Move move, out _))
            {
                if (singularSearchInProgress && MovesEqual(move, excludeMove))
                    continue;

                bool isQuiet = !move.IsCapture && !move.IsPromotion;
                bool isTTMove = MovesEqual(move, ttMove);

                // LMP: skip when improving or losing so we don't prune conversion or the one saving move
                if (!isPV && !inCheck && depth >= 4 && movesSearched > 0 && isQuiet && !improving && !losing)
                {
                    if (depth < LMPThresholds.Length && movesSearched >= LMPThresholds[depth])
                        continue;
                }

                if (canFutilityPrune && movesSearched > 0 && isQuiet)
                    continue;

                if (canReverseFutilityPrune && movesSearched > 0 && isQuiet)
                    continue;

                // SEE pruning: restrict to depth 1 only for accuracy
                if (move.IsCapture && !move.IsPromotion && movesSearched > 0 && depth <= 1)
                {
                    if (SEE(board, move) < 0)
                        continue;
                }
                // At depth 2-3, prune clearly bad captures
                if (move.IsCapture && !move.IsPromotion && movesSearched > 0 && depth >= 2 && depth <= 3)
                {
                    if (SEE(board, move) < -100)
                        continue;
                }

                int extension = (isTTMove && singularExtension > 0) ? singularExtension : 0;
                bool isRecapture = prevMove.From != prevMove.To && prevMove.IsCapture && move.IsCapture && move.To == prevMove.To;
                if (isRecapture) extension++;
                if (extension == 0 && move.IsCapture && !move.IsPromotion && depth >= 4 && SEE(board, move) >= 0) extension++;
                int newDepth = depth - 1 + extension;

                PreviousMove = move;
                board.MakeMove(move);
                bool givesCheck = board.IsKingInCheck(board.WhiteToMove);

                // LMR: no reduction at PV nodes; dynamic reduction for late quiet moves
                int reduction = 0;
                if (depth >= 3 && movesSearched >= 3 && !isPV && isQuiet && !inCheck && !givesCheck && !isRecapture)
                {
                    reduction = 1 + (depth / 5) + (movesSearched / 8);
                    reduction = Math.Min(reduction, depth - 2);
                }

                int score;
                if (movesSearched == 0)
                    score = -AlphaBeta(board, newDepth, -beta, -alpha, ply + 1, false);
                else
                {
                    score = -AlphaBeta(board, newDepth - reduction, -alpha - 1, -alpha, ply + 1, false);
                    if (reduction > 0 && score > alpha)
                    {
                        Interlocked.Increment(ref _lmrResearches);
                        score = -AlphaBeta(board, newDepth, -alpha - 1, -alpha, ply + 1, false);
                    }
                    if (score > alpha && score < beta)
                        score = -AlphaBeta(board, newDepth, -beta, -alpha, ply + 1, false);
                }

                board.UndoMove(move);
                movesSearched++;

                if (score >= beta)
                {
                    Interlocked.Increment(ref _betaCutoffs);
                    _tt.Store(board.ZobristHash, depth, beta, TTFlag.Beta, move, ply);
                    if (isQuiet && ply < MaxPly)
                    {
                        if (!MovesEqual(Killers[ply, 0], move))
                        {
                            Killers[ply, 1] = Killers[ply, 0];
                            Killers[ply, 0] = move;
                        }
                    }
                    if (prevMove.From != prevMove.To && isQuiet)
                    {
                        Piece prevPiece = board.PieceAt(prevMove.To);
                        if (prevPiece != Piece.Empty)
                            Counters[(int)prevPiece, prevMove.To] = move;
                    }
                    if (isQuiet)
                    {
                        Piece piece = board.PieceAt(move.From);
                        if (piece == Piece.Empty)
                            piece = board.PieceAt(move.To);
                        History[(int)piece, move.To] += depth * depth;
                    }
                    PreviousMove = prevMove;
                    return beta;
                }

                if (score > alpha)
                {
                    alpha = score;
                    bestMove = move;
                    if (isQuiet)
                    {
                        Piece piece = board.PieceAt(move.From);
                        if (piece == Piece.Empty)
                            piece = board.PieceAt(move.To);
                        History[(int)piece, move.To] += depth;
                    }
                }
            }

            PreviousMove = prevMove;
            if (movesSearched == 0)
                return inCheck ? -MateScore + ply : 0;
            TTFlag flag = alpha > originalAlpha ? TTFlag.Exact : TTFlag.Alpha;
            _tt.Store(board.ZobristHash, depth, alpha, flag, bestMove, ply);
            return alpha;
        }

        private static bool HasNonPawnMaterial(Board board)
        {
            if (board.WhiteToMove)
                return (board.WN | board.WB | board.WR | board.WQ) != 0;
            else
                return (board.BN | board.BB | board.BR | board.BQ) != 0;
        }

        private static bool MovesEqual(Move a, Move b)
        {
            return a.From == b.From && a.To == b.To && a.Promotion == b.Promotion;
        }

        /// <summary>
        /// Static Exchange Evaluation - evaluates a capture sequence without making moves.
        /// Returns the approximate material gain/loss from the sequence.
        /// </summary>
        private static int SEE(Board board, Move move)
        {
            if (!move.IsCapture)
                return 0;

            int to = move.To;
            int from = move.From;

            // Get the victim piece value
            Piece victim = move.IsEnPassant
                ? (board.WhiteToMove ? Piece.BP : Piece.WP)
                : board.PieceAt(to);
            
            if (victim == Piece.Empty)
                return 0;

            Piece attacker = board.PieceAt(from);
            int attackerValue = GetSEEPieceValue(attacker);
            int victimValue = GetSEEPieceValue(victim);

            // Simple SEE approximation: if we're capturing with a less valuable piece,
            // or equal exchange, it's likely good
            if (attackerValue <= victimValue)
                return victimValue - attackerValue;

            // Check if the square is defended
            ulong occupied = board.AllPieces ^ Bitboard.SquareBB[from];
            if (move.IsEnPassant)
            {
                int epCaptureSq = board.WhiteToMove ? to - 8 : to + 8;
                occupied ^= Bitboard.SquareBB[epCaptureSq];
            }

            ulong attackers = board.AttackersTo(to, occupied) & occupied;
            
            // Remove the initial attacker
            attackers &= ~Bitboard.SquareBB[from];

            // Get defenders (opponent's attackers)
            ulong defenders = attackers & (board.WhiteToMove ? board.BlackPieces : board.WhitePieces);

            if (defenders == 0)
                return victimValue; // No defenders, we just win the piece

            // Find least valuable defender
            int minDefenderValue = GetMinAttackerValue(defenders, board, !board.WhiteToMove);

            // If our attacker is worth more than victim + their counter-attack potential, it's bad
            if (attackerValue > victimValue + minDefenderValue)
                return victimValue - attackerValue;

            // Simplified: if attacker > victim, assume it's a losing capture
            // unless we have overwhelming force
            ulong ourAttackers = attackers & (board.WhiteToMove ? board.WhitePieces : board.BlackPieces);
            int ourAttackerCount = Bitboard.PopCount(ourAttackers);
            int theirDefenderCount = Bitboard.PopCount(defenders);

            if (ourAttackerCount > theirDefenderCount)
                return victimValue - attackerValue + 50; // Slight bonus for overwhelming force

            return victimValue - attackerValue;
        }

        private static int GetSEEPieceValue(Piece p)
        {
            return p switch
            {
                Piece.WP or Piece.BP => 100,
                Piece.WN or Piece.BN => 320,
                Piece.WB or Piece.BB => 330,
                Piece.WR or Piece.BR => 500,
                Piece.WQ or Piece.BQ => 900,
                Piece.WK or Piece.BK => 20000,
                _ => 0
            };
        }

        private static int GetMinAttackerValue(ulong attackers, Board board, bool white)
        {
            if (white)
            {
                if ((attackers & board.WP) != 0) return 100;
                if ((attackers & board.WN) != 0) return 320;
                if ((attackers & board.WB) != 0) return 330;
                if ((attackers & board.WR) != 0) return 500;
                if ((attackers & board.WQ) != 0) return 900;
                if ((attackers & board.WK) != 0) return 20000;
            }
            else
            {
                if ((attackers & board.BP) != 0) return 100;
                if ((attackers & board.BN) != 0) return 320;
                if ((attackers & board.BB) != 0) return 330;
                if ((attackers & board.BR) != 0) return 500;
                if ((attackers & board.BQ) != 0) return 900;
                if ((attackers & board.BK) != 0) return 20000;
            }
            return 20000;
        }

        private static int Quiesce(Board board, int alpha, int beta, int ply)
        {
            CheckTime();

            // Check for draws
            if (board.IsRepetition() || board.IsFiftyMoveRule())
                return 0;
            if (Evaluator.IsInsufficientMaterial(board))
                return 0;

            bool inCheck = board.IsKingInCheck(board.WhiteToMove);
            int standPat = Evaluator.Evaluate(board);

            // When in check, we must search all evasions
            if (inCheck)
            {
                int qPly = Math.Min(ply, MaxPly - 1);
                Move[] moves = MoveStacks[qPly];
                int moveCount = MoveGenerator.GenerateLegalMoves(board, moves);
                
                if (moveCount == 0)
                    return -MateScore + ply; // Checkmate
                
                OrderMoves(board, moves, moveCount, default, qPly);
                
                for (int i = 0; i < moveCount; i++)
                {
                    Move move = moves[i];
                    board.MakeMove(move);
                    int score = -Quiesce(board, -beta, -alpha, ply + 1);
                    board.UndoMove(move);

                    if (score >= beta)
                        return beta;
                    if (score > alpha)
                        alpha = score;
                }
                return alpha;
            }

            if (standPat >= beta)
                return beta;
            if (standPat > alpha)
                alpha = standPat;

            // Stage 1: Captures and promotions (GenerateLegalCaptures includes promotion pushes)
            int qPly2 = Math.Min(ply, MaxPly - 1);
            Move[] captureMoves = MoveStacks[qPly2];
            int noisyCount = MoveGenerator.GenerateLegalCaptures(board, captureMoves);

            if (noisyCount > 0)
                OrderMoves(board, captureMoves, noisyCount, default, qPly2);

            const int DeltaMargin = 350;

            for (int i = 0; i < noisyCount; i++)
            {
                Move move = captureMoves[i];

                // Delta pruning: skip captures that can't possibly raise alpha
                if (!move.IsPromotion)
                {
                    Piece victim = move.IsEnPassant
                        ? (board.WhiteToMove ? Piece.BP : Piece.WP)
                        : board.PieceAt(move.To);

                    int captureValue = Evaluator.GetPieceValue(victim);
                    if (standPat + captureValue + DeltaMargin < alpha)
                        continue;
                }

                // SEE pruning: loosened for accuracy - only skip clearly bad captures
                if (move.IsCapture && !move.IsPromotion)
                {
                    int seeScore = SEE(board, move);
                    if (seeScore < -150)
                        continue;
                }

                board.MakeMove(move);
                int score = -Quiesce(board, -beta, -alpha, ply + 1);
                board.UndoMove(move);

                if (score >= beta)
                    return beta;
                if (score > alpha)
                    alpha = score;
            }

            // Stage 2: Passed-pawn pushes (conversion moves; limit to 4 to keep quiescence fast)
            int passerCount = MoveGenerator.GenerateLegalPassedPawnPushes(board, captureMoves);
            const int MaxPassedPawnPushesInQuiesce = 4;
            int passerLimit = Math.Min(passerCount, MaxPassedPawnPushesInQuiesce);

            for (int i = 0; i < passerLimit; i++)
            {
                Move move = captureMoves[i];
                board.MakeMove(move);
                int score = -Quiesce(board, -beta, -alpha, ply + 1);
                board.UndoMove(move);

                if (score >= beta)
                    return beta;
                if (score > alpha)
                    alpha = score;
            }

            return alpha;
        }

        private static void CheckTime()
        {
            Interlocked.Increment(ref _nodeCount);

            // Only check time every 2048 nodes to reduce overhead
            if ((_nodeCount & 2047) != 0)
                return;

            if (_sw == null)
                return;
            if (_stopRequested)
                throw new SearchTimeoutException();

            long elapsed = _sw.ElapsedMilliseconds;
            if (_timeManager != null)
            {
                if (_timeManager.MustStop(elapsed))
                    throw new SearchTimeoutException();
            }
            else
            {
                if (elapsed >= _hardTimeLimit)
                    throw new SearchTimeoutException();
            }
        }

        /// <summary>
        /// Outputs UCI info string with search statistics.
        /// </summary>
        private static void SendUciInfo(int depth, int score, Move bestMove, long nodes, long elapsedMs)
        {
            long nps = elapsedMs > 0 ? (nodes * 1000) / elapsedMs : nodes;
            int hashfull = _tt.Hashfull();

            // Format score - detect mate scores
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

        private static void OrderMoves(Board board, Move[] moves, int moveCount, Move ttMove, int ply)
        {
            // Score all moves
            for (int i = 0; i < moveCount; i++)
                MoveScores[i] = ScoreMove(board, moves[i], ttMove, ply);

            // Sort all moves for correct root ordering (best move must be first for PVS)
            int sortLimit = moveCount - 1;
            for (int i = 0; i < sortLimit; i++)
            {
                int bestIdx = i;
                int bestScore = MoveScores[i];
                for (int j = i + 1; j < moveCount; j++)
                {
                    if (MoveScores[j] > bestScore)
                    {
                        bestScore = MoveScores[j];
                        bestIdx = j;
                    }
                }
                if (bestIdx != i)
                {
                    // Swap moves and scores
                    (moves[i], moves[bestIdx]) = (moves[bestIdx], moves[i]);
                    (MoveScores[i], MoveScores[bestIdx]) = (MoveScores[bestIdx], MoveScores[i]);
                }
            }
        }

        private static int ScoreMove(Board board, Move move, Move ttMove, int ply)
        {
            // TT move gets highest priority
            if (MovesEqual(move, ttMove))
                return 10_000_000;

            int score = 0;

            // Promotions
            if (move.IsPromotion)
                score += 1_000_000 + Evaluator.GetPieceValue(move.Promotion);

            // Captures - MVV-LVA
            if (move.IsCapture)
            {
                Piece attacker = board.PieceAt(move.From);
                Piece victim = move.IsEnPassant
                    ? (board.WhiteToMove ? Piece.BP : Piece.WP)
                    : board.PieceAt(move.To);

                int victimValue = Evaluator.GetPieceValue(victim);
                int attackerValue = Evaluator.GetPieceValue(attacker);
                score += 500_000 + (victimValue * 10) - attackerValue;
            }
            else
            {
                // Killer moves (only for quiet moves)
                if (ply < MaxPly)
                {
                    if (MovesEqual(move, Killers[ply, 0]))
                        return 400_000;
                    if (MovesEqual(move, Killers[ply, 1]))
                        return 300_000;
                }

                // Countermove heuristic
                if (PreviousMove.From != PreviousMove.To)
                {
                    Piece prevPiece = board.PieceAt(PreviousMove.To);
                    if (prevPiece != Piece.Empty && MovesEqual(move, Counters[(int)prevPiece, PreviousMove.To]))
                        return 200_000;
                }

                // History heuristic for quiet moves
                Piece piece = board.PieceAt(move.From);
                if (piece != Piece.Empty)
                    score += History[(int)piece, move.To];
            }

            if (move.IsCastling)
                score += 50;

            return score;
        }
    }
}