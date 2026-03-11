using System;

namespace ChessEngine
{
    public sealed class SearchTimeoutException : Exception { }

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
}
