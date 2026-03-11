using System.Collections.Generic;

namespace ChessEngine
{
    public struct PerftResult
    {
        public long Nodes;
        public long Captures;
        public long Checks;
        public long Checkmates;

        public static PerftResult operator +(PerftResult a, PerftResult b) => new PerftResult
        {
            Nodes = a.Nodes + b.Nodes,
            Captures = a.Captures + b.Captures,
            Checks = a.Checks + b.Checks,
            Checkmates = a.Checkmates + b.Checkmates
        };
    }

    public static class Perft
    {
        public static PerftResult Run(Board board, int depth)
        {
            if (depth == 0)
                return new PerftResult { Nodes = 1 };

            var result = new PerftResult();
            List<Move> moves = MoveGenerator.GenerateLegalMoves(board);

            foreach (var move in moves)
            {
                if (move.IsCapture)
                    result.Captures++;

                board.MakeMove(move);

                bool opponentInCheck = board.IsKingInCheck(board.WhiteToMove);
                if (opponentInCheck)
                {
                    result.Checks++;
                    if (MoveGenerator.GenerateLegalMoves(board).Count == 0)
                        result.Checkmates++;
                }

                PerftResult subResult = Run(board, depth - 1);
                result.Nodes += subResult.Nodes;
                result.Captures += subResult.Captures;
                result.Checks += subResult.Checks;
                result.Checkmates += subResult.Checkmates;

                board.UndoMove(move);
            }

            return result;
        }
    }
}