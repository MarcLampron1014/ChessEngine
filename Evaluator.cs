using System;
using System.Runtime.CompilerServices;

namespace ChessEngine
{
    public static class Evaluator
    {
        // Phase weights for each piece type (total = 24 for full material)
        private const int KnightPhase = 1;
        private const int BishopPhase = 1;
        private const int RookPhase = 2;
        private const int QueenPhase = 4;
        private const int TotalPhase = 24; // 4*1 + 4*1 + 4*2 + 2*4 = 24

        // Precomputed lookup tables for performance
        private static readonly ulong[,] PassedPawnMasks = new ulong[2, 64]; // [white=1/black=0, square]
        private static readonly int[,] ChebyshevDistanceTable = new int[64, 64];

        static Evaluator()
        {
            // Initialize passed pawn masks
            for (int sq = 0; sq < 64; sq++)
            {
                PassedPawnMasks[0, sq] = ComputePassedPawnMask(sq, false);
                PassedPawnMasks[1, sq] = ComputePassedPawnMask(sq, true);
            }

            // Initialize Chebyshev distance table
            for (int sq1 = 0; sq1 < 64; sq1++)
            {
                int f1 = Bitboard.FileOf(sq1);
                int r1 = Bitboard.RankOf(sq1);
                for (int sq2 = 0; sq2 < 64; sq2++)
                {
                    int f2 = Bitboard.FileOf(sq2);
                    int r2 = Bitboard.RankOf(sq2);
                    ChebyshevDistanceTable[sq1, sq2] = Math.Max(Math.Abs(f1 - f2), Math.Abs(r1 - r2));
                }
            }
        }

        private static ulong ComputePassedPawnMask(int sq, bool white)
        {
            int file = Bitboard.FileOf(sq);
            int rank = Bitboard.RankOf(sq);

            ulong mask = 0;
            ulong fileMask = Bitboard.FileMasks[file];
            if (file > 0) fileMask |= Bitboard.FileMasks[file - 1];
            if (file < 7) fileMask |= Bitboard.FileMasks[file + 1];

            if (white)
            {
                for (int r = rank + 1; r <= 7; r++)
                    mask |= fileMask & Bitboard.RankMasks[r];
            }
            else
            {
                for (int r = rank - 1; r >= 0; r--)
                    mask |= fileMask & Bitboard.RankMasks[r];
            }

            return mask;
        }

        // Middlegame piece values
        private const int PawnValueMG = 100;
        private const int KnightValueMG = 320;
        private const int BishopValueMG = 330;
        private const int RookValueMG = 500;
        private const int QueenValueMG = 900;

        // Endgame piece values (knights worth less, rooks worth more)
        private const int PawnValueEG = 100;
        private const int KnightValueEG = 280;
        private const int BishopValueEG = 320;
        private const int RookValueEG = 550;
        private const int QueenValueEG = 950;

        // Evaluation bonuses/penalties
        private const int BishopPairBonusMG = 30;
        private const int BishopPairBonusEG = 50;
        private const int RookOpenFileBonusMG = 20;
        private const int RookOpenFileBonusEG = 15;
        private const int RookSemiOpenFileBonusMG = 10;
        private const int RookSemiOpenFileBonusEG = 8;
        private const int DoubledPawnPenaltyMG = -10;
        private const int DoubledPawnPenaltyEG = -20;
        private const int IsolatedPawnPenaltyMG = -15;
        private const int IsolatedPawnPenaltyEG = -25;
        private const int MobilityBonusMG = 3;
        private const int MobilityBonusEG = 2;
        private const int KingShieldBonus = 10;
        private const int RookBehindPasserBonus = 30;
        private const int ConnectedPasserBonus = 20;

        // Middlegame PSTs
        private static readonly int[] PawnPstMG =
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

        private static readonly int[] PawnPstEG =
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

        private static readonly int[] KnightPstMG =
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

        private static readonly int[] KnightPstEG =
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

        private static readonly int[] BishopPstMG =
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

        private static readonly int[] BishopPstEG =
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

        private static readonly int[] RookPstMG =
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

