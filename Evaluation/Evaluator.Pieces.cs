using System;

namespace ChessEngine
{
    public static partial class Evaluator
    {
        private static void EvaluateBishopPair(Board board, ref int mgScore, ref int egScore)
        {
            if (Bitboard.PopCount(board.WB) >= 2)
            {
                mgScore += P.BishopPairBonusMG;
                egScore += P.BishopPairBonusEG;
            }

            if (Bitboard.PopCount(board.BB) >= 2)
            {
                mgScore -= P.BishopPairBonusMG;
                egScore -= P.BishopPairBonusEG;
            }
        }

        private static void EvaluateBishopsQuality(Board board, ref int mgScore, ref int egScore)
        {
            ulong wBishops = board.WB;
            while (wBishops != 0)
            {
                int sq = Bitboard.PopLsb(ref wBishops);
                if (IsOnLongDiagonal(sq))
                {
                    mgScore += P.BishopLongDiagonalBonus;
                    egScore += P.BishopLongDiagonalBonus;
                }
                int sameColorPawns = CountSameColorPawns(board.WP, sq, true);
                if (sameColorPawns >= 2)
                {
                    mgScore += P.BadBishopPenalty;
                    egScore += P.BadBishopPenalty;
                }
            }
            ulong bBishops = board.BB;
            while (bBishops != 0)
            {
                int sq = Bitboard.PopLsb(ref bBishops);
                if (IsOnLongDiagonal(sq))
                {
                    mgScore -= P.BishopLongDiagonalBonus;
                    egScore -= P.BishopLongDiagonalBonus;
                }
                int sameColorPawns = CountSameColorPawns(board.BP, sq, false);
                if (sameColorPawns >= 2)
                {
                    mgScore -= P.BadBishopPenalty;
                    egScore -= P.BadBishopPenalty;
                }
            }
        }

        private static bool IsOnLongDiagonal(int sq)
        {
            int f = Bitboard.FileOf(sq);
            int r = Bitboard.RankOf(sq);
            return f == r || f + r == 7;
        }

        private static int CountSameColorPawns(ulong pawns, int bishopSq, bool whiteBishop)
        {
            bool bishopOnLight = IsLightSquare(bishopSq);
            int count = 0;
            while (pawns != 0)
            {
                int sq = Bitboard.PopLsb(ref pawns);
                if (IsLightSquare(sq) == bishopOnLight)
                {
                    count++;
                }
            }
            return count;
        }

        private static void EvaluateRooks(Board board, ref int mgScore, ref int egScore)
        {
            ulong allPawns = board.WP | board.BP;

            ulong rooks = board.WR;
            while (rooks != 0)
            {
                int sq = Bitboard.PopLsb(ref rooks);
                int file = Bitboard.FileOf(sq);
                ulong fileMask = Bitboard.FileMasks[file];

                if ((allPawns & fileMask) == 0)
                {
                    mgScore += P.RookOpenFileBonus;
                    egScore += P.RookOpenFileBonus;
                }
                else if ((board.WP & fileMask) == 0)
                {
                    mgScore += P.RookSemiOpenFileBonus;
                    egScore += P.RookSemiOpenFileBonus;
                }
            }

            rooks = board.BR;
            while (rooks != 0)
            {
                int sq = Bitboard.PopLsb(ref rooks);
                int file = Bitboard.FileOf(sq);
                ulong fileMask = Bitboard.FileMasks[file];

                if ((allPawns & fileMask) == 0)
                {
                    mgScore -= P.RookOpenFileBonus;
                    egScore -= P.RookOpenFileBonus;
                }
                else if ((board.BP & fileMask) == 0)
                {
                    mgScore -= P.RookSemiOpenFileBonus;
                    egScore -= P.RookSemiOpenFileBonus;
                }
            }
        }

