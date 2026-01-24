using System;

namespace ChessEngine
{
    class Program
    {
        // static void Main(string[] args)
        // {
        //     Board board = new Board();

        //     for (int depth = 1; depth <= 4; depth++)
        //     {
        //         long nodes = Perft.Run(board, depth);
        //         Console.WriteLine($"Perft({depth}) = {nodes}");
        //     }

        //     Console.ReadLine();
        // }
        static void Main(string[] args)
        {
            Board board = new Board();

            for (int depth = 1; depth <= 5; depth++)
            {
                var result = Perft.Run(board, depth);
                Console.WriteLine($"Perft({depth}) = {result.Nodes}");
                Console.WriteLine($"  Captures: {result.Captures}");
                Console.WriteLine($"  Checks: {result.Checks}");
                Console.WriteLine($"  Checkmates: {result.Checkmates}");
                Console.WriteLine();
            }

            Console.ReadLine();
        }
    }
}