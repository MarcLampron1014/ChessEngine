using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ChessEngine
{
    public static class MoveGenerator
    {
        // Preallocated scratch array for pseudo-legal move generation
        [ThreadStatic]
        private static Move[]? _scratchMoves;

        private static Move[] GetScratchArray()
        {
            _scratchMoves ??= new Move[256];
            return _scratchMoves;
        }

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
        /// Generate legal moves directly into a preallocated array.
        /// Returns the number of moves generated.
        /// </summary>
        public static int GenerateLegalMoves(Board board, Move[] moves)
        {
            var scratch = GetScratchArray();
            int pseudoCount = GenerateMoves(board, scratch);
            int legalCount = 0;

            bool sideThatMoved = board.WhiteToMove;

            for (int i = 0; i < pseudoCount; i++)
            {
                Move move = scratch[i];
                board.MakeMove(move);
                bool inCheck = board.IsKingInCheck(sideThatMoved);
                board.UndoMove(move);

                if (!inCheck)
                    moves[legalCount++] = move;
            }

            return legalCount;
        }

        /// <summary>
        /// Generate legal captures and promotions into a preallocated array.
        /// Returns the number of moves generated.
        /// </summary>
        public static int GenerateLegalCaptures(Board board, Move[] moves)
        {
            var scratch = GetScratchArray();
            int pseudoCount = GenerateCaptures(board, scratch);
            int legalCount = 0;

            bool sideThatMoved = board.WhiteToMove;

            for (int i = 0; i < pseudoCount; i++)
            {
                Move move = scratch[i];
                board.MakeMove(move);
                bool inCheck = board.IsKingInCheck(sideThatMoved);
                board.UndoMove(move);

                if (!inCheck)
                    moves[legalCount++] = move;
            }

            return legalCount;
        }

        /// <summary>
        /// Generate all pseudo-legal moves into an array. Returns move count.
        /// </summary>
        public static int GenerateMoves(Board board, Move[] moves)
        {
            int count = 0;
            bool white = board.WhiteToMove;

            if (white)
            {
                count = GeneratePawnMoves(board, moves, count, white);
                count = GenerateKnightMoves(board, board.WN, moves, count, board.WhitePieces);
                count = GenerateBishopMoves(board, board.WB, moves, count, board.WhitePieces);
                count = GenerateRookMoves(board, board.WR, moves, count, board.WhitePieces);
                count = GenerateQueenMoves(board, board.WQ, moves, count, board.WhitePieces);
                count = GenerateKingMoves(board, board.WK, moves, count, white);
            }
            else
            {
                count = GeneratePawnMoves(board, moves, count, white);
                count = GenerateKnightMoves(board, board.BN, moves, count, board.BlackPieces);
                count = GenerateBishopMoves(board, board.BB, moves, count, board.BlackPieces);
                count = GenerateRookMoves(board, board.BR, moves, count, board.BlackPieces);
                count = GenerateQueenMoves(board, board.BQ, moves, count, board.BlackPieces);
                count = GenerateKingMoves(board, board.BK, moves, count, white);
            }

            return count;
        }

        /// <summary>
        /// Generate captures and promotions into an array. Returns move count.
        /// </summary>
        public static int GenerateCaptures(Board board, Move[] moves)
        {
            int count = 0;
            bool white = board.WhiteToMove;
            ulong enemies = white ? board.BlackPieces : board.WhitePieces;

            if (white)
            {
                count = GeneratePawnCaptures(board, moves, count, white);
                count = GenerateKnightCaptures(board, board.WN, moves, count, enemies);
                count = GenerateBishopCaptures(board, board.WB, moves, count, board.WhitePieces, enemies);
                count = GenerateRookCaptures(board, board.WR, moves, count, board.WhitePieces, enemies);
                count = GenerateQueenCaptures(board, board.WQ, moves, count, board.WhitePieces, enemies);
                count = GenerateKingCaptures(board, board.WK, moves, count, white, enemies);
            }
            else
            {
                count = GeneratePawnCaptures(board, moves, count, white);
                count = GenerateKnightCaptures(board, board.BN, moves, count, enemies);
                count = GenerateBishopCaptures(board, board.BB, moves, count, board.BlackPieces, enemies);
                count = GenerateRookCaptures(board, board.BR, moves, count, board.BlackPieces, enemies);
                count = GenerateQueenCaptures(board, board.BQ, moves, count, board.BlackPieces, enemies);
                count = GenerateKingCaptures(board, board.BK, moves, count, white, enemies);
            }

            return count;
        }

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
            ulong prePromotionRank = white ? Bitboard.Rank8 : Bitboard.Rank1;

            ulong singlePush = white ? Bitboard.ShiftNorth(pawns) : Bitboard.ShiftSouth(pawns);
            singlePush &= empty;

            ulong doublePush = white
                ? Bitboard.ShiftNorth(singlePush & Bitboard.Rank3)
                : Bitboard.ShiftSouth(singlePush & Bitboard.Rank6);
            doublePush &= empty;

            ulong promotions = singlePush & prePromotionRank;
            singlePush &= ~prePromotionRank;

            while (singlePush != 0)
            {
                int to = Bitboard.PopLsb(ref singlePush);
                moves.Add(new Move(to - direction, to));
            }

            while (doublePush != 0)
            {
                int to = Bitboard.PopLsb(ref doublePush);
                moves.Add(new Move(to - direction * 2, to, Piece.Empty, MoveFlags.PawnDoublePush));
            }

            while (promotions != 0)
            {
                int to = Bitboard.PopLsb(ref promotions);
                AddPromotions(to - direction, to, white, moves);
            }

            ulong leftCaptures, rightCaptures;
            if (white)
            {
                leftCaptures = Bitboard.ShiftNorthWest(pawns) & enemies;
                rightCaptures = Bitboard.ShiftNorthEast(pawns) & enemies;
            }
            else
            {
                leftCaptures = Bitboard.ShiftSouthWest(pawns) & enemies;
                rightCaptures = Bitboard.ShiftSouthEast(pawns) & enemies;
            }

            ulong leftPromotions = leftCaptures & prePromotionRank;
            leftCaptures &= ~prePromotionRank;

            while (leftCaptures != 0)
            {
                int to = Bitboard.PopLsb(ref leftCaptures);
                moves.Add(new Move(white ? to - 7 : to + 9, to, Piece.Empty, MoveFlags.Capture));
            }

            while (leftPromotions != 0)
            {
                int to = Bitboard.PopLsb(ref leftPromotions);
                AddPromotions(white ? to - 7 : to + 9, to, white, moves, MoveFlags.Capture);
            }

            ulong rightPromotions = rightCaptures & prePromotionRank;
            rightCaptures &= ~prePromotionRank;

            while (rightCaptures != 0)
            {
                int to = Bitboard.PopLsb(ref rightCaptures);
                moves.Add(new Move(white ? to - 9 : to + 7, to, Piece.Empty, MoveFlags.Capture));
            }

            while (rightPromotions != 0)
            {
                int to = Bitboard.PopLsb(ref rightPromotions);
                AddPromotions(white ? to - 9 : to + 7, to, white, moves, MoveFlags.Capture);
            }

            // En passant
            if (board.EnPassantSquare >= 0)
            {
                int epSquare = board.EnPassantSquare;
                int capSq = white ? epSquare - 8 : epSquare + 8;
                Piece enemyPawn = white ? Piece.BP : Piece.WP;

                if (board.PieceAt(capSq) == enemyPawn)
                {
                    ulong epAttackers = white
                        ? Bitboard.PawnAttacks[1][epSquare] & pawns
                        : Bitboard.PawnAttacks[0][epSquare] & pawns;

                    while (epAttackers != 0)
                    {
                        int from = Bitboard.PopLsb(ref epAttackers);
                        moves.Add(new Move(from, epSquare, Piece.Empty, MoveFlags.EnPassant | MoveFlags.Capture));
                    }
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

            ulong leftCaptures, rightCaptures;
            if (white)
            {
                leftCaptures = Bitboard.ShiftNorthWest(pawns) & enemies;
                rightCaptures = Bitboard.ShiftNorthEast(pawns) & enemies;
            }
            else
            {
                leftCaptures = Bitboard.ShiftSouthWest(pawns) & enemies;
                rightCaptures = Bitboard.ShiftSouthEast(pawns) & enemies;
            }

            ulong leftPromotions = leftCaptures & prePromotionRank;
            leftCaptures &= ~prePromotionRank;

            while (leftCaptures != 0)
            {
                int to = Bitboard.PopLsb(ref leftCaptures);
                moves.Add(new Move(white ? to - 7 : to + 9, to, Piece.Empty, MoveFlags.Capture));
            }

            while (leftPromotions != 0)
            {
                int to = Bitboard.PopLsb(ref leftPromotions);
                AddPromotions(white ? to - 7 : to + 9, to, white, moves, MoveFlags.Capture);
            }

            ulong rightPromotions = rightCaptures & prePromotionRank;
            rightCaptures &= ~prePromotionRank;

            while (rightCaptures != 0)
            {
                int to = Bitboard.PopLsb(ref rightCaptures);
                moves.Add(new Move(white ? to - 9 : to + 7, to, Piece.Empty, MoveFlags.Capture));
            }

            while (rightPromotions != 0)
            {
                int to = Bitboard.PopLsb(ref rightPromotions);
                AddPromotions(white ? to - 9 : to + 7, to, white, moves, MoveFlags.Capture);
            }

            // En passant
            if (board.EnPassantSquare >= 0)
            {
                int epSquare = board.EnPassantSquare;
                int capSq = white ? epSquare - 8 : epSquare + 8;
                Piece enemyPawn = white ? Piece.BP : Piece.WP;

                if (board.PieceAt(capSq) == enemyPawn)
                {
                    ulong epAttackers = white
                        ? Bitboard.PawnAttacks[1][epSquare] & pawns
                        : Bitboard.PawnAttacks[0][epSquare] & pawns;

                    while (epAttackers != 0)
                    {
                        int from = Bitboard.PopLsb(ref epAttackers);
                        moves.Add(new Move(from, epSquare, Piece.Empty, MoveFlags.EnPassant | MoveFlags.Capture));
                    }
                }
            }

            // Promotion pushes (included in quiescence search)
            ulong singlePush = white ? Bitboard.ShiftNorth(pawns) : Bitboard.ShiftSouth(pawns);
            singlePush &= empty;
            ulong promotions = singlePush & prePromotionRank;

            while (promotions != 0)
            {
                int to = Bitboard.PopLsb(ref promotions);
                AddPromotions(to - direction, to, white, moves);
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

        // Array-based pawn move generation
        private static int GeneratePawnMoves(Board board, Move[] moves, int count, bool white)
        {
            ulong pawns = white ? board.WP : board.BP;
            ulong enemies = white ? board.BlackPieces : board.WhitePieces;
            ulong empty = ~board.AllPieces;
            int direction = white ? 8 : -8;
            ulong prePromotionRank = white ? Bitboard.Rank8 : Bitboard.Rank1;

            ulong singlePush = white ? Bitboard.ShiftNorth(pawns) : Bitboard.ShiftSouth(pawns);
            singlePush &= empty;

            ulong doublePush = white
                ? Bitboard.ShiftNorth(singlePush & Bitboard.Rank3)
                : Bitboard.ShiftSouth(singlePush & Bitboard.Rank6);
            doublePush &= empty;

            ulong promotions = singlePush & prePromotionRank;
            singlePush &= ~prePromotionRank;

            while (singlePush != 0)
            {
                int to = Bitboard.PopLsb(ref singlePush);
                moves[count++] = new Move(to - direction, to);
            }

            while (doublePush != 0)
            {
                int to = Bitboard.PopLsb(ref doublePush);
                moves[count++] = new Move(to - direction * 2, to, Piece.Empty, MoveFlags.PawnDoublePush);
            }

            while (promotions != 0)
            {
                int to = Bitboard.PopLsb(ref promotions);
                count = AddPromotions(moves, count, to - direction, to, white);
            }

            ulong leftCaptures, rightCaptures;
            if (white)
            {
                leftCaptures = Bitboard.ShiftNorthWest(pawns) & enemies;
                rightCaptures = Bitboard.ShiftNorthEast(pawns) & enemies;
            }
            else
            {
                leftCaptures = Bitboard.ShiftSouthWest(pawns) & enemies;
                rightCaptures = Bitboard.ShiftSouthEast(pawns) & enemies;
            }

            ulong leftPromotions = leftCaptures & prePromotionRank;
            leftCaptures &= ~prePromotionRank;

            while (leftCaptures != 0)
            {
                int to = Bitboard.PopLsb(ref leftCaptures);
                moves[count++] = new Move(white ? to - 7 : to + 9, to, Piece.Empty, MoveFlags.Capture);
            }

            while (leftPromotions != 0)
            {
                int to = Bitboard.PopLsb(ref leftPromotions);
                count = AddPromotions(moves, count, white ? to - 7 : to + 9, to, white, MoveFlags.Capture);
            }

            ulong rightPromotions = rightCaptures & prePromotionRank;
            rightCaptures &= ~prePromotionRank;

            while (rightCaptures != 0)
            {
                int to = Bitboard.PopLsb(ref rightCaptures);
                moves[count++] = new Move(white ? to - 9 : to + 7, to, Piece.Empty, MoveFlags.Capture);
            }

            while (rightPromotions != 0)
            {
                int to = Bitboard.PopLsb(ref rightPromotions);
                count = AddPromotions(moves, count, white ? to - 9 : to + 7, to, white, MoveFlags.Capture);
            }

            // En passant
            if (board.EnPassantSquare >= 0)
            {
                int epSquare = board.EnPassantSquare;
                int capSq = white ? epSquare - 8 : epSquare + 8;
                Piece enemyPawn = white ? Piece.BP : Piece.WP;

                if (board.PieceAt(capSq) == enemyPawn)
                {
                    ulong epAttackers = white
                        ? Bitboard.PawnAttacks[1][epSquare] & pawns
                        : Bitboard.PawnAttacks[0][epSquare] & pawns;

                    while (epAttackers != 0)
                    {
                        int from = Bitboard.PopLsb(ref epAttackers);
                        moves[count++] = new Move(from, epSquare, Piece.Empty, MoveFlags.EnPassant | MoveFlags.Capture);
                    }
                }
            }

            return count;
        }

        private static int GeneratePawnCaptures(Board board, Move[] moves, int count, bool white)
        {
            ulong pawns = white ? board.WP : board.BP;
            ulong enemies = white ? board.BlackPieces : board.WhitePieces;
            ulong prePromotionRank = white ? Bitboard.Rank8 : Bitboard.Rank1;
            ulong empty = ~board.AllPieces;
            int direction = white ? 8 : -8;

            ulong leftCaptures, rightCaptures;
            if (white)
            {
                leftCaptures = Bitboard.ShiftNorthWest(pawns) & enemies;
                rightCaptures = Bitboard.ShiftNorthEast(pawns) & enemies;
            }
            else
            {
                leftCaptures = Bitboard.ShiftSouthWest(pawns) & enemies;
                rightCaptures = Bitboard.ShiftSouthEast(pawns) & enemies;
            }

            ulong leftPromotions = leftCaptures & prePromotionRank;
            leftCaptures &= ~prePromotionRank;

            while (leftCaptures != 0)
            {
                int to = Bitboard.PopLsb(ref leftCaptures);
                moves[count++] = new Move(white ? to - 7 : to + 9, to, Piece.Empty, MoveFlags.Capture);
            }

            while (leftPromotions != 0)
            {
                int to = Bitboard.PopLsb(ref leftPromotions);
                count = AddPromotions(moves, count, white ? to - 7 : to + 9, to, white, MoveFlags.Capture);
            }

            ulong rightPromotions = rightCaptures & prePromotionRank;
            rightCaptures &= ~prePromotionRank;

            while (rightCaptures != 0)
            {
                int to = Bitboard.PopLsb(ref rightCaptures);
                moves[count++] = new Move(white ? to - 9 : to + 7, to, Piece.Empty, MoveFlags.Capture);
            }

            while (rightPromotions != 0)
            {
                int to = Bitboard.PopLsb(ref rightPromotions);
                count = AddPromotions(moves, count, white ? to - 9 : to + 7, to, white, MoveFlags.Capture);
            }

            // En passant
            if (board.EnPassantSquare >= 0)
            {
                int epSquare = board.EnPassantSquare;
                int capSq = white ? epSquare - 8 : epSquare + 8;
                Piece enemyPawn = white ? Piece.BP : Piece.WP;

                if (board.PieceAt(capSq) == enemyPawn)
                {
                    ulong epAttackers = white
                        ? Bitboard.PawnAttacks[1][epSquare] & pawns
                        : Bitboard.PawnAttacks[0][epSquare] & pawns;

                    while (epAttackers != 0)
                    {
                        int from = Bitboard.PopLsb(ref epAttackers);
                        moves[count++] = new Move(from, epSquare, Piece.Empty, MoveFlags.EnPassant | MoveFlags.Capture);
                    }
                }
            }

            // Promotion pushes
            ulong singlePush = white ? Bitboard.ShiftNorth(pawns) : Bitboard.ShiftSouth(pawns);
            singlePush &= empty;
            ulong promotions = singlePush & prePromotionRank;

            while (promotions != 0)
            {
                int to = Bitboard.PopLsb(ref promotions);
                count = AddPromotions(moves, count, to - direction, to, white);
            }

            return count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int AddPromotions(Move[] moves, int count, int from, int to, bool white, MoveFlags extraFlags = MoveFlags.None)
        {
            moves[count++] = new Move(from, to, white ? Piece.WQ : Piece.BQ, extraFlags);
            moves[count++] = new Move(from, to, white ? Piece.WR : Piece.BR, extraFlags);
            moves[count++] = new Move(from, to, white ? Piece.WB : Piece.BB, extraFlags);
            moves[count++] = new Move(from, to, white ? Piece.WN : Piece.BN, extraFlags);
            return count;
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

        // Array-based knight moves
        private static int GenerateKnightMoves(Board board, ulong knights, Move[] moves, int count, ulong friendly)
        {
            while (knights != 0)
            {
                int from = Bitboard.PopLsb(ref knights);
                ulong attacks = Bitboard.KnightAttacks[from] & ~friendly;

                while (attacks != 0)
                {
                    int to = Bitboard.PopLsb(ref attacks);
                    MoveFlags flags = Bitboard.HasBit(board.AllPieces, to) ? MoveFlags.Capture : MoveFlags.None;
                    moves[count++] = new Move(from, to, Piece.Empty, flags);
                }
            }
            return count;
        }

        private static int GenerateKnightCaptures(Board board, ulong knights, Move[] moves, int count, ulong enemies)
        {
            while (knights != 0)
            {
                int from = Bitboard.PopLsb(ref knights);
                ulong attacks = Bitboard.KnightAttacks[from] & enemies;

                while (attacks != 0)
                {
                    int to = Bitboard.PopLsb(ref attacks);
                    moves[count++] = new Move(from, to, Piece.Empty, MoveFlags.Capture);
                }
            }
            return count;
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

        // Array-based bishop moves
        private static int GenerateBishopMoves(Board board, ulong bishops, Move[] moves, int count, ulong friendly)
        {
            while (bishops != 0)
            {
                int from = Bitboard.PopLsb(ref bishops);
                ulong attacks = MagicBitboards.GetBishopAttacks(from, board.AllPieces) & ~friendly;

                while (attacks != 0)
                {
                    int to = Bitboard.PopLsb(ref attacks);
                    MoveFlags flags = Bitboard.HasBit(board.AllPieces, to) ? MoveFlags.Capture : MoveFlags.None;
                    moves[count++] = new Move(from, to, Piece.Empty, flags);
                }
            }
            return count;
        }

        private static int GenerateBishopCaptures(Board board, ulong bishops, Move[] moves, int count, ulong friendly, ulong enemies)
        {
            while (bishops != 0)
            {
                int from = Bitboard.PopLsb(ref bishops);
                ulong attacks = MagicBitboards.GetBishopAttacks(from, board.AllPieces) & enemies;

                while (attacks != 0)
                {
                    int to = Bitboard.PopLsb(ref attacks);
                    moves[count++] = new Move(from, to, Piece.Empty, MoveFlags.Capture);
                }
            }
            return count;
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

        // Array-based rook moves
        private static int GenerateRookMoves(Board board, ulong rooks, Move[] moves, int count, ulong friendly)
        {
            while (rooks != 0)
            {
                int from = Bitboard.PopLsb(ref rooks);
                ulong attacks = MagicBitboards.GetRookAttacks(from, board.AllPieces) & ~friendly;

                while (attacks != 0)
                {
                    int to = Bitboard.PopLsb(ref attacks);
                    MoveFlags flags = Bitboard.HasBit(board.AllPieces, to) ? MoveFlags.Capture : MoveFlags.None;
                    moves[count++] = new Move(from, to, Piece.Empty, flags);
                }
            }
            return count;
        }

        private static int GenerateRookCaptures(Board board, ulong rooks, Move[] moves, int count, ulong friendly, ulong enemies)
        {
            while (rooks != 0)
            {
                int from = Bitboard.PopLsb(ref rooks);
                ulong attacks = MagicBitboards.GetRookAttacks(from, board.AllPieces) & enemies;

                while (attacks != 0)
                {
                    int to = Bitboard.PopLsb(ref attacks);
                    moves[count++] = new Move(from, to, Piece.Empty, MoveFlags.Capture);
                }
            }
            return count;
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

        // Array-based queen moves
        private static int GenerateQueenMoves(Board board, ulong queens, Move[] moves, int count, ulong friendly)
        {
            while (queens != 0)
            {
                int from = Bitboard.PopLsb(ref queens);
                ulong attacks = MagicBitboards.GetQueenAttacks(from, board.AllPieces) & ~friendly;

                while (attacks != 0)
                {
                    int to = Bitboard.PopLsb(ref attacks);
                    MoveFlags flags = Bitboard.HasBit(board.AllPieces, to) ? MoveFlags.Capture : MoveFlags.None;
                    moves[count++] = new Move(from, to, Piece.Empty, flags);
                }
            }
            return count;
        }

        private static int GenerateQueenCaptures(Board board, ulong queens, Move[] moves, int count, ulong friendly, ulong enemies)
        {
            while (queens != 0)
            {
                int from = Bitboard.PopLsb(ref queens);
                ulong attacks = MagicBitboards.GetQueenAttacks(from, board.AllPieces) & enemies;

                while (attacks != 0)
                {
                    int to = Bitboard.PopLsb(ref attacks);
                    moves[count++] = new Move(from, to, Piece.Empty, MoveFlags.Capture);
                }
            }
            return count;
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
                if (board.IsSquareAttacked(to, !white)) continue;

                MoveFlags flags = Bitboard.HasBit(board.AllPieces, to) ? MoveFlags.Capture : MoveFlags.None;
                moves.Add(new Move(from, to, Piece.Empty, flags));
            }

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
                if (board.IsSquareAttacked(to, !white)) continue;
                moves.Add(new Move(from, to, Piece.Empty, MoveFlags.Capture));
            }
        }

        // Array-based king moves
        private static int GenerateKingMoves(Board board, ulong king, Move[] moves, int count, bool white)
        {
            if (king == 0) return count;

            int from = Bitboard.BitScanForward(king);
            ulong friendly = white ? board.WhitePieces : board.BlackPieces;
            ulong attacks = Bitboard.KingAttacks[from] & ~friendly;

            while (attacks != 0)
            {
                int to = Bitboard.PopLsb(ref attacks);
                if (board.IsSquareAttacked(to, !white)) continue;

                MoveFlags flags = Bitboard.HasBit(board.AllPieces, to) ? MoveFlags.Capture : MoveFlags.None;
                moves[count++] = new Move(from, to, Piece.Empty, flags);
            }

            count = GenerateCastling(board, moves, count, white);
            return count;
        }

        private static int GenerateKingCaptures(Board board, ulong king, Move[] moves, int count, bool white, ulong enemies)
        {
            if (king == 0) return count;

            int from = Bitboard.BitScanForward(king);
            ulong attacks = Bitboard.KingAttacks[from] & enemies;

            while (attacks != 0)
            {
                int to = Bitboard.PopLsb(ref attacks);
                if (board.IsSquareAttacked(to, !white)) continue;
                moves[count++] = new Move(from, to, Piece.Empty, MoveFlags.Capture);
            }
            return count;
        }

        private static int GenerateCastling(Board board, Move[] moves, int count, bool white)
        {
            if (white)
            {
                // Kingside: e1 to g1
                if ((board.Castling & CastlingRights.WhiteKingSide) != 0 &&
                    (board.AllPieces & 0x60UL) == 0 &&
                    !board.IsSquareAttacked(4, false) &&
                    !board.IsSquareAttacked(5, false) &&
                    !board.IsSquareAttacked(6, false))
                {
                    moves[count++] = new Move(4, 6, Piece.Empty, MoveFlags.Castling);
                }

                // Queenside: e1 to c1
                if ((board.Castling & CastlingRights.WhiteQueenSide) != 0 &&
                    (board.AllPieces & 0x0EUL) == 0 &&
                    !board.IsSquareAttacked(4, false) &&
                    !board.IsSquareAttacked(3, false) &&
                    !board.IsSquareAttacked(2, false))
                {
                    moves[count++] = new Move(4, 2, Piece.Empty, MoveFlags.Castling);
                }
            }
            else
            {
                // Kingside: e8 to g8
                if ((board.Castling & CastlingRights.BlackKingSide) != 0 &&
                    (board.AllPieces & 0x6000000000000000UL) == 0 &&
                    !board.IsSquareAttacked(60, true) &&
                    !board.IsSquareAttacked(61, true) &&
                    !board.IsSquareAttacked(62, true))
                {
                    moves[count++] = new Move(60, 62, Piece.Empty, MoveFlags.Castling);
                }

                // Queenside: e8 to c8
                if ((board.Castling & CastlingRights.BlackQueenSide) != 0 &&
                    (board.AllPieces & 0x0E00000000000000UL) == 0 &&
                    !board.IsSquareAttacked(60, true) &&
                    !board.IsSquareAttacked(59, true) &&
                    !board.IsSquareAttacked(58, true))
                {
                    moves[count++] = new Move(60, 58, Piece.Empty, MoveFlags.Castling);
                }
            }
            return count;
        }

        private static void GenerateCastling(Board board, List<Move> moves, bool white)
        {
            if (white)
            {
                // Kingside: e1 to g1
                if ((board.Castling & CastlingRights.WhiteKingSide) != 0 &&
                    (board.AllPieces & 0x60UL) == 0 &&
                    !board.IsSquareAttacked(4, false) &&
                    !board.IsSquareAttacked(5, false) &&
                    !board.IsSquareAttacked(6, false))
                {
                    moves.Add(new Move(4, 6, Piece.Empty, MoveFlags.Castling));
                }

                // Queenside: e1 to c1
                if ((board.Castling & CastlingRights.WhiteQueenSide) != 0 &&
                    (board.AllPieces & 0x0EUL) == 0 &&
                    !board.IsSquareAttacked(4, false) &&
                    !board.IsSquareAttacked(3, false) &&
                    !board.IsSquareAttacked(2, false))
                {
                    moves.Add(new Move(4, 2, Piece.Empty, MoveFlags.Castling));
                }
            }
            else
            {
                // Kingside: e8 to g8
                if ((board.Castling & CastlingRights.BlackKingSide) != 0 &&
                    (board.AllPieces & 0x6000000000000000UL) == 0 &&
                    !board.IsSquareAttacked(60, true) &&
                    !board.IsSquareAttacked(61, true) &&
                    !board.IsSquareAttacked(62, true))
                {
                    moves.Add(new Move(60, 62, Piece.Empty, MoveFlags.Castling));
                }

                // Queenside: e8 to c8
                if ((board.Castling & CastlingRights.BlackQueenSide) != 0 &&
                    (board.AllPieces & 0x0E00000000000000UL) == 0 &&
                    !board.IsSquareAttacked(60, true) &&
                    !board.IsSquareAttacked(59, true) &&
                    !board.IsSquareAttacked(58, true))
                {
                    moves.Add(new Move(60, 58, Piece.Empty, MoveFlags.Castling));
                }
            }
        }

        #endregion
    }
}
