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

        /// <summary>L2 regularization strength (0 = disabled). Penalizes deviation from defaults to reduce overfitting.</summary>
        public static double L2RegularizationStrength { get; set; } = 0.0;

        /// <summary>Default subset size for fast parameter steps (use full set if null or >= count).</summary>
        public const int DefaultTuneSubsetSize = 50_000;

        /// <summary>Re-optimize K every this many iterations (0 = only at start).</summary>
        public const int KReoptimizeInterval = 5;

        /// <summary>
        /// Returns a random subset of positions for fast tuning steps.
        /// If subsetSize is null or >= positions.Count, returns the full list (by reference to avoid copy).
        /// </summary>
        public static List<TuningPosition> GetRandomSubset(List<TuningPosition> positions, int? subsetSize, int seed)
        {
            if (!subsetSize.HasValue || subsetSize.Value <= 0 || subsetSize.Value >= positions.Count)
                return positions;

            int n = subsetSize.Value;
            var rnd = new Random(seed);
            var indices = new int[positions.Count];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;
            for (int i = indices.Length - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                (indices[i], indices[j]) = (indices[j], indices[i]);
            }
            var subset = new List<TuningPosition>(n);
            for (int i = 0; i < n; i++)
                subset.Add(positions[indices[i]]);
            return subset;
        }

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
        /// Finds optimal K value for sigmoid scaling (coarse-to-fine: 4 coarse points, then refine in best interval).
        /// When subsetSize is set and less than positions.Count, uses a random subset for speed.
        /// </summary>
        public static double OptimizeK(List<TuningPosition> positions, int? subsetSize = null)
        {
            Console.WriteLine("Optimizing K...");
            var evalList = subsetSize.HasValue && subsetSize.Value > 0 && subsetSize.Value < positions.Count
                ? GetRandomSubset(positions, subsetSize.Value, 0)
                : positions;

            double bestK = 1.0;
            double bestError = double.MaxValue;

            // Coarse: 4 points
            double[] coarseK = { 0.5, 1.0, 1.5, 2.0 };
            int bestIdx = 0;
            foreach (double testK in coarseK)
            {
                K = testK;
                Evaluator.ClearCache();
                double error = ComputeErrorParallel(evalList);
                if (error < bestError)
                {
                    bestError = error;
                    bestK = testK;
                    bestIdx = Array.IndexOf(coarseK, testK);
                }
            }

            // Refine in best interval [a, b]
            double a = bestIdx == 0 ? 0.5 : coarseK[bestIdx] - 0.25;
            double b = bestIdx == coarseK.Length - 1 ? 2.0 : coarseK[bestIdx] + 0.25;
            a = Math.Max(0.5, a);
            b = Math.Min(2.0, b);

            int refinePoints = 10;
            for (int i = 0; i <= refinePoints; i++)
            {
                double testK = a + (b - a) * i / refinePoints;
                K = testK;
                Evaluator.ClearCache();
                double error = ComputeErrorParallel(evalList);
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
        /// Tunes a single integer parameter using local search with adaptive step (greedy line search).
        /// Returns the new optimal value. Optional L2 regularization penalizes deviation from defaultValue.
        /// </summary>
        public static int TuneParameter(List<TuningPosition> positions,
            Func<int> getter, Action<int> setter, string name, int step = 1, int defaultValue = 0)
        {
            int current = getter();
            double currentError = ComputeErrorParallel(positions) + RegularizationPenalty(current, defaultValue);

            // Try increasing
            setter(current + step);
            Evaluator.ClearCache();
            double errorPlus = ComputeErrorParallel(positions) + RegularizationPenalty(getter(), defaultValue);

            // Try decreasing
            setter(current - step);
            Evaluator.ClearCache();
            double errorMinus = ComputeErrorParallel(positions) + RegularizationPenalty(getter(), defaultValue);

            const int maxExtraSteps = 3;

            if (errorPlus < currentError && errorPlus <= errorMinus)
            {
                // +step wins: greedy line search in positive direction
                int bestVal = current + step;
                double bestErr = errorPlus;
                setter(current + step);
                Evaluator.ClearCache();
                for (int i = 0; i < maxExtraSteps; i++)
                {
                    int nextVal = getter() + step;
                    setter(nextVal);
                    Evaluator.ClearCache();
                    double err = ComputeErrorParallel(positions) + RegularizationPenalty(getter(), defaultValue);
                    if (err < bestErr) { bestErr = err; bestVal = nextVal; }
                    else { setter(bestVal); Evaluator.ClearCache(); break; }
                }
                Console.WriteLine($"  {name}: {current} -> {bestVal} (error: {bestErr:F8})");
                return bestVal;
            }
            else if (errorMinus < currentError)
            {
                // -step wins: greedy line search in negative direction
                int bestVal = current - step;
                double bestErr = errorMinus;
                setter(current - step);
                Evaluator.ClearCache();
                for (int i = 0; i < maxExtraSteps; i++)
                {
                    int nextVal = getter() - step;
                    setter(nextVal);
                    Evaluator.ClearCache();
                    double err = ComputeErrorParallel(positions) + RegularizationPenalty(getter(), defaultValue);
                    if (err < bestErr) { bestErr = err; bestVal = nextVal; }
                    else { setter(bestVal); Evaluator.ClearCache(); break; }
                }
                Console.WriteLine($"  {name}: {current} -> {bestVal} (error: {bestErr:F8})");
                return bestVal;
            }
            else if (step > 1)
            {
                // Neither wins: try half-step refinement
                setter(current);
                Evaluator.ClearCache();
                int halfStep = step / 2;
                if (halfStep < 1) return current;
                setter(current + halfStep);
                Evaluator.ClearCache();
                double errPlusHalf = ComputeErrorParallel(positions) + RegularizationPenalty(getter(), defaultValue);
                setter(current - halfStep);
                Evaluator.ClearCache();
                double errMinusHalf = ComputeErrorParallel(positions) + RegularizationPenalty(getter(), defaultValue);
                if (errPlusHalf < currentError && errPlusHalf <= errMinusHalf)
                {
                    setter(current + halfStep);
                    Evaluator.ClearCache();
                    Console.WriteLine($"  {name}: {current} -> {current + halfStep} (error: {errPlusHalf:F8})");
                    return current + halfStep;
                }
                if (errMinusHalf < currentError)
                {
                    Console.WriteLine($"  {name}: {current} -> {current - halfStep} (error: {errMinusHalf:F8})");
                    return current - halfStep;
                }
                setter(current);
                Evaluator.ClearCache();
                return current;
            }
            else
            {
                setter(current);
                Evaluator.ClearCache();
                return current;
            }
        }

        /// <summary>
        /// Builds parameter list for tuning. Excludes volatile/low-signal params (RookBehindPasserBonus,
        /// KingAttackWeightPenalty, MopUp*, SpaceBonusMG, QueenTropismBonus, KnightOutpostBonus) for stability.
        /// Uses consolidated params (single DoubledPawnPenalty, MobilityBonus, etc.).
        /// </summary>
        private static List<(string name, Func<int> getter, Action<int> setter, int step, int defaultValue)> BuildParameterActions(EvalParams p)
        {
            var def = new EvalParams();
            var list = new List<(string, Func<int>, Action<int>, int, int)>
            {
                ("PawnValueMG", () => p.PawnValueMG, v => { p.PawnValueMG = v; EvalParams.ClampAllParameters(p); }, 5, def.PawnValueMG),
                ("KnightValueMG", () => p.KnightValueMG, v => { p.KnightValueMG = v; EvalParams.ClampAllParameters(p); }, 5, def.KnightValueMG),
                ("BishopValueMG", () => p.BishopValueMG, v => { p.BishopValueMG = v; EvalParams.ClampAllParameters(p); }, 5, def.BishopValueMG),
                ("RookValueMG", () => p.RookValueMG, v => { p.RookValueMG = v; EvalParams.ClampAllParameters(p); }, 5, def.RookValueMG),
                ("QueenValueMG", () => p.QueenValueMG, v => { p.QueenValueMG = v; EvalParams.ClampAllParameters(p); }, 10, def.QueenValueMG),
                ("PawnValueEG", () => p.PawnValueEG, v => { p.PawnValueEG = v; EvalParams.ClampAllParameters(p); }, 5, def.PawnValueEG),
                ("KnightValueEG", () => p.KnightValueEG, v => { p.KnightValueEG = v; EvalParams.ClampAllParameters(p); }, 5, def.KnightValueEG),
                ("BishopValueEG", () => p.BishopValueEG, v => { p.BishopValueEG = v; EvalParams.ClampAllParameters(p); }, 5, def.BishopValueEG),
                ("RookValueEG", () => p.RookValueEG, v => { p.RookValueEG = v; EvalParams.ClampAllParameters(p); }, 5, def.RookValueEG),
                ("QueenValueEG", () => p.QueenValueEG, v => { p.QueenValueEG = v; EvalParams.ClampAllParameters(p); }, 10, def.QueenValueEG),
                ("BishopPairBonusMG", () => p.BishopPairBonusMG, v => { p.BishopPairBonusMG = v; EvalParams.ClampAllParameters(p); }, 1, def.BishopPairBonusMG),
                ("BishopPairBonusEG", () => p.BishopPairBonusEG, v => { p.BishopPairBonusEG = v; EvalParams.ClampAllParameters(p); }, 1, def.BishopPairBonusEG),
                ("RookOpenFileBonus", () => p.RookOpenFileBonus, v => { p.RookOpenFileBonus = v; EvalParams.ClampAllParameters(p); }, 1, def.RookOpenFileBonus),
                ("RookSemiOpenFileBonus", () => p.RookSemiOpenFileBonus, v => { p.RookSemiOpenFileBonus = v; EvalParams.ClampAllParameters(p); }, 1, def.RookSemiOpenFileBonus),
                ("DoubledPawnPenalty", () => p.DoubledPawnPenalty, v => { p.DoubledPawnPenalty = v; EvalParams.ClampAllParameters(p); }, 1, def.DoubledPawnPenalty),
                ("IsolatedPawnPenalty", () => p.IsolatedPawnPenalty, v => { p.IsolatedPawnPenalty = v; EvalParams.ClampAllParameters(p); }, 1, def.IsolatedPawnPenalty),
                ("BackwardPawnPenalty", () => p.BackwardPawnPenalty, v => { p.BackwardPawnPenalty = v; EvalParams.ClampAllParameters(p); }, 1, def.BackwardPawnPenalty),
                ("MobilityBonus", () => p.MobilityBonus, v => { p.MobilityBonus = v; EvalParams.ClampAllParameters(p); }, 1, def.MobilityBonus),
                ("KingShieldBonus", () => p.KingShieldBonus, v => { p.KingShieldBonus = v; EvalParams.ClampAllParameters(p); }, 1, def.KingShieldBonus),
                ("KingOpenFilePenalty", () => p.KingOpenFilePenalty, v => { p.KingOpenFilePenalty = v; EvalParams.ClampAllParameters(p); }, 1, def.KingOpenFilePenalty),
                ("RookOnSeventhBonusMG", () => p.RookOnSeventhBonusMG, v => { p.RookOnSeventhBonusMG = v; EvalParams.ClampAllParameters(p); }, 1, def.RookOnSeventhBonusMG),
                ("RookOnSeventhBonusEG", () => p.RookOnSeventhBonusEG, v => { p.RookOnSeventhBonusEG = v; EvalParams.ClampAllParameters(p); }, 1, def.RookOnSeventhBonusEG),
                ("RookOnSeventhWithKingBonus", () => p.RookOnSeventhWithKingBonus, v => { p.RookOnSeventhWithKingBonus = v; EvalParams.ClampAllParameters(p); }, 1, def.RookOnSeventhWithKingBonus),
                ("BadBishopPenalty", () => p.BadBishopPenalty, v => { p.BadBishopPenalty = v; EvalParams.ClampAllParameters(p); }, 1, def.BadBishopPenalty),
                ("BishopLongDiagonalBonus", () => p.BishopLongDiagonalBonus, v => { p.BishopLongDiagonalBonus = v; EvalParams.ClampAllParameters(p); }, 1, def.BishopLongDiagonalBonus),
                ("KingOwnPasserProximity", () => p.KingOwnPasserProximity, v => { p.KingOwnPasserProximity = v; EvalParams.ClampAllParameters(p); }, 1, def.KingOwnPasserProximity),
                ("KingEnemyPasserProximity", () => p.KingEnemyPasserProximity, v => { p.KingEnemyPasserProximity = v; EvalParams.ClampAllParameters(p); }, 1, def.KingEnemyPasserProximity)
            };
            return list;
        }

        private static void ShuffleParameterActions(List<(string name, Func<int> getter, Action<int> setter, int step, int defaultValue)> list, int seed)
        {
            var rnd = new Random(seed);
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private static double RegularizationPenalty(int current, int defaultValue)
        {
            if (L2RegularizationStrength <= 0) return 0;
            double diff = current - defaultValue;
            return L2RegularizationStrength * diff * diff;
        }

        /// <summary>
        /// Runs coordinate descent optimization on all tunable parameters.
        /// Accepts .csv, .txt, or any file; format is auto-detected (CSV or FEN;result).
        /// When tuneSubsetSize is set and less than position count, parameter steps use a random subset for speed;
        /// initial error, end-of-iteration error, and convergence use the full dataset.
        /// </summary>
        public static void RunTuning(string positionsFile, int maxIterations = 100, int? maxPositions = null, int? tuneSubsetSize = null)
        {
            Console.WriteLine("Loading positions...");
            var positions = LoadPositionsFromFile(positionsFile, maxPositions);
            Console.WriteLine($"Loaded {positions.Count} positions.");

            if (positions.Count == 0)
            {
                Console.WriteLine("No positions loaded. Check file format.");
                return;
            }

            int? effectiveSubset = tuneSubsetSize.HasValue && tuneSubsetSize.Value > 0 && tuneSubsetSize.Value < positions.Count ? tuneSubsetSize : null;
            if (effectiveSubset.HasValue)
                Console.WriteLine($"Tune subset size: {effectiveSubset.Value} (full set used for iteration/convergence error).");

            // First, optimize K (coarse-to-fine, optionally on subset for speed)
            OptimizeK(positions, effectiveSubset);

            double previousError = ComputeErrorParallel(positions);
            Console.WriteLine($"Initial error: {previousError:F8}");

            var p = EvalParams.Instance;
            var paramActions = BuildParameterActions(p);

            for (int iter = 0; iter < maxIterations; iter++)
            {
                Console.WriteLine($"\n=== Iteration {iter + 1} ===");

                // Re-optimize K every N iterations
                if (iter > 0 && KReoptimizeInterval > 0 && iter % KReoptimizeInterval == 0)
                {
                    OptimizeK(positions, effectiveSubset);
                }

                // Use random subset for parameter steps when configured
                var tuneList = effectiveSubset.HasValue ? GetRandomSubset(positions, effectiveSubset.Value, iter) : positions;

                ShuffleParameterActions(paramActions, iter);

                foreach (var a in paramActions)
                    TuneParameter(tuneList, a.getter, a.setter, a.name, a.step, a.defaultValue);

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
            Console.WriteLine($"Rook Open/Semi-Open: {p.RookOpenFileBonus} / {p.RookSemiOpenFileBonus}");
            Console.WriteLine($"Doubled/Isolated/Backward Pawn: {p.DoubledPawnPenalty} / {p.IsolatedPawnPenalty} / {p.BackwardPawnPenalty}");
            Console.WriteLine($"Mobility: {p.MobilityBonus}");
            Console.WriteLine($"King Shield: {p.KingShieldBonus} Open File Penalty: {p.KingOpenFilePenalty}");
            Console.WriteLine($"Rook on 7th: MG={p.RookOnSeventhBonusMG} EG={p.RookOnSeventhBonusEG} WithKing={p.RookOnSeventhWithKingBonus}");
            Console.WriteLine($"Bad Bishop: {p.BadBishopPenalty} Bishop Long Diagonal: {p.BishopLongDiagonalBonus}");
            Console.WriteLine($"King Passer Proximity: Own={p.KingOwnPasserProximity} Enemy={p.KingEnemyPasserProximity}");
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
