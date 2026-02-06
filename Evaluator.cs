using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace ChessEngine
{
    /// <summary>
    /// Entry for the evaluation cache.
    /// </summary>
    public struct EvalCacheEntry
    {
        public ulong Hash;
        public int Score;
    }

    public static class Evaluator
    {
        // Phase weights for each piece type (total = 24 for full material)
        private const int KnightPhase = 1;
        private const int BishopPhase = 1;
        private const int RookPhase = 2;
        private const int QueenPhase = 4;

        // Eval cache: keyed by Zobrist hash, stores static eval score (thread-local for parallel search)
        // Size: 2^20 entries (~16MB) - power of 2 for fast masking
        private const int EvalCacheSize = 1 << 20;
        private const ulong EvalCacheMask = EvalCacheSize - 1;
        private static readonly ThreadLocal<EvalCacheEntry[]> _evalCacheTls = new ThreadLocal<EvalCacheEntry[]>(() => new EvalCacheEntry[EvalCacheSize]);
        private static volatile int _evalCacheGeneration;
        private static readonly ThreadLocal<int> _myEvalCacheGeneration = new ThreadLocal<int>(() => -1);

        // Precomputed lookup tables for performance
        private static readonly ulong[,] PassedPawnMasks = new ulong[2, 64]; // [white=1/black=0, square]
        private static readonly int[,] ChebyshevDistanceTable = new int[64, 64];

        // Center distance for mop-up evaluation (not tunable, purely geometric)
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

        // Shorthand accessor for evaluation parameters
        private static EvalParams P => EvalParams.Instance;

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

        /// <summary>
        /// Clears the evaluation cache on all threads. Call on ucinewgame.
        /// </summary>
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

        /// <summary>
        /// Quick evaluation using only incremental material.
        /// Useful for pruning decisions where full accuracy is not needed.
        /// Returns score from perspective of side to move.
        /// </summary>
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

            // Probe eval cache
            if (ProbeEvalCache(hash, out int cachedScore))
                return cachedScore;

            int mgScore = 0;
            int egScore = 0;

            // Calculate game phase
            int phase = CalculatePhase(board);

            // Material and PST evaluation
            EvaluateMaterialAndPST(board, ref mgScore, ref egScore);

            // Pawn structure (also computes passed pawns for later use)
            ulong wPassedPawns = 0, bPassedPawns = 0;
            EvaluatePawnStructure(board, ref mgScore, ref egScore, out wPassedPawns, out bPassedPawns);

            // Bishop pair (simplified: no bishop quality)
            EvaluateBishopPair(board, ref mgScore, ref egScore);

            // Rook on open/semi-open files
            EvaluateRooks(board, ref mgScore, ref egScore);

            // Tapered evaluation: interpolate between MG and EG
            int score = (mgScore * phase + egScore * (P.TotalPhase - phase)) / P.TotalPhase;

            // Return from perspective of side to move
            int finalScore = board.WhiteToMove ? score : -score;

            // Store in eval cache
            StoreEvalCache(hash, finalScore);

            return finalScore;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CalculatePhase(Board board)
        {
            // Use precomputed phase from board (updated incrementally in MakeMove/UndoMove)
            return Math.Min(board.Phase, P.TotalPhase);
        }

        private static void EvaluateMaterialAndPST(Board board, ref int mgScore, ref int egScore)
        {
            // White pawns
            ulong bb = board.WP;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore += P.PawnValueMG + P.PawnPstMG[sq];
                egScore += P.PawnValueEG + P.PawnPstEG[sq];
            }

            // Black pawns
            bb = board.BP;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore -= P.PawnValueMG + P.PawnPstMG[Bitboard.MirrorSquare(sq)];
                egScore -= P.PawnValueEG + P.PawnPstEG[Bitboard.MirrorSquare(sq)];
            }

            // White knights
            bb = board.WN;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore += P.KnightValueMG + P.KnightPstMG[sq];
                egScore += P.KnightValueEG + P.KnightPstEG[sq];
            }

            // Black knights
            bb = board.BN;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore -= P.KnightValueMG + P.KnightPstMG[Bitboard.MirrorSquare(sq)];
                egScore -= P.KnightValueEG + P.KnightPstEG[Bitboard.MirrorSquare(sq)];
            }

            // White bishops
            bb = board.WB;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore += P.BishopValueMG + P.BishopPstMG[sq];
                egScore += P.BishopValueEG + P.BishopPstEG[sq];
            }

            // Black bishops
            bb = board.BB;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore -= P.BishopValueMG + P.BishopPstMG[Bitboard.MirrorSquare(sq)];
                egScore -= P.BishopValueEG + P.BishopPstEG[Bitboard.MirrorSquare(sq)];
            }

            // White rooks
            bb = board.WR;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore += P.RookValueMG + P.RookPstMG[sq];
                egScore += P.RookValueEG + P.RookPstEG[sq];
            }

            // Black rooks
            bb = board.BR;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore -= P.RookValueMG + P.RookPstMG[Bitboard.MirrorSquare(sq)];
                egScore -= P.RookValueEG + P.RookPstEG[Bitboard.MirrorSquare(sq)];
            }

            // White queens
            bb = board.WQ;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore += P.QueenValueMG + P.QueenPstMG[sq];
                egScore += P.QueenValueEG + P.QueenPstEG[sq];
            }

            // Black queens
            bb = board.BQ;
            while (bb != 0)
            {
                int sq = Bitboard.PopLsb(ref bb);
                mgScore -= P.QueenValueMG + P.QueenPstMG[Bitboard.MirrorSquare(sq)];
                egScore -= P.QueenValueEG + P.QueenPstEG[Bitboard.MirrorSquare(sq)];
            }

            // White king
            int wkSq = Bitboard.BitScanForward(board.WK);
            mgScore += P.KingPstMG[wkSq];
            egScore += P.KingPstEG[wkSq];

            // Black king
            int bkSq = Bitboard.BitScanForward(board.BK);
            mgScore -= P.KingPstMG[Bitboard.MirrorSquare(bkSq)];
            egScore -= P.KingPstEG[Bitboard.MirrorSquare(bkSq)];
        }

        private static void EvaluatePawnStructure(Board board, ref int mgScore, ref int egScore,
            out ulong wPassedPawns, out ulong bPassedPawns)
        {
            // White pawn structure
            EvaluatePawnStructureSide(board, board.WP, board.BP, true, ref mgScore, ref egScore, out wPassedPawns);

            // Black pawn structure
            EvaluatePawnStructureSide(board, board.BP, board.WP, false, ref mgScore, ref egScore, out bPassedPawns);
        }

        private static void EvaluatePawnStructureSide(Board board, ulong friendlyPawns, ulong enemyPawns,
            bool white, ref int mgScore, ref int egScore, out ulong passedPawns)
        {
            int sign = white ? 1 : -1;
            passedPawns = 0;

            // Doubled pawns
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

            // Isolated and passed pawns (simplified: no backward pawn, rook behind, or connected passers)
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
                    int penalty = sign * P.IsolatedPawnPenalty;
                    mgScore += penalty;
                    egScore += penalty;
                }

                // Passed pawn check
                ulong passedMask = GetPassedPawnMask(sq, white);
                if ((enemyPawns & passedMask) == 0)
                {
                    passedPawns |= Bitboard.SquareBB[sq];
                    int effectiveRank = white ? rank : 7 - rank;
                    mgScore += sign * P.PassedPawnBonusMG[effectiveRank];
                    egScore += sign * P.PassedPawnBonusEG[effectiveRank];
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong GetPassedPawnMask(int sq, bool white) => PassedPawnMasks[white ? 1 : 0, sq];

        /// <summary>
        /// Returns true if the pawn at sq for the given color has no enemy pawns in its path to promotion.
        /// Used by quiescence to generate passed-pawn pushes.
        /// </summary>
        public static bool IsPassedPawn(Board board, int sq, bool white)
        {
            ulong mask = PassedPawnMasks[white ? 1 : 0, sq];
            ulong enemyPawns = white ? board.BP : board.WP;
            return (enemyPawns & mask) == 0;
        }

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
                bool onLongDiagonal = IsOnLongDiagonal(sq);
                if (onLongDiagonal)
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
                bool onLongDiagonal = IsOnLongDiagonal(sq);
                if (onLongDiagonal)
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
            bool bishopOnLight = ((Bitboard.FileOf(bishopSq) + Bitboard.RankOf(bishopSq)) & 1) == 0;
            int count = 0;
            while (pawns != 0)
            {
                int sq = Bitboard.PopLsb(ref pawns);
                bool pawnOnLight = ((Bitboard.FileOf(sq) + Bitboard.RankOf(sq)) & 1) == 0;
                if (pawnOnLight == bishopOnLight)
                    count++;
            }
            return count;
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
                    int bonus = P.RookOpenFileBonus;
                    mgScore += bonus;
                    egScore += bonus;
                }
                else if ((board.WP & fileMask) == 0)
                {
                    int bonus = P.RookSemiOpenFileBonus;
                    mgScore += bonus;
                    egScore += bonus;
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
                    int bonus = P.RookOpenFileBonus;
                    mgScore -= bonus;
                    egScore -= bonus;
                }
                else if ((board.BP & fileMask) == 0)
                {
                    int bonus = P.RookSemiOpenFileBonus;
                    mgScore -= bonus;
                    egScore -= bonus;
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
                int sq = Bitboard.PopLsb(ref wRooks);
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
                int sq = Bitboard.PopLsb(ref bRooks);
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
            // White mobility (knights, bishops, rooks)
            int wMobility = CountKnightMobility(board.WN, board.WhitePieces);
            wMobility += CountSlidingMobility(board.WB, board.WhitePieces, board, true);
            wMobility += CountSlidingMobility(board.WR, board.WhitePieces, board, false);

            // Black mobility (knights, bishops, rooks)
            int bMobility = CountKnightMobility(board.BN, board.BlackPieces);
            bMobility += CountSlidingMobility(board.BB, board.BlackPieces, board, true);
            bMobility += CountSlidingMobility(board.BR, board.BlackPieces, board, false);

            // Combined mobility (knights, bishops, rooks, queens) for stable eval
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
                // Queen attacks = bishop attacks | rook attacks
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

        /// <summary>
        /// Evaluates knight outposts: knights on ranks 4-6 that cannot be attacked by enemy pawns.
        /// </summary>
        private static void EvaluateOutposts(Board board, ref int mgScore, ref int egScore)
        {
            // White knight outposts (ranks 4-6)
            ulong wOutpostSquares = board.WN & (Bitboard.Rank4 | Bitboard.Rank5 | Bitboard.Rank6);
            while (wOutpostSquares != 0)
            {
                int sq = Bitboard.PopLsb(ref wOutpostSquares);
                int file = Bitboard.FileOf(sq);
                int rank = Bitboard.RankOf(sq);
                
                // Check if no black pawn can attack this square (on adjacent files in front)
                ulong attackMask = 0;
                for (int r = rank; r <= 7; r++)
                {
                    if (file > 0) attackMask |= Bitboard.SquareBB[r * 8 + file - 1];
                    if (file < 7) attackMask |= Bitboard.SquareBB[r * 8 + file + 1];
                }
                
                if ((board.BP & attackMask) == 0)
                {
                    mgScore += P.KnightOutpostBonusMG;
                    egScore += P.KnightOutpostBonusEG;
                }
            }

            // Black knight outposts (ranks 3-5 from black's perspective)
            ulong bOutpostSquares = board.BN & (Bitboard.Rank3 | Bitboard.Rank4 | Bitboard.Rank5);
            while (bOutpostSquares != 0)
            {
                int sq = Bitboard.PopLsb(ref bOutpostSquares);
                int file = Bitboard.FileOf(sq);
                int rank = Bitboard.RankOf(sq);
                
                // Check if no white pawn can attack this square
                ulong attackMask = 0;
                for (int r = rank; r >= 0; r--)
                {
                    if (file > 0) attackMask |= Bitboard.SquareBB[r * 8 + file - 1];
                    if (file < 7) attackMask |= Bitboard.SquareBB[r * 8 + file + 1];
                }
                
                if ((board.WP & attackMask) == 0)
                {
                    mgScore -= P.KnightOutpostBonusMG;
                    egScore -= P.KnightOutpostBonusEG;
                }
            }
        }

        /// <summary>
        /// Queen tropism: bonus for queen proximity to enemy king.
        /// Uses smooth formula max(0, 8 - dist) for predictable gradients (was 14 - dist).
        /// </summary>
        private static void EvaluateQueenTropism(Board board, ref int mgScore, ref int egScore)
        {
            if (board.WQ == 0 && board.BQ == 0) return;
            int wkSq = board.WK != 0 ? Bitboard.BitScanForward(board.WK) : -1;
            int bkSq = board.BK != 0 ? Bitboard.BitScanForward(board.BK) : -1;
            if (wkSq < 0 || bkSq < 0) return;

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

        /// <summary>
        /// King safety: scaled by phase for smooth MG transition (no hard gate).
        /// </summary>
        private static void EvaluateKingSafety(Board board, ref int mgScore, int phase)
        {
            if (board.WQ == 0 && board.BQ == 0) return;
            int rawScore = EvaluateKingSafetySide(board, true) - EvaluateKingSafetySide(board, false);
            mgScore += rawScore * phase / P.TotalPhase;
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

            // King zone: king attacks + one rank in front
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

            // Attack weight: enemy pieces attacking zone, weighted (queen 2, rook 1, bishop 1, knight 1, pawn 1)
            // Cap at 10 for stable, predictable gradients
            int attackWeight = CountAttackWeight(board, zone, !white);
            int defenseWeight = CountAttackWeight(board, zone, white);
            int netAttack = Math.Min(10, Math.Max(0, attackWeight - defenseWeight));
            score -= netAttack * P.KingAttackWeightPenalty;

            // Pawn shield bonus
            ulong shieldMask = Bitboard.KingAttacks[kingSq];
            if (white)
                shieldMask &= Bitboard.Rank2 | Bitboard.Rank3;
            else
                shieldMask &= Bitboard.Rank6 | Bitboard.Rank7;

            int shieldPawns = Bitboard.PopCount(friendlyPawns & shieldMask);
            score += shieldPawns * P.KingShieldBonus;

            // Penalty for open files near king
            for (int f = Math.Max(0, kingFile - 1); f <= Math.Min(7, kingFile + 1); f++)
            {
                ulong fileMask = Bitboard.FileMasks[f];
                if ((friendlyPawns & fileMask) == 0)
                    score -= P.KingOpenFilePenalty;
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
                if ((attacks & zone) != 0) weight += 2;
            }
            ulong rooks = byWhite ? board.WR : board.BR;
            while (rooks != 0)
            {
                int sq = Bitboard.PopLsb(ref rooks);
                if ((MagicBitboards.GetRookAttacks(sq, occupied) & zone) != 0) weight += 1;
            }
            ulong bishops = byWhite ? board.WB : board.BB;
            while (bishops != 0)
            {
                int sq = Bitboard.PopLsb(ref bishops);
                if ((MagicBitboards.GetBishopAttacks(sq, occupied) & zone) != 0) weight += 1;
            }
            ulong knights = byWhite ? board.WN : board.BN;
            while (knights != 0)
            {
                int sq = Bitboard.PopLsb(ref knights);
                if ((Bitboard.KnightAttacks[sq] & zone) != 0) weight += 1;
            }
            ulong pawns = byWhite ? board.WP : board.BP;
            while (pawns != 0)
            {
                int sq = Bitboard.PopLsb(ref pawns);
                if ((Bitboard.PawnAttacks[byWhite ? 1 : 0][sq] & zone) != 0) weight += 1;
            }
            return weight;
        }

        /// <summary>
        /// Space: pawn control of central squares. Scaled by phase for smooth MG transition (no hard gate).
        /// </summary>
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
                ulong controlled = Bitboard.PawnAttacks[1][sq];
                wSpace += Bitboard.PopCount(controlled & centralFiles & whiteSpaceRanks);
            }
            int bSpace = 0;
            ulong bPawns = board.BP;
            while (bPawns != 0)
            {
                int sq = Bitboard.PopLsb(ref bPawns);
                ulong controlled = Bitboard.PawnAttacks[0][sq];
                bSpace += Bitboard.PopCount(controlled & centralFiles & blackSpaceRanks);
            }
            // Scale by phase for smooth gradient (was hard gate at phase >= TotalPhase/2)
            int spaceDelta = (wSpace - bSpace) * P.SpaceBonusMG * phase / P.TotalPhase;
            mgScore += spaceDelta;
        }

        /// <summary>
        /// Endgame terms: king-pawn proximity and mop-up. Scaled by (TotalPhase - phase) for smooth EG transition.
        /// Mop-up uses sigmoid scaling instead of hard threshold for predictable gradients.
        /// </summary>
        private static void EvaluateEndgame(Board board, ref int egScore, int phase,
            ulong wPassedPawns, ulong bPassedPawns)
        {
            int egScale = P.TotalPhase - phase;
            if (egScale <= 0) return;

            int wkSq = Bitboard.BitScanForward(board.WK);
            int bkSq = Bitboard.BitScanForward(board.BK);

            // King-pawn proximity (scale by egScale for smooth transition)
            int kppDelta = 0;
            EvaluateKingPawnProximity(wkSq, bkSq, wPassedPawns, bPassedPawns, ref kppDelta);
            egScore += kppDelta * egScale / P.TotalPhase;

            // Mop-up: smooth sigmoid scaling instead of hard 200 cp threshold
            int materialBalance = ComputeMaterialBalance(board);
            double mopUpScale = 1.0 / (1.0 + Math.Exp(-((double)Math.Abs(materialBalance) - 150) / 100));
            int mopUpDelta = 0;
            EvaluateMopUp(wkSq, bkSq, materialBalance, ref mopUpDelta);
            int scaledMopUp = (int)(mopUpDelta * mopUpScale);
            egScore += scaledMopUp * egScale / P.TotalPhase;
        }

        private static void EvaluateKingPawnProximity(int wkSq, int bkSq,
            ulong wPassedPawns, ulong bPassedPawns, ref int delta)
        {
            // White king should be close to own passers (to escort) and enemy passers (to stop)
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

            // Black king proximity
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
            // When winning, drive enemy king to corner and get our king close
            if (materialBalance > 0)
            {
                // White is winning - push black king to corner
                int enemyCenterDist = CenterManhattanDistance[bkSq];
                egScore += enemyCenterDist * P.MopUpCenterDistanceWeight;

                // Bonus for king proximity
                int kingDist = ChebyshevDistance(wkSq, bkSq);
                egScore += (14 - kingDist) * P.MopUpKingProximityWeight;
            }
            else
            {
                // Black is winning - push white king to corner
                int enemyCenterDist = CenterManhattanDistance[wkSq];
                egScore -= enemyCenterDist * P.MopUpCenterDistanceWeight;

                int kingDist = ChebyshevDistance(wkSq, bkSq);
                egScore -= (14 - kingDist) * P.MopUpKingProximityWeight;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ChebyshevDistance(int sq1, int sq2) => ChebyshevDistanceTable[sq1, sq2];

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
