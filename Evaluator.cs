using System;
using System.Runtime.CompilerServices;

namespace ChessEngine
{
    public static class Evaluator
    {
        private const int PawnValue = 100;
        private const int KnightValue = 320;
        private const int BishopValue = 330;
        private const int RookValue = 500;
        private const int QueenValue = 900;

        private const int BishopPairBonus = 30;
        private const int RookOpenFileBonus = 20;
        private const int RookSemiOpenFileBonus = 10;
        private const int DoubledPawnPenalty = -10;
        private const int IsolatedPawnPenalty = -20;
        private const int MobilityBonus = 2;
        private const int KingShieldBonus = 10;

        private static readonly int[] PawnPst =
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

        private static readonly int[] KnightPst =
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

        private static readonly int[] BishopPst =
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

        private static readonly int[] RookPst =
        {
            0, 0, 0, 0, 0, 0, 0, 0,
            5, 10, 10, 10, 10, 10, 10, 5,
            -5, 0, 0, 0, 0, 0, 0, -5,
            -5, 0, 0, 0, 0, 0, 0, -5,
            -5, 0, 0, 0, 0, 0, 0, -5,
            -5, 0, 0, 0, 0, 0, 0, -5,
            -5, 0, 0, 0, 0, 0, 0, -5,
            0, 0, 0, 5, 5, 0, 0, 0,
        };

        private static readonly int[] QueenPst =
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

        private static readonly int[] KingMiddlegamePst =
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

        private static readonly int[] KingEndgamePst =
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

        private static readonly int[] PassedPawnBonusByRank = { 0, 10, 20, 30, 50, 80, 120, 0 };

        public static int Evaluate(Board board)
        {
            int score = 0;

            // Material evaluation using PopCount (fast)
            score += EvaluateMaterial(board);

            // Piece-square tables
            score += EvaluatePST(board);

            // Pawn structure
            score += EvaluatePawnStructure(board);

            // Bishop pair
            score += EvaluateBishopPair(board);

            // Rook on open/semi-open files
            score += EvaluateRooks(board);

            // Mobility (simplified - count attacks)
            score += EvaluateMobility(board);

            // King safety
            score += EvaluateKingSafety(board);

            // Return from perspective of side to move
            return board.WhiteToMove ? score : -score;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int EvaluateMaterial(Board board)
        {
            int score = 0;

            score += Bitboard.PopCount(board.WP) * PawnValue;
            score += Bitboard.PopCount(board.WN) * KnightValue;
            score += Bitboard.PopCount(board.WB) * BishopValue;
            score += Bitboard.PopCount(board.WR) * RookValue;
            score += Bitboard.PopCount(board.WQ) * QueenValue;

            score -= Bitboard.PopCount(board.BP) * PawnValue;
            score -= Bitboard.PopCount(board.BN) * KnightValue;
            score -= Bitboard.PopCount(board.BB) * BishopValue;
            score -= Bitboard.PopCount(board.BR) * RookValue;
            score -= Bitboard.PopCount(board.BQ) * QueenValue;

            return score;
        }

        private static int EvaluatePST(Board board)
        {
            int score = 0;

            // White pieces
            ulong bb = board.WP;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                score += PawnPst[sq];
            }

            bb = board.WN;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                score += KnightPst[sq];
            }

            bb = board.WB;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                score += BishopPst[sq];
            }

