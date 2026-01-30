using System;
using System.Diagnostics;

namespace ChessEngine
{
    class Program
    {
        static void Main(string[] args)
        {
            Bitboard.Init();
            MagicBitboards.Init();

            if (args.Length > 0 && args[0] == "perft")
            {
                RunPerftTests();
                return;
            }

            Uci.Run();
        }

        static void RunPerftTests()
        {
            Console.WriteLine("Running Perft Tests...\n");

            var board = new Board();
            var sw = new Stopwatch();
            bool allPassed = true;

            Console.WriteLine("Position 1: Starting position");
            long[] expected1 = { 1, 20, 400, 8902, 197281, 4865609 };
            allPassed &= RunPerftSuite(board, expected1, sw);

            Console.WriteLine("\nPosition 2: Kiwipete");
            Fen.Load(board, "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1");
            long[] expected2 = { 1, 48, 2039, 97862, 4085603 };
            allPassed &= RunPerftSuite(board, expected2, sw);

            Console.WriteLine("\nPosition 3: En passant test");
            Fen.Load(board, "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1");
            long[] expected3 = { 1, 6, 264, 9467, 422333 };
            allPassed &= RunPerftSuite(board, expected3, sw);

            Console.WriteLine($"\n{(allPassed ? "All tests PASSED!" : "Some tests FAILED!")}");

            Console.WriteLine("\nPerformance Benchmark (Starting position depth 5):");
            board.Reset();
            sw.Restart();
            var result = Perft.Run(board, 5);
            sw.Stop();
            Console.WriteLine($"  Nodes: {result.Nodes}");
            Console.WriteLine($"  Time: {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"  NPS: {result.Nodes / (sw.ElapsedMilliseconds / 1000.0):N0}");
        }

        static bool RunPerftSuite(Board board, long[] expected, Stopwatch sw)
        {
            bool passed = true;
            for (int depth = 0; depth < expected.Length; depth++)
            {
                sw.Restart();
                var result = Perft.Run(board, depth);
                sw.Stop();

                bool ok = result.Nodes == expected[depth];
                Console.WriteLine($"  Perft({depth}) = {result.Nodes,10} (expected {expected[depth],10}) [{(ok ? "PASS" : "FAIL")}] - {sw.ElapsedMilliseconds}ms");
                if (!ok) passed = false;
            }
            return passed;
        }
    }
}
