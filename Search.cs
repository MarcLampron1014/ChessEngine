using System;
using System.Collections.Generic;
using System.Diagnostics;

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

        // Killer moves: 2 killers per ply
        private static readonly Move[,] _killerMoves = new Move[MaxPly, 2];

        // History heuristic: [piece][toSquare]
        private static readonly int[,] _historyTable = new int[13, 64];

        // Countermove heuristic: [piece][toSquare] -> move that refutes it
        private static readonly Move[,] _counterMoves = new Move[13, 64];

        // Node counter for time check optimization
        private static int _nodeCount;

        // Previous move tracking for countermove heuristic
        private static Move _previousMove;

        // Late Move Pruning thresholds by depth
        private static readonly int[] LMPThresholds = { 0, 5, 8, 12, 18, 25, 33 };

        // Preallocated move arrays per ply to avoid allocations
        private static readonly Move[][] _moveStacks = new Move[MaxPly][];
        private static readonly int[] _moveScores = new int[256];

        static Search()
        {
            for (int i = 0; i < MaxPly; i++)
                _moveStacks[i] = new Move[256];
        }

        private sealed class SearchTimeoutException : Exception { }

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
                                    Move killer = _killerMoves[_ply, k];
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
                            if (_previousMove.From != _previousMove.To)
                            {
                                Piece prevPiece = _board.PieceAt(_previousMove.To);
                                if (prevPiece != Piece.Empty)
                                {
                                    Move counter = _counterMoves[(int)prevPiece, _previousMove.To];
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
                                    (_ply < MaxPly && (MovesEqual(move, _killerMoves[_ply, 0]) || MovesEqual(move, _killerMoves[_ply, 1]))))
                                {
                                    SwapMoves(bestIdx, _currentIdx);
                                    _currentIdx++;
                                    continue;
                                }

                                // Skip countermove if already returned
                                if (_previousMove.From != _previousMove.To)
                                {
                                    Piece prevPiece = _board.PieceAt(_previousMove.To);
                                    if (prevPiece != Piece.Empty && MovesEqual(move, _counterMoves[(int)prevPiece, _previousMove.To]))
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
                        _moveScores[captureIdx] = ScoreMoveInternal(_moves[captureIdx]);
                        captureIdx++;
                    }
                    else
                    {
                        // Swap with a quiet from the end
                        (_moves[captureIdx], _moves[quietIdx]) = (_moves[quietIdx], _moves[captureIdx]);
                        _moveScores[quietIdx] = ScoreMoveInternal(_moves[quietIdx]);
                        quietIdx--;
                    }
                }

                _captureEnd = captureIdx;
                _currentIdx = 0;

                // Score remaining quiets  
                for (int i = _captureEnd; i < _moveCount; i++)
                {
                    _moveScores[i] = ScoreMoveInternal(_moves[i]);
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
                        score += _historyTable[(int)piece, move.To];
                }

                if (move.IsCastling)
                    score += 50;

                return score;
            }

            private int SelectBest(int start, int end)
            {
                int bestIdx = start;
                int bestScore = _moveScores[start];
                for (int i = start + 1; i < end; i++)
                {
                    if (_moveScores[i] > bestScore)
                    {
                        bestScore = _moveScores[i];
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
                    (_moveScores[a], _moveScores[b]) = (_moveScores[b], _moveScores[a]);
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

            public SearchResult(Move bestMove, int bestScore, int depthReached)
            {
                BestMove = bestMove;
                BestScore = bestScore;
                DepthReached = depthReached;
            }
        }

        public static void SetHashSize(int sizeMB) => _tt.Resize(sizeMB);
        public static void ClearHash() => _tt.Clear();

        private static void ClearSearchTables()
        {
            Array.Clear(_killerMoves, 0, _killerMoves.Length);
            Array.Clear(_historyTable, 0, _historyTable.Length);
            Array.Clear(_counterMoves, 0, _counterMoves.Length);
            _nodeCount = 0;
            _previousMove = default;
        }

        public static SearchResult FindBestMove(Board board, TimeManager timeManager, int maxDepth = 64)
        {
            if (maxDepth <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxDepth));

            _timeManager = timeManager;
            _sw = Stopwatch.StartNew();
            _hardTimeLimit = timeManager.MaxTimeMs;
            ClearSearchTables();

            List<Move> rootMoves = MoveGenerator.GenerateLegalMoves(board);
            if (rootMoves.Count == 0)
                return new SearchResult(default, 0, 0);

            Move bestMoveOverall = rootMoves[0];
            int bestScoreOverall = 0;
            int depthReached = 0;

            // Aspiration window parameters
            int alpha = -Infinity;
            int beta = Infinity;
            const int AspirationDelta = 25;

            for (int depth = 1; depth <= maxDepth; depth++)
            {
                try
                {
                    int delta = AspirationDelta;

                    // Use aspiration windows starting from depth 4
                    if (depth >= 4)
                    {
                        alpha = bestScoreOverall - delta;
                        beta = bestScoreOverall + delta;
                    }

                    while (true)
                    {
                        var (bestMoveAtDepth, bestScoreAtDepth) = SearchRoot(board, depth, alpha, beta);

                        // Check for aspiration window fail
                        if (bestScoreAtDepth <= alpha)
                        {
                            // Fail low - widen alpha
                            alpha = Math.Max(-Infinity, alpha - delta);
                            delta *= 2;
                        }
                        else if (bestScoreAtDepth >= beta)
                        {
                            // Fail high - widen beta
                            beta = Math.Min(Infinity, beta + delta);
                            delta *= 2;
                        }
                        else
                        {
                            // Score within window
                            bestMoveOverall = bestMoveAtDepth;
                            bestScoreOverall = bestScoreAtDepth;
                            depthReached = depth;
                            break;
                        }
                    }

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
            return new SearchResult(bestMoveOverall, bestScoreOverall, depthReached);
        }

        public static SearchResult FindBestMove(Board board, int timeMs, int maxDepth = 64)
        {
            if (timeMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeMs));
            if (maxDepth <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxDepth));

            _timeManager = null;
            _sw = Stopwatch.StartNew();
            _hardTimeLimit = timeMs;
            ClearSearchTables();

            List<Move> rootMoves = MoveGenerator.GenerateLegalMoves(board);
            if (rootMoves.Count == 0)
                return new SearchResult(default, 0, 0);

            Move bestMoveOverall = rootMoves[0];
            int bestScoreOverall = 0;
            int depthReached = 0;

            // Aspiration window parameters
            int alpha = -Infinity;
            int beta = Infinity;
            const int AspirationDelta = 25;

            for (int depth = 1; depth <= maxDepth; depth++)
            {
                try
                {
                    int delta = AspirationDelta;

                    // Use aspiration windows starting from depth 4
                    if (depth >= 4)
                    {
                        alpha = bestScoreOverall - delta;
                        beta = bestScoreOverall + delta;
                    }

                    while (true)
                    {
                        var (bestMoveAtDepth, bestScoreAtDepth) = SearchRoot(board, depth, alpha, beta);

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
                }
                catch (SearchTimeoutException)
                {
                    break;
                }
            }

            return new SearchResult(bestMoveOverall, bestScoreOverall, depthReached);
        }

        private static (Move bestMove, int bestScore) SearchRoot(Board board, int depth, int alpha, int beta)
        {
            CheckTime();

            Move[] moves = _moveStacks[0];
            int moveCount = MoveGenerator.GenerateLegalMoves(board, moves);
            if (moveCount == 0)
            {
                bool stmInCheck = board.IsKingInCheck(board.WhiteToMove);
                return (default, stmInCheck ? -MateScore : 0);
            }

            Move ttMove = _tt.GetTTMove(board.ZobristHash);
            OrderMoves(board, moves, moveCount, ttMove, 0);

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

            // Check extension: extend search when in check
            bool inCheck = board.IsKingInCheck(board.WhiteToMove);
            if (inCheck)
                depth++;

            if (depth <= 0)
                return Quiesce(board, alpha, beta, ply);

            // Determine if this is a PV node
            bool isPV = beta - alpha > 1;

            if (_tt.Probe(board.ZobristHash, depth, alpha, beta, ply, out int ttScore, out Move ttMove))
                return ttScore;

            // Static evaluation for pruning decisions
            int staticEval = Evaluator.Evaluate(board);

            // Razoring: at low depth, if we're far below alpha, drop into qsearch
            if (!isPV && !inCheck && depth <= 2)
            {
                int razorMargin = 300 + 200 * depth;
                if (staticEval + razorMargin < alpha)
                {
                    int qScore = Quiesce(board, alpha, beta, ply);
                    if (qScore < alpha)
                        return qScore;
                }
            }

            // Reverse Futility Pruning (Static Null Move Pruning)
            if (!inCheck && !isPV && depth <= 3)
            {
                int rfpMargin = 120 * depth;
                if (staticEval - rfpMargin >= beta)
                    return staticEval;
            }

            // Null move pruning with variable R
            if (!isNullMove && !inCheck && depth >= 3 && HasNonPawnMaterial(board))
            {
                // Variable R based on depth and eval
                int R = 3 + depth / 6;
                if (staticEval - beta > 200) R++; // More aggressive when winning
                R = Math.Min(R, depth - 1);

                board.MakeNullMove();
                int nullScore = -AlphaBeta(board, depth - R, -beta, -beta + 1, ply + 1, true);
                board.UndoNullMove();

                if (nullScore >= beta)
                    return beta;
            }

            // Internal Iterative Deepening (IID)
            // If we don't have a TT move at high depth, do a shallow search first
            if (ttMove.From == ttMove.To && depth >= 6 && isPV)
            {
                AlphaBeta(board, depth - 4, alpha, beta, ply, false);
                ttMove = _tt.GetTTMove(board.ZobristHash);
            }

            Move[] moves = _moveStacks[ply];
            int moveCount = MoveGenerator.GenerateLegalMoves(board, moves);
            if (moveCount == 0)
            {
                return inCheck ? -MateScore + ply : 0;
            }

            OrderMoves(board, moves, moveCount, ttMove, ply);

            // Singular extension: if the TT move is significantly better than alternatives
            int singularExtension = 0;
            bool singularSearchInProgress = excludeMove.From != excludeMove.To;
            
            if (!singularSearchInProgress && depth >= 8 && ttMove.From != ttMove.To && !inCheck)
            {
                // Check if we have a valid TT entry for singular extension
                if (_tt.ProbeForSingular(board.ZobristHash, depth, out int ttEntryScore, out TTFlag ttFlag))
                {
                    if (ttFlag != TTFlag.Alpha) // Not a fail-low entry
                    {
                        int singularBeta = ttEntryScore - 3 * depth;
                        int singularScore = AlphaBeta(board, depth / 2, singularBeta - 1, singularBeta, ply, false, ttMove);
                        
                        if (singularScore < singularBeta)
                            singularExtension = 1; // TT move is singular, extend its search
                    }
                }
            }

            int originalAlpha = alpha;
            Move bestMove = moves[0];
            int movesSearched = 0;

            // Futility pruning margins
            int[] futilityMargins = { 0, 200, 400, 600 };
            bool canFutilityPrune = !inCheck && !isPV && depth <= 3 && staticEval + futilityMargins[depth] < alpha;

            // Store previous move for countermove updates
            Move prevMove = _previousMove;

            for (int i = 0; i < moveCount; i++)
            {
                Move move = moves[i];
                
                // Skip the excluded move (for singular extension search)
                if (singularSearchInProgress && MovesEqual(move, excludeMove))
                    continue;

                bool isQuiet = !move.IsCapture && !move.IsPromotion;
                bool isTTMove = MovesEqual(move, ttMove);

                // Late Move Pruning (LMP): skip late quiet moves entirely at low depth
                if (!isPV && !inCheck && depth <= 6 && movesSearched > 0 && isQuiet)
                {
                    if (depth < LMPThresholds.Length && movesSearched >= LMPThresholds[depth])
                        continue;
                }

                // Futility Pruning: skip quiet moves that can't possibly raise alpha
                if (canFutilityPrune && movesSearched > 0 && isQuiet)
                {
                    continue;
                }

                // SEE pruning for captures at low depth
                if (depth <= 2 && move.IsCapture && !move.IsPromotion && movesSearched > 0)
                {
                    int seeScore = SEE(board, move);
                    if (seeScore < 0)
                        continue;
                }

                _previousMove = move;
                board.MakeMove(move);

                int score;
                // Apply singular extension to TT move
                int extension = (isTTMove && singularExtension > 0) ? singularExtension : 0;
                int newDepth = depth - 1 + extension;

                // Late Move Reductions (LMR)
                int reduction = 0;
                if (depth >= 3 && movesSearched >= 4 && isQuiet && !inCheck)
                {
                    reduction = 1 + (depth / 4) + (movesSearched / 8);
                    reduction = Math.Min(reduction, depth - 2);
                }

                // PVS: search first move with full window, rest with null window
                if (movesSearched == 0)
                {
                    score = -AlphaBeta(board, newDepth, -beta, -alpha, ply + 1, false);
                }
                else
                {
                    // Reduced depth null window search
                    score = -AlphaBeta(board, newDepth - reduction, -alpha - 1, -alpha, ply + 1, false);

                    // Re-search at full depth if reduced search fails high
                    if (reduction > 0 && score > alpha)
                        score = -AlphaBeta(board, newDepth, -alpha - 1, -alpha, ply + 1, false);

                    // Re-search with full window if null window fails high
                    if (score > alpha && score < beta)
                        score = -AlphaBeta(board, newDepth, -beta, -alpha, ply + 1, false);
                }

                board.UndoMove(move);
                movesSearched++;

                if (score >= beta)
                {
                    _tt.Store(board.ZobristHash, depth, beta, TTFlag.Beta, move, ply);

                    // Store killer move if quiet
                    if (isQuiet && ply < MaxPly)
                    {
                        if (!MovesEqual(_killerMoves[ply, 0], move))
                        {
                            _killerMoves[ply, 1] = _killerMoves[ply, 0];
                            _killerMoves[ply, 0] = move;
                        }
                    }

                    // Update countermove heuristic
                    if (prevMove.From != prevMove.To && isQuiet)
                    {
                        Piece prevPiece = board.PieceAt(prevMove.To);
                        if (prevPiece != Piece.Empty)
                            _counterMoves[(int)prevPiece, prevMove.To] = move;
                    }

                    // Update history for quiet moves
                    if (isQuiet)
                    {
                        Piece piece = board.PieceAt(move.From);
                        if (piece == Piece.Empty)
                            piece = board.PieceAt(move.To);
                        _historyTable[(int)piece, move.To] += depth * depth;
                    }

                    _previousMove = prevMove;
                    return beta;
                }

                if (score > alpha)
                {
                    alpha = score;
                    bestMove = move;

                    // Update history for quiet moves that improve alpha
                    if (isQuiet)
                    {
                        Piece piece = board.PieceAt(move.From);
                        if (piece == Piece.Empty)
                            piece = board.PieceAt(move.To);
                        _historyTable[(int)piece, move.To] += depth;
                    }
                }
            }

            _previousMove = prevMove;
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
                Move[] moves = _moveStacks[qPly];
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

            // Use GenerateLegalCaptures directly - much faster than generating all moves
            int qPly2 = Math.Min(ply, MaxPly - 1);
            Move[] captureMoves = _moveStacks[qPly2];
            int noisyCount = MoveGenerator.GenerateLegalCaptures(board, captureMoves);

            if (noisyCount == 0)
                return alpha;

            OrderMoves(board, captureMoves, noisyCount, default, qPly2);

            // Delta pruning margin
            const int DeltaMargin = 200;

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

                // SEE pruning: skip losing captures
                if (move.IsCapture && !move.IsPromotion)
                {
                    int seeScore = SEE(board, move);
                    if (seeScore < 0)
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

            return alpha;
        }

        private static void CheckTime()
        {
            // Only check time every 2048 nodes to reduce overhead
            if ((++_nodeCount & 2047) != 0)
                return;

            if (_sw == null)
                return;

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

        private static void OrderMoves(Board board, Move[] moves, int moveCount, Move ttMove, int ply)
        {
            // Score all moves
            for (int i = 0; i < moveCount; i++)
                _moveScores[i] = ScoreMove(board, moves[i], ttMove, ply);

            // Lazy selection sort - only sort top 10 moves since cutoffs usually happen early
            int sortLimit = Math.Min(10, moveCount - 1);
            for (int i = 0; i < sortLimit; i++)
            {
                int bestIdx = i;
                int bestScore = _moveScores[i];
                for (int j = i + 1; j < moveCount; j++)
                {
                    if (_moveScores[j] > bestScore)
                    {
                        bestScore = _moveScores[j];
                        bestIdx = j;
                    }
                }
                if (bestIdx != i)
                {
                    // Swap moves and scores
                    (moves[i], moves[bestIdx]) = (moves[bestIdx], moves[i]);
                    (_moveScores[i], _moveScores[bestIdx]) = (_moveScores[bestIdx], _moveScores[i]);
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
                    if (MovesEqual(move, _killerMoves[ply, 0]))
                        return 400_000;
                    if (MovesEqual(move, _killerMoves[ply, 1]))
                        return 300_000;
                }

                // Countermove heuristic
                if (_previousMove.From != _previousMove.To)
                {
                    Piece prevPiece = board.PieceAt(_previousMove.To);
                    if (prevPiece != Piece.Empty && MovesEqual(move, _counterMoves[(int)prevPiece, _previousMove.To]))
                        return 200_000;
                }

                // History heuristic for quiet moves
                Piece piece = board.PieceAt(move.From);
                if (piece != Piece.Empty)
                    score += _historyTable[(int)piece, move.To];
            }

            if (move.IsCastling)
                score += 50;

            return score;
        }
    }
}