        private static void EvaluateRookBehindPasser(Board board, ref int mgScore, ref int egScore,
            ulong wPassedPawns, ulong bPassedPawns)
        {
            ulong pp = wPassedPawns;
            while (pp != 0)
            {
                int psq = Bitboard.PopLsb(ref pp);
                int pfile = Bitboard.FileOf(psq);
                int prank = Bitboard.RankOf(psq);
                ulong fileMask = Bitboard.FileMasks[pfile];
                ulong rooksBehind = board.WR & fileMask;
                while (rooksBehind != 0)
                {
                    int rsq = Bitboard.PopLsb(ref rooksBehind);
                    if (Bitboard.RankOf(rsq) < prank)
                    {
                        mgScore += P.RookBehindPasserBonus;
                        egScore += P.RookBehindPasserBonus;
                    }
                }
            }

            pp = bPassedPawns;
            while (pp != 0)
            {
                int psq = Bitboard.PopLsb(ref pp);
                int pfile = Bitboard.FileOf(psq);
                int prank = Bitboard.RankOf(psq);
                ulong fileMask = Bitboard.FileMasks[pfile];
                ulong rooksBehind = board.BR & fileMask;
                while (rooksBehind != 0)
                {
                    int rsq = Bitboard.PopLsb(ref rooksBehind);
                    if (Bitboard.RankOf(rsq) > prank)
                    {
                        mgScore -= P.RookBehindPasserBonus;
                        egScore -= P.RookBehindPasserBonus;
                    }
                }
            }
        }

        private static void EvaluateRookOnSeventh(Board board, ref int mgScore, ref int egScore)
        {
            const int WhiteSeventhRank = 6;
            const int BlackSeventhRank = 1;

            int bkSq = board.BK != 0 ? Bitboard.BitScanForward(board.BK) : -1;
            int wkSq = board.WK != 0 ? Bitboard.BitScanForward(board.WK) : -1;
            bool blackKingOnSeventh = bkSq >= 0 && Bitboard.RankOf(bkSq) == BlackSeventhRank;
            bool whiteKingOnSeventh = wkSq >= 0 && Bitboard.RankOf(wkSq) == WhiteSeventhRank;

            ulong whiteSeventh = Bitboard.RankMasks[WhiteSeventhRank];
            ulong blackSeventh = Bitboard.RankMasks[BlackSeventhRank];

            ulong wRooks = board.WR & whiteSeventh;
            while (wRooks != 0)
            {
                Bitboard.PopLsb(ref wRooks);
                mgScore += P.RookOnSeventhBonusMG;
                egScore += P.RookOnSeventhBonusEG;
                if (blackKingOnSeventh)
                {
                    mgScore += P.RookOnSeventhWithKingBonus;
                    egScore += P.RookOnSeventhWithKingBonus;
                }
            }

            ulong bRooks = board.BR & blackSeventh;
            while (bRooks != 0)
            {
                Bitboard.PopLsb(ref bRooks);
                mgScore -= P.RookOnSeventhBonusMG;
                egScore -= P.RookOnSeventhBonusEG;
                if (whiteKingOnSeventh)
                {
                    mgScore -= P.RookOnSeventhWithKingBonus;
                    egScore -= P.RookOnSeventhWithKingBonus;
                }
            }
        }

        private static void EvaluateMobility(Board board, ref int mgScore, ref int egScore)
        {
            int wMobility = CountKnightMobility(board.WN, board.WhitePieces);
            wMobility += CountSlidingMobility(board.WB, board.WhitePieces, board, true);
            wMobility += CountSlidingMobility(board.WR, board.WhitePieces, board, false);

            int bMobility = CountKnightMobility(board.BN, board.BlackPieces);
            bMobility += CountSlidingMobility(board.BB, board.BlackPieces, board, true);
            bMobility += CountSlidingMobility(board.BR, board.BlackPieces, board, false);

            int wQueenMobility = CountQueenMobility(board.WQ, board.WhitePieces, board);
            int bQueenMobility = CountQueenMobility(board.BQ, board.BlackPieces, board);
            int totalMobilityDiff = (wMobility - bMobility) + (wQueenMobility - bQueenMobility);
            int bonus = P.MobilityBonus;
            mgScore += totalMobilityDiff * bonus;
            egScore += totalMobilityDiff * bonus;
        }

