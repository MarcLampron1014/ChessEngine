using System;

namespace ChessEngine
{
    public static class Zobrist
    {
        public static readonly ulong[,] PieceKeys = new ulong[13, 64];
        public static readonly ulong[] CastlingKeys = new ulong[16];
        public static readonly ulong[] EnPassantKeys = new ulong[8];
        public static readonly ulong SideToMoveKey;

        static Zobrist()
        {
            var rng = new Random(1234);

            for (int piece = 1; piece <= 12; piece++)
                for (int sq = 0; sq < 64; sq++)
                    PieceKeys[piece, sq] = NextUInt64(rng);

            for (int i = 0; i < 16; i++)
                CastlingKeys[i] = NextUInt64(rng);

            for (int file = 0; file < 8; file++)
                EnPassantKeys[file] = NextUInt64(rng);

            SideToMoveKey = NextUInt64(rng);
        }

        private static ulong NextUInt64(Random rng)
        {
            byte[] buffer = new byte[8];
            rng.NextBytes(buffer);
            return BitConverter.ToUInt64(buffer, 0);
        }
    }
}
