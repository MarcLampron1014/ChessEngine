using System;

namespace ChessEngine
{
    public static class Fen
    {
        public const string StartPosition = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

        public static void Load(Board board, string fen)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (string.IsNullOrWhiteSpace(fen)) throw new ArgumentException("FEN is empty", nameof(fen));

            string[] parts = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
                throw new ArgumentException("Invalid FEN (not enough fields)", nameof(fen));

            board.WP = board.WN = board.WB = board.WR = board.WQ = board.WK = 0;
            board.BP = board.BN = board.BB = board.BR = board.BQ = board.BK = 0;

            string placement = parts[0];
            int rank = 7, file = 0;

            foreach (char c in placement)
            {
                if (c == '/')
                {
                    rank--;
                    file = 0;
                    continue;
                }

                if (char.IsDigit(c))
                {
                    file += c - '0';
                    continue;
                }

                Piece p = CharToPiece(c);
                if (p == Piece.Empty)
                    throw new ArgumentException($"Invalid piece char '{c}' in FEN", nameof(fen));
                if (file < 0 || file > 7 || rank < 0 || rank > 7)
                    throw new ArgumentException("Invalid FEN board coordinates", nameof(fen));

                SetPieceDirect(board, rank * 8 + file, p);
                file++;
            }

            bool whiteToMove = parts[1] == "w";

            CastlingRights castling = CastlingRights.None;
            if (parts[2] != "-")
            {
                if (parts[2].Contains('K')) castling |= CastlingRights.WhiteKingSide;
                if (parts[2].Contains('Q')) castling |= CastlingRights.WhiteQueenSide;
                if (parts[2].Contains('k')) castling |= CastlingRights.BlackKingSide;
                if (parts[2].Contains('q')) castling |= CastlingRights.BlackQueenSide;
            }

            int ep = parts[3] != "-" && parts[3].Length == 2 ? SquareStringToIndex(parts[3]) : -1;

            int.TryParse(parts.Length >= 5 ? parts[4] : "0", out int halfmove);
            int.TryParse(parts.Length >= 6 ? parts[5] : "1", out int fullmove);

            board.LoadPosition(whiteToMove, castling, ep, halfmove, fullmove);
        }

        public static string Generate(Board board)
        {
            var sb = new System.Text.StringBuilder();

            for (int rank = 7; rank >= 0; rank--)
            {
                int emptyCount = 0;
                for (int file = 0; file < 8; file++)
                {
                    Piece p = board.PieceAt(rank * 8 + file);
                    if (p == Piece.Empty)
                    {
                        emptyCount++;
                    }
                    else
                    {
                        if (emptyCount > 0) { sb.Append(emptyCount); emptyCount = 0; }
                        sb.Append(PieceToChar(p));
                    }
                }
                if (emptyCount > 0) sb.Append(emptyCount);
                if (rank > 0) sb.Append('/');
            }

            sb.Append(' ').Append(board.WhiteToMove ? 'w' : 'b');

            sb.Append(' ');
            if (board.Castling == CastlingRights.None)
            {
                sb.Append('-');
            }
            else
            {
                if ((board.Castling & CastlingRights.WhiteKingSide) != 0) sb.Append('K');
                if ((board.Castling & CastlingRights.WhiteQueenSide) != 0) sb.Append('Q');
                if ((board.Castling & CastlingRights.BlackKingSide) != 0) sb.Append('k');
                if ((board.Castling & CastlingRights.BlackQueenSide) != 0) sb.Append('q');
            }

            sb.Append(' ').Append(board.EnPassantSquare == -1 ? "-" : IndexToSquareString(board.EnPassantSquare));
            sb.Append(' ').Append(board.HalfMoveClock);
            sb.Append(' ').Append(board.FullMoveNumber);

            return sb.ToString();
        }

        private static void SetPieceDirect(Board board, int sq, Piece p)
        {
            ulong mask = 1UL << sq;

            switch (p)
            {
                case Piece.WP: board.WP |= mask; break;
                case Piece.WN: board.WN |= mask; break;
                case Piece.WB: board.WB |= mask; break;
                case Piece.WR: board.WR |= mask; break;
                case Piece.WQ: board.WQ |= mask; break;
                case Piece.WK: board.WK |= mask; break;
                case Piece.BP: board.BP |= mask; break;
                case Piece.BN: board.BN |= mask; break;
                case Piece.BB: board.BB |= mask; break;
                case Piece.BR: board.BR |= mask; break;
                case Piece.BQ: board.BQ |= mask; break;
                case Piece.BK: board.BK |= mask; break;
            }
        }

        private static Piece CharToPiece(char c)
        {
            return c switch
            {
                'P' => Piece.WP,
                'N' => Piece.WN,
                'B' => Piece.WB,
                'R' => Piece.WR,
                'Q' => Piece.WQ,
                'K' => Piece.WK,
                'p' => Piece.BP,
                'n' => Piece.BN,
                'b' => Piece.BB,
                'r' => Piece.BR,
                'q' => Piece.BQ,
                'k' => Piece.BK,
                _ => Piece.Empty
            };
        }

        private static char PieceToChar(Piece p)
        {
            return p switch
            {
                Piece.WP => 'P',
                Piece.WN => 'N',
                Piece.WB => 'B',
                Piece.WR => 'R',
                Piece.WQ => 'Q',
                Piece.WK => 'K',
                Piece.BP => 'p',
                Piece.BN => 'n',
                Piece.BB => 'b',
                Piece.BR => 'r',
                Piece.BQ => 'q',
                Piece.BK => 'k',
                _ => '?'
            };
        }

        private static int SquareStringToIndex(string sq)
        {
            char fileChar = sq[0];
            char rankChar = sq[1];

            if (fileChar < 'a' || fileChar > 'h')
                return -1;
            if (rankChar < '1' || rankChar > '8')
                return -1;

            int file = fileChar - 'a';
            int rank = rankChar - '1';
            return rank * 8 + file;
        }

        private static string IndexToSquareString(int sq)
        {
            int file = sq % 8;
            int rank = sq / 8;
            return $"{(char)('a' + file)}{rank + 1}";
        }
    }
}
