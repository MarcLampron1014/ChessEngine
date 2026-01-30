using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ChessEngine
{
    public static class MoveGenerator
    {
        /// <summary>
        /// Generate all legal moves for the current position.
        /// </summary>
        public static List<Move> GenerateLegalMoves(Board board)
        {
            var moves = GenerateMoves(board);
            var legalMoves = new List<Move>(moves.Count);
            
            foreach (var move in moves)
            {
                bool sideThatMoved = board.WhiteToMove;
                
                board.MakeMove(move);
                bool inCheck = board.IsKingInCheck(sideThatMoved);
                board.UndoMove(move);

                if (!inCheck)
                    legalMoves.Add(move);
            }

            return legalMoves;
        }

        /// <summary>
        /// Generate all pseudo-legal moves (may leave king in check).
        /// </summary>
        public static List<Move> GenerateMoves(Board board)
        {
            var moves = new List<Move>(64);
            bool white = board.WhiteToMove;

            if (white)
            {
                GeneratePawnMoves(board, moves, white);
                GenerateKnightMoves(board, board.WN, moves, board.WhitePieces);
                GenerateBishopMoves(board, board.WB, moves, board.WhitePieces);
                GenerateRookMoves(board, board.WR, moves, board.WhitePieces);
                GenerateQueenMoves(board, board.WQ, moves, board.WhitePieces);
                GenerateKingMoves(board, board.WK, moves, white);
            }
            else
            {
                GeneratePawnMoves(board, moves, white);
                GenerateKnightMoves(board, board.BN, moves, board.BlackPieces);
                GenerateBishopMoves(board, board.BB, moves, board.BlackPieces);
                GenerateRookMoves(board, board.BR, moves, board.BlackPieces);
                GenerateQueenMoves(board, board.BQ, moves, board.BlackPieces);
                GenerateKingMoves(board, board.BK, moves, white);
            }

            return moves;
        }

        /// <summary>
        /// Generate only capture moves (for quiescence search).
        /// </summary>
        public static List<Move> GenerateCaptures(Board board)
        {
            var moves = new List<Move>(32);
            bool white = board.WhiteToMove;
            ulong enemies = white ? board.BlackPieces : board.WhitePieces;

            if (white)
            {
                GeneratePawnCaptures(board, moves, white);
                GenerateKnightCaptures(board, board.WN, moves, enemies);
                GenerateBishopCaptures(board, board.WB, moves, board.WhitePieces, enemies);
                GenerateRookCaptures(board, board.WR, moves, board.WhitePieces, enemies);
                GenerateQueenCaptures(board, board.WQ, moves, board.WhitePieces, enemies);
                GenerateKingCaptures(board, board.WK, moves, white, enemies);
            }
            else
            {
                GeneratePawnCaptures(board, moves, white);
                GenerateKnightCaptures(board, board.BN, moves, enemies);
                GenerateBishopCaptures(board, board.BB, moves, board.BlackPieces, enemies);
                GenerateRookCaptures(board, board.BR, moves, board.BlackPieces, enemies);
                GenerateQueenCaptures(board, board.BQ, moves, board.BlackPieces, enemies);
                GenerateKingCaptures(board, board.BK, moves, white, enemies);
            }

            return moves;
        }

        #region Pawn Moves

        private static void GeneratePawnMoves(Board board, List<Move> moves, bool white)
        {
            ulong pawns = white ? board.WP : board.BP;
            ulong enemies = white ? board.BlackPieces : board.WhitePieces;
            ulong empty = ~board.AllPieces;

            int direction = white ? 8 : -8;
            ulong startRank = white ? Bitboard.Rank2 : Bitboard.Rank7;
            ulong promotionRank = white ? Bitboard.Rank7 : Bitboard.Rank2;
            ulong prePromotionRank = white ? Bitboard.Rank8 : Bitboard.Rank1;

            // Single push
            ulong singlePush = white ? Bitboard.ShiftNorth(pawns) : Bitboard.ShiftSouth(pawns);
            singlePush &= empty;

            // Double push
            ulong doublePush = white ? Bitboard.ShiftNorth(singlePush & Bitboard.Rank3) 
                                     : Bitboard.ShiftSouth(singlePush & Bitboard.Rank6);
            doublePush &= empty;

            // Promotions
            ulong promotions = singlePush & prePromotionRank;
            singlePush &= ~prePromotionRank;

            // Add single pushes
            while (singlePush != 0)
            {
                int to = Bitboard.PopLsb(ref singlePush);
                int from = to - direction;
                moves.Add(new Move(from, to));
            }

            // Add double pushes
            while (doublePush != 0)
            {
                int to = Bitboard.PopLsb(ref doublePush);
                int from = to - direction * 2;
                moves.Add(new Move(from, to, Piece.Empty, MoveFlags.PawnDoublePush));
            }

            // Add promotions
            while (promotions != 0)
            {
                int to = Bitboard.PopLsb(ref promotions);
                int from = to - direction;
                AddPromotions(from, to, white, moves);
            }

            // Captures
            ulong leftCaptures, rightCaptures;
            if (white)
            {
                leftCaptures = Bitboard.ShiftNorthWest(pawns) & enemies;
                rightCaptures = Bitboard.ShiftNorthEast(pawns) & enemies;
            }
            else
            {
                leftCaptures = Bitboard.ShiftSouthEast(pawns) & enemies;
                rightCaptures = Bitboard.ShiftSouthWest(pawns) & enemies;
            }

            // Left captures (promotions and non-promotions)
            ulong leftPromotions = leftCaptures & prePromotionRank;
            leftCaptures &= ~prePromotionRank;

            while (leftCaptures != 0)
            {
                int to = Bitboard.PopLsb(ref leftCaptures);
                int from = white ? to - 7 : to + 9;
                moves.Add(new Move(from, to, Piece.Empty, MoveFlags.Capture));
            }

            while (leftPromotions != 0)
            {
                int to = Bitboard.PopLsb(ref leftPromotions);
                int from = white ? to - 7 : to + 9;
                AddPromotions(from, to, white, moves, MoveFlags.Capture);
            }

            // Right captures (promotions and non-promotions)
            ulong rightPromotions = rightCaptures & prePromotionRank;
            rightCaptures &= ~prePromotionRank;

            while (rightCaptures != 0)
            {
                int to = Bitboard.PopLsb(ref rightCaptures);
                int from = white ? to - 9 : to + 7;
                moves.Add(new Move(from, to, Piece.Empty, MoveFlags.Capture));
            }

            while (rightPromotions != 0)
            {
                int to = Bitboard.PopLsb(ref rightPromotions);
                int from = white ? to - 9 : to + 7;
                AddPromotions(from, to, white, moves, MoveFlags.Capture);
            }

            // En passant
            if (board.EnPassantSquare != -1)
            {
                ulong epBB = 1UL << board.EnPassantSquare;
                ulong epAttackers;

                if (white)
                {
                    epAttackers = Bitboard.PawnAttacks[1][board.EnPassantSquare] & pawns;
                }
                else
                {
                    epAttackers = Bitboard.PawnAttacks[0][board.EnPassantSquare] & pawns;
                }

                while (epAttackers != 0)
                {
                    int from = Bitboard.PopLsb(ref epAttackers);
                    moves.Add(new Move(from, board.EnPassantSquare, Piece.Empty, MoveFlags.EnPassant | MoveFlags.Capture));
                }
            }
        }

        private static void GeneratePawnCaptures(Board board, List<Move> moves, bool white)
        {
            ulong pawns = white ? board.WP : board.BP;
            ulong enemies = white ? board.BlackPieces : board.WhitePieces;
            ulong prePromotionRank = white ? Bitboard.Rank8 : Bitboard.Rank1;
            ulong empty = ~board.AllPieces;
            int direction = white ? 8 : -8;

            // Captures
            ulong leftCaptures, rightCaptures;
            if (white)
            {
                leftCaptures = Bitboard.ShiftNorthWest(pawns) & enemies;
                rightCaptures = Bitboard.ShiftNorthEast(pawns) & enemies;
            }
            else
            {
                leftCaptures = Bitboard.ShiftSouthEast(pawns) & enemies;
                rightCaptures = Bitboard.ShiftSouthWest(pawns) & enemies;
            }

            // Left captures
            ulong leftPromotions = leftCaptures & prePromotionRank;
            leftCaptures &= ~prePromotionRank;

            while (leftCaptures != 0)
            {
                int to = Bitboard.PopLsb(ref leftCaptures);
                int from = white ? to - 7 : to + 9;
                moves.Add(new Move(from, to, Piece.Empty, MoveFlags.Capture));
            }

            while (leftPromotions != 0)
            {
                int to = Bitboard.PopLsb(ref leftPromotions);
                int from = white ? to - 7 : to + 9;
                AddPromotions(from, to, white, moves, MoveFlags.Capture);
            }

            // Right captures
            ulong rightPromotions = rightCaptures & prePromotionRank;
            rightCaptures &= ~prePromotionRank;

            while (rightCaptures != 0)
            {
                int to = Bitboard.PopLsb(ref rightCaptures);
                int from = white ? to - 9 : to + 7;
                moves.Add(new Move(from, to, Piece.Empty, MoveFlags.Capture));
            }

            while (rightPromotions != 0)
            {
                int to = Bitboard.PopLsb(ref rightPromotions);
                int from = white ? to - 9 : to + 7;
                AddPromotions(from, to, white, moves, MoveFlags.Capture);
            }

            // En passant
            if (board.EnPassantSquare != -1)
            {
                ulong epAttackers;
                if (white)
                    epAttackers = Bitboard.PawnAttacks[1][board.EnPassantSquare] & pawns;
                else
                    epAttackers = Bitboard.PawnAttacks[0][board.EnPassantSquare] & pawns;

                while (epAttackers != 0)
                {
                    int from = Bitboard.PopLsb(ref epAttackers);
                    moves.Add(new Move(from, board.EnPassantSquare, Piece.Empty, MoveFlags.EnPassant | MoveFlags.Capture));
                }
            }

            // Promotion pushes (non-captures but included in quiescence)
            ulong singlePush = white ? Bitboard.ShiftNorth(pawns) : Bitboard.ShiftSouth(pawns);
            singlePush &= empty;
            ulong promotions = singlePush & prePromotionRank;

            while (promotions != 0)
            {
                int to = Bitboard.PopLsb(ref promotions);
                int from = to - direction;
                AddPromotions(from, to, white, moves);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AddPromotions(int from, int to, bool white, List<Move> moves, MoveFlags extraFlags = MoveFlags.None)
        {
            moves.Add(new Move(from, to, white ? Piece.WQ : Piece.BQ, extraFlags));
            moves.Add(new Move(from, to, white ? Piece.WR : Piece.BR, extraFlags));
            moves.Add(new Move(from, to, white ? Piece.WB : Piece.BB, extraFlags));
            moves.Add(new Move(from, to, white ? Piece.WN : Piece.BN, extraFlags));
        }

        #endregion

        #region Knight Moves

        private static void GenerateKnightMoves(Board board, ulong knights, List<Move> moves, ulong friendly)
        {
            while (knights != 0)
            {
                int from = Bitboard.PopLsb(ref knights);
                ulong attacks = Bitboard.KnightAttacks[from] & ~friendly;

                while (attacks != 0)
                {
                    int to = Bitboard.PopLsb(ref attacks);
                    MoveFlags flags = Bitboard.HasBit(board.AllPieces, to) ? MoveFlags.Capture : MoveFlags.None;
                    moves.Add(new Move(from, to, Piece.Empty, flags));
                }
            }
        }

        private static void GenerateKnightCaptures(Board board, ulong knights, List<Move> moves, ulong enemies)
        {
            while (knights != 0)
            {
                int from = Bitboard.PopLsb(ref knights);
                ulong attacks = Bitboard.KnightAttacks[from] & enemies;

                while (attacks != 0)
                {
                    int to = Bitboard.PopLsb(ref attacks);
                    moves.Add(new Move(from, to, Piece.Empty, MoveFlags.Capture));
                }
            }
        }

        #endregion

        #region Bishop Moves

        private static void GenerateBishopMoves(Board board, ulong bishops, List<Move> moves, ulong friendly)
        {
            while (bishops != 0)
            {
                int from = Bitboard.PopLsb(ref bishops);
                ulong attacks = MagicBitboards.GetBishopAttacks(from, board.AllPieces) & ~friendly;

                while (attacks != 0)
                {
                    int to = Bitboard.PopLsb(ref attacks);
                    MoveFlags flags = Bitboard.HasBit(board.AllPieces, to) ? MoveFlags.Capture : MoveFlags.None;
                    moves.Add(new Move(from, to, Piece.Empty, flags));
                }
            }
        }

        private static void GenerateBishopCaptures(Board board, ulong bishops, List<Move> moves, ulong friendly, ulong enemies)
        {
            while (bishops != 0)
            {
                int from = Bitboard.PopLsb(ref bishops);
                ulong attacks = MagicBitboards.GetBishopAttacks(from, board.AllPieces) & enemies;

                while (attacks != 0)
                {
                    int to = Bitboard.PopLsb(ref attacks);
                    moves.Add(new Move(from, to, Piece.Empty, MoveFlags.Capture));
                }
            }
        }

        #endregion

        #region Rook Moves

        private static void GenerateRookMoves(Board board, ulong rooks, List<Move> moves, ulong friendly)
        {
            while (rooks != 0)
            {
                int from = Bitboard.PopLsb(ref rooks);
                ulong attacks = MagicBitboards.GetRookAttacks(from, board.AllPieces) & ~friendly;

                while (attacks != 0)
                {
                    int to = Bitboard.PopLsb(ref attacks);
                    MoveFlags flags = Bitboard.HasBit(board.AllPieces, to) ? MoveFlags.Capture : MoveFlags.None;
                    moves.Add(new Move(from, to, Piece.Empty, flags));
                }
            }
        }

        private static void GenerateRookCaptures(Board board, ulong rooks, List<Move> moves, ulong friendly, ulong enemies)
        {
            while (rooks != 0)
            {
                int from = Bitboard.PopLsb(ref rooks);
                ulong attacks = MagicBitboards.GetRookAttacks(from, board.AllPieces) & enemies;

                while (attacks != 0)
                {
                    int to = Bitboard.PopLsb(ref attacks);
                    moves.Add(new Move(from, to, Piece.Empty, MoveFlags.Capture));
                }
            }
        }

        #endregion

        #region Queen Moves

        private static void GenerateQueenMoves(Board board, ulong queens, List<Move> moves, ulong friendly)
        {
            while (queens != 0)
            {
                int from = Bitboard.PopLsb(ref queens);
                ulong attacks = MagicBitboards.GetQueenAttacks(from, board.AllPieces) & ~friendly;

                while (attacks != 0)
                {
                    int to = Bitboard.PopLsb(ref attacks);
                    MoveFlags flags = Bitboard.HasBit(board.AllPieces, to) ? MoveFlags.Capture : MoveFlags.None;
                    moves.Add(new Move(from, to, Piece.Empty, flags));
                }
            }
        }

        private static void GenerateQueenCaptures(Board board, ulong queens, List<Move> moves, ulong friendly, ulong enemies)
        {
            while (queens != 0)
            {
                int from = Bitboard.PopLsb(ref queens);
                ulong attacks = MagicBitboards.GetQueenAttacks(from, board.AllPieces) & enemies;

                while (attacks != 0)
                {
                    int to = Bitboard.PopLsb(ref attacks);
                    moves.Add(new Move(from, to, Piece.Empty, MoveFlags.Capture));
                }
            }
        }

        #endregion

        #region King Moves

        private static void GenerateKingMoves(Board board, ulong king, List<Move> moves, bool white)
        {
            if (king == 0) return;

            int from = Bitboard.BitScanForward(king);
            ulong friendly = white ? board.WhitePieces : board.BlackPieces;
            ulong attacks = Bitboard.KingAttacks[from] & ~friendly;

            while (attacks != 0)
            {
                int to = Bitboard.PopLsb(ref attacks);

                // Check if destination is attacked by enemy
                if (board.IsSquareAttacked(to, !white))
                    continue;

                MoveFlags flags = Bitboard.HasBit(board.AllPieces, to) ? MoveFlags.Capture : MoveFlags.None;
                moves.Add(new Move(from, to, Piece.Empty, flags));
            }

            // Castling
            GenerateCastling(board, moves, white);
        }

        private static void GenerateKingCaptures(Board board, ulong king, List<Move> moves, bool white, ulong enemies)
        {
            if (king == 0) return;

            int from = Bitboard.BitScanForward(king);
            ulong attacks = Bitboard.KingAttacks[from] & enemies;

            while (attacks != 0)
            {
                int to = Bitboard.PopLsb(ref attacks);

                if (board.IsSquareAttacked(to, !white))
                    continue;

                moves.Add(new Move(from, to, Piece.Empty, MoveFlags.Capture));
            }
        }

        private static void GenerateCastling(Board board, List<Move> moves, bool white)
        {
            if (white)
            {
                // White king side (e1 to g1)
                if ((board.Castling & CastlingRights.WhiteKingSide) != 0)
                {
                    // Squares f1, g1 must be empty
                    if ((board.AllPieces & 0x60UL) == 0)
                    {
                        // e1, f1, g1 must not be attacked
                        if (!board.IsSquareAttacked(4, false) &&
                            !board.IsSquareAttacked(5, false) &&
                            !board.IsSquareAttacked(6, false))
                        {
                            moves.Add(new Move(4, 6, Piece.Empty, MoveFlags.Castling));
                        }
                    }
                }

                // White queen side (e1 to c1)
                if ((board.Castling & CastlingRights.WhiteQueenSide) != 0)
                {
                    // Squares d1, c1, b1 must be empty
                    if ((board.AllPieces & 0x0EUL) == 0)
                    {
                        // e1, d1, c1 must not be attacked
                        if (!board.IsSquareAttacked(4, false) &&
                            !board.IsSquareAttacked(3, false) &&
                            !board.IsSquareAttacked(2, false))
                        {
                            moves.Add(new Move(4, 2, Piece.Empty, MoveFlags.Castling));
                        }
                    }
                }
            }
            else
            {
                // Black king side (e8 to g8)
                if ((board.Castling & CastlingRights.BlackKingSide) != 0)
                {
                    // Squares f8, g8 must be empty
                    if ((board.AllPieces & 0x6000000000000000UL) == 0)
                    {
                        // e8, f8, g8 must not be attacked
                        if (!board.IsSquareAttacked(60, true) &&
                            !board.IsSquareAttacked(61, true) &&
                            !board.IsSquareAttacked(62, true))
                        {
                            moves.Add(new Move(60, 62, Piece.Empty, MoveFlags.Castling));
                        }
                    }
                }

                // Black queen side (e8 to c8)
                if ((board.Castling & CastlingRights.BlackQueenSide) != 0)
                {
                    // Squares d8, c8, b8 must be empty
                    if ((board.AllPieces & 0x0E00000000000000UL) == 0)
                    {
                        // e8, d8, c8 must not be attacked
                        if (!board.IsSquareAttacked(60, true) &&
                            !board.IsSquareAttacked(59, true) &&
                            !board.IsSquareAttacked(58, true))
                        {
                            moves.Add(new Move(60, 58, Piece.Empty, MoveFlags.Castling));
                        }
                    }
                }
            }
        }

        #endregion
    }
}
