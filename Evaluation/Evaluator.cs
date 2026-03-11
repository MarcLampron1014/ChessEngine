using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace ChessEngine
{
    public struct EvalCacheEntry
    {
        public ulong Hash;
        public int Score;
    }

    public static partial class Evaluator
    {
        private const int EvalCacheSize = 1 << 20;
        private const ulong EvalCacheMask = EvalCacheSize - 1;
        private static readonly ThreadLocal<EvalCacheEntry[]> _evalCacheTls = new ThreadLocal<EvalCacheEntry[]>(() => new EvalCacheEntry[EvalCacheSize]);
        private static volatile int _evalCacheGeneration;
        private static readonly ThreadLocal<int> _myEvalCacheGeneration = new ThreadLocal<int>(() => -1);

        private static readonly ulong[,] PassedPawnMasks = new ulong[2, 64];
        private static readonly int[,] ChebyshevDistanceTable = new int[64, 64];

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

        private static EvalParams P => EvalParams.Instance;

        static Evaluator()
        {
            for (int sq = 0; sq < 64; sq++)
            {
                PassedPawnMasks[0, sq] = ComputePassedPawnMask(sq, false);
                PassedPawnMasks[1, sq] = ComputePassedPawnMask(sq, true);
            }

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

        public static void ClearCache()
        {
            _evalCacheGeneration++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EnsureEvalCacheCurrent()
        {
            if (_myEvalCacheGeneration.Value != _evalCacheGeneration)
            {
                Array.Clear(_evalCacheTls.Value!, 0, EvalCacheSize);
                _myEvalCacheGeneration.Value = _evalCacheGeneration;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ProbeEvalCache(ulong hash, out int score)
        {
            EnsureEvalCacheCurrent();
            ref EvalCacheEntry entry = ref _evalCacheTls.Value![(int)(hash & EvalCacheMask)];
            if (entry.Hash == hash)
            {
                score = entry.Score;
                return true;
            }
            score = 0;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void StoreEvalCache(ulong hash, int score)
        {
            EnsureEvalCacheCurrent();
            ref EvalCacheEntry entry = ref _evalCacheTls.Value![(int)(hash & EvalCacheMask)];
            entry.Hash = hash;
            entry.Score = score;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int QuickEvaluate(Board board)
        {
            int phase = Math.Min(board.Phase, P.TotalPhase);
            int score = (board.MaterialMG * phase + board.MaterialEG * (P.TotalPhase - phase)) / P.TotalPhase;
            return board.WhiteToMove ? score : -score;
        }

        public static int Evaluate(Board board)
        {
            ulong hash = board.ZobristHash;

            if (ProbeEvalCache(hash, out int cachedScore))
                return cachedScore;

            int mgScore = 0;
            int egScore = 0;

            int phase = CalculatePhase(board);

            EvaluateMaterialAndPST(board, ref mgScore, ref egScore);

            ulong wPassedPawns = 0, bPassedPawns = 0;
            EvaluatePawnStructure(board, ref mgScore, ref egScore, out wPassedPawns, out bPassedPawns);

            EvaluateBishopPair(board, ref mgScore, ref egScore);
            EvaluateRooks(board, ref mgScore, ref egScore);
            EvaluateMobility(board, ref mgScore, ref egScore);
            EvaluateBishopsQuality(board, ref mgScore, ref egScore);
            EvaluateRookOnSeventh(board, ref mgScore, ref egScore);
            EvaluateRookBehindPasser(board, ref mgScore, ref egScore, wPassedPawns, bPassedPawns);
            EvaluateOutposts(board, ref mgScore, ref egScore);
            EvaluateQueenTropism(board, ref mgScore, ref egScore);
            EvaluateKnightTropism(board, ref mgScore, ref egScore);
            EvaluateKingSafety(board, ref mgScore, phase);
            EvaluatePawnStorm(board, ref mgScore, phase);
            EvaluateSpace(board, ref mgScore, phase);
            EvaluateThreats(board, ref mgScore, ref egScore);

            int score = (mgScore * phase + egScore * (P.TotalPhase - phase)) / P.TotalPhase;

            int egScorePost = 0;
            EvaluateEndgame(board, ref egScorePost, phase, wPassedPawns, bPassedPawns);
            score += egScorePost;

            if (HasOppositeColoredBishops(board))
                score = ApplyOppositeColoredBishopsScaling(score, phase);

            int finalScore = board.WhiteToMove ? score : -score;

            StoreEvalCache(hash, finalScore);

            return finalScore;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CalculatePhase(Board board)
        {
            return Math.Min(board.Phase, P.TotalPhase);
        }

        private static void EvaluateMaterialAndPST(Board board, ref int mgScore, ref int egScore)
        {
            ulong bb = board.WP;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore += P.PawnValueMG + P.PawnPstMG[sq];
                egScore += P.PawnValueEG + P.PawnPstEG[sq];
            }

            bb = board.BP;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore -= P.PawnValueMG + P.PawnPstMG[Bitboard.MirrorSquare(sq)];
                egScore -= P.PawnValueEG + P.PawnPstEG[Bitboard.MirrorSquare(sq)];
            }

            bb = board.WN;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore += P.KnightValueMG + P.KnightPstMG[sq];
                egScore += P.KnightValueEG + P.KnightPstEG[sq];
            }

            bb = board.BN;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore -= P.KnightValueMG + P.KnightPstMG[Bitboard.MirrorSquare(sq)];
                egScore -= P.KnightValueEG + P.KnightPstEG[Bitboard.MirrorSquare(sq)];
            }

            bb = board.WB;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore += P.BishopValueMG + P.BishopPstMG[sq];
                egScore += P.BishopValueEG + P.BishopPstEG[sq];
            }

            bb = board.BB;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore -= P.BishopValueMG + P.BishopPstMG[Bitboard.MirrorSquare(sq)];
                egScore -= P.BishopValueEG + P.BishopPstEG[Bitboard.MirrorSquare(sq)];
            }

            bb = board.WR;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore += P.RookValueMG + P.RookPstMG[sq];
                egScore += P.RookValueEG + P.RookPstEG[sq];
            }

            bb = board.BR;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore -= P.RookValueMG + P.RookPstMG[Bitboard.MirrorSquare(sq)];
                egScore -= P.RookValueEG + P.RookPstEG[Bitboard.MirrorSquare(sq)];
            }

            bb = board.WQ;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore += P.QueenValueMG + P.QueenPstMG[sq];
                egScore += P.QueenValueEG + P.QueenPstEG[sq];
            }

            bb = board.BQ;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore -= P.QueenValueMG + P.QueenPstMG[Bitboard.MirrorSquare(sq)];
                egScore -= P.QueenValueEG + P.QueenPstEG[Bitboard.MirrorSquare(sq)];
            }

            int wkSq = Bitboard.BitScanForward(board.WK);
            mgScore += P.KingPstMG[wkSq];
            egScore += P.KingPstEG[wkSq];

            int bkSq = Bitboard.BitScanForward(board.BK);
            mgScore -= P.KingPstMG[Bitboard.MirrorSquare(bkSq)];
            egScore -= P.KingPstEG[Bitboard.MirrorSquare(bkSq)];
        }

        public static int GetPieceValue(Piece p)
        {
            return p switch
            {
                Piece.WP or Piece.BP => P.PawnValueMG,
                Piece.WN or Piece.BN => P.KnightValueMG,
                Piece.WB or Piece.BB => P.BishopValueMG,
                Piece.WR or Piece.BR => P.RookValueMG,
                Piece.WQ or Piece.BQ => P.QueenValueMG,
                Piece.WK or Piece.BK => 20000,
                _ => 0
            };
        }

        private static int ComputeMaterialBalance(Board board)
        {
            int score = 0;
            score += Bitboard.PopCount(board.WP) * P.PawnValueMG;
            score += Bitboard.PopCount(board.WN) * P.KnightValueMG;
            score += Bitboard.PopCount(board.WB) * P.BishopValueMG;
            score += Bitboard.PopCount(board.WR) * P.RookValueMG;
            score += Bitboard.PopCount(board.WQ) * P.QueenValueMG;
            score -= Bitboard.PopCount(board.BP) * P.PawnValueMG;
            score -= Bitboard.PopCount(board.BN) * P.KnightValueMG;
            score -= Bitboard.PopCount(board.BB) * P.BishopValueMG;
            score -= Bitboard.PopCount(board.BR) * P.RookValueMG;
            score -= Bitboard.PopCount(board.BQ) * P.QueenValueMG;
            return score;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ChebyshevDistance(int sq1, int sq2) => ChebyshevDistanceTable[sq1, sq2];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsLightSquare(int sq) =>
            ((Bitboard.FileOf(sq) + Bitboard.RankOf(sq)) & 1) != 0;
    }
}
