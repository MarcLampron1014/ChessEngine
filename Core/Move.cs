using System;

namespace ChessEngine
{
    public struct Move
    {
        public readonly int From;
        public readonly int To;
        public readonly Piece Promotion;
        public readonly MoveFlags Flags;

        public Move(int from, int to, Piece promotion = Piece.Empty, MoveFlags flags = MoveFlags.None)
        {
            From = from;
            To = to;
            Promotion = promotion;
            Flags = flags;
        }

        public bool IsPromotion => Promotion != Piece.Empty;
        public bool IsCapture => (Flags & MoveFlags.Capture) != 0;
        public bool IsEnPassant => (Flags & MoveFlags.EnPassant) != 0;
        public bool IsCastling => (Flags & MoveFlags.Castling) != 0;

        public override string ToString()
        {
            return $"{SquareToString(From)}{SquareToString(To)}" +
                   (IsPromotion ? PromotionToChar(Promotion).ToString() : "");
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
}
