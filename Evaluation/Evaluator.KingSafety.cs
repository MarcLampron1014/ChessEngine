using System;

namespace ChessEngine
{
    public static partial class Evaluator
    {
        private const int MaxAttackWeight = 10;
        private const int PawnStormPhaseThreshold = 12;

        private static void EvaluateKingSafety(Board board, ref int mgScore, int phase)
        {
            if (board.WQ == 0 && board.BQ == 0)
            {
                return;
            }
            int rawScore = EvaluateKingSafetySide(board, true) - EvaluateKingSafetySide(board, false);
            mgScore += rawScore * phase / P.TotalPhase;
        }

        private static int EvaluateKingSafetySide(Board board, bool white)
        {
            int score = 0;
            ulong king = white ? board.WK : board.BK;
            ulong friendlyPawns = white ? board.WP : board.BP;

            if (king == 0)
            {
                return 0;
            }

            int kingSq = Bitboard.BitScanForward(king);
            int kingFile = Bitboard.FileOf(kingSq);
            int kingRank = Bitboard.RankOf(kingSq);

            ulong zone = Bitboard.KingAttacks[kingSq];
            if (white && kingRank > 0)
            {
                ulong frontRank = Bitboard.RankMasks[kingRank - 1];
                ulong frontFiles = (kingFile > 0 ? Bitboard.FileMasks[kingFile - 1] : 0) | Bitboard.FileMasks[kingFile] | (kingFile < 7 ? Bitboard.FileMasks[kingFile + 1] : 0);
                zone |= frontRank & frontFiles;
            }
            else if (!white && kingRank < 7)
            {
                ulong frontRank = Bitboard.RankMasks[kingRank + 1];
                ulong frontFiles = (kingFile > 0 ? Bitboard.FileMasks[kingFile - 1] : 0) | Bitboard.FileMasks[kingFile] | (kingFile < 7 ? Bitboard.FileMasks[kingFile + 1] : 0);
                zone |= frontRank & frontFiles;
            }

            int attackWeight = CountAttackWeight(board, zone, !white);
            int defenseWeight = CountAttackWeight(board, zone, white);
            int netAttack = Math.Min(MaxAttackWeight, Math.Max(0, attackWeight - defenseWeight));
            score -= (netAttack * netAttack * P.KingAttackWeightPenalty) / 4;

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
            score += shieldPawns * P.KingShieldBonus;

            for (int f = Math.Max(0, kingFile - 1); f <= Math.Min(7, kingFile + 1); f++)
            {
                ulong fileMask = Bitboard.FileMasks[f];
                if ((friendlyPawns & fileMask) == 0)
                {
                    score -= P.KingOpenFilePenalty;
                }
            }

            return score;
        }

        private static int CountAttackWeight(Board board, ulong zone, bool byWhite)
        {
            int weight = 0;
            ulong occupied = board.AllPieces;

            ulong queens = byWhite ? board.WQ : board.BQ;
            while (queens != 0)
            {
                int sq = Bitboard.PopLsb(ref queens);
                ulong attacks = MagicBitboards.GetBishopAttacks(sq, occupied) | MagicBitboards.GetRookAttacks(sq, occupied);
                if ((attacks & zone) != 0)
                {
                    weight += 2;
                }
            }
            ulong rooks = byWhite ? board.WR : board.BR;
            while (rooks != 0)
            {
                int sq = Bitboard.PopLsb(ref rooks);
                if ((MagicBitboards.GetRookAttacks(sq, occupied) & zone) != 0)
                {
                    weight += 1;
                }
            }
            ulong bishops = byWhite ? board.WB : board.BB;
            while (bishops != 0)
            {
                int sq = Bitboard.PopLsb(ref bishops);
                if ((MagicBitboards.GetBishopAttacks(sq, occupied) & zone) != 0)
                {
                    weight += 1;
                }
            }
            ulong knights = byWhite ? board.WN : board.BN;
            while (knights != 0)
            {
                int sq = Bitboard.PopLsb(ref knights);
                if ((Bitboard.KnightAttacks[sq] & zone) != 0)
                {
                    weight += 1;
                }
            }
            ulong pawns = byWhite ? board.WP : board.BP;
            while (pawns != 0)
            {
                int sq = Bitboard.PopLsb(ref pawns);
                if ((Bitboard.PawnAttacks[byWhite ? 0 : 1][sq] & zone) != 0)
                {
                    weight += 1;
                }
            }
            return weight;
        }

        private static void EvaluatePawnStorm(Board board, ref int mgScore, int phase)
        {
            if (phase < PawnStormPhaseThreshold)
            {
                return;
            }
            mgScore += EvaluatePawnStormSide(board, true) - EvaluatePawnStormSide(board, false);
        }

        private static int EvaluatePawnStormSide(Board board, bool white)
        {
            int score = 0;
            ulong enemyKing = white ? board.BK : board.WK;
            if (enemyKing == 0)
            {
                return 0;
            }

            int enemyKingSq = Bitboard.BitScanForward(enemyKing);
            int enemyKingFile = Bitboard.FileOf(enemyKingSq);

            ulong ourPawns = white ? board.WP : board.BP;
            ulong stormZone = 0;
            for (int f = Math.Max(0, enemyKingFile - 1); f <= Math.Min(7, enemyKingFile + 1); f++)
            {
                stormZone |= Bitboard.FileMasks[f];
            }

            ulong stormPawns = ourPawns & stormZone;
            while (stormPawns != 0)
            {
                int sq = Bitboard.PopLsb(ref stormPawns);
                int rank = white ? Bitboard.RankOf(sq) : 7 - Bitboard.RankOf(sq);
                if (rank == 4)
                {
                    score += P.PawnStormBonus4;
                }
                else if (rank == 5)
                {
                    score += P.PawnStormBonus5;
                }
                else if (rank == 6)
                {
                    score += P.PawnStormBonus6;
                }
            }
            return score;
        }