            bb = board.WR;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                score += RookPst[sq];
            }

            bb = board.WQ;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                score += QueenPst[sq];
            }

            // White king (use endgame PST if few pieces)
            int totalPieces = Bitboard.PopCount(board.AllPieces);
            int[] kingPst = totalPieces <= 12 ? KingEndgamePst : KingMiddlegamePst;
            int wkSq = Bitboard.BitScanForward(board.WK);
            score += kingPst[wkSq];

            // Black pieces (mirror square for PST)
            bb = board.BP;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                score -= PawnPst[Bitboard.MirrorSquare(sq)];
            }

            bb = board.BN;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                score -= KnightPst[Bitboard.MirrorSquare(sq)];
            }

            bb = board.BB;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                score -= BishopPst[Bitboard.MirrorSquare(sq)];
            }

            bb = board.BR;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                score -= RookPst[Bitboard.MirrorSquare(sq)];
            }

            bb = board.BQ;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                score -= QueenPst[Bitboard.MirrorSquare(sq)];
            }

            int bkSq = Bitboard.BitScanForward(board.BK);
            score -= kingPst[Bitboard.MirrorSquare(bkSq)];

            return score;
        }

        private static int EvaluatePawnStructure(Board board)
        {
            int score = 0;

            // White pawn structure
            score += EvaluatePawnStructureSide(board.WP, board.BP, true);

            // Black pawn structure
            score -= EvaluatePawnStructureSide(board.BP, board.WP, false);

            return score;
        }

        private static int EvaluatePawnStructureSide(ulong friendlyPawns, ulong enemyPawns, bool white)
        {
            int score = 0;

            // Doubled pawns (pawns on same file)
            for (int file = 0; file < 8; file++)
            {
                ulong fileMask = Bitboard.FileMasks[file];
                int pawnsOnFile = Bitboard.PopCount(friendlyPawns & fileMask);
                if (pawnsOnFile > 1)
                {
                    score += DoubledPawnPenalty * (pawnsOnFile - 1);
                }
            }

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
                    score += IsolatedPawnPenalty;
                }

                // Passed pawn check
                ulong passedMask = GetPassedPawnMask(sq, white);
                if ((enemyPawns & passedMask) == 0)
                {
                    // Passed pawn bonus based on rank
                    int effectiveRank = white ? rank : 7 - rank;
                    score += PassedPawnBonusByRank[effectiveRank];
                }
            }

            return score;
        }

        private static ulong GetPassedPawnMask(int sq, bool white)
        {
            int file = Bitboard.FileOf(sq);
            int rank = Bitboard.RankOf(sq);

            ulong mask = 0;

            // Include own file and adjacent files
            ulong fileMask = Bitboard.FileMasks[file];
            if (file > 0) fileMask |= Bitboard.FileMasks[file - 1];
            if (file < 7) fileMask |= Bitboard.FileMasks[file + 1];

            // Only include ranks ahead of the pawn
            if (white)
            {
                for (int r = rank + 1; r <= 7; r++)
                {
                    mask |= fileMask & Bitboard.RankMasks[r];
                }
            }
            else
            {
                for (int r = rank - 1; r >= 0; r--)
                {
                    mask |= fileMask & Bitboard.RankMasks[r];
                }
            }

            return mask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int EvaluateBishopPair(Board board)
        {
            int score = 0;

            if (Bitboard.PopCount(board.WB) >= 2)
                score += BishopPairBonus;

            if (Bitboard.PopCount(board.BB) >= 2)
                score -= BishopPairBonus;

            return score;
        }

        private static int EvaluateRooks(Board board)
        {
            int score = 0;
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
                    score += RookOpenFileBonus; // Open file
                }
                else if ((board.WP & fileMask) == 0)
                {
                    score += RookSemiOpenFileBonus; // Semi-open file
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
                    score -= RookOpenFileBonus;
                }
                else if ((board.BP & fileMask) == 0)
                {
                    score -= RookSemiOpenFileBonus;
                }
            }

            return score;
        }

        private static int EvaluateMobility(Board board)
        {
            int score = 0;

            // White mobility
            score += CountMobility(board.WN, board.WhitePieces, board, false) * MobilityBonus;
            score += CountSlidingMobility(board.WB, board.WhitePieces, board, true) * MobilityBonus;
            score += CountSlidingMobility(board.WR, board.WhitePieces, board, false) * MobilityBonus;

            // Black mobility
            score -= CountMobility(board.BN, board.BlackPieces, board, false) * MobilityBonus;
            score -= CountSlidingMobility(board.BB, board.BlackPieces, board, true) * MobilityBonus;
            score -= CountSlidingMobility(board.BR, board.BlackPieces, board, false) * MobilityBonus;

            return score;
        }

        private static int CountMobility(ulong pieces, ulong friendly, Board board, bool isBishop)
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
                ulong attacks;
                if (isBishop)
                    attacks = MagicBitboards.GetBishopAttacks(sq, board.AllPieces);
                else
                    attacks = MagicBitboards.GetRookAttacks(sq, board.AllPieces);

                attacks &= ~friendly;
                count += Bitboard.PopCount(attacks);
            }
            return count;
        }

        private static int EvaluateKingSafety(Board board)
        {
            int score = 0;

            // Only evaluate king safety if queens are on the board
            if (board.WQ != 0 || board.BQ != 0)
            {
                score += EvaluateKingSafetySide(board, true);
                score -= EvaluateKingSafetySide(board, false);
            }

            return score;
        }

        private static int EvaluateKingSafetySide(Board board, bool white)
        {
            int score = 0;
            ulong king = white ? board.WK : board.BK;
            ulong friendlyPawns = white ? board.WP : board.BP;

            if (king == 0) return 0;

            int kingSq = Bitboard.BitScanForward(king);
            int kingFile = Bitboard.FileOf(kingSq);
            int kingRank = Bitboard.RankOf(kingSq);

            // Pawn shield bonus
            ulong shieldMask = Bitboard.KingAttacks[kingSq];
            if (white)
            {
                shieldMask &= Bitboard.Rank2 | Bitboard.Rank3;
            }
            else
            {
                shieldMask &= Bitboard.Rank6 | Bitboard.Rank7;
            }

            int shieldPawns = Bitboard.PopCount(friendlyPawns & shieldMask);
            score += shieldPawns * KingShieldBonus;

            // Penalty for open files near king
            for (int f = Math.Max(0, kingFile - 1); f <= Math.Min(7, kingFile + 1); f++)
            {
                ulong fileMask = Bitboard.FileMasks[f];
                if ((friendlyPawns & fileMask) == 0)
                {
                    score -= 10; // Penalty for open file near king
                }
            }

            return score;
        }

        public static int GetPieceValue(Piece p)
        {
            return p switch
            {
                Piece.WP or Piece.BP => PawnValue,
                Piece.WN or Piece.BN => KnightValue,
                Piece.WB or Piece.BB => BishopValue,
                Piece.WR or Piece.BR => RookValue,
                Piece.WQ or Piece.BQ => QueenValue,
                Piece.WK or Piece.BK => 20000,
                _ => 0
            };
        }
    }
}
