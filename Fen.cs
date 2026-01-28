using System;

namespace ChessEngine
{
    public static class Fen
    {
        // Supports: pieces, side-to-move, castling, en-passant, halfmove, fullmove
        public static void Load(Board board, string fen)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (string.IsNullOrWhiteSpace(fen)) throw new ArgumentException("FEN is empty", nameof(fen));

            string[] parts = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
                throw new ArgumentException("Invalid FEN (not enough fields)", nameof(fen));

            // Field 1: piece placement (ranks 8 to 1)
            Piece[] squares = new Piece[64];
            Array.Fill(squares, Piece.Empty);

            string placement = parts[0];
            int rank = 7;
            int file = 0;

            for (int i = 0; i < placement.Length; i++)
            {
                char c = placement[i];
                if (c == '/')
                {
                    rank--;
                    file = 0;
                    continue;
                }

                if (char.IsDigit(c))
                {
                    file += (c - '0');
                    continue;
                }

                Piece p = CharToPiece(c);
                if (p == Piece.Empty)
                    throw new ArgumentException($"Invalid piece char '{c}' in FEN", nameof(fen));

                if (file < 0 || file > 7 || rank < 0 || rank > 7)
                    throw new ArgumentException("Invalid FEN board coordinates", nameof(fen));

                int square = rank * 8 + file;
                squares[square] = p;
                file++;
            }

            // Field 2: active color
            bool whiteToMove = parts[1] == "w";

            // Field 3: castling
            CastlingRights castling = CastlingRights.None;
            string castlingStr = parts[2];
            if (castlingStr != "-")
            {
                if (castlingStr.Contains('K')) castling |= CastlingRights.WhiteKingSide;
                if (castlingStr.Contains('Q')) castling |= CastlingRights.WhiteQueenSide;
                if (castlingStr.Contains('k')) castling |= CastlingRights.BlackKingSide;
                if (castlingStr.Contains('q')) castling |= CastlingRights.BlackQueenSide;
            }

            // Field 4: en-passant
            int ep = -1;
            string epStr = parts[3];
            if (epStr != "-" && epStr.Length == 2)
            {
                ep = SquareStringToIndex(epStr);
            }

            int halfmove = 0;
            int fullmove = 1;

            if (parts.Length >= 5)
                int.TryParse(parts[4], out halfmove);
            if (parts.Length >= 6)
                int.TryParse(parts[5], out fullmove);

            board.LoadPosition(squares, whiteToMove, castling, ep, halfmove, fullmove);
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
    }
}
