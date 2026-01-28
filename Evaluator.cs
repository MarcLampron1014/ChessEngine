using System;

namespace ChessEngine
{
    public static class Evaluator
    {
        // Positive = White better, Negative = Black better.
        // Values in centipawns.
        private const int PawnValue = 100;
        private const int KnightValue = 320;
        private const int BishopValue = 330;
        private const int RookValue = 500;
        private const int QueenValue = 900;
        private const int KingValue = 0; // King value is handled via mate scores in search.

        // Piece-square tables oriented from White's perspective.
        // Index 0 = a1, 7 = h1, 56 = a8, 63 = h8 (matches your board indexing).
        private static readonly int[] PawnPst =
        {
            0, 0, 0, 0, 0, 0, 0, 0,
            5, 10, 10, -20, -20, 10, 10, 5,
            5, -5, -10, 0, 0, -10, -5, 5,
            0, 0, 0, 20, 20, 0, 0, 0,
            5, 5, 10, 25, 25, 10, 5, 5,
            10, 10, 20, 30, 30, 20, 10, 10,
            50, 50, 50, 50, 50, 50, 50, 50,
            0, 0, 0, 0, 0, 0, 0, 0,
        };

        private static readonly int[] KnightPst =
        {
            -50, -40, -30, -30, -30, -30, -40, -50,
            -40, -20, 0, 0, 0, 0, -20, -40,
            -30, 0, 10, 15, 15, 10, 0, -30,
            -30, 5, 15, 20, 20, 15, 5, -30,
            -30, 0, 15, 20, 20, 15, 0, -30,
            -30, 5, 10, 15, 15, 10, 5, -30,
            -40, -20, 0, 5, 5, 0, -20, -40,
            -50, -40, -30, -30, -30, -30, -40, -50,
        };

        private static readonly int[] BishopPst =
        {
            -20, -10, -10, -10, -10, -10, -10, -20,
            -10, 0, 0, 0, 0, 0, 0, -10,
            -10, 0, 5, 10, 10, 5, 0, -10,
            -10, 5, 5, 10, 10, 5, 5, -10,
            -10, 0, 10, 10, 10, 10, 0, -10,
            -10, 10, 10, 10, 10, 10, 10, -10,
            -10, 5, 0, 0, 0, 0, 5, -10,
            -20, -10, -10, -10, -10, -10, -10, -20,
        };

        private static readonly int[] RookPst =
        {
            0, 0, 0, 0, 0, 0, 0, 0,
            5, 10, 10, 10, 10, 10, 10, 5,
            -5, 0, 0, 0, 0, 0, 0, -5,
            -5, 0, 0, 0, 0, 0, 0, -5,
            -5, 0, 0, 0, 0, 0, 0, -5,
            -5, 0, 0, 0, 0, 0, 0, -5,
            -5, 0, 0, 0, 0, 0, 0, -5,
            0, 0, 0, 5, 5, 0, 0, 0,
        };

        private static readonly int[] QueenPst =
        {
            -20, -10, -10, -5, -5, -10, -10, -20,
            -10, 0, 0, 0, 0, 0, 0, -10,
            -10, 0, 5, 5, 5, 5, 0, -10,
            -5, 0, 5, 5, 5, 5, 0, -5,
            0, 0, 5, 5, 5, 5, 0, -5,
            -10, 5, 5, 5, 5, 5, 0, -10,
            -10, 0, 5, 0, 0, 0, 0, -10,
            -20, -10, -10, -5, -5, -10, -10, -20,
        };

        // Very light king PST (middlegame-ish). Endgame tuning is out of scope here.
        private static readonly int[] KingPst =
        {
            -30, -40, -40, -50, -50, -40, -40, -30,
            -30, -40, -40, -50, -50, -40, -40, -30,
            -30, -40, -40, -50, -50, -40, -40, -30,
            -30, -40, -40, -50, -50, -40, -40, -30,
            -20, -30, -30, -40, -40, -30, -30, -20,
            -10, -20, -20, -20, -20, -20, -20, -10,
            20, 20, 0, 0, 0, 0, 20, 20,
            20, 30, 10, 0, 0, 10, 30, 20,
        };

        public static int Evaluate(Board board)
        {
            int score = 0;

            for (int square = 0; square < 64; square++)
            {
                Piece p = board.Squares[square];
                if (p == Piece.Empty)
                    continue;

                bool white = p.IsWhite();
                int pieceScore = GetPieceValue(p) + GetPstValue(p, square);

                score += white ? pieceScore : -pieceScore;
            }

            return score;
        }

        public static int GetPieceValue(Piece p)
        {
            return p switch
            {
                Piece.WP or Piece.BP => PawnValue,
                Piece.WN or Piece.BN => KnightValue,
                Piece.WB or Piece.BB => BishopValue,
                Piece.WR or Piece.BR => RookValue,
                Piece.WQ or Piece.BQ => QueenValue,
                Piece.WK or Piece.BK => KingValue,
                _ => 0
            };
        }

        private static int GetPstValue(Piece p, int square)
        {
            bool white = p.IsWhite();
            int idx = white ? square : MirrorSquare(square);

            return p switch
            {
                Piece.WP or Piece.BP => PawnPst[idx],
                Piece.WN or Piece.BN => KnightPst[idx],
                Piece.WB or Piece.BB => BishopPst[idx],
                Piece.WR or Piece.BR => RookPst[idx],
                Piece.WQ or Piece.BQ => QueenPst[idx],
                Piece.WK or Piece.BK => KingPst[idx],
                _ => 0
            };
        }

        private static int MirrorSquare(int square)
        {
            // Flip rank: a1<->a8 etc.
            int file = square % 8;
            int rank = square / 8;
            int mirroredRank = 7 - rank;
            return mirroredRank * 8 + file;
        }
    }
}
