using System;
using System.Diagnostics;

namespace ChessEngine
{
    class Program
    {
        static void Main(string[] args)
        {
            // Initialize bitboard tables (must be called before using the engine)
            Bitboard.Init();
            MagicBitboards.Init();

            // Check for perft test mode
            if (args.Length > 0 && args[0] == "perft")
            {
                RunPerftTests();
                return;
            }

            // UCI loop for Arena (stdin/stdout).
            Uci.Run();
        }

        static void RunPerftTests()
        {
            Console.WriteLine("Running Perft Tests...\n");

            // Test position 1: Starting position
            Console.WriteLine("Position 1: Starting position");
            Board board = new Board();
            
            // Expected perft values for starting position
            long[] expected = { 1, 20, 400, 8902, 197281, 4865609 };
            
            var sw = Stopwatch.StartNew();
            bool allPassed = true;

            for (int depth = 0; depth <= 5; depth++)
            {
                sw.Restart();
                var result = Perft.Run(board, depth);
                sw.Stop();
                
                bool passed = result.Nodes == expected[depth];
                string status = passed ? "PASS" : "FAIL";
                Console.WriteLine($"  Perft({depth}) = {result.Nodes,10} (expected {expected[depth],10}) [{status}] - {sw.ElapsedMilliseconds}ms");
                
                if (!passed) allPassed = false;
            }

            // Test position 2: Kiwipete (complex position)
            Console.WriteLine("\nPosition 2: Kiwipete");
            Fen.Load(board, "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1");
            
            long[] expectedKiwipete = { 1, 48, 2039, 97862, 4085603 };
            
            for (int depth = 0; depth <= 4; depth++)
            {
                sw.Restart();
                var result = Perft.Run(board, depth);
                sw.Stop();
                
                bool passed = result.Nodes == expectedKiwipete[depth];
                string status = passed ? "PASS" : "FAIL";
                Console.WriteLine($"  Perft({depth}) = {result.Nodes,10} (expected {expectedKiwipete[depth],10}) [{status}] - {sw.ElapsedMilliseconds}ms");
                
                if (!passed) allPassed = false;
            }

            // Test position 3: Position with en passant
            Console.WriteLine("\nPosition 3: En passant test");
            Fen.Load(board, "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1");
            
            long[] expectedEP = { 1, 6, 264, 9467, 422333 };
            
            for (int depth = 0; depth <= 4; depth++)
            {
                sw.Restart();
                var result = Perft.Run(board, depth);
                sw.Stop();
                
                bool passed = result.Nodes == expectedEP[depth];
                string status = passed ? "PASS" : "FAIL";
                Console.WriteLine($"  Perft({depth}) = {result.Nodes,10} (expected {expectedEP[depth],10}) [{status}] - {sw.ElapsedMilliseconds}ms");
                
                if (!passed) allPassed = false;
            }

            Console.WriteLine($"\n{(allPassed ? "All tests PASSED!" : "Some tests FAILED!")}");

            // Performance benchmark
            Console.WriteLine("\nPerformance Benchmark (Starting position depth 5):");
            board.Reset();
            sw.Restart();
            var benchResult = Perft.Run(board, 5);
            sw.Stop();
            double nps = benchResult.Nodes / (sw.ElapsedMilliseconds / 1000.0);
            Console.WriteLine($"  Nodes: {benchResult.Nodes}");
            Console.WriteLine($"  Time: {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"  NPS: {nps:N0}");
        }
    }
}
