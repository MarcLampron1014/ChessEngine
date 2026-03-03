using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

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

        // Bonuses/penalties (consolidated for stability: single value, phase taper handles MG/EG blend)
        public int BishopPairBonusMG { get; set; } = 30;
        public int BishopPairBonusEG { get; set; } = 50;
        public int RookOpenFileBonus { get; set; } = 18;
        public int RookSemiOpenFileBonus { get; set; } = 9;
        public int DoubledPawnPenalty { get; set; } = -15;
        public int IsolatedPawnPenalty { get; set; } = -20;
        public int BackwardPawnPenalty { get; set; } = -15;
        public int BlockedPawnPenalty { get; set; } = -12;
        public int PhalanxBonus { get; set; } = 10;
        public int MobilityBonus { get; set; } = 2;
        public int KingShieldBonus { get; set; } = 10;
        public int KingOpenFilePenalty { get; set; } = 10;
        public int RookBehindPasserBonus { get; set; } = 30;

        // Rook on 7th rank
        public int RookOnSeventhBonusMG { get; set; } = 25;
        public int RookOnSeventhBonusEG { get; set; } = 35;
        public int RookOnSeventhWithKingBonus { get; set; } = 15;

        // King safety attack weight (penalty per unit of attack weight over defense)
        public int KingAttackWeightPenalty { get; set; } = 8;

        // Space (middlegame)
        public int SpaceBonusMG { get; set; } = 3;

        // Bishop: good vs bad, long diagonal
        public int BadBishopPenalty { get; set; } = -15;
        public int BishopLongDiagonalBonus { get; set; } = 10;

        // Queen tropism (distance to enemy king)
        public int QueenTropismBonus { get; set; } = 2;

        // Knight tropism (distance to enemy king)
        public int KnightTropismMG { get; set; } = 3;

        // Opposite-colored bishops (endgame draw tendency)
        public int OppositeColoredBishopsDrawFactor { get; set; } = 25;

        // Pawn storm bonus by rank (index 0-7 for advancing pawns toward enemy king)
        public int PawnStormBonus4 { get; set; } = 5;
        public int PawnStormBonus5 { get; set; } = 10;
        public int PawnStormBonus6 { get; set; } = 20;

        // Hanging piece penalty (threat detection)
        public int HangingPiecePenalty { get; set; } = 15;

        // King-pawn proximity weights (endgame)
        public int KingOwnPasserProximity { get; set; } = 5;
        public int KingEnemyPasserProximity { get; set; } = 3;

        // Mop-up evaluation weights
        public int MopUpCenterDistanceWeight { get; set; } = 10;
        public int MopUpKingProximityWeight { get; set; } = 4;

        // Knight outpost bonus
        public int KnightOutpostBonusMG { get; set; } = 20;
        public int KnightOutpostBonusEG { get; set; } = 15;

        // Legacy properties for JSON backward compatibility (not written when saving)
        [JsonPropertyName("DoubledPawnPenaltyMG")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int DoubledPawnPenaltyMG { get; set; }

        [JsonPropertyName("DoubledPawnPenaltyEG")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int DoubledPawnPenaltyEG { get; set; }

        [JsonPropertyName("IsolatedPawnPenaltyMG")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int IsolatedPawnPenaltyMG { get; set; }

        [JsonPropertyName("IsolatedPawnPenaltyEG")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int IsolatedPawnPenaltyEG { get; set; }

        [JsonPropertyName("BackwardPawnPenaltyMG")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int BackwardPawnPenaltyMG { get; set; }

        [JsonPropertyName("BackwardPawnPenaltyEG")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int BackwardPawnPenaltyEG { get; set; }

        [JsonPropertyName("RookOpenFileBonusMG")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int RookOpenFileBonusMG { get; set; }

        [JsonPropertyName("RookOpenFileBonusEG")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int RookOpenFileBonusEG { get; set; }

        [JsonPropertyName("RookSemiOpenFileBonusMG")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int RookSemiOpenFileBonusMG { get; set; }

        [JsonPropertyName("RookSemiOpenFileBonusEG")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int RookSemiOpenFileBonusEG { get; set; }

        [JsonPropertyName("MobilityBonusMG")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int MobilityBonusMG { get; set; }

        [JsonPropertyName("MobilityBonusEG")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int MobilityBonusEG { get; set; }

        [JsonPropertyName("QueenMobilityBonusMG")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int QueenMobilityBonusMG { get; set; }

        [JsonPropertyName("QueenMobilityBonusEG")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int QueenMobilityBonusEG { get; set; }

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

        // Parameter bounds — per-parameter sign constraints prevent tuner drift
        private const int PawnValueMin = 80, PawnValueMax = 130;
        private const int KnightValueMin = 270, KnightValueMax = 370;
        private const int BishopValueMin = 270, BishopValueMax = 380;
        private const int RookValueMin = 450, RookValueMax = 600;
        private const int QueenValueMin = 850, QueenValueMax = 1050;

        /// <summary>
        /// Loads parameters from a JSON file.
        /// All parameters are clamped to reasonable bounds for stable, predictable evaluation.
        /// </summary>
        public static void LoadFromFile(string path)
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<EvalParams>(json);
                if (loaded != null)
                {
                    MigrateLegacyParameters(loaded);
                    ClampAllParameters(loaded);
                    EnsureValidPstAndPhase(loaded);
                    Instance = loaded;
                }
            }
        }

        /// <summary>
        /// Migrates legacy MG/EG split parameters to consolidated single values when loading old JSON.
        /// </summary>
        private static void MigrateLegacyParameters(EvalParams p)
        {
            if (p.DoubledPawnPenaltyMG != 0 || p.DoubledPawnPenaltyEG != 0)
                p.DoubledPawnPenalty = (p.DoubledPawnPenaltyMG + p.DoubledPawnPenaltyEG) / 2;
            if (p.IsolatedPawnPenaltyMG != 0 || p.IsolatedPawnPenaltyEG != 0)
                p.IsolatedPawnPenalty = (p.IsolatedPawnPenaltyMG + p.IsolatedPawnPenaltyEG) / 2;
            if (p.BackwardPawnPenaltyMG != 0 || p.BackwardPawnPenaltyEG != 0)
                p.BackwardPawnPenalty = (p.BackwardPawnPenaltyMG + p.BackwardPawnPenaltyEG) / 2;
            if (p.RookOpenFileBonusMG != 0 || p.RookOpenFileBonusEG != 0)
                p.RookOpenFileBonus = (p.RookOpenFileBonusMG + p.RookOpenFileBonusEG) / 2;
            if (p.RookSemiOpenFileBonusMG != 0 || p.RookSemiOpenFileBonusEG != 0)
                p.RookSemiOpenFileBonus = (p.RookSemiOpenFileBonusMG + p.RookSemiOpenFileBonusEG) / 2;
            if (p.MobilityBonusMG != 0 || p.MobilityBonusEG != 0 || p.QueenMobilityBonusMG != 0 || p.QueenMobilityBonusEG != 0)
                p.MobilityBonus = (p.MobilityBonusMG + p.MobilityBonusEG + p.QueenMobilityBonusMG + p.QueenMobilityBonusEG) / 4;
        }

        /// <summary>
        /// Clamps all parameters to reasonable bounds. Used by LoadFromFile and Tuner.
        /// </summary>
        public static void ClampAllParameters(EvalParams p)
        {
            // Piece values
            p.PawnValueMG = Math.Clamp(p.PawnValueMG, PawnValueMin, PawnValueMax);
            p.PawnValueEG = Math.Clamp(p.PawnValueEG, PawnValueMin, PawnValueMax);
            p.KnightValueMG = Math.Clamp(p.KnightValueMG, KnightValueMin, KnightValueMax);
            p.KnightValueEG = Math.Clamp(p.KnightValueEG, KnightValueMin, KnightValueMax);
            p.BishopValueMG = Math.Clamp(p.BishopValueMG, BishopValueMin, BishopValueMax);
            p.BishopValueEG = Math.Clamp(p.BishopValueEG, BishopValueMin, BishopValueMax);
            p.RookValueMG = Math.Clamp(p.RookValueMG, RookValueMin, RookValueMax);
            p.RookValueEG = Math.Clamp(p.RookValueEG, RookValueMin, RookValueMax);
            p.QueenValueMG = Math.Clamp(p.QueenValueMG, QueenValueMin, QueenValueMax);
            p.QueenValueEG = Math.Clamp(p.QueenValueEG, QueenValueMin, QueenValueMax);

            // Bishop pair: always a positive bonus (10-60cp)
            p.BishopPairBonusMG = Math.Clamp(p.BishopPairBonusMG, 10, 60);
            p.BishopPairBonusEG = Math.Clamp(p.BishopPairBonusEG, 10, 60);

            // Rook file bonuses: always positive, open >= semi-open
            p.RookOpenFileBonus = Math.Clamp(p.RookOpenFileBonus, 5, 50);
            p.RookSemiOpenFileBonus = Math.Clamp(p.RookSemiOpenFileBonus, 0, 50);
            if (p.RookSemiOpenFileBonus > p.RookOpenFileBonus)
                p.RookSemiOpenFileBonus = p.RookOpenFileBonus;

            // Pawn structure penalties: always negative
            p.DoubledPawnPenalty = Math.Clamp(p.DoubledPawnPenalty, -50, 0);
            p.IsolatedPawnPenalty = Math.Clamp(p.IsolatedPawnPenalty, -50, 0);
            p.BackwardPawnPenalty = Math.Clamp(p.BackwardPawnPenalty, -50, 0);
            p.BlockedPawnPenalty = Math.Clamp(p.BlockedPawnPenalty, -50, 0);
            p.PhalanxBonus = Math.Clamp(p.PhalanxBonus, 0, 30);

            // Mobility: must be positive (more moves = better)
            p.MobilityBonus = Math.Clamp(p.MobilityBonus, 0, 15);

            // King safety: always positive contributions
            p.KingShieldBonus = Math.Clamp(p.KingShieldBonus, 0, 30);
            p.KingOpenFilePenalty = Math.Clamp(p.KingOpenFilePenalty, 0, 40);
            p.KingAttackWeightPenalty = Math.Clamp(p.KingAttackWeightPenalty, 0, 30);

            // Rook positional bonuses: always positive
            p.RookBehindPasserBonus = Math.Clamp(p.RookBehindPasserBonus, 0, 50);
            p.RookOnSeventhBonusMG = Math.Clamp(p.RookOnSeventhBonusMG, 0, 50);
            p.RookOnSeventhBonusEG = Math.Clamp(p.RookOnSeventhBonusEG, 0, 50);
            p.RookOnSeventhWithKingBonus = Math.Clamp(p.RookOnSeventhWithKingBonus, 0, 50);

            // Space and tropism: positive
            p.SpaceBonusMG = Math.Clamp(p.SpaceBonusMG, 0, 10);
            p.QueenTropismBonus = Math.Clamp(p.QueenTropismBonus, 0, 10);
            p.KnightTropismMG = Math.Clamp(p.KnightTropismMG, 0, 10);

            // Bishop quality
            p.BadBishopPenalty = Math.Clamp(p.BadBishopPenalty, -50, 0);
            p.BishopLongDiagonalBonus = Math.Clamp(p.BishopLongDiagonalBonus, 0, 30);

            p.OppositeColoredBishopsDrawFactor = Math.Clamp(p.OppositeColoredBishopsDrawFactor, 0, 100);

            // Pawn storm: positive
            p.PawnStormBonus4 = Math.Clamp(p.PawnStormBonus4, 0, 30);
            p.PawnStormBonus5 = Math.Clamp(p.PawnStormBonus5, 0, 40);
            p.PawnStormBonus6 = Math.Clamp(p.PawnStormBonus6, 0, 50);

            // Threats: positive penalty amount
            p.HangingPiecePenalty = Math.Clamp(p.HangingPiecePenalty, 0, 50);

            // King-passer proximity: positive (closer king = better)
            p.KingOwnPasserProximity = Math.Clamp(p.KingOwnPasserProximity, 0, 30);
            p.KingEnemyPasserProximity = Math.Clamp(p.KingEnemyPasserProximity, 0, 30);

            // Mop-up: positive
            p.MopUpCenterDistanceWeight = Math.Clamp(p.MopUpCenterDistanceWeight, 0, 30);
            p.MopUpKingProximityWeight = Math.Clamp(p.MopUpKingProximityWeight, 0, 20);

            // Knight outposts: positive
            p.KnightOutpostBonusMG = Math.Clamp(p.KnightOutpostBonusMG, 0, 50);
            p.KnightOutpostBonusEG = Math.Clamp(p.KnightOutpostBonusEG, 0, 50);
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
