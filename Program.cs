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

            for (int depth = 1; depth <= 4; depth++)
            {
                long nodes = Perft.Run(board, depth);
                Console.WriteLine($"Perft({depth}) = {nodes}");
            }

            Console.ReadLine();
        }
    }
}