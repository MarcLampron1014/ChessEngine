using System;
using System.Runtime.CompilerServices;

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

        // Eval cache: keyed by Zobrist hash, stores static eval score
        // Size: 2^20 entries (~16MB) - power of 2 for fast masking
        private const int EvalCacheSize = 1 << 20;
        private const ulong EvalCacheMask = EvalCacheSize - 1;
        private static readonly EvalCacheEntry[] _evalCache = new EvalCacheEntry[EvalCacheSize];

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
        /// Clears the evaluation cache. Call on ucinewgame.
        /// </summary>
        public static void ClearCache()
        {
            Array.Clear(_evalCache, 0, _evalCache.Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ProbeEvalCache(ulong hash, out int score)
        {
            ref EvalCacheEntry entry = ref _evalCache[(int)(hash & EvalCacheMask)];
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
            ref EvalCacheEntry entry = ref _evalCache[(int)(hash & EvalCacheMask)];
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

            // Bishop pair
            EvaluateBishopPair(board, ref mgScore, ref egScore);

            // Rook on open/semi-open files
            EvaluateRooks(board, ref mgScore, ref egScore);

            // Mobility
            EvaluateMobility(board, ref mgScore, ref egScore);

            // Knight outposts
            EvaluateOutposts(board, ref mgScore, ref egScore);

            // King safety (middlegame only)
            EvaluateKingSafety(board, ref mgScore);

            // Endgame-specific evaluations (reuse passed pawns computed above)
            EvaluateEndgame(board, ref egScore, phase, wPassedPawns, bPassedPawns);

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
            ulong friendlyRooks = white ? board.WR : board.BR;
            passedPawns = 0;

            // Doubled pawns
            for (int file = 0; file < 8; file++)
            {
                ulong fileMask = Bitboard.FileMasks[file];
                int pawnsOnFile = Bitboard.PopCount(friendlyPawns & fileMask);
                if (pawnsOnFile > 1)
                {
                    mgScore += sign * P.DoubledPawnPenaltyMG * (pawnsOnFile - 1);
                    egScore += sign * P.DoubledPawnPenaltyEG * (pawnsOnFile - 1);
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
                    mgScore += sign * P.IsolatedPawnPenaltyMG;
                    egScore += sign * P.IsolatedPawnPenaltyEG;
                }

                // Passed pawn check
                ulong passedMask = GetPassedPawnMask(sq, white);
                if ((enemyPawns & passedMask) == 0)
                {
                    passedPawns |= Bitboard.SquareBB[sq];
                    int effectiveRank = white ? rank : 7 - rank;
                    mgScore += sign * P.PassedPawnBonusMG[effectiveRank];
                    egScore += sign * P.PassedPawnBonusEG[effectiveRank];

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
                                egScore += sign * P.RookBehindPasserBonus;
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
                    mgScore += sign * P.ConnectedPasserBonusByRank[effectiveRank] / 2; // Divide by 2 to avoid double counting
                    egScore += sign * P.ConnectedPasserBonusByRank[effectiveRank];
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong GetPassedPawnMask(int sq, bool white) => PassedPawnMasks[white ? 1 : 0, sq];

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
                    mgScore += P.RookOpenFileBonusMG;
                    egScore += P.RookOpenFileBonusEG;
                }
                else if ((board.WP & fileMask) == 0)
                {
                    mgScore += P.RookSemiOpenFileBonusMG;
                    egScore += P.RookSemiOpenFileBonusEG;
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
                    mgScore -= P.RookOpenFileBonusMG;
                    egScore -= P.RookOpenFileBonusEG;
                }
                else if ((board.BP & fileMask) == 0)
                {
                    mgScore -= P.RookSemiOpenFileBonusMG;
                    egScore -= P.RookSemiOpenFileBonusEG;
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

            int mobilityDiff = wMobility - bMobility;
            mgScore += mobilityDiff * P.MobilityBonusMG;
            egScore += mobilityDiff * P.MobilityBonusEG;

            // Queen mobility (separate bonus, weighted differently)
            int wQueenMobility = CountQueenMobility(board.WQ, board.WhitePieces, board);
            int bQueenMobility = CountQueenMobility(board.BQ, board.BlackPieces, board);
            int queenMobilityDiff = wQueenMobility - bQueenMobility;
            mgScore += queenMobilityDiff * P.QueenMobilityBonusMG;
            egScore += queenMobilityDiff * P.QueenMobilityBonusEG;
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

        private static void EvaluateEndgame(Board board, ref int egScore, int phase, 
            ulong wPassedPawns, ulong bPassedPawns)
        {
            // Only apply endgame-specific terms when transitioning to endgame
            if (phase >= P.TotalPhase - 4) return; // Still in middlegame

            int wkSq = Bitboard.BitScanForward(board.WK);
            int bkSq = Bitboard.BitScanForward(board.BK);

            // King-pawn proximity in endgame (reuse passed pawns from pawn structure eval)
            EvaluateKingPawnProximity(wkSq, bkSq, wPassedPawns, bPassedPawns, ref egScore);

            // Mop-up evaluation for winning positions
            // Compute material balance once here instead of calling GetMaterialBalance
            int materialBalance = ComputeMaterialBalance(board);
            if (Math.Abs(materialBalance) >= 400)
            {
                EvaluateMopUp(wkSq, bkSq, materialBalance, ref egScore);
            }
        }

        private static void EvaluateKingPawnProximity(int wkSq, int bkSq, 
            ulong wPassedPawns, ulong bPassedPawns, ref int egScore)
        {
            // White king should be close to own passers (to escort) and enemy passers (to stop)
            ulong passers = wPassedPawns;
            while (passers != 0)
            {
                int sq = Bitboard.PopLsb(ref passers);
                int dist = ChebyshevDistance(wkSq, sq);
                egScore += (7 - dist) * P.KingOwnPasserProximity; // Bonus for white king near own passer
            }

            passers = bPassedPawns;
            while (passers != 0)
            {
                int sq = Bitboard.PopLsb(ref passers);
                int dist = ChebyshevDistance(wkSq, sq);
                egScore += (7 - dist) * P.KingEnemyPasserProximity; // Bonus for white king near enemy passer (to stop it)
            }

            // Black king proximity
            passers = bPassedPawns;
            while (passers != 0)
            {
                int sq = Bitboard.PopLsb(ref passers);
                int dist = ChebyshevDistance(bkSq, sq);
                egScore -= (7 - dist) * P.KingOwnPasserProximity;
            }

            passers = wPassedPawns;
            while (passers != 0)
            {
                int sq = Bitboard.PopLsb(ref passers);
                int dist = ChebyshevDistance(bkSq, sq);
                egScore -= (7 - dist) * P.KingEnemyPasserProximity;
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
