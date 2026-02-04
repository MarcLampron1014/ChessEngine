using System;
using System.IO;
using System.Text.Json;

namespace ChessEngine
{
    /// <summary>
    /// Holds all tunable evaluation parameters.
    /// Can be loaded from / saved to a JSON config file for tuning.
    /// </summary>
    public class EvalParams
    {
        // Phase weights
        public int TotalPhase { get; set; } = 24;

        // Middlegame piece values
        public int PawnValueMG { get; set; } = 100;
        public int KnightValueMG { get; set; } = 320;
        public int BishopValueMG { get; set; } = 330;
        public int RookValueMG { get; set; } = 500;
        public int QueenValueMG { get; set; } = 900;

        // Endgame piece values
        public int PawnValueEG { get; set; } = 100;
        public int KnightValueEG { get; set; } = 280;
        public int BishopValueEG { get; set; } = 320;
        public int RookValueEG { get; set; } = 550;
        public int QueenValueEG { get; set; } = 950;

        // Bonuses/penalties
        public int BishopPairBonusMG { get; set; } = 30;
        public int BishopPairBonusEG { get; set; } = 50;
        public int RookOpenFileBonusMG { get; set; } = 20;
        public int RookOpenFileBonusEG { get; set; } = 15;
        public int RookSemiOpenFileBonusMG { get; set; } = 10;
        public int RookSemiOpenFileBonusEG { get; set; } = 8;
        public int DoubledPawnPenaltyMG { get; set; } = -10;
        public int DoubledPawnPenaltyEG { get; set; } = -20;
        public int IsolatedPawnPenaltyMG { get; set; } = -15;
        public int IsolatedPawnPenaltyEG { get; set; } = -25;
        public int MobilityBonusMG { get; set; } = 3;
        public int MobilityBonusEG { get; set; } = 2;
        public int QueenMobilityBonusMG { get; set; } = 1;
        public int QueenMobilityBonusEG { get; set; } = 2;
        public int KingShieldBonus { get; set; } = 10;
        public int KingOpenFilePenalty { get; set; } = 10;
        public int RookBehindPasserBonus { get; set; } = 30;

        // Rook on 7th rank
        public int RookOnSeventhBonusMG { get; set; } = 25;
        public int RookOnSeventhBonusEG { get; set; } = 35;
        public int RookOnSeventhWithKingBonus { get; set; } = 15;

        // King safety attack weight (penalty per unit of attack weight over defense)
        public int KingAttackWeightPenalty { get; set; } = 8;

        // Backward pawn
        public int BackwardPawnPenaltyMG { get; set; } = -12;
        public int BackwardPawnPenaltyEG { get; set; } = -18;

        // Space (middlegame)
        public int SpaceBonusMG { get; set; } = 3;

        // Bishop: good vs bad, long diagonal
        public int BadBishopPenalty { get; set; } = -15;
        public int BishopLongDiagonalBonus { get; set; } = 10;

        // Queen tropism (distance to enemy king)
        public int QueenTropismBonus { get; set; } = 2;

        // King-pawn proximity weights (endgame)
        public int KingOwnPasserProximity { get; set; } = 5;
        public int KingEnemyPasserProximity { get; set; } = 3;

        // Mop-up evaluation weights
        public int MopUpCenterDistanceWeight { get; set; } = 10;
        public int MopUpKingProximityWeight { get; set; } = 4;

        // Knight outpost bonus
        public int KnightOutpostBonusMG { get; set; } = 20;
        public int KnightOutpostBonusEG { get; set; } = 15;

        // Piece-Square Tables (64 entries each)
        public int[] PawnPstMG { get; set; } = new int[]
        {
            0, 0, 0, 0, 0, 0, 0, 0,
            5, 10, 10, -20, -20, 10, 10, 5,
            5, -5, -10, 0, 0, -10, -5, 5,
            0, 0, 0, 20, 20, 0, 0, 0,
            5, 5, 10, 25, 25, 10, 5, 5,
            10, 10, 20, 30, 30, 20, 10, 10,
            50, 50, 50, 50, 50, 50, 50, 50,
            0, 0, 0, 0, 0, 0, 0, 0,
        };

        public int[] PawnPstEG { get; set; } = new int[]
        {
            0, 0, 0, 0, 0, 0, 0, 0,
            10, 10, 10, 10, 10, 10, 10, 10,
            10, 10, 10, 10, 10, 10, 10, 10,
            20, 20, 20, 20, 20, 20, 20, 20,
            30, 30, 30, 30, 30, 30, 30, 30,
            50, 50, 50, 50, 50, 50, 50, 50,
            80, 80, 80, 80, 80, 80, 80, 80,
            0, 0, 0, 0, 0, 0, 0, 0,
        };

        public int[] KnightPstMG { get; set; } = new int[]
        {
            -50, -40, -30, -30, -30, -30, -40, -50,
            -40, -20, 0, 0, 0, 0, -20, -40,
            -30, 0, 10, 15, 15, 10, 0, -30,
            -30, 5, 15, 20, 20, 15, 5, -30,
            -30, 0, 15, 20, 20, 15, 0, -30,
            -30, 5, 10, 15, 15, 10, 5, -30,
            -40, -20, 0, 5, 5, 0, -20, -40,
            -50, -40, -30, -30, -30, -30, -40, -50,
        };