        private static readonly int[] RookPstEG =
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

        private static readonly int[] QueenPstMG =
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

        private static readonly int[] QueenPstEG =
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

        private static readonly int[] KingPstMG =
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

        private static readonly int[] KingPstEG =
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

        // Passed pawn bonuses by rank (MG and EG)
        private static readonly int[] PassedPawnBonusMG = { 0, 5, 10, 20, 35, 60, 100, 0 };
        private static readonly int[] PassedPawnBonusEG = { 0, 15, 25, 40, 65, 100, 150, 0 };

        // Connected passed pawn bonus by rank (additional to regular passed pawn bonus)
        private static readonly int[] ConnectedPasserBonusByRank = { 0, 5, 10, 15, 30, 50, 80, 0 };

        // Center distance for mop-up evaluation
        private static readonly int[] CenterManhattanDistance =
        {
            6, 5, 4, 3, 3, 4, 5, 6,
            5, 4, 3, 2, 2, 3, 4, 5,
            4, 3, 2, 1, 1, 2, 3, 4,
            3, 2, 1, 0, 0, 1, 2, 3,
            3, 2, 1, 0, 0, 1, 2, 3,
            4, 3, 2, 1, 1, 2, 3, 4,
            5, 4, 3, 2, 2, 3, 4, 5,
            6, 5, 4, 3, 3, 4, 5, 6,
        };

        public static int Evaluate(Board board)
        {
            int mgScore = 0;
            int egScore = 0;

            // Calculate game phase
            int phase = CalculatePhase(board);

            // Material and PST evaluation
            EvaluateMaterialAndPST(board, ref mgScore, ref egScore);

            // Pawn structure
            EvaluatePawnStructure(board, ref mgScore, ref egScore);

            // Bishop pair
            EvaluateBishopPair(board, ref mgScore, ref egScore);

            // Rook on open/semi-open files
            EvaluateRooks(board, ref mgScore, ref egScore);

            // Mobility
            EvaluateMobility(board, ref mgScore, ref egScore);

            // King safety (middlegame only)
            EvaluateKingSafety(board, ref mgScore);

            // Endgame-specific evaluations
            EvaluateEndgame(board, ref egScore, phase);

            // Tapered evaluation: interpolate between MG and EG
            int score = (mgScore * phase + egScore * (TotalPhase - phase)) / TotalPhase;

            // Return from perspective of side to move
            return board.WhiteToMove ? score : -score;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CalculatePhase(Board board)
        {
            // Use precomputed phase from board (updated incrementally in MakeMove/UndoMove)
            return Math.Min(board.Phase, TotalPhase);
        }

        private static void EvaluateMaterialAndPST(Board board, ref int mgScore, ref int egScore)
        {
            // White pawns
            ulong bb = board.WP;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore += PawnValueMG + PawnPstMG[sq];
                egScore += PawnValueEG + PawnPstEG[sq];
            }

            // Black pawns
            bb = board.BP;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore -= PawnValueMG + PawnPstMG[Bitboard.MirrorSquare(sq)];
                egScore -= PawnValueEG + PawnPstEG[Bitboard.MirrorSquare(sq)];
            }

            // White knights
            bb = board.WN;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore += KnightValueMG + KnightPstMG[sq];
                egScore += KnightValueEG + KnightPstEG[sq];
            }

