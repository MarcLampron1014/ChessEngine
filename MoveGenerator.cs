using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ChessEngine
{
    public static class MoveGenerator
    {
        private static readonly int[] KnightOffsets =
        {
            15, 17, 10, 6, -15, -17, -10, -6
        };
        private static readonly int[] BishopDirections = 
        { 
            7, 9, -7, -9 
        };
        private static readonly int[] RookDirections   = 
        { 
            8, -8, 1, -1 
        };
        private static readonly int[] KingOffsets =
        {
            8, -8, 1, -1, 9, 7, -9, -7
        };

        public static List<Move> GenerateLegalMoves(Board board)
        {
            var moves = GenerateMoves(board);
            var legalMoves = new List<Move>();

            foreach (var move in moves)
            {
                // Store which side is moving before the move
                bool sideThatMoved = board.WhiteToMove;
                
                board.MakeMove(move);

                // Check if the side that just moved is in check
                bool inCheck = board.IsKingInCheck(sideThatMoved);

                board.UndoMove(move);

                if (!inCheck)
                    legalMoves.Add(move);
            }

            return legalMoves;
        }

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
                    case Piece.WB:
                    case Piece.BB:
                        GenerateSlidingMoves(board, square, moves, BishopDirections);
                        break;

                    case Piece.WR:
                    case Piece.BR:
                        GenerateSlidingMoves(board, square, moves, RookDirections);
                        break;

                    case Piece.WQ:
                    case Piece.BQ:
                        GenerateSlidingMoves(board, square, moves, BishopDirections);
                        GenerateSlidingMoves(board, square, moves, RookDirections);
                        break;
                    case Piece.WK:
                    case Piece.BK:
                        GenerateKingMoves(board, square, moves);
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

        //This functions despite the complexity is fast because for example: max amount of bishop square= 13 for the queen it's 27 which is far too small to worry about
        private static void GenerateSlidingMoves(Board board, int from, List<Move> moves, int[] directions)
        {
            bool white = board.WhiteToMove;
            int fromFile = from % 8;
            int fromRank = from / 8;

            foreach (int direction in directions)
            {
                int to = from;
                while (true)
                {
                    int previous = to;
                    to += direction;
                    if(!IsOnBoard(to))
                        break;
                    
                    //prevents rooks and queens from going around the board
                    if((direction == 1 || direction == -1) && Math.Abs((to % 8) - (previous % 8)) > 1)
                        break;
                    
                    Piece target = board.Squares[to];
                    if (target == Piece.Empty)
                    {
                        moves.Add(new Move(from,to));
                    }
                    else
                    {
                        if(target.IsWhite() != white){
                            moves.Add(new Move(from, to,Piece.Empty,MoveFlags.Capture));
                        }
                        break;
                    }
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


        public static void GenerateKingMoves(Board board, int from, List<Move> moves)
        {
            bool white = board.WhiteToMove;
            int fromFile = from % 8;
            int fromRank = from / 8;

            foreach (int offset in KingOffsets)
            {
                int to = from + offset;
                if (!IsOnBoard(to))
                    continue;

                int toFile = to % 8;
                int toRank = to / 8;

                if (Math.Abs(fromFile - toFile) > 1 ||
                    Math.Abs(fromRank - toRank) > 1)
                    continue;

                Piece target = board.Squares[to];
                bool enemyIsWhite = !white;

                if (board.IsSquareAttacked(to, enemyIsWhite))
                    continue;

                if (target == Piece.Empty)
                {
                    moves.Add(new Move(from, to));
                }
                else if (target.IsWhite() != white)
                {
                    moves.Add(new Move(from, to, Piece.Empty, MoveFlags.Capture));
                }
            }

            // ==========================
            // Castling (LEGAL)
            // ==========================
            if (white)
            {
                // White king side
                if ((board.Castling & CastlingRights.WhiteKingSide) != 0 &&
                    board.Squares[5] == Piece.Empty &&
                    board.Squares[6] == Piece.Empty &&
                    !board.IsSquareAttacked(4, false) &&
                    !board.IsSquareAttacked(5, false) &&
                    !board.IsSquareAttacked(6, false))
                {
                    moves.Add(new Move(4, 6, Piece.Empty, MoveFlags.Castling));
                }

                // White queen side
                if ((board.Castling & CastlingRights.WhiteQueenSide) != 0 &&
                    board.Squares[3] == Piece.Empty &&
                    board.Squares[2] == Piece.Empty &&
                    board.Squares[1] == Piece.Empty &&
                    !board.IsSquareAttacked(4, false) &&
                    !board.IsSquareAttacked(3, false) &&
                    !board.IsSquareAttacked(2, false))
                {
                    moves.Add(new Move(4, 2, Piece.Empty, MoveFlags.Castling));
                }
            }
            else
            {
                // Black king side
                if ((board.Castling & CastlingRights.BlackKingSide) != 0 &&
                    board.Squares[61] == Piece.Empty &&
                    board.Squares[62] == Piece.Empty &&
                    !board.IsSquareAttacked(60, true) &&
                    !board.IsSquareAttacked(61, true) &&
                    !board.IsSquareAttacked(62, true))
                {
                    moves.Add(new Move(60, 62, Piece.Empty, MoveFlags.Castling));
                }

                // Black queen side
                if ((board.Castling & CastlingRights.BlackQueenSide) != 0 &&
                    board.Squares[59] == Piece.Empty &&
                    board.Squares[58] == Piece.Empty &&
                    board.Squares[57] == Piece.Empty &&
                    !board.IsSquareAttacked(60, true) &&
                    !board.IsSquareAttacked(59, true) &&
                    !board.IsSquareAttacked(58, true))
                {
                    moves.Add(new Move(60, 58, Piece.Empty, MoveFlags.Castling));
                }
            }
        }




    }
}
// TO DO:

// add king and castling
 
// add en passant

// filter illegal moves( king in checked)

// add alpha beta search

// implement Perft