        public int[] KnightPstEG { get; set; } = new int[]
        {
            -50, -40, -30, -30, -30, -30, -40, -50,
            -40, -20, -10, -10, -10, -10, -20, -40,
            -30, -10, 0, 5, 5, 0, -10, -30,
            -30, -5, 10, 15, 15, 10, -5, -30,
            -30, -5, 10, 15, 15, 10, -5, -30,
            -30, -10, 0, 5, 5, 0, -10, -30,
            -40, -20, -10, -10, -10, -10, -20, -40,
            -50, -40, -30, -30, -30, -30, -40, -50,
        };

        public int[] BishopPstMG { get; set; } = new int[]
        {
            -20, -10, -10, -10, -10, -10, -10, -20,
            -10, 0, 0, 0, 0, 0, 0, -10,
            -10, 0, 5, 10, 10, 5, 0, -10,
            -10, 5, 5, 10, 10, 5, 5, -10,
            -10, 0, 10, 10, 10, 10, 0, -10,
            -10, 10, 10, 10, 10, 10, 10, -10,
            -10, 5, 0, 0, 0, 0, 5, -10,
            -20, -10, -10, -10, -10, -10, -10, -20,
        };

        public int[] BishopPstEG { get; set; } = new int[]
        {
            -20, -10, -10, -10, -10, -10, -10, -20,
            -10, 0, 0, 0, 0, 0, 0, -10,
            -10, 0, 5, 5, 5, 5, 0, -10,
            -10, 0, 5, 10, 10, 5, 0, -10,
            -10, 0, 5, 10, 10, 5, 0, -10,
            -10, 0, 5, 5, 5, 5, 0, -10,
            -10, 0, 0, 0, 0, 0, 0, -10,
            -20, -10, -10, -10, -10, -10, -10, -20,
        };

        public int[] RookPstMG { get; set; } = new int[]
        {
            0, 0, 0, 5, 5, 0, 0, 0,
            -5, 0, 0, 0, 0, 0, 0, -5,
            -5, 0, 0, 0, 0, 0, 0, -5,
            -5, 0, 0, 0, 0, 0, 0, -5,
            -5, 0, 0, 0, 0, 0, 0, -5,
            -5, 0, 0, 0, 0, 0, 0, -5,
            5, 10, 10, 10, 10, 10, 10, 5,
            0, 0, 0, 0, 0, 0, 0, 0,
        };

        public int[] RookPstEG { get; set; } = new int[]
        {
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
        };

        public int[] QueenPstMG { get; set; } = new int[]
        {
            -20, -10, -10, -5, -5, -10, -10, -20,
            -10, 0, 0, 0, 0, 0, 0, -10,
            -10, 0, 5, 5, 5, 5, 0, -10,
            -5, 0, 5, 5, 5, 5, 0, -5,
            0, 0, 5, 5, 5, 5, 0, -5,
            -10, 5, 5, 5, 5, 5, 0, -10,
            -10, 0, 5, 0, 0, 0, 0, -10,
            -20, -10, -10, -5, -5, -10, -10, -20,
        };

        public int[] QueenPstEG { get; set; } = new int[]
        {
            -20, -10, -10, -5, -5, -10, -10, -20,
            -10, 0, 0, 0, 0, 0, 0, -10,
            -10, 0, 5, 5, 5, 5, 0, -10,
            -5, 0, 5, 10, 10, 5, 0, -5,
            -5, 0, 5, 10, 10, 5, 0, -5,
            -10, 0, 5, 5, 5, 5, 0, -10,
            -10, 0, 0, 0, 0, 0, 0, -10,
            -20, -10, -10, -5, -5, -10, -10, -20,
        };

        public int[] KingPstMG { get; set; } = new int[]
        {
            -30, -40, -40, -50, -50, -40, -40, -30,
            -30, -40, -40, -50, -50, -40, -40, -30,
            -30, -40, -40, -50, -50, -40, -40, -30,
            -30, -40, -40, -50, -50, -40, -40, -30,
            -20, -30, -30, -40, -40, -30, -30, -20,
            -10, -20, -20, -20, -20, -20, -20, -10,
            20, 20, 0, 0, 0, 0, 20, 20,
            20, 30, 10, 0, 0, 10, 30, 20,
        };

        public int[] KingPstEG { get; set; } = new int[]
        {
            -50, -40, -30, -20, -20, -30, -40, -50,
            -30, -20, -10, 0, 0, -10, -20, -30,
            -30, -10, 20, 30, 30, 20, -10, -30,
            -30, -10, 30, 40, 40, 30, -10, -30,
            -30, -10, 30, 40, 40, 30, -10, -30,
            -30, -10, 20, 30, 30, 20, -10, -30,
            -30, -30, 0, 0, 0, 0, -30, -30,
            -50, -30, -30, -30, -30, -30, -30, -50,
        };