            // Black knights
            bb = board.BN;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore -= KnightValueMG + KnightPstMG[Bitboard.MirrorSquare(sq)];
                egScore -= KnightValueEG + KnightPstEG[Bitboard.MirrorSquare(sq)];
            }

            // White bishops
            bb = board.WB;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore += BishopValueMG + BishopPstMG[sq];
                egScore += BishopValueEG + BishopPstEG[sq];
            }

            // Black bishops
            bb = board.BB;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore -= BishopValueMG + BishopPstMG[Bitboard.MirrorSquare(sq)];
                egScore -= BishopValueEG + BishopPstEG[Bitboard.MirrorSquare(sq)];
            }

            // White rooks
            bb = board.WR;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore += RookValueMG + RookPstMG[sq];
                egScore += RookValueEG + RookPstEG[sq];
            }

            // Black rooks
            bb = board.BR;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore -= RookValueMG + RookPstMG[Bitboard.MirrorSquare(sq)];
                egScore -= RookValueEG + RookPstEG[Bitboard.MirrorSquare(sq)];
            }

            // White queens
            bb = board.WQ;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore += QueenValueMG + QueenPstMG[sq];
                egScore += QueenValueEG + QueenPstEG[sq];
            }

            // Black queens
            bb = board.BQ;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore -= QueenValueMG + QueenPstMG[Bitboard.MirrorSquare(sq)];
                egScore -= QueenValueEG + QueenPstEG[Bitboard.MirrorSquare(sq)];
            }

            // White king
            int wkSq = Bitboard.BitScanForward(board.WK);
            mgScore += KingPstMG[wkSq];
            egScore += KingPstEG[wkSq];

            // Black king
            int bkSq = Bitboard.BitScanForward(board.BK);
            mgScore -= KingPstMG[Bitboard.MirrorSquare(bkSq)];
            egScore -= KingPstEG[Bitboard.MirrorSquare(bkSq)];
        }

        private static void EvaluatePawnStructure(Board board, ref int mgScore, ref int egScore)
        {
            // White pawn structure
            EvaluatePawnStructureSide(board, board.WP, board.BP, true, ref mgScore, ref egScore);

            // Black pawn structure
            EvaluatePawnStructureSide(board, board.BP, board.WP, false, ref mgScore, ref egScore);
        }

        private static void EvaluatePawnStructureSide(Board board, ulong friendlyPawns, ulong enemyPawns, 
            bool white, ref int mgScore, ref int egScore)
        {
            int sign = white ? 1 : -1;
            ulong friendlyRooks = white ? board.WR : board.BR;

            // Doubled pawns
            for (int file = 0; file < 8; file++)
            {
                ulong fileMask = Bitboard.FileMasks[file];
                int pawnsOnFile = Bitboard.PopCount(friendlyPawns & fileMask);
                if (pawnsOnFile > 1)
                {
                    mgScore += sign * DoubledPawnPenaltyMG * (pawnsOnFile - 1);
                    egScore += sign * DoubledPawnPenaltyEG * (pawnsOnFile - 1);
                }
            }

            // Track passed pawns for connected passer detection
            ulong passedPawns = 0;

            // Isolated and passed pawns
            ulong pawns = friendlyPawns;
            while (pawns != 0)
            {
                int sq = Bitboard.PopLsb(ref pawns);
                int file = Bitboard.FileOf(sq);
                int rank = Bitboard.RankOf(sq);

                // Isolated pawn check
                ulong adjacentFiles = Bitboard.AdjacentFiles[file];
                if ((friendlyPawns & adjacentFiles) == 0)
                {
                    mgScore += sign * IsolatedPawnPenaltyMG;
                    egScore += sign * IsolatedPawnPenaltyEG;
                }

                // Passed pawn check
                ulong passedMask = GetPassedPawnMask(sq, white);
                if ((enemyPawns & passedMask) == 0)
                {
                    passedPawns |= Bitboard.SquareBB[sq];
                    int effectiveRank = white ? rank : 7 - rank;
                    mgScore += sign * PassedPawnBonusMG[effectiveRank];
                    egScore += sign * PassedPawnBonusEG[effectiveRank];

                    // Rook behind passed pawn bonus
                    ulong fileMask = Bitboard.FileMasks[file];
                    ulong rooksBehind = friendlyRooks & fileMask;
                    if (rooksBehind != 0)
                    {
                        // Check if rook is actually behind the pawn
                        while (rooksBehind != 0)
                        {
                            int rookSq = Bitboard.PopLsb(ref rooksBehind);
                            int rookRank = Bitboard.RankOf(rookSq);
                            bool isBehind = white ? (rookRank < rank) : (rookRank > rank);
                            if (isBehind)
                            {
                                egScore += sign * RookBehindPasserBonus;
                                break;
                            }
                        }
                    }
                }
            }

            // Connected passed pawns bonus
            ulong connectedPassers = passedPawns;
            while (connectedPassers != 0)
            {
                int sq = Bitboard.PopLsb(ref connectedPassers);
                int file = Bitboard.FileOf(sq);
                int rank = Bitboard.RankOf(sq);

                // Check for adjacent passed pawn
                ulong adjacentFiles = Bitboard.AdjacentFiles[file];
                if ((passedPawns & adjacentFiles) != 0)
                {
                    int effectiveRank = white ? rank : 7 - rank;
                    mgScore += sign * ConnectedPasserBonusByRank[effectiveRank] / 2; // Divide by 2 to avoid double counting
                    egScore += sign * ConnectedPasserBonusByRank[effectiveRank];
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong GetPassedPawnMask(int sq, bool white) => PassedPawnMasks[white ? 1 : 0, sq];

        private static void EvaluateBishopPair(Board board, ref int mgScore, ref int egScore)
        {
            if (Bitboard.PopCount(board.WB) >= 2)
            {
                mgScore += BishopPairBonusMG;
                egScore += BishopPairBonusEG;
            }

            if (Bitboard.PopCount(board.BB) >= 2)
            {
                mgScore -= BishopPairBonusMG;
                egScore -= BishopPairBonusEG;
            }
        }

        private static void EvaluateRooks(Board board, ref int mgScore, ref int egScore)
        {
            ulong allPawns = board.WP | board.BP;

            // White rooks
            ulong rooks = board.WR;
            while (rooks != 0)
            {
                int sq = Bitboard.PopLsb(ref rooks);
                int file = Bitboard.FileOf(sq);
                ulong fileMask = Bitboard.FileMasks[file];

                if ((allPawns & fileMask) == 0)
                {
                    mgScore += RookOpenFileBonusMG;
                    egScore += RookOpenFileBonusEG;
                }
                else if ((board.WP & fileMask) == 0)
                {
                    mgScore += RookSemiOpenFileBonusMG;
                    egScore += RookSemiOpenFileBonusEG;
                }
            }

            // Black rooks
            rooks = board.BR;
            while (rooks != 0)
            {
                int sq = Bitboard.PopLsb(ref rooks);
                int file = Bitboard.FileOf(sq);
                ulong fileMask = Bitboard.FileMasks[file];

                if ((allPawns & fileMask) == 0)
                {
                    mgScore -= RookOpenFileBonusMG;
                    egScore -= RookOpenFileBonusEG;
                }
                else if ((board.BP & fileMask) == 0)
                {
                    mgScore -= RookSemiOpenFileBonusMG;
                    egScore -= RookSemiOpenFileBonusEG;
                }
            }
        }

        private static void EvaluateMobility(Board board, ref int mgScore, ref int egScore)
        {
            // White mobility
            int wMobility = CountKnightMobility(board.WN, board.WhitePieces);
            wMobility += CountSlidingMobility(board.WB, board.WhitePieces, board, true);
            wMobility += CountSlidingMobility(board.WR, board.WhitePieces, board, false);

            // Black mobility
            int bMobility = CountKnightMobility(board.BN, board.BlackPieces);
            bMobility += CountSlidingMobility(board.BB, board.BlackPieces, board, true);
            bMobility += CountSlidingMobility(board.BR, board.BlackPieces, board, false);

            int mobilityDiff = wMobility - bMobility;
            mgScore += mobilityDiff * MobilityBonusMG;
            egScore += mobilityDiff * MobilityBonusEG;
        }

        private static int CountKnightMobility(ulong pieces, ulong friendly)
        {
            int count = 0;
            while (pieces != 0)
            {
                int sq = Bitboard.PopLsb(ref pieces);
                ulong attacks = Bitboard.KnightAttacks[sq] & ~friendly;
                count += Bitboard.PopCount(attacks);
            }
            return count;
        }

        private static int CountSlidingMobility(ulong pieces, ulong friendly, Board board, bool isBishop)
        {
            int count = 0;
            while (pieces != 0)
            {
                int sq = Bitboard.PopLsb(ref pieces);
                ulong attacks = isBishop
                    ? MagicBitboards.GetBishopAttacks(sq, board.AllPieces)
                    : MagicBitboards.GetRookAttacks(sq, board.AllPieces);
                attacks &= ~friendly;
                count += Bitboard.PopCount(attacks);
            }
            return count;
        }

        private static void EvaluateKingSafety(Board board, ref int mgScore)
        {
            // Only evaluate king safety if queens are on the board
            if (board.WQ != 0 || board.BQ != 0)
            {
                mgScore += EvaluateKingSafetySide(board, true);
                mgScore -= EvaluateKingSafetySide(board, false);
            }
        }

        private static int EvaluateKingSafetySide(Board board, bool white)
        {
            int score = 0;
            ulong king = white ? board.WK : board.BK;
            ulong friendlyPawns = white ? board.WP : board.BP;

            if (king == 0) return 0;

            int kingSq = Bitboard.BitScanForward(king);
            int kingFile = Bitboard.FileOf(kingSq);

            // Pawn shield bonus
            ulong shieldMask = Bitboard.KingAttacks[kingSq];
            if (white)
                shieldMask &= Bitboard.Rank2 | Bitboard.Rank3;
            else
                shieldMask &= Bitboard.Rank6 | Bitboard.Rank7;

            int shieldPawns = Bitboard.PopCount(friendlyPawns & shieldMask);
            score += shieldPawns * KingShieldBonus;

            // Penalty for open files near king
            for (int f = Math.Max(0, kingFile - 1); f <= Math.Min(7, kingFile + 1); f++)
            {
                ulong fileMask = Bitboard.FileMasks[f];
                if ((friendlyPawns & fileMask) == 0)
                    score -= 10;
            }

            return score;
        }

        private static void EvaluateEndgame(Board board, ref int egScore, int phase)
        {
            // Only apply endgame-specific terms when transitioning to endgame
            if (phase >= TotalPhase - 4) return; // Still in middlegame

            int wkSq = Bitboard.BitScanForward(board.WK);
            int bkSq = Bitboard.BitScanForward(board.BK);

            // King-pawn proximity in endgame
            EvaluateKingPawnProximity(board, wkSq, bkSq, ref egScore);

            // Mop-up evaluation for winning positions
            int materialBalance = GetMaterialBalance(board);
            if (Math.Abs(materialBalance) >= 400)
            {
                EvaluateMopUp(wkSq, bkSq, materialBalance, ref egScore);
            }
        }

        private static void EvaluateKingPawnProximity(Board board, int wkSq, int bkSq, ref int egScore)
        {
            // White king proximity to passed pawns
            ulong wPassedPawns = GetPassedPawns(board.WP, board.BP, true);
            ulong bPassedPawns = GetPassedPawns(board.BP, board.WP, false);

            // White king should be close to own passers (to escort) and enemy passers (to stop)
            ulong passers = wPassedPawns;
            while (passers != 0)
            {
                int sq = Bitboard.PopLsb(ref passers);
                int dist = ChebyshevDistance(wkSq, sq);
                egScore += (7 - dist) * 5; // Bonus for white king near own passer
            }

            passers = bPassedPawns;
            while (passers != 0)
            {
                int sq = Bitboard.PopLsb(ref passers);
                int dist = ChebyshevDistance(wkSq, sq);
                egScore += (7 - dist) * 3; // Bonus for white king near enemy passer (to stop it)
            }

            // Black king proximity
            passers = bPassedPawns;
            while (passers != 0)
            {
                int sq = Bitboard.PopLsb(ref passers);
                int dist = ChebyshevDistance(bkSq, sq);
                egScore -= (7 - dist) * 5;
            }

            passers = wPassedPawns;
            while (passers != 0)
            {
                int sq = Bitboard.PopLsb(ref passers);
                int dist = ChebyshevDistance(bkSq, sq);
                egScore -= (7 - dist) * 3;
            }
        }

        private static ulong GetPassedPawns(ulong friendlyPawns, ulong enemyPawns, bool white)
        {
            ulong passed = 0;
            ulong pawns = friendlyPawns;
            while (pawns != 0)
            {
                int sq = Bitboard.PopLsb(ref pawns);
                ulong passedMask = GetPassedPawnMask(sq, white);
                if ((enemyPawns & passedMask) == 0)
                    passed |= Bitboard.SquareBB[sq];
            }
            return passed;
        }

        private static void EvaluateMopUp(int wkSq, int bkSq, int materialBalance, ref int egScore)
        {
            // When winning, drive enemy king to corner and get our king close
            if (materialBalance > 0)
            {
                // White is winning - push black king to corner
                int enemyCenterDist = CenterManhattanDistance[bkSq];
                egScore += enemyCenterDist * 10;

                // Bonus for king proximity
                int kingDist = ChebyshevDistance(wkSq, bkSq);
                egScore += (14 - kingDist) * 4;
            }
            else
            {
                // Black is winning - push white king to corner
                int enemyCenterDist = CenterManhattanDistance[wkSq];
                egScore -= enemyCenterDist * 10;

                int kingDist = ChebyshevDistance(wkSq, bkSq);
                egScore -= (14 - kingDist) * 4;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ChebyshevDistance(int sq1, int sq2) => ChebyshevDistanceTable[sq1, sq2];

        private static int GetMaterialBalance(Board board)
        {
            int score = 0;
            score += Bitboard.PopCount(board.WP) * PawnValueMG;
            score += Bitboard.PopCount(board.WN) * KnightValueMG;
            score += Bitboard.PopCount(board.WB) * BishopValueMG;
            score += Bitboard.PopCount(board.WR) * RookValueMG;
            score += Bitboard.PopCount(board.WQ) * QueenValueMG;
            score -= Bitboard.PopCount(board.BP) * PawnValueMG;
            score -= Bitboard.PopCount(board.BN) * KnightValueMG;
            score -= Bitboard.PopCount(board.BB) * BishopValueMG;
            score -= Bitboard.PopCount(board.BR) * RookValueMG;
            score -= Bitboard.PopCount(board.BQ) * QueenValueMG;
            return score;
        }

        public static int GetPieceValue(Piece p)
        {
            return p switch
            {
                Piece.WP or Piece.BP => PawnValueMG,
                Piece.WN or Piece.BN => KnightValueMG,
                Piece.WB or Piece.BB => BishopValueMG,
                Piece.WR or Piece.BR => RookValueMG,
                Piece.WQ or Piece.BQ => QueenValueMG,
                Piece.WK or Piece.BK => 20000,
                _ => 0
            };
        }

        /// <summary>
        /// Check if the position has insufficient material to mate.
        /// </summary>
        public static bool IsInsufficientMaterial(Board board)
        {
            // If there are pawns, rooks, or queens, there's sufficient material
            if ((board.WP | board.BP | board.WR | board.BR | board.WQ | board.BQ) != 0)
                return false;

            int wMinors = Bitboard.PopCount(board.WN | board.WB);
            int bMinors = Bitboard.PopCount(board.BN | board.BB);

            // KvK
            if (wMinors == 0 && bMinors == 0)
                return true;

            // KNvK or KBvK
            if (wMinors <= 1 && bMinors == 0)
                return true;
            if (bMinors <= 1 && wMinors == 0)
                return true;

            // KBvKB with same-colored bishops
            if (wMinors == 1 && bMinors == 1 && board.WN == 0 && board.BN == 0)
            {
                // Check if bishops are on same color
                int wbSq = Bitboard.BitScanForward(board.WB);
                int bbSq = Bitboard.BitScanForward(board.BB);
                bool wbLight = ((Bitboard.FileOf(wbSq) + Bitboard.RankOf(wbSq)) % 2) == 1;
                bool bbLight = ((Bitboard.FileOf(bbSq) + Bitboard.RankOf(bbSq)) % 2) == 1;
                if (wbLight == bbLight)
                    return true;
            }

            return false;
        }
    }
}
