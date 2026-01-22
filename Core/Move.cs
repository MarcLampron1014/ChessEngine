using System;

namespace ChessEngine.Core
{
    public struct Move
    {
        public readonly int From;        // 0–63
        public readonly int To;          // 0–63
        public readonly Piece Promotion; // Empty if not a promotion
        public readonly MoveFlags Flags; // Special move information
    };

    [Flags]
    public enum MoveFlags
    {
        None = 0,
        Capture = 1,
        EnPassant = 2,
        Castling = 4,
        PawnDoublePush = 8
    }

    public Move(int from, int to, Piece Promotion, MoveFlags flags = MoveFlags.None){
        From = from;
        To = to;
        Promotion = promotion;
        Flags = flags;
    }

    public bool IsPromotion => Promotion != Piece.Empty;
    public bool IsCapture => (Flags & MoveFlags.Capture) != 0;
    public bool IsEnPassant => (Flags & MoveFlags.EnPassant) != 0;
    public bool IsCastling => (Flags & MoveFlags.Castling) != 0;

    // To output UCI format ex: e2e4, e7e8q
    public override string ToString()
    {
        return $"{SquareToString(From)}{SquareToString(To)}" +(IsPromotion ? PromotionToChar(Promotion).ToString() : "");
    }
    
    private static string SquareToString(int square)
    {
        int file = square % 8;
        int rank = square / 8;
        return $"{(char)('a' + file)}{rank + 1}";
    }

    private static char PromotionToChar(Piece p)
    {
        return p switch
        {
            Piece.WQ or Piece.BQ => 'q',
            Piece.WR or Piece.BR => 'r',
            Piece.WB or Piece.BB => 'b',
            Piece.WN or Piece.BN => 'n',
            _ => ' '
        };
    }
}