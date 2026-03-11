using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace ChessEngine
{
    /// <summary>
    /// Core bitboard utilities and pre-computed attack tables.
    /// Square mapping: a1=0, h1=7, a8=56, h8=63.
    /// </summary>
    public static class Bitboard
    {
        public const ulong FileA = 0x0101010101010101UL;
        public const ulong FileB = 0x0202020202020202UL;
        public const ulong FileC = 0x0404040404040404UL;
        public const ulong FileD = 0x0808080808080808UL;
        public const ulong FileE = 0x1010101010101010UL;
        public const ulong FileF = 0x2020202020202020UL;
        public const ulong FileG = 0x4040404040404040UL;
        public const ulong FileH = 0x8080808080808080UL;

        public const ulong Rank1 = 0x00000000000000FFUL;
        public const ulong Rank2 = 0x000000000000FF00UL;
        public const ulong Rank3 = 0x0000000000FF0000UL;
        public const ulong Rank4 = 0x00000000FF000000UL;
        public const ulong Rank5 = 0x000000FF00000000UL;
        public const ulong Rank6 = 0x0000FF0000000000UL;
        public const ulong Rank7 = 0x00FF000000000000UL;
        public const ulong Rank8 = 0xFF00000000000000UL;

        public static readonly ulong[] FileMasks = { FileA, FileB, FileC, FileD, FileE, FileF, FileG, FileH };
        public static readonly ulong[] RankMasks = { Rank1, Rank2, Rank3, Rank4, Rank5, Rank6, Rank7, Rank8 };

        public const ulong NotFileA = ~FileA;
        public const ulong NotFileH = ~FileH;
        public const ulong NotFileAB = ~(FileA | FileB);
        public const ulong NotFileGH = ~(FileG | FileH);

        public static readonly ulong[] KnightAttacks = new ulong[64];
        public static readonly ulong[] KingAttacks = new ulong[64];
        public static readonly ulong[][] PawnAttacks = new ulong[2][];
        public static readonly ulong[] SquareBB = new ulong[64];
        public static readonly ulong[,] BetweenBB = new ulong[64, 64];
        public static readonly ulong[,] LineBB = new ulong[64, 64];
        public static readonly ulong[] AdjacentFiles = new ulong[8];

        private static bool _initialized;

        public static void Init()
        {
            if (_initialized) return;

            InitSquareBitboards();
            InitKnightAttacks();
            InitKingAttacks();
            InitPawnAttacks();
            InitBetweenAndLineBB();
            InitAdjacentFiles();

            _initialized = true;
        }

        #region Bit Operations

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PopCount(ulong bb) => BitOperations.PopCount(bb);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int BitScanForward(ulong bb) => BitOperations.TrailingZeroCount(bb);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int BitScanReverse(ulong bb) => 63 - BitOperations.LeadingZeroCount(bb);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PopLsb(ref ulong bb)
        {
            int idx = BitScanForward(bb);
            bb &= bb - 1;
            return idx;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasBit(ulong bb, int square) => (bb & (1UL << square)) != 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong SetBit(ulong bb, int square) => bb | (1UL << square);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ClearBit(ulong bb, int square) => bb & ~(1UL << square);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ToggleBit(ulong bb, int square) => bb ^ (1UL << square);

        #endregion

        #region Square Helpers

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FileOf(int square) => square & 7;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int RankOf(int square) => square >> 3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SquareOf(int file, int rank) => (rank << 3) | file;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MirrorSquare(int square) => square ^ 56;

        #endregion

        #region Shift Operations

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ShiftNorth(ulong bb) => bb << 8;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ShiftSouth(ulong bb) => bb >> 8;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ShiftEast(ulong bb) => (bb << 1) & NotFileA;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ShiftWest(ulong bb) => (bb >> 1) & NotFileH;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ShiftNorthEast(ulong bb) => (bb << 9) & NotFileA;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ShiftNorthWest(ulong bb) => (bb << 7) & NotFileH;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ShiftSouthEast(ulong bb) => (bb >> 7) & NotFileA;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ShiftSouthWest(ulong bb) => (bb >> 9) & NotFileH;

        #endregion

        #region Initialization

        private static void InitSquareBitboards()
        {
            for (int sq = 0; sq < 64; sq++)
            {
                SquareBB[sq] = 1UL << sq;
            }
        }

        private static void InitKnightAttacks()
        {
            for (int sq = 0; sq < 64; sq++)
            {
                ulong bb = SquareBB[sq];
                ulong attacks = 0;

                // All 8 knight moves
                attacks |= (bb << 17) & NotFileA;  // NNE
                attacks |= (bb << 15) & NotFileH;  // NNW
                attacks |= (bb << 10) & NotFileAB; // NEE
                attacks |= (bb << 6) & NotFileGH;  // NWW
                attacks |= (bb >> 17) & NotFileH;  // SSW
                attacks |= (bb >> 15) & NotFileA;  // SSE
                attacks |= (bb >> 10) & NotFileGH; // SWW
                attacks |= (bb >> 6) & NotFileAB;  // SEE

                KnightAttacks[sq] = attacks;
            }
        }

        private static void InitKingAttacks()
        {
            for (int sq = 0; sq < 64; sq++)
            {
                ulong bb = SquareBB[sq];
                ulong attacks = 0;

                // All 8 king moves
                attacks |= ShiftNorth(bb);
                attacks |= ShiftSouth(bb);
                attacks |= ShiftEast(bb);
                attacks |= ShiftWest(bb);
                attacks |= ShiftNorthEast(bb);
                attacks |= ShiftNorthWest(bb);
                attacks |= ShiftSouthEast(bb);
                attacks |= ShiftSouthWest(bb);

                KingAttacks[sq] = attacks;
            }
        }

        private static void InitPawnAttacks()
        {
            PawnAttacks[0] = new ulong[64]; // White
            PawnAttacks[1] = new ulong[64]; // Black

            for (int sq = 0; sq < 64; sq++)
            {
                ulong bb = SquareBB[sq];

                // White pawn attacks (moving north)
                PawnAttacks[0][sq] = ShiftNorthEast(bb) | ShiftNorthWest(bb);

                // Black pawn attacks (moving south)
                PawnAttacks[1][sq] = ShiftSouthEast(bb) | ShiftSouthWest(bb);
            }
        }

        private static void InitBetweenAndLineBB()
        {
            // Initialize all to 0
            for (int sq1 = 0; sq1 < 64; sq1++)
            {
                for (int sq2 = 0; sq2 < 64; sq2++)
                {
                    BetweenBB[sq1, sq2] = 0;
                    LineBB[sq1, sq2] = 0;
                }
            }

            // Directions: N, S, E, W, NE, NW, SE, SW
            int[] directions = { 8, -8, 1, -1, 9, 7, -9, -7 };

            for (int sq1 = 0; sq1 < 64; sq1++)
            {
                int file1 = FileOf(sq1);
                int rank1 = RankOf(sq1);

                foreach (int dir in directions)
                {
                    ulong line = SquareBB[sq1];
                    int sq = sq1;

                    while (true)
                    {
                        int prevFile = FileOf(sq);
                        int prevRank = RankOf(sq);
                        sq += dir;

                        if (sq < 0 || sq >= 64) break;

                        int newFile = FileOf(sq);
                        int newRank = RankOf(sq);

                        // Check for wrapping
                        int fileDiff = Math.Abs(newFile - prevFile);
                        int rankDiff = Math.Abs(newRank - prevRank);

                        // For horizontal moves, file changes by 1, rank stays same
                        // For vertical moves, rank changes by 1, file stays same
                        // For diagonal moves, both change by 1
                        bool validMove = false;
                        if (dir == 8 || dir == -8) validMove = fileDiff == 0 && rankDiff == 1;
                        else if (dir == 1 || dir == -1) validMove = fileDiff == 1 && rankDiff == 0;
                        else validMove = fileDiff == 1 && rankDiff == 1;

                        if (!validMove) break;

                        line |= SquareBB[sq];
                        LineBB[sq1, sq] = line | GetRayBeyond(sq, dir);

                        // Between is everything except endpoints
                        BetweenBB[sq1, sq] = line & ~SquareBB[sq1] & ~SquareBB[sq];
                    }
                }
            }
        }

        private static ulong GetRayBeyond(int sq, int dir)
        {
            ulong ray = 0;
            while (true)
            {
                int prevFile = FileOf(sq);
                int prevRank = RankOf(sq);
                sq += dir;

                if (sq < 0 || sq >= 64) break;

                int newFile = FileOf(sq);
                int newRank = RankOf(sq);

                int fileDiff = Math.Abs(newFile - prevFile);
                int rankDiff = Math.Abs(newRank - prevRank);

                bool validMove = false;
                if (dir == 8 || dir == -8) validMove = fileDiff == 0 && rankDiff == 1;
                else if (dir == 1 || dir == -1) validMove = fileDiff == 1 && rankDiff == 0;
                else validMove = fileDiff == 1 && rankDiff == 1;

                if (!validMove) break;

                ray |= SquareBB[sq];
            }
            return ray;
        }

        private static void InitAdjacentFiles()
        {
            for (int f = 0; f < 8; f++)
            {
                ulong adj = 0;
                if (f > 0) adj |= FileMasks[f - 1];
                if (f < 7) adj |= FileMasks[f + 1];
                AdjacentFiles[f] = adj;
            }
        }

        #endregion

        #region Debug Helpers

        /// <summary>
        /// Print a bitboard as a visual 8x8 grid.
        /// </summary>
        public static void PrintBitboard(ulong bb)
        {
            Console.WriteLine("  a b c d e f g h");
            for (int rank = 7; rank >= 0; rank--)
            {
                Console.Write($"{rank + 1} ");
                for (int file = 0; file < 8; file++)
                {
                    int sq = SquareOf(file, rank);
                    Console.Write(HasBit(bb, sq) ? "1 " : ". ");
                }
                Console.WriteLine();
            }
            Console.WriteLine();
        }

        #endregion
    }
}
