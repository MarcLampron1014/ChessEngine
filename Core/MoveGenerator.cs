using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ChessEngine.Core
{
    public static class MoveGenerator
    {
        private static readonly int[] KnightOffsets =
        {
            15, 17, 10, 6, -15, -17, -10, -6
        };

        public static List<Move> GenerateMoves(Board board)
        {
            var moves = new List<Move>(64);

            for(int square= 0; square< 64; square++)
            {
                Piece piece= board.Squares[square];
                if(piece == Piece.Empty)
                    continue;
                if (board.WhiteToMove && !piece.IsWhite())
                    continue;
                if (!board.WhiteToMove && !piece.IsBlack())
                    continue;

                switch (piece)
                {
                    case Piece.WP:
                    case Piece.BP:
                        GeneratePawnMoves(board, square, moves);
                        break;
                    case Piece.WN:
                    case Piece.BN:
                        GenerateKnightMoves(board, square, moves);
                        break;
                }
            }
            return moves;
        }
        private static void GeneratePawnMoves(Board board, int from, List<Move> moves)
        {
            bool white = board.WhiteToMove;
            int rank = from / 8;
            int file = from % 8;

            int direction = white ? 8 : -8;
            int startRank = white ? 1 : 6;
            int promotionRank = white ? 6 : 1;

            int oneForward = from + direction;

            // Forward move
            if (IsOnBoard(oneForward) && board.Squares[oneForward] == Piece.Empty)
            {
                if (rank == promotionRank)
                {
                    AddPromotions(from, oneForward, white, moves);
                }
                else
                {
                    moves.Add(new Move(from, oneForward));

                    // Double push
                    if (rank == startRank)
                    {
                        int twoForward = oneForward + direction;
                        if (board.Squares[twoForward] == Piece.Empty)
                        {
                            moves.Add(new Move(
                                from,
                                twoForward,
                                Piece.Empty,
                                MoveFlags.PawnDoublePush
                            ));
                        }
                    }
                }
            }
            // Captures
            int[] captureOffsets = white ? new[] { 7, 9 } : new[] { -9, -7 };

            foreach (int offset in captureOffsets)
            {
                int to = from + offset;
                if (!IsOnBoard(to))
                    continue;

                int targetFile = to % 8;
                if (System.Math.Abs(targetFile - file) != 1)
                    continue;

                Piece target = board.Squares[to];
                if (target != Piece.Empty && target.IsWhite() != white)
                {
                    if (rank == promotionRank)
                    {
                        AddPromotions(from, to, white, moves, MoveFlags.Capture);
                    }
                    else
                    {
                        moves.Add(new Move(from, to, Piece.Empty, MoveFlags.Capture));
                    }
                }
            }

            // En passant (placeholder — requires board.EnPassantSquare)
        }
        
        
        private static void AddPromotions(
            int from,
            int to,
            bool white,
            List<Move> moves,
            MoveFlags extraFlags = MoveFlags.None)
        {
            moves.Add(new Move(from, to, white ? Piece.WQ : Piece.BQ, extraFlags));
            moves.Add(new Move(from, to, white ? Piece.WR : Piece.BR, extraFlags));
            moves.Add(new Move(from, to, white ? Piece.WB : Piece.BB, extraFlags));
            moves.Add(new Move(from, to, white ? Piece.WN : Piece.BN, extraFlags));
        }

        // ==========================
        // Knight Moves
        // ==========================
        private static void GenerateKnightMoves(Board board, int from, List<Move> moves)
        {
            bool white = board.WhiteToMove;
            int fromFile = from % 8;
            int fromRank = from / 8;

            foreach (int offset in KnightOffsets)
            {
                int to = from + offset;
                if (!IsOnBoard(to))
                    continue;

                int toFile = to % 8;
                int toRank = to / 8;

                int fileDiff = System.Math.Abs(fromFile - toFile);
                int rankDiff = System.Math.Abs(fromRank - toRank);

                if (fileDiff + rankDiff != 3)
                    continue;

                Piece target = board.Squares[to];

                if (target == Piece.Empty)
                {
                    moves.Add(new Move(from, to));
                }
                else if (target.IsWhite() != white)
                {
                    moves.Add(new Move(from, to, Piece.Empty, MoveFlags.Capture));
                }
            }
        }

        // ==========================
        // Helpers
        // ==========================
        private static bool IsOnBoard(int square)
        {
            return square >= 0 && square < 64;
        }
    }
}
// TO DO:
// Add bishop
// Add rooks
// add queen sliding moves

// add king and castling
 
// add en passant

// filter illegal moves( king in checked)

// add alpha beta search

// implement Perft