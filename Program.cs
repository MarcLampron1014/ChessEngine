using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

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

            if (args.Length > 0 && args[0] == "convert")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: ChessEngine convert <input.csv> [output.txt] [max_positions]");
                    Console.WriteLine("  Converts CSV (FEN, result columns) to FEN;result format for tuning.");
                    Console.WriteLine("  output.txt defaults to positions.txt; max_positions limits rows (default: all).");
                    return;
                }

                string csvPath = args[1];
                string outPath = args.Length > 2 ? args[2] : "positions.txt";
                int? maxPos = null;
                if (args.Length > 3 && int.TryParse(args[3], out int mp) && mp > 0)
                    maxPos = mp;

                if (!File.Exists(csvPath))
                {
                    Console.WriteLine($"File not found: {csvPath}");
                    return;
                }

                Console.WriteLine($"Converting {csvPath} -> {outPath} ...");
                int count = Tuner.ConvertCsvToPositions(csvPath, outPath, maxPos);
                Console.WriteLine($"Wrote {count} positions to {outPath}");
                return;
            }

            if (args.Length > 0 && args[0] == "tune")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: ChessEngine tune <dataset_file> [iterations] [max_positions] [tune_subset_size]");
                    Console.WriteLine("  dataset_file: .csv (fen,result) or .txt (FEN;result) — format auto-detected");
                    Console.WriteLine("  iterations: Max tuning iterations (default 100)");
                    Console.WriteLine("  max_positions: Optional cap (e.g. 500000) for large files");
                    Console.WriteLine("  tune_subset_size: Optional subset for fast param steps (e.g. 50000); full set for iteration/convergence error");
                    return;
                }

                string posFile = args[1];
                int maxIter = args.Length > 2 && int.TryParse(args[2], out int i) ? i : 100;
                int? maxPos = null;
                int? tuneSubset = null;
                if (args.Length > 3 && int.TryParse(args[3], out int mp) && mp > 0)
                    maxPos = mp;
                if (args.Length > 4 && int.TryParse(args[4], out int ts) && ts > 0)
                    tuneSubset = ts;
                Tuner.RunTuning(posFile, maxIter, maxPos, tuneSubset);
                return;
            }

            if (args.Length > 0 && args[0] == "eval-error")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: ChessEngine eval-error <positions_file>");
                    return;
                }

                Tuner.EvaluateError(args[1]);
                return;
            }

            if (args.Length > 0 && args[0] == "save-params")
            {
                string path = args.Length > 1 ? args[1] : "eval_params.json";
                EvalParams.SaveToFile(path);
                Console.WriteLine($"Parameters saved to {path}");
                return;
            }

            if (args.Length > 0 && args[0] == "load-params")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: ChessEngine load-params <params_file>");
                    return;
                }

                EvalParams.LoadFromFile(args[1]);
                Console.WriteLine($"Parameters loaded from {args[1]}");
                Tuner.PrintParameters();
                return;
            }

            if (args.Length > 0 && args[0] == "lichess")
            {
                var token = LichessBot.LoadToken();
                if (string.IsNullOrEmpty(token))
                {
                    Console.Error.WriteLine("Lichess token not found. Set LICHESS_BOT_TOKEN or create lichess_token.txt (see lichess_token.example.txt).");
                    Environment.Exit(1);
                }
                LichessBot.Run(token);
                return;
            }

            // Load eval params from file if exists
            if (File.Exists("eval_params.json"))
            {
                EvalParams.LoadFromFile("eval_params.json");
                // #region agent log
                try
                {
                    var logPath = @"c:\Users\marcl\ChessEngine\.cursor\debug.log";
                    Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                    var p = EvalParams.Instance;
                    var line = JsonSerializer.Serialize(new { sessionId = "debug-session", runId = "post-fix", hypothesisId = "H1", location = "Program.cs:118", message = "params after load", data = new { TotalPhase = p.TotalPhase, PawnValueEG = p.PawnValueEG, PawnValueMG = p.PawnValueMG, PawnPstMGLength = p.PawnPstMG?.Length ?? -1 }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }) + "\n";
                    File.AppendAllText(logPath, line);
                }
                catch { }
                // #endregion
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
