using System;
using System.Runtime.CompilerServices;

namespace ChessEngine
{
    public enum Piece
    {
        Empty = 0,
        WP, WN, WB, WR, WQ, WK,
        BP, BN, BB, BR, BQ, BK
    }

    [Flags]
    public enum CastlingRights
    {
        None = 0,
        WhiteKingSide = 1,
        WhiteQueenSide = 2,
        BlackKingSide = 4,
        BlackQueenSide = 8,
    }

    [Flags]
    public enum MoveFlags
    {
        None = 0,
        Capture = 1,
        EnPassant = 2,
        Castling = 4,
        PawnDoublePush = 8
    }

    public static class PieceExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsWhite(this Piece p) => p >= Piece.WP && p <= Piece.WK;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsBlack(this Piece p) => p >= Piece.BP && p <= Piece.BK;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsEmpty(this Piece p) => p == Piece.Empty;
    }
}
