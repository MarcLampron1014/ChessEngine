using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ChessEngine
{
    /// <summary>
    /// Represents a position with its game result for tuning.
    /// </summary>
    public struct TuningPosition
    {
        public string Fen;
        public double Result; // 1.0 = white win, 0.5 = draw, 0.0 = black win
    }

    /// <summary>
    /// Texel-style tuning for evaluation parameters.
    /// Minimizes the error between sigmoid(eval) and actual game result.
    /// </summary>
    public static class Tuner
    {
        // Sigmoid scaling factor (higher = sharper sigmoid)
        private const double DefaultK = 1.13;
        private static double K = DefaultK;

        // Learning rate for gradient descent
        private const double LearningRate = 1.0;

        // Convergence threshold
        private const double ConvergenceThreshold = 1e-6;

        /// <summary>
        /// Loads positions from a file, auto-detecting format.
        /// Supports: CSV (fen,result or similar), FEN;result, or FEN [result].
        /// Use this for tune/eval-error so .csv and .txt datasets work without a convert step.
        /// </summary>
        public static List<TuningPosition> LoadPositionsFromFile(string path, int? maxPositions = null)
        {
            if (!File.Exists(path))
                return new List<TuningPosition>();

            string? firstLine = null;
            using (var r = new StreamReader(path))
            {
                while ((firstLine = r.ReadLine()) != null && string.IsNullOrWhiteSpace(firstLine)) { }
            }

            if (string.IsNullOrWhiteSpace(firstLine))
                return new List<TuningPosition>();

            // CSV: comma-separated with header like "fen,result" or data line with FEN,result
            if (firstLine.Contains(','))
            {
                string[] cells = ParseCsvLine(firstLine);
                if (cells != null && cells.Length >= 2 &&
                    (LooksLikeHeader(cells) || (LooksLikeFen(cells[0].Trim()) && TryParseResult(cells[1].Trim(), out _))))
                    return LoadPositionsFromCsv(path, maxPositions);
            }

            // Otherwise FEN;result or FEN [result] format
            return LoadPositions(path);
        }

        /// <summary>
        /// Loads tuning positions from a file (FEN;result or FEN [result] format only).
        /// For CSV or auto-detect, use LoadPositionsFromFile.
        /// </summary>
        public static List<TuningPosition> LoadPositions(string path)
        {
            var positions = new List<TuningPosition>();

            foreach (string line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                TuningPosition pos = default;

                // Try parsing "FEN;result" format
                if (line.Contains(';'))
                {
                    string[] parts = line.Split(';');
                    if (parts.Length >= 2)
                    {
                        pos.Fen = parts[0].Trim();
                        if (double.TryParse(parts[1].Trim(), out double result))
                            pos.Result = result;
                        else
                            continue;
                    }
                }
                // Try parsing "FEN [result]" format
                else if (line.Contains("["))
                {
                    int bracketStart = line.IndexOf('[');
                    int bracketEnd = line.IndexOf(']');
                    if (bracketStart > 0 && bracketEnd > bracketStart)
                    {
                        pos.Fen = line.Substring(0, bracketStart).Trim();
                        string resultStr = line.Substring(bracketStart + 1, bracketEnd - bracketStart - 1).Trim();
                        pos.Result = ParseResult(resultStr);
                    }
                }
                // Try parsing with result at end like "1-0" or "0-1" or "1/2-1/2"
                else
                {
                    // Check for common result patterns at the end
                    string trimmed = line.Trim();
                    if (trimmed.EndsWith("1-0"))
                    {
                        pos.Fen = trimmed.Substring(0, trimmed.Length - 3).Trim();
                        pos.Result = 1.0;
                    }
                    else if (trimmed.EndsWith("0-1"))
                    {
                        pos.Fen = trimmed.Substring(0, trimmed.Length - 3).Trim();
                        pos.Result = 0.0;
                    }
                    else if (trimmed.EndsWith("1/2-1/2"))
                    {
                        pos.Fen = trimmed.Substring(0, trimmed.Length - 7).Trim();
                        pos.Result = 0.5;
                    }
                    else
                    {
                        continue; // Skip lines we can't parse
                    }
                }

                if (!string.IsNullOrEmpty(pos.Fen))
                    positions.Add(pos);
            }

            return positions;
        }

        private static double ParseResult(string s)
        {
            return s switch
            {
                "1-0" => 1.0,
                "0-1" => 0.0,
                "1/2-1/2" => 0.5,
                "1" => 1.0,
                "0" => 0.0,
                "0.5" => 0.5,
                _ => 0.5
            };
        }

        /// <summary>
        /// Converts a CSV tuning dataset to positions.txt format (FEN;result per line).
        /// Auto-detects FEN and result columns. Handles header row and quoted fields.
        /// </summary>
        public static int ConvertCsvToPositions(string csvPath, string outputPath, int? maxPositions = null)
        {
            var positions = LoadPositionsFromCsv(csvPath, maxPositions);
            using (var writer = new StreamWriter(outputPath))
            {
                foreach (var pos in positions)
                    writer.WriteLine($"{pos.Fen};{pos.Result}");
            }
            return positions.Count;
        }

        /// <summary>
        /// Loads positions from a CSV file. Detects FEN and result columns automatically.
        /// </summary>
        public static List<TuningPosition> LoadPositionsFromCsv(string csvPath, int? maxPositions = null)
        {
            var positions = new List<TuningPosition>();
            string[]? header = null;
            int fenCol = -1, resultCol = -1;
            int lineCount = 0;

            foreach (string line in File.ReadLines(csvPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                lineCount++;

                string[] cells = ParseCsvLine(line);
                if (cells == null || cells.Length == 0) continue;

                if (header == null)
                {
                    if (LooksLikeHeader(cells))
                    {
                        header = cells;
                        fenCol = FindFenColumn(header);
                        resultCol = FindResultColumn(header);
                        if (fenCol < 0 || resultCol < 0)
                        {
                            fenCol = FindFenColumnByContent(cells);
                            resultCol = FindResultColumnByContent(cells);
                        }
                        continue;
                    }
                    header = new string[cells.Length];
                    fenCol = FindFenColumnByContent(cells);
                    resultCol = FindResultColumnByContent(cells);
                }

                if (fenCol < 0 || fenCol >= cells.Length || resultCol < 0 || resultCol >= cells.Length)
                    continue;

                string fen = cells[fenCol].Trim();
                if (string.IsNullOrEmpty(fen) || !LooksLikeFen(fen)) continue;

                if (!TryParseResult(cells[resultCol].Trim(), out double result))
                    continue;

                positions.Add(new TuningPosition { Fen = fen, Result = result });
                if (maxPositions.HasValue && positions.Count >= maxPositions.Value)
                    break;
            }

            return positions;
        }

        private static string[] ParseCsvLine(string line)
        {
            var list = new List<string>();
            int i = 0;
            while (i < line.Length)
            {
                if (line[i] == '"')
                {
                    i++;
                    int start = i;
                    while (i < line.Length && (line[i] != '"' || (i + 1 < line.Length && line[i + 1] == '"')))
                    {
                        if (line[i] == '"') i += 2;
                        else i++;
                    }
                    list.Add(line.Substring(start, i - start).Replace("\"\"", "\""));
                    if (i < line.Length) i++;
                }
                else
                {
                    int start = i;
                    while (i < line.Length && line[i] != ',') i++;
                    list.Add(line.Substring(start, i - start));
                }
                if (i < line.Length && line[i] == ',') i++;
            }
            return list.ToArray();
        }

        private static bool LooksLikeHeader(string[] cells)
        {
            foreach (var c in cells)
            {
                string s = c.Trim().ToLowerInvariant();
                if (s == "fen" || s == "result" || s == "score" || s == "outcome") return true;
            }
            return false;
        }

        private static bool LooksLikeFen(string s)
        {
            return s.Length > 10 && (s.Contains(" w ") || s.Contains(" b ")) && s.Contains("/");
        }

        private static int FindFenColumn(string[] header)
        {
            for (int i = 0; i < header.Length; i++)
            {
                string h = header[i].Trim().ToLowerInvariant();
                if (h == "fen") return i;
            }
            return -1;
        }

        private static int FindResultColumn(string[] header)
        {
            for (int i = 0; i < header.Length; i++)
            {
                string h = header[i].Trim().ToLowerInvariant();
                if (h == "result" || h == "score" || h == "outcome") return i;
            }
            return -1;
        }

        private static int FindFenColumnByContent(string[] cells)
        {
            for (int i = 0; i < cells.Length; i++)
                if (LooksLikeFen(cells[i].Trim())) return i;
            return -1;
        }

        private static int FindResultColumnByContent(string[] cells)
        {
            for (int i = 0; i < cells.Length; i++)
                if (TryParseResult(cells[i].Trim(), out _)) return i;
            return -1;
        }

        private static bool TryParseResult(string s, out double result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double r)
                && (r == 0 || r == 0.5 || r == 1))
            {
                result = r;
                return true;
            }
            string t = s.Trim();
            if (t == "1-0") { result = 1.0; return true; }
            if (t == "0-1") { result = 0.0; return true; }
            if (t == "1/2-1/2") { result = 0.5; return true; }
            return false;
        }

        /// <summary>
        /// Sigmoid function for converting eval to expected result.
        /// </summary>
        private static double Sigmoid(double eval)
        {
            return 1.0 / (1.0 + Math.Pow(10.0, -K * eval / 400.0));
        }

        /// <summary>
        /// Computes mean squared error for current parameters on given positions.
        /// </summary>
        public static double ComputeError(List<TuningPosition> positions)
        {
            double totalError = 0;
            var board = new Board();

            foreach (var pos in positions)
            {
                Fen.Load(board, pos.Fen);
                int eval = Evaluator.Evaluate(board);
                double predicted = Sigmoid(eval);
                double error = pos.Result - predicted;
                totalError += error * error;
            }

            return totalError / positions.Count;
        }

        /// <summary>
        /// Computes error in parallel for better performance.
        /// </summary>
        public static double ComputeErrorParallel(List<TuningPosition> positions)
        {
            double totalError = 0;
            object lockObj = new object();

            Parallel.ForEach(positions, 
                () => (new Board(), 0.0), // local state: board + error accumulator
                (pos, state, local) =>
                {
                    var (board, localError) = local;
                    Fen.Load(board, pos.Fen);
                    int eval = Evaluator.Evaluate(board);
                    double predicted = Sigmoid(eval);
                    double error = pos.Result - predicted;
                    return (board, localError + error * error);
                },
                local =>
                {
                    lock (lockObj)
                    {
                        totalError += local.Item2;
                    }
                });

            return totalError / positions.Count;
        }

        /// <summary>
        /// Finds optimal K value for sigmoid scaling.
        /// </summary>
        public static double OptimizeK(List<TuningPosition> positions)
        {
            Console.WriteLine("Optimizing K...");

            double bestK = 1.0;
            double bestError = double.MaxValue;

            // Search K from 0.5 to 2.0 in steps of 0.01
            for (double testK = 0.5; testK <= 2.0; testK += 0.01)
            {
                K = testK;
                Evaluator.ClearCache(); // Clear cache since we changed K
                double error = ComputeErrorParallel(positions);
                if (error < bestError)
                {
                    bestError = error;
                    bestK = testK;
                }
            }

            K = bestK;
            Console.WriteLine($"Optimal K = {bestK:F4}, Error = {bestError:F8}");
            return bestK;
        }

        /// <summary>
        /// Tunes a single integer parameter using local search.
        /// Returns the new optimal value.
        /// </summary>
        public static int TuneParameter(List<TuningPosition> positions, 
            Func<int> getter, Action<int> setter, string name, int step = 1)
        {
            int current = getter();
            double currentError = ComputeErrorParallel(positions);

            // Try increasing
            setter(current + step);
            Evaluator.ClearCache();
            double errorPlus = ComputeErrorParallel(positions);

            // Try decreasing
            setter(current - step);
            Evaluator.ClearCache();
            double errorMinus = ComputeErrorParallel(positions);

            // Choose best
            if (errorPlus < currentError && errorPlus <= errorMinus)
            {
                setter(current + step);
                Evaluator.ClearCache();
                Console.WriteLine($"  {name}: {current} -> {current + step} (error: {errorPlus:F8})");
                return current + step;
            }
            else if (errorMinus < currentError)
            {
                // Already set to current - step
                Console.WriteLine($"  {name}: {current} -> {current - step} (error: {errorMinus:F8})");
                return current - step;
            }
            else
            {
                // Keep original
                setter(current);
                Evaluator.ClearCache();
                return current;
            }
        }

        /// <summary>
        /// Runs coordinate descent optimization on all tunable parameters.
        /// Accepts .csv, .txt, or any file; format is auto-detected (CSV or FEN;result).
        /// </summary>
        public static void RunTuning(string positionsFile, int maxIterations = 100, int? maxPositions = null)
        {
            Console.WriteLine("Loading positions...");
            var positions = LoadPositionsFromFile(positionsFile, maxPositions);
            Console.WriteLine($"Loaded {positions.Count} positions.");

            if (positions.Count == 0)
            {
                Console.WriteLine("No positions loaded. Check file format.");
                return;
            }

            // First, optimize K
            OptimizeK(positions);

            double previousError = ComputeErrorParallel(positions);
            Console.WriteLine($"Initial error: {previousError:F8}");

            var p = EvalParams.Instance;

            for (int iter = 0; iter < maxIterations; iter++)
            {
                Console.WriteLine($"\n=== Iteration {iter + 1} ===");

                // Tune piece values
                TuneParameter(positions, () => p.PawnValueMG, v => p.PawnValueMG = v, "PawnValueMG", 5);
                TuneParameter(positions, () => p.KnightValueMG, v => p.KnightValueMG = v, "KnightValueMG", 5);
                TuneParameter(positions, () => p.BishopValueMG, v => p.BishopValueMG = v, "BishopValueMG", 5);
                TuneParameter(positions, () => p.RookValueMG, v => p.RookValueMG = v, "RookValueMG", 5);
                TuneParameter(positions, () => p.QueenValueMG, v => p.QueenValueMG = v, "QueenValueMG", 10);

                TuneParameter(positions, () => p.PawnValueEG, v => p.PawnValueEG = v, "PawnValueEG", 5);
                TuneParameter(positions, () => p.KnightValueEG, v => p.KnightValueEG = v, "KnightValueEG", 5);
                TuneParameter(positions, () => p.BishopValueEG, v => p.BishopValueEG = v, "BishopValueEG", 5);
                TuneParameter(positions, () => p.RookValueEG, v => p.RookValueEG = v, "RookValueEG", 5);
                TuneParameter(positions, () => p.QueenValueEG, v => p.QueenValueEG = v, "QueenValueEG", 10);

                // Tune bonuses/penalties
                TuneParameter(positions, () => p.BishopPairBonusMG, v => p.BishopPairBonusMG = v, "BishopPairBonusMG");
                TuneParameter(positions, () => p.BishopPairBonusEG, v => p.BishopPairBonusEG = v, "BishopPairBonusEG");
                TuneParameter(positions, () => p.RookOpenFileBonusMG, v => p.RookOpenFileBonusMG = v, "RookOpenFileBonusMG");
                TuneParameter(positions, () => p.RookOpenFileBonusEG, v => p.RookOpenFileBonusEG = v, "RookOpenFileBonusEG");
                TuneParameter(positions, () => p.RookSemiOpenFileBonusMG, v => p.RookSemiOpenFileBonusMG = v, "RookSemiOpenFileBonusMG");
                TuneParameter(positions, () => p.RookSemiOpenFileBonusEG, v => p.RookSemiOpenFileBonusEG = v, "RookSemiOpenFileBonusEG");
                TuneParameter(positions, () => p.DoubledPawnPenaltyMG, v => p.DoubledPawnPenaltyMG = v, "DoubledPawnPenaltyMG");
                TuneParameter(positions, () => p.DoubledPawnPenaltyEG, v => p.DoubledPawnPenaltyEG = v, "DoubledPawnPenaltyEG");
                TuneParameter(positions, () => p.IsolatedPawnPenaltyMG, v => p.IsolatedPawnPenaltyMG = v, "IsolatedPawnPenaltyMG");
                TuneParameter(positions, () => p.IsolatedPawnPenaltyEG, v => p.IsolatedPawnPenaltyEG = v, "IsolatedPawnPenaltyEG");
                TuneParameter(positions, () => p.MobilityBonusMG, v => p.MobilityBonusMG = v, "MobilityBonusMG");
                TuneParameter(positions, () => p.MobilityBonusEG, v => p.MobilityBonusEG = v, "MobilityBonusEG");
                TuneParameter(positions, () => p.QueenMobilityBonusMG, v => p.QueenMobilityBonusMG = v, "QueenMobilityBonusMG");
                TuneParameter(positions, () => p.QueenMobilityBonusEG, v => p.QueenMobilityBonusEG = v, "QueenMobilityBonusEG");
                TuneParameter(positions, () => p.KingShieldBonus, v => p.KingShieldBonus = v, "KingShieldBonus");
                TuneParameter(positions, () => p.KingOpenFilePenalty, v => p.KingOpenFilePenalty = v, "KingOpenFilePenalty");
                TuneParameter(positions, () => p.RookBehindPasserBonus, v => p.RookBehindPasserBonus = v, "RookBehindPasserBonus");
                TuneParameter(positions, () => p.KnightOutpostBonusMG, v => p.KnightOutpostBonusMG = v, "KnightOutpostBonusMG");
                TuneParameter(positions, () => p.KnightOutpostBonusEG, v => p.KnightOutpostBonusEG = v, "KnightOutpostBonusEG");

                // Tune endgame weights
                TuneParameter(positions, () => p.KingOwnPasserProximity, v => p.KingOwnPasserProximity = v, "KingOwnPasserProximity");
                TuneParameter(positions, () => p.KingEnemyPasserProximity, v => p.KingEnemyPasserProximity = v, "KingEnemyPasserProximity");
                TuneParameter(positions, () => p.MopUpCenterDistanceWeight, v => p.MopUpCenterDistanceWeight = v, "MopUpCenterDistanceWeight");
                TuneParameter(positions, () => p.MopUpKingProximityWeight, v => p.MopUpKingProximityWeight = v, "MopUpKingProximityWeight");

                // Check convergence
                double currentError = ComputeErrorParallel(positions);
                Console.WriteLine($"\nError after iteration {iter + 1}: {currentError:F8}");

                if (Math.Abs(previousError - currentError) < ConvergenceThreshold)
                {
                    Console.WriteLine("Converged!");
                    break;
                }

                previousError = currentError;

                // Save intermediate results
                EvalParams.SaveToFile("eval_params_tuning.json");
            }

            Console.WriteLine("\nFinal parameters:");
            PrintParameters();

            EvalParams.SaveToFile("eval_params_tuned.json");
            Console.WriteLine("\nParameters saved to eval_params_tuned.json");
        }

        /// <summary>
        /// Prints current parameter values.
        /// </summary>
        public static void PrintParameters()
        {
            var p = EvalParams.Instance;

            Console.WriteLine($"Piece Values (MG): P={p.PawnValueMG} N={p.KnightValueMG} B={p.BishopValueMG} R={p.RookValueMG} Q={p.QueenValueMG}");
            Console.WriteLine($"Piece Values (EG): P={p.PawnValueEG} N={p.KnightValueEG} B={p.BishopValueEG} R={p.RookValueEG} Q={p.QueenValueEG}");
            Console.WriteLine($"Bishop Pair: MG={p.BishopPairBonusMG} EG={p.BishopPairBonusEG}");
            Console.WriteLine($"Rook Open File: MG={p.RookOpenFileBonusMG} EG={p.RookOpenFileBonusEG}");
            Console.WriteLine($"Rook Semi-Open: MG={p.RookSemiOpenFileBonusMG} EG={p.RookSemiOpenFileBonusEG}");
            Console.WriteLine($"Doubled Pawn: MG={p.DoubledPawnPenaltyMG} EG={p.DoubledPawnPenaltyEG}");
            Console.WriteLine($"Isolated Pawn: MG={p.IsolatedPawnPenaltyMG} EG={p.IsolatedPawnPenaltyEG}");
            Console.WriteLine($"Mobility: MG={p.MobilityBonusMG} EG={p.MobilityBonusEG}");
            Console.WriteLine($"Queen Mobility: MG={p.QueenMobilityBonusMG} EG={p.QueenMobilityBonusEG}");
            Console.WriteLine($"King Shield: {p.KingShieldBonus}");
            Console.WriteLine($"King Open File Penalty: {p.KingOpenFilePenalty}");
            Console.WriteLine($"Knight Outpost: MG={p.KnightOutpostBonusMG} EG={p.KnightOutpostBonusEG}");
        }

        /// <summary>
        /// Quick evaluation of current error on a positions file (.csv or FEN;result, auto-detected).
        /// </summary>
        public static void EvaluateError(string positionsFile)
        {
            var positions = LoadPositionsFromFile(positionsFile);
            Console.WriteLine($"Loaded {positions.Count} positions.");

            double error = ComputeErrorParallel(positions);
            Console.WriteLine($"Mean Squared Error: {error:F8}");

            // Also show predicted vs actual distribution
            int correctPredictions = 0;
            var board = new Board();
            foreach (var pos in positions)
            {
                Fen.Load(board, pos.Fen);
                int eval = Evaluator.Evaluate(board);
                double predicted = Sigmoid(eval);

                // Consider prediction correct if within 0.2 of actual
                if (Math.Abs(predicted - pos.Result) < 0.2)
                    correctPredictions++;
            }

            Console.WriteLine($"Prediction accuracy (within 0.2): {100.0 * correctPredictions / positions.Count:F1}%");
        }
    }
}
