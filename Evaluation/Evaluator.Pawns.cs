using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace ChessEngine
{
    public struct PawnHashEntry
    {
        public ulong Key;
        public int MgScore;
        public int EgScore;
        public ulong WPassedPawns;
        public ulong BPassedPawns;
    }

    public static partial class Evaluator
    {
        private const int PawnHashSize = 1 << 14;
        private const ulong PawnHashMask = PawnHashSize - 1;
        private static readonly ThreadLocal<PawnHashEntry[]> _pawnHashTls =
            new ThreadLocal<PawnHashEntry[]>(() => new PawnHashEntry[PawnHashSize]);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong ComputePawnHash(Board board)
        {
            ulong h = board.WP * 0x9E3779B97F4A7C15UL;
            h ^= board.BP * 0x517CC1B727220A95UL;
            return h;
        }

        public static void ClearPawnHash()
        {
            var table = _pawnHashTls.Value!;
            Array.Clear(table, 0, PawnHashSize);
        }

        private static void EvaluatePawnStructure(Board board, ref int mgScore, ref int egScore,
            out ulong wPassedPawns, out ulong bPassedPawns)
        {
            ulong pawnKey = ComputePawnHash(board);
            var pawnTable = _pawnHashTls.Value!;
            ref PawnHashEntry entry = ref pawnTable[(int)(pawnKey & PawnHashMask)];

            if (entry.Key == pawnKey)
            {
                mgScore += entry.MgScore;
                egScore += entry.EgScore;
                wPassedPawns = entry.WPassedPawns;
                bPassedPawns = entry.BPassedPawns;
                return;
            }

            int mg = 0, eg = 0;
            EvaluatePawnStructureSide(board, board.WP, board.BP, true, ref mg, ref eg, out wPassedPawns);
            EvaluatePawnStructureSide(board, board.BP, board.WP, false, ref mg, ref eg, out bPassedPawns);

            entry.Key = pawnKey;
            entry.MgScore = mg;
            entry.EgScore = eg;
            entry.WPassedPawns = wPassedPawns;
            entry.BPassedPawns = bPassedPawns;

            mgScore += mg;
            egScore += eg;
        }

        private static void EvaluatePawnStructureSide(Board board, ulong friendlyPawns, ulong enemyPawns,
            bool white, ref int mgScore, ref int egScore, out ulong passedPawns)
        {
            int sign = white ? 1 : -1;
            passedPawns = 0;

            for (int file = 0; file < 8; file++)
            {
                ulong fileMask = Bitboard.FileMasks[file];
                int pawnsOnFile = Bitboard.PopCount(friendlyPawns & fileMask);
                if (pawnsOnFile > 1)
                {
                    int penalty = sign * P.DoubledPawnPenalty * (pawnsOnFile - 1);
                    mgScore += penalty;
                    egScore += penalty;
                }
            }

            ulong pawns = friendlyPawns;
            while (pawns != 0)
            {
                int sq = Bitboard.PopLsb(ref pawns);
                int file = Bitboard.FileOf(sq);
                int rank = Bitboard.RankOf(sq);

                ulong adjacentFiles = Bitboard.AdjacentFiles[file];
                bool isIsolated = (friendlyPawns & adjacentFiles) == 0;
                if (isIsolated)
                {
                    int penalty = sign * P.IsolatedPawnPenalty;
                    mgScore += penalty;
                    egScore += penalty;
                }

                if (!isIsolated)
                {
                    ulong behindMask = GetRanksBehind(rank, white);
                    if ((friendlyPawns & adjacentFiles & behindMask) == 0)
                    {
                        int stopSq = sq + (white ? 8 : -8);
                        if (stopSq >= 0 && stopSq < 64)
                        {
                            ulong stopAttacks = Bitboard.PawnAttacks[white ? 0 : 1][stopSq];
                            if ((enemyPawns & stopAttacks) != 0)
                            {
                                mgScore += sign * P.BackwardPawnPenalty;
                                egScore += sign * P.BackwardPawnPenalty;
                            }
                        }
                    }
                }

                ulong adjacentSameRank = adjacentFiles & Bitboard.RankMasks[rank];
                if ((friendlyPawns & adjacentSameRank) != 0)
                {
                    mgScore += sign * P.PhalanxBonus;
                    egScore += sign * P.PhalanxBonus;
                }

                int stopSqBlocked = sq + (white ? 8 : -8);
                if (stopSqBlocked >= 0 && stopSqBlocked < 64)
                {
                    if ((enemyPawns & Bitboard.SquareBB[stopSqBlocked]) != 0)
                    {
                        mgScore += sign * P.BlockedPawnPenalty;
                        egScore += sign * P.BlockedPawnPenalty;
                    }
                }

                ulong passedMask = GetPassedPawnMask(sq, white);
                if ((enemyPawns & passedMask) == 0)
                {
                    passedPawns |= Bitboard.SquareBB[sq];
                    int effectiveRank = white ? rank : 7 - rank;
                    mgScore += sign * P.PassedPawnBonusMG[effectiveRank];
                    egScore += sign * P.PassedPawnBonusEG[effectiveRank];
                }
            }

            ulong pp = passedPawns;
            while (pp != 0)
            {
                int psq = Bitboard.PopLsb(ref pp);
                int pfile = Bitboard.FileOf(psq);
                if ((passedPawns & Bitboard.AdjacentFiles[pfile] & ~Bitboard.SquareBB[psq]) != 0)
                {
                    int effectiveRank = white ? Bitboard.RankOf(psq) : 7 - Bitboard.RankOf(psq);
                    mgScore += sign * P.ConnectedPasserBonusByRank[effectiveRank];
                    egScore += sign * P.ConnectedPasserBonusByRank[effectiveRank];
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong GetRanksBehind(int rank, bool white)
        {
            ulong mask = 0;
            if (white)
            {
                for (int r = 0; r < rank; r++)
                {
                    mask |= Bitboard.RankMasks[r];
                }
            }
            else
            {
                for (int r = rank + 1; r < 8; r++)
                {
                    mask |= Bitboard.RankMasks[r];
                }
            }
            return mask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong GetPassedPawnMask(int sq, bool white) => PassedPawnMasks[white ? 1 : 0, sq];

        public static bool IsPassedPawn(Board board, int sq, bool white)
        {
            ulong mask = PassedPawnMasks[white ? 1 : 0, sq];
            ulong enemyPawns = white ? board.BP : board.WP;
            return (enemyPawns & mask) == 0;
        }
    }
}