        // Passed pawn bonuses by rank (index 0 = rank 1, index 7 = rank 8)
        public int[] PassedPawnBonusMG { get; set; } = new int[] { 0, 5, 10, 20, 35, 60, 100, 0 };
        public int[] PassedPawnBonusEG { get; set; } = new int[] { 0, 15, 25, 40, 65, 100, 150, 0 };

        // Connected passed pawn bonus by rank
        public int[] ConnectedPasserBonusByRank { get; set; } = new int[] { 0, 5, 10, 15, 30, 50, 80, 0 };

        /// <summary>
        /// Global instance of evaluation parameters used by the engine.
        /// </summary>
        public static EvalParams Instance { get; private set; } = new EvalParams();

        /// <summary>
        /// Loads parameters from a JSON file.
        /// Piece values (Pawn/Knight/Bishop/Rook/Queen MG/EG) must be positive; tuned values can go negative, so we clamp to avoid inverted evaluation.
        /// </summary>
        public static void LoadFromFile(string path)
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<EvalParams>(json);
                if (loaded != null)
                {
                    ClampPieceValuesToPositive(loaded);
                    EnsureValidPstAndPhase(loaded);
                    Instance = loaded;
                }
            }
        }

        private static void ClampPieceValuesToPositive(EvalParams p)
        {
            p.PawnValueMG = Math.Max(1, p.PawnValueMG);
            p.PawnValueEG = Math.Max(1, p.PawnValueEG);
            p.KnightValueMG = Math.Max(1, p.KnightValueMG);
            p.KnightValueEG = Math.Max(1, p.KnightValueEG);
            p.BishopValueMG = Math.Max(1, p.BishopValueMG);
            p.BishopValueEG = Math.Max(1, p.BishopValueEG);
            p.RookValueMG = Math.Max(1, p.RookValueMG);
            p.RookValueEG = Math.Max(1, p.RookValueEG);
            p.QueenValueMG = Math.Max(1, p.QueenValueMG);
            p.QueenValueEG = Math.Max(1, p.QueenValueEG);
        }

        /// <summary>
        /// Ensures TotalPhase >= 1 and all PST arrays are non-null and length 64 after JSON load,
        /// to prevent NullReferenceException, IndexOutOfRangeException, or DivideByZeroException in the engine subprocess.
        /// </summary>
        private static void EnsureValidPstAndPhase(EvalParams p)
        {
            p.TotalPhase = Math.Max(1, p.TotalPhase);
            var def = new EvalParams();
            if (p.PawnPstMG == null || p.PawnPstMG.Length != 64) p.PawnPstMG = (int[])def.PawnPstMG.Clone();
            if (p.PawnPstEG == null || p.PawnPstEG.Length != 64) p.PawnPstEG = (int[])def.PawnPstEG.Clone();
            if (p.KnightPstMG == null || p.KnightPstMG.Length != 64) p.KnightPstMG = (int[])def.KnightPstMG.Clone();
            if (p.KnightPstEG == null || p.KnightPstEG.Length != 64) p.KnightPstEG = (int[])def.KnightPstEG.Clone();
            if (p.BishopPstMG == null || p.BishopPstMG.Length != 64) p.BishopPstMG = (int[])def.BishopPstMG.Clone();
            if (p.BishopPstEG == null || p.BishopPstEG.Length != 64) p.BishopPstEG = (int[])def.BishopPstEG.Clone();
            if (p.RookPstMG == null || p.RookPstMG.Length != 64) p.RookPstMG = (int[])def.RookPstMG.Clone();
            if (p.RookPstEG == null || p.RookPstEG.Length != 64) p.RookPstEG = (int[])def.RookPstEG.Clone();
            if (p.QueenPstMG == null || p.QueenPstMG.Length != 64) p.QueenPstMG = (int[])def.QueenPstMG.Clone();
            if (p.QueenPstEG == null || p.QueenPstEG.Length != 64) p.QueenPstEG = (int[])def.QueenPstEG.Clone();
            if (p.KingPstMG == null || p.KingPstMG.Length != 64) p.KingPstMG = (int[])def.KingPstMG.Clone();
            if (p.KingPstEG == null || p.KingPstEG.Length != 64) p.KingPstEG = (int[])def.KingPstEG.Clone();
            if (p.PassedPawnBonusMG == null || p.PassedPawnBonusMG.Length != 8) p.PassedPawnBonusMG = (int[])def.PassedPawnBonusMG.Clone();
            if (p.PassedPawnBonusEG == null || p.PassedPawnBonusEG.Length != 8) p.PassedPawnBonusEG = (int[])def.PassedPawnBonusEG.Clone();
            if (p.ConnectedPasserBonusByRank == null || p.ConnectedPasserBonusByRank.Length != 8) p.ConnectedPasserBonusByRank = (int[])def.ConnectedPasserBonusByRank.Clone();
        }

        /// <summary>
        /// Saves current parameters to a JSON file.
        /// </summary>
        public static void SaveToFile(string path)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(Instance, options);
            File.WriteAllText(path, json);
        }

        /// <summary>
        /// Resets parameters to defaults.
        /// </summary>
        public static void ResetToDefaults()
        {
            Instance = new EvalParams();
        }
    }
}
