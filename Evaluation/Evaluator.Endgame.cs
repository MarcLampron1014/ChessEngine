using System;

namespace ChessEngine
{
    public static partial class Evaluator
    {
        private const double MopUpSigmoidCenter = 150.0;
        private const double MopUpSigmoidScale = 100.0;
        private const int MaxKingChebyshevDistance = 14;

        private static bool HasOppositeColoredBishops(Board board)
        {
            if (Bitboard.PopCount(board.WB) != 1 || Bitboard.PopCount(board.BB) != 1)
            {
                return false;
            }
            int wbSq = Bitboard.BitScanForward(board.WB);
            int bbSq = Bitboard.BitScanForward(board.BB);
            return IsLightSquare(wbSq) != IsLightSquare(bbSq);
        }

        private static int ApplyOppositeColoredBishopsScaling(int score, int phase)
        {
            int egPhase = P.TotalPhase - phase;
            int drawFactor = P.OppositeColoredBishopsDrawFactor;
            int reduction = (Math.Abs(score) * drawFactor * egPhase) / (100 * P.TotalPhase);
            return score > 0 ? score - reduction : score + reduction;
        }

        private static void EvaluateEndgame(Board board, ref int egScore, int phase,
            ulong wPassedPawns, ulong bPassedPawns)
        {
            int egScale = P.TotalPhase - phase;
            if (egScale <= 0)
            {
                return;
            }

            int wkSq = Bitboard.BitScanForward(board.WK);
            int bkSq = Bitboard.BitScanForward(board.BK);

            int kppDelta = 0;
            EvaluateKingPawnProximity(wkSq, bkSq, wPassedPawns, bPassedPawns, ref kppDelta);
            egScore += kppDelta * egScale / P.TotalPhase;

            int materialBalance = ComputeMaterialBalance(board);
            double mopUpScale = 1.0 / (1.0 + Math.Exp(-((double)Math.Abs(materialBalance) - MopUpSigmoidCenter) / MopUpSigmoidScale));
            int mopUpDelta = 0;
            EvaluateMopUp(wkSq, bkSq, materialBalance, ref mopUpDelta);
            int scaledMopUp = (int)(mopUpDelta * mopUpScale);
            egScore += scaledMopUp * egScale / P.TotalPhase;
        }

        private static void EvaluateKingPawnProximity(int wkSq, int bkSq,
            ulong wPassedPawns, ulong bPassedPawns, ref int delta)
        {
            ulong passers = wPassedPawns;
            while (passers != 0)
            {
                int sq = Bitboard.PopLsb(ref passers);
                int dist = ChebyshevDistance(wkSq, sq);
                delta += (7 - dist) * P.KingOwnPasserProximity;
            }

            passers = bPassedPawns;
            while (passers != 0)
            {
                int sq = Bitboard.PopLsb(ref passers);
                int dist = ChebyshevDistance(wkSq, sq);
                delta += (7 - dist) * P.KingEnemyPasserProximity;
            }

            passers = bPassedPawns;
            while (passers != 0)
            {
                int sq = Bitboard.PopLsb(ref passers);
                int dist = ChebyshevDistance(bkSq, sq);
                delta -= (7 - dist) * P.KingOwnPasserProximity;
            }

            passers = wPassedPawns;
            while (passers != 0)
            {
                int sq = Bitboard.PopLsb(ref passers);
                int dist = ChebyshevDistance(bkSq, sq);
                delta -= (7 - dist) * P.KingEnemyPasserProximity;
            }
        }

        private static void EvaluateMopUp(int wkSq, int bkSq, int materialBalance, ref int egScore)
        {
            if (materialBalance > 0)
            {
                int enemyCenterDist = CenterManhattanDistance[bkSq];
                egScore += enemyCenterDist * P.MopUpCenterDistanceWeight;

                int kingDist = ChebyshevDistance(wkSq, bkSq);
                egScore += (MaxKingChebyshevDistance - kingDist) * P.MopUpKingProximityWeight;
            }
            else
            {
                int enemyCenterDist = CenterManhattanDistance[wkSq];
                egScore -= enemyCenterDist * P.MopUpCenterDistanceWeight;

                int kingDist = ChebyshevDistance(wkSq, bkSq);
                egScore -= (MaxKingChebyshevDistance - kingDist) * P.MopUpKingProximityWeight;
            }
        }

        public static bool IsInsufficientMaterial(Board board)
        {
            if ((board.WP | board.BP | board.WR | board.BR | board.WQ | board.BQ) != 0)
            {
                return false;
            }

            int wMinors = Bitboard.PopCount(board.WN | board.WB);
            int bMinors = Bitboard.PopCount(board.BN | board.BB);

            if (wMinors == 0 && bMinors == 0)
            {
                return true;
            }

            if (wMinors <= 1 && bMinors == 0)
            {
                return true;
            }
            if (bMinors <= 1 && wMinors == 0)
            {
                return true;
            }

            if (wMinors == 1 && bMinors == 1 && board.WN == 0 && board.BN == 0)
            {
                int wbSq = Bitboard.BitScanForward(board.WB);
                int bbSq = Bitboard.BitScanForward(board.BB);
                if (IsLightSquare(wbSq) == IsLightSquare(bbSq))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
