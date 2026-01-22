using System;

namespace ChessEngine
{
    class Program
    {
        static void Main(string[] args)
        {
            Board board = new Board();

            for (int depth = 1; depth <= 4; depth++)
            {
                long nodes = Perft.Run(board, depth);
                Console.WriteLine($"Perft({depth}) = {nodes}");
            }

            Console.ReadLine();
        }
        // static void Main(string[] args)
        // {
        //     Board board = new Board();
        //     var moves = MoveGenerator.GenerateMoves(board);

        //     long total = 0;
        //     foreach (var move in moves)
        //     {
        //         board.MakeMove(move);
        //         long count = Perft.Run(board, 3);
        //         board.UndoMove(move);

        //         Console.WriteLine($"{move}: {count}");
        //         total += count;
        //     }

        //     Console.WriteLine($"Total = {total}");
        // }
    }
}