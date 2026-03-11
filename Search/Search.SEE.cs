using System;
using System.Runtime.CompilerServices;

namespace ChessEngine
{
    public static partial class Search
    {
        private static readonly int[] SEEPieceValues = { 0, 100, 320, 330, 500, 900, 20000, 100, 320, 330, 500, 900, 20000 };

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

            int[] gain = GainArray;
            int depth = 0;

            ulong occupied = board.AllPieces;
            if (move.IsEnPassant)
            {
                int epCaptureSq = board.WhiteToMove ? to - 8 : to + 8;
                occupied ^= Bitboard.SquareBB[epCaptureSq];
            }

            ulong fromBB = Bitboard.SquareBB[from];

            gain[depth] = SEEPieceValues[(int)victim];

            bool sideToMove = !board.WhiteToMove;

            do
            {
                depth++;
                gain[depth] = SEEPieceValues[(int)attacker] - gain[depth - 1];

                if (Math.Max(-gain[depth - 1], gain[depth]) < 0)
                    break;

                occupied ^= fromBB;

                ulong attackers = board.AttackersTo(to, occupied) & occupied;

                ulong sideAttackers = attackers & (sideToMove ? board.WhitePieces : board.BlackPieces);
                if (sideAttackers == 0)
                    break;

                attacker = GetLeastValuableAttacker(sideAttackers, board, sideToMove, out fromBB);
                sideToMove = !sideToMove;
            } while (true);

            while (--depth > 0)
            {
                gain[depth - 1] = -Math.Max(-gain[depth - 1], gain[depth]);
            }

            return gain[0];
        }

        [ThreadStatic]
        private static int[]? _gainArray;
        private static int[] GainArray => _gainArray ??= new int[33];

        private static Piece GetLeastValuableAttacker(ulong attackers, Board board, bool white, out ulong fromBB)
        {
            if (white)
            {
                ulong bb;
                bb = attackers & board.WP; if (bb != 0) { fromBB = bb & (ulong)-(long)bb; return Piece.WP; }
                bb = attackers & board.WN; if (bb != 0) { fromBB = bb & (ulong)-(long)bb; return Piece.WN; }
                bb = attackers & board.WB; if (bb != 0) { fromBB = bb & (ulong)-(long)bb; return Piece.WB; }
                bb = attackers & board.WR; if (bb != 0) { fromBB = bb & (ulong)-(long)bb; return Piece.WR; }
                bb = attackers & board.WQ; if (bb != 0) { fromBB = bb & (ulong)-(long)bb; return Piece.WQ; }
                bb = attackers & board.WK; if (bb != 0) { fromBB = bb & (ulong)-(long)bb; return Piece.WK; }
            }
            else
            {
                ulong bb;
                bb = attackers & board.BP; if (bb != 0) { fromBB = bb & (ulong)-(long)bb; return Piece.BP; }
                bb = attackers & board.BN; if (bb != 0) { fromBB = bb & (ulong)-(long)bb; return Piece.BN; }
                bb = attackers & board.BB; if (bb != 0) { fromBB = bb & (ulong)-(long)bb; return Piece.BB; }
                bb = attackers & board.BR; if (bb != 0) { fromBB = bb & (ulong)-(long)bb; return Piece.BR; }
                bb = attackers & board.BQ; if (bb != 0) { fromBB = bb & (ulong)-(long)bb; return Piece.BQ; }
                bb = attackers & board.BK; if (bb != 0) { fromBB = bb & (ulong)-(long)bb; return Piece.BK; }
            }
            fromBB = 0;
            return Piece.Empty;
        }

        private static int GetSEEPieceValue(Piece p)
        {
            return SEEPieceValues[(int)p];
        }
    }
}