        private static int CountQueenMobility(ulong queens, ulong friendly, Board board)
        {
            int count = 0;
            while (queens != 0)
            {
                int sq = Bitboard.PopLsb(ref queens);
                ulong attacks = MagicBitboards.GetBishopAttacks(sq, board.AllPieces) |
                                MagicBitboards.GetRookAttacks(sq, board.AllPieces);
                attacks &= ~friendly;
                count += Bitboard.PopCount(attacks);
            }
            return count;
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

        private static void EvaluateOutposts(Board board, ref int mgScore, ref int egScore)
        {
            ulong wOutpostSquares = board.WN & (Bitboard.Rank4 | Bitboard.Rank5 | Bitboard.Rank6);
            while (wOutpostSquares != 0)
            {
                int sq = Bitboard.PopLsb(ref wOutpostSquares);
                int file = Bitboard.FileOf(sq);
                int rank = Bitboard.RankOf(sq);
                
                ulong attackMask = 0;
                for (int r = rank; r <= 7; r++)
                {
                    if (file > 0)
                    {
                        attackMask |= Bitboard.SquareBB[r * 8 + file - 1];
                    }
                    if (file < 7)
                    {
                        attackMask |= Bitboard.SquareBB[r * 8 + file + 1];
                    }
                }
                
                if ((board.BP & attackMask) == 0)
                {
                    mgScore += P.KnightOutpostBonusMG;
                    egScore += P.KnightOutpostBonusEG;
                }
            }

            ulong bOutpostSquares = board.BN & (Bitboard.Rank3 | Bitboard.Rank4 | Bitboard.Rank5);
            while (bOutpostSquares != 0)
            {
                int sq = Bitboard.PopLsb(ref bOutpostSquares);
                int file = Bitboard.FileOf(sq);
                int rank = Bitboard.RankOf(sq);
                
                ulong attackMask = 0;
                for (int r = rank; r >= 0; r--)
                {
                    if (file > 0)
                    {
                        attackMask |= Bitboard.SquareBB[r * 8 + file - 1];
                    }
                    if (file < 7)
                    {
                        attackMask |= Bitboard.SquareBB[r * 8 + file + 1];
                    }
                }
                
                if ((board.WP & attackMask) == 0)
                {
                    mgScore -= P.KnightOutpostBonusMG;
                    egScore -= P.KnightOutpostBonusEG;
                }
            }
        }

        private static void EvaluateQueenTropism(Board board, ref int mgScore, ref int egScore)
        {
            if (board.WQ == 0 && board.BQ == 0)
            {
                return;
            }
            int wkSq = board.WK != 0 ? Bitboard.BitScanForward(board.WK) : -1;
            int bkSq = board.BK != 0 ? Bitboard.BitScanForward(board.BK) : -1;
            if (wkSq < 0 || bkSq < 0)
            {
                return;
            }

            int wTropism = 0;
            ulong wQueens = board.WQ;
            while (wQueens != 0)
            {
                int sq = Bitboard.PopLsb(ref wQueens);
                int dist = ChebyshevDistance(sq, bkSq);
                wTropism += Math.Max(0, 8 - dist);
            }
            int bTropism = 0;
            ulong bQueens = board.BQ;
            while (bQueens != 0)
            {
                int sq = Bitboard.PopLsb(ref bQueens);
                int dist = ChebyshevDistance(sq, wkSq);
                bTropism += Math.Max(0, 8 - dist);
            }
            mgScore += (wTropism - bTropism) * P.QueenTropismBonus;
            egScore += (wTropism - bTropism) * P.QueenTropismBonus;
        }

        private static void EvaluateKnightTropism(Board board, ref int mgScore, ref int egScore)
        {
            if (board.WN == 0 && board.BN == 0)
            {
                return;
            }
            int wkSq = board.WK != 0 ? Bitboard.BitScanForward(board.WK) : -1;
            int bkSq = board.BK != 0 ? Bitboard.BitScanForward(board.BK) : -1;
            if (wkSq < 0 || bkSq < 0)
            {
                return;
            }

            int wTropism = 0;
            ulong wKnights = board.WN;
            while (wKnights != 0)
            {
                int sq = Bitboard.PopLsb(ref wKnights);
                int dist = ChebyshevDistance(sq, bkSq);
                wTropism += Math.Max(0, 7 - dist);
            }
            int bTropism = 0;
            ulong bKnights = board.BN;
            while (bKnights != 0)
            {
                int sq = Bitboard.PopLsb(ref bKnights);
                int dist = ChebyshevDistance(sq, wkSq);
                bTropism += Math.Max(0, 7 - dist);
            }
            mgScore += (wTropism - bTropism) * P.KnightTropismMG;
        }
    }
}