        private static void EvaluateSpace(Board board, ref int mgScore, int phase)
        {
            ulong centralFiles = Bitboard.FileMasks[2] | Bitboard.FileMasks[3] | Bitboard.FileMasks[4] | Bitboard.FileMasks[5];
            ulong whiteSpaceRanks = Bitboard.RankMasks[3] | Bitboard.RankMasks[4] | Bitboard.RankMasks[5];
            ulong blackSpaceRanks = Bitboard.RankMasks[2] | Bitboard.RankMasks[3] | Bitboard.RankMasks[4];

            int wSpace = 0;
            ulong wPawns = board.WP;
            while (wPawns != 0)
            {
                int sq = Bitboard.PopLsb(ref wPawns);
                ulong controlled = Bitboard.PawnAttacks[0][sq];
                wSpace += Bitboard.PopCount(controlled & centralFiles & whiteSpaceRanks);
            }
            int bSpace = 0;
            ulong bPawns = board.BP;
            while (bPawns != 0)
            {
                int sq = Bitboard.PopLsb(ref bPawns);
                ulong controlled = Bitboard.PawnAttacks[1][sq];
                bSpace += Bitboard.PopCount(controlled & centralFiles & blackSpaceRanks);
            }
            int spaceDelta = (wSpace - bSpace) * P.SpaceBonusMG * phase / P.TotalPhase;
            mgScore += spaceDelta;
        }

        private static void EvaluateThreats(Board board, ref int mgScore, ref int egScore)
        {
            int wThreats = CountHangingPieces(board, true);
            int bThreats = CountHangingPieces(board, false);
            mgScore -= wThreats * P.HangingPiecePenalty;
            mgScore += bThreats * P.HangingPiecePenalty;
            egScore -= wThreats * P.HangingPiecePenalty;
            egScore += bThreats * P.HangingPiecePenalty;
        }

        private static int CountHangingPieces(Board board, bool white)
        {
            int count = 0;
            ulong ourPieces = white ? board.WhitePieces : board.BlackPieces;
            ulong theirPawns = white ? board.BP : board.WP;
            ulong ourKing = white ? board.WK : board.BK;

            ulong theirAttacks = GetAllAttacks(board, !white);
            ulong ourDefenses = GetAllAttacks(board, white);

            ulong piecesToCheck = ourPieces & ~(white ? board.WP : board.BP) & ~ourKing;

            while (piecesToCheck != 0)
            {
                int sq = Bitboard.PopLsb(ref piecesToCheck);
                if ((theirAttacks & Bitboard.SquareBB[sq]) != 0)
                {
                    bool isDefended = (ourDefenses & Bitboard.SquareBB[sq]) != 0;
                    if (!isDefended)
                    {
                        count++;
                    }
                    else
                    {
                        bool attackedByPawn = (theirPawns & GetPawnAttackers(sq, !white)) != 0;
                        if (attackedByPawn)
                        {
                            Piece piece = board.PieceAt(sq);
                            if (piece != Piece.Empty && piece != Piece.WP && piece != Piece.BP)
                            {
                                count++;
                            }
                        }
                    }
                }
            }
            return count;
        }

        private static ulong GetAllAttacks(Board board, bool white)
        {
            ulong attacks = 0;

            ulong pawns = white ? board.WP : board.BP;
            while (pawns != 0)
            {
                int sq = Bitboard.PopLsb(ref pawns);
                attacks |= Bitboard.PawnAttacks[white ? 0 : 1][sq];
            }

            ulong knights = white ? board.WN : board.BN;
            while (knights != 0)
            {
                int sq = Bitboard.PopLsb(ref knights);
                attacks |= Bitboard.KnightAttacks[sq];
            }

            ulong bishops = white ? board.WB : board.BB;
            while (bishops != 0)
            {
                int sq = Bitboard.PopLsb(ref bishops);
                attacks |= MagicBitboards.GetBishopAttacks(sq, board.AllPieces);
            }

            ulong rooks = white ? board.WR : board.BR;
            while (rooks != 0)
            {
                int sq = Bitboard.PopLsb(ref rooks);
                attacks |= MagicBitboards.GetRookAttacks(sq, board.AllPieces);
            }

            ulong queens = white ? board.WQ : board.BQ;
            while (queens != 0)
            {
                int sq = Bitboard.PopLsb(ref queens);
                attacks |= MagicBitboards.GetQueenAttacks(sq, board.AllPieces);
            }

            ulong king = white ? board.WK : board.BK;
            if (king != 0)
            {
                int kSq = Bitboard.BitScanForward(king);
                attacks |= Bitboard.KingAttacks[kSq];
            }

            return attacks;
        }

        private static ulong GetPawnAttackers(int sq, bool attackerIsWhite)
        {
            return Bitboard.PawnAttacks[attackerIsWhite ? 1 : 0][sq];
        }
    }
}
