using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ChessEngine
{
    public static class Search
    {
        // Large values in centipawns.
        private const int MateScore = 100000;
        private const int Infinity = 1_000_000;

        // If time runs out mid-search, we throw internally to unwind quickly.
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

        // Entry point: iterative deepening until time budget is hit.
        public static SearchResult FindBestMove(Board board, int timeMs, int maxDepth = 64)
        {
            if (timeMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeMs));
            if (maxDepth <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxDepth));

            var sw = Stopwatch.StartNew();

            // Fallback: if search fails early, return first legal move if any.
            List<Move> rootMoves = MoveGenerator.GenerateLegalMoves(board);
            if (rootMoves.Count == 0)
                return new SearchResult(default, 0, 0);

            Move bestMoveOverall = rootMoves[0];
            int bestScoreOverall = 0;
            int depthReached = 0;

            // Iterative deepening. Keep last fully-completed depth.
            for (int depth = 1; depth <= maxDepth; depth++)
            {
                try
                {
                    var (bestMoveAtDepth, bestScoreAtDepth) = SearchRoot(board, depth, sw, timeMs);
                    bestMoveOverall = bestMoveAtDepth;
                    bestScoreOverall = bestScoreAtDepth;
                    depthReached = depth;
                }
                catch (SearchTimeoutException)
                {
                    break;
                }
            }

            return new SearchResult(bestMoveOverall, bestScoreOverall, depthReached);
        }

        private static (Move bestMove, int bestScore) SearchRoot(Board board, int depth, Stopwatch sw, int timeMs)
        {
            CheckTime(sw, timeMs);

            List<Move> moves = MoveGenerator.GenerateLegalMoves(board);
            if (moves.Count == 0)
            {
                // Terminal at root.
                bool stmInCheck = board.IsKingInCheck(board.WhiteToMove);
                int score = stmInCheck ? -MateScore : 0;
                return (default, score);
            }

            // Order moves before searching.
            OrderMoves(board, moves);

            Move bestMove = moves[0];
            int bestScore = -Infinity;
            int alpha = -Infinity;
            int beta = Infinity;

            for (int i = 0; i < moves.Count; i++)
            {
                CheckTime(sw, timeMs);

                Move move = moves[i];
                board.MakeMove(move);

                int score = -AlphaBeta(board, depth - 1, -beta, -alpha, 1, sw, timeMs);

                board.UndoMove(move);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMove = move;
                }

                if (score > alpha)
                    alpha = score;
            }

            return (bestMove, bestScore);
        }

        private static int AlphaBeta(Board board, int depth, int alpha, int beta, int ply, Stopwatch sw, int timeMs)
        {
            CheckTime(sw, timeMs);

            if (depth <= 0)
            {
                return Quiesce(board, alpha, beta, ply, sw, timeMs);
            }

            List<Move> moves = MoveGenerator.GenerateLegalMoves(board);
            if (moves.Count == 0)
            {
                // No legal moves: mate or stalemate
                bool stmInCheck = board.IsKingInCheck(board.WhiteToMove);
                if (stmInCheck)
                {
                    // Prefer faster mates: losing side gets more negative with ply.
                    return -MateScore + ply;
                }
                return 0;
            }

            OrderMoves(board, moves);

            for (int i = 0; i < moves.Count; i++)
            {
                CheckTime(sw, timeMs);

                Move move = moves[i];
                board.MakeMove(move);

                int score = -AlphaBeta(board, depth - 1, -beta, -alpha, ply + 1, sw, timeMs);

                board.UndoMove(move);

                if (score >= beta)
                    return beta;

                if (score > alpha)
                    alpha = score;
            }

            return alpha;
        }

        private static int Quiesce(Board board, int alpha, int beta, int ply, Stopwatch sw, int timeMs)
        {
            CheckTime(sw, timeMs);

            int standPat = EvaluateForSideToMove(board);

            if (standPat >= beta)
                return beta;
            if (standPat > alpha)
                alpha = standPat;

            List<Move> moves = MoveGenerator.GenerateLegalMoves(board);
            if (moves.Count == 0)
            {
                // Terminal: mate or stalemate
                bool stmInCheck = board.IsKingInCheck(board.WhiteToMove);
                if (stmInCheck)
                    return -MateScore + ply;
                return 0;
            }

            // Captures only (and promotions).
            var noisyMoves = new List<Move>(moves.Count);
            for (int i = 0; i < moves.Count; i++)
            {
                Move m = moves[i];
                if (m.IsCapture || m.IsPromotion)
                    noisyMoves.Add(m);
            }

            if (noisyMoves.Count == 0)
                return alpha;

            OrderMoves(board, noisyMoves);

            for (int i = 0; i < noisyMoves.Count; i++)
            {
                CheckTime(sw, timeMs);

                Move move = noisyMoves[i];
                board.MakeMove(move);

                int score = -Quiesce(board, -beta, -alpha, ply + 1, sw, timeMs);

                board.UndoMove(move);

                if (score >= beta)
                    return beta;
                if (score > alpha)
                    alpha = score;
            }

            return alpha;
        }

        private static int EvaluateForSideToMove(Board board)
        {
            // Evaluator is White-centric. Convert to side-to-move perspective for negamax.
            int eval = Evaluator.Evaluate(board);
            return board.WhiteToMove ? eval : -eval;
        }

        private static void CheckTime(Stopwatch sw, int timeMs)
        {
            if (sw.ElapsedMilliseconds >= timeMs)
                throw new SearchTimeoutException();
        }

        private static void OrderMoves(Board board, List<Move> moves)
        {
            moves.Sort((a, b) => ScoreMove(board, b).CompareTo(ScoreMove(board, a)));
        }

        private static int ScoreMove(Board board, Move move)
        {
            // Higher is better. Simple ordering:
            // Promotions > captures (MVV-LVA-ish) > quiet.
            int score = 0;

            if (move.IsPromotion)
                score += 1_000_000 + Evaluator.GetPieceValue(move.Promotion);

            if (move.IsCapture)
            {
                Piece attacker = board.Squares[move.From];
                Piece victim;

                if (move.IsEnPassant)
                {
                    victim = board.WhiteToMove ? Piece.BP : Piece.WP;
                }
                else
                {
                    victim = board.Squares[move.To];
                }

                int victimValue = Evaluator.GetPieceValue(victim);
                int attackerValue = Evaluator.GetPieceValue(attacker);

                // Prioritize winning captures.
                score += 500_000 + (victimValue * 10) - attackerValue;
            }

            // Small bias for castling (helps choose it a bit earlier).
            if (move.IsCastling)
                score += 50;

            return score;
        }
    }
}