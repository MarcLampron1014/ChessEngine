
namespace ChessEngine{
    public static class Perft
    {
        public static long Run(Board board, int depth)
        {
            if (depth == 0)
                return 1;

            long nodes = 0;
            List<Move> moves = MoveGenerator.GenerateLegalMoves(board);
            

            foreach (var move in moves)
            {
                board.MakeMove(move);
                nodes += Run(board, depth - 1);
                board.UndoMove(move);
            }

            return nodes;
        }
        
    }
}