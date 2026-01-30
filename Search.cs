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

        private sealed class SearchTimeoutException : Exception { }

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

            List<Move> moves = MoveGenerator.GenerateLegalMoves(board);
            if (moves.Count == 0)
            {
                bool stmInCheck = board.IsKingInCheck(board.WhiteToMove);
                return (default, stmInCheck ? -MateScore : 0);
            }

            Move ttMove = _tt.GetTTMove(board.ZobristHash);
            OrderMoves(board, moves, ttMove, 0);

            Move bestMove = moves[0];
            int bestScore = -Infinity;
            int originalAlpha = alpha;

            for (int i = 0; i < moves.Count; i++)
            {
                CheckTime();

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

        private static int AlphaBeta(Board board, int depth, int alpha, int beta, int ply, bool isNullMove)
        {
            CheckTime();

            // Check extension: extend search when in check
            bool inCheck = board.IsKingInCheck(board.WhiteToMove);
            if (inCheck)
                depth++;

            if (depth <= 0)
                return Quiesce(board, alpha, beta, ply);

            if (_tt.Probe(board.ZobristHash, depth, alpha, beta, ply, out int ttScore, out Move ttMove))
                return ttScore;

            // Null move pruning
            if (!isNullMove && !inCheck && depth >= 3 && HasNonPawnMaterial(board))
            {
                board.MakeNullMove();
                int nullScore = -AlphaBeta(board, depth - 3, -beta, -beta + 1, ply + 1, true);
                board.UndoNullMove();

                if (nullScore >= beta)
                    return beta;
            }

            List<Move> moves = MoveGenerator.GenerateLegalMoves(board);
            if (moves.Count == 0)
            {
                return inCheck ? -MateScore + ply : 0;
            }

            OrderMoves(board, moves, ttMove, ply);

            int originalAlpha = alpha;
            Move bestMove = moves[0];
            int movesSearched = 0;

            for (int i = 0; i < moves.Count; i++)
            {
                CheckTime();

                Move move = moves[i];
                bool isQuiet = !move.IsCapture && !move.IsPromotion;

                board.MakeMove(move);

                int score;
                int newDepth = depth - 1;

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
                        // Don't store duplicate
                        if (!MovesEqual(_killerMoves[ply, 0], move))
                        {
                            _killerMoves[ply, 1] = _killerMoves[ply, 0];
                            _killerMoves[ply, 0] = move;
                        }
                    }

                    // Update history for quiet moves
                    if (isQuiet)
                    {
                        Piece piece = board.PieceAt(move.From);
                        if (piece == Piece.Empty)
                            piece = board.PieceAt(move.To); // piece already moved for hash
                        _historyTable[(int)piece, move.To] += depth * depth;
                    }

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

        private static int Quiesce(Board board, int alpha, int beta, int ply)
        {
            CheckTime();

            int standPat = Evaluator.Evaluate(board);

            if (standPat >= beta)
                return beta;
            if (standPat > alpha)
                alpha = standPat;

            List<Move> moves = MoveGenerator.GenerateLegalMoves(board);
            if (moves.Count == 0)
            {
                bool stmInCheck = board.IsKingInCheck(board.WhiteToMove);
                return stmInCheck ? -MateScore + ply : 0;
            }

            var noisyMoves = new List<Move>(moves.Count);
            for (int i = 0; i < moves.Count; i++)
            {
                Move m = moves[i];
                if (m.IsCapture || m.IsPromotion)
                    noisyMoves.Add(m);
            }

            if (noisyMoves.Count == 0)
                return alpha;

            OrderMoves(board, noisyMoves, default, ply);

            // Delta pruning margin
            const int DeltaMargin = 200;

            for (int i = 0; i < noisyMoves.Count; i++)
            {
                CheckTime();

                Move move = noisyMoves[i];

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

        private static void OrderMoves(Board board, List<Move> moves, Move ttMove, int ply)
        {
            moves.Sort((a, b) => ScoreMove(board, b, ttMove, ply).CompareTo(ScoreMove(board, a, ttMove, ply)));
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