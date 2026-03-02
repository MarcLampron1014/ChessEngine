using System;

namespace ChessEngine
{
    public static partial class Search
    {
        private static int SEE(Board board, Move move)
        {
            if (!move.IsCapture)
                return 0;

            int to = move.To;
            int from = move.From;

            Piece victim = move.IsEnPassant
                ? (board.WhiteToMove ? Piece.BP : Piece.WP)
                : board.PieceAt(to);
            
            if (victim == Piece.Empty)
                return 0;

            Piece attacker = board.PieceAt(from);
            int attackerValue = GetSEEPieceValue(attacker);
            int victimValue = GetSEEPieceValue(victim);

            if (attackerValue <= victimValue)
                return victimValue - attackerValue;

            ulong occupied = board.AllPieces ^ Bitboard.SquareBB[from];
            if (move.IsEnPassant)
            {
                int epCaptureSq = board.WhiteToMove ? to - 8 : to + 8;
                occupied ^= Bitboard.SquareBB[epCaptureSq];
            }

            ulong attackers = board.AttackersTo(to, occupied) & occupied;
            attackers &= ~Bitboard.SquareBB[from];

            ulong defenders = attackers & (board.WhiteToMove ? board.BlackPieces : board.WhitePieces);

            if (defenders == 0)
                return victimValue;

            int minDefenderValue = GetMinAttackerValue(defenders, board, !board.WhiteToMove);

            if (attackerValue > victimValue + minDefenderValue)
                return victimValue - attackerValue;

            ulong ourAttackers = attackers & (board.WhiteToMove ? board.WhitePieces : board.BlackPieces);
            int ourAttackerCount = Bitboard.PopCount(ourAttackers);
            int theirDefenderCount = Bitboard.PopCount(defenders);

            if (ourAttackerCount > theirDefenderCount)
                return victimValue - attackerValue + 50;

            return victimValue - attackerValue;
        }

        private static int GetSEEPieceValue(Piece p)
        {
            return p switch
            {
                Piece.WP or Piece.BP => 100,
                Piece.WN or Piece.BN => 320,
                Piece.WB or Piece.BB => 330,
                Piece.WR or Piece.BR => 500,
                Piece.WQ or Piece.BQ => 900,
                Piece.WK or Piece.BK => 20000,
                _ => 0
            };
        }

        private static int GetMinAttackerValue(ulong attackers, Board board, bool white)
        {
            if (white)
            {
                if ((attackers & board.WP) != 0) return 100;
                if ((attackers & board.WN) != 0) return 320;
                if ((attackers & board.WB) != 0) return 330;
                if ((attackers & board.WR) != 0) return 500;
                if ((attackers & board.WQ) != 0) return 900;
                if ((attackers & board.WK) != 0) return 20000;
            }
            else
            {
                if ((attackers & board.BP) != 0) return 100;
                if ((attackers & board.BN) != 0) return 320;
                if ((attackers & board.BB) != 0) return 330;
                if ((attackers & board.BR) != 0) return 500;
                if ((attackers & board.BQ) != 0) return 900;
                if ((attackers & board.BK) != 0) return 20000;
            }
            return 20000;
        }
    }
}
