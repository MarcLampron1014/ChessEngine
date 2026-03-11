using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ChessEngine
{
    public static class MoveGenerator
    {
        private const ulong WhiteKingsidePath = 0x60UL;
        private const ulong WhiteQueensidePath = 0x0EUL;
        private const ulong BlackKingsidePath = 0x6000000000000000UL;
        private const ulong BlackQueensidePath = 0x0E00000000000000UL;

        [ThreadStatic]
        private static Move[]? _scratchMoves;

        private static Move[] GetScratchArray()
        {
            _scratchMoves ??= new Move[256];
            return _scratchMoves;
        }

        public static List<Move> GenerateLegalMoves(Board board)
        {
            var scratch = GetScratchArray();
            int count = GenerateLegalMoves(board, scratch);
            var result = new List<Move>(count);
            for (int i = 0; i < count; i++)
            {
                result.Add(scratch[i]);
            }
            return result;
        }

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
                {
                    moves[legalCount++] = move;
                }
            }

            return legalCount;
        }

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
                {
                    moves[legalCount++] = move;
                }
            }

            return legalCount;
        }

        public static int GenerateLegalPassedPawnPushes(Board board, Move[] moves)
        {
            ulong pawns = board.WhiteToMove ? board.WP : board.BP;
            ulong empty = ~board.AllPieces;
            bool white = board.WhiteToMove;
            int direction = white ? 8 : -8;
            ulong prePromotionRank = white ? Bitboard.Rank8 : Bitboard.Rank1;
            int count = 0;

            ulong singlePush = white ? Bitboard.ShiftNorth(pawns) : Bitboard.ShiftSouth(pawns);
            singlePush &= empty;
            singlePush &= ~prePromotionRank;

            ulong candidates = singlePush != 0 ? (white ? singlePush >> 8 : singlePush << 8) : 0;
            if (candidates == 0)
            {
                return 0;
            }

            while (candidates != 0)
            {
                int from = Bitboard.PopLsb(ref candidates);
                int to = from + direction;
                bool isPassed = Evaluator.IsPassedPawn(board, from, white);
                int rank = Bitboard.RankOf(from);
                bool onSeventh = white ? (rank == 6) : (rank == 1);
                if (!isPassed && !onSeventh)
                {
                    continue;
                }

                Move move = new Move(from, to);
                board.MakeMove(move);
                if (!board.IsKingInCheck(white))
                {
                    moves[count++] = move;
                }
                board.UndoMove(move);
            }

            return count;
        }

        public static int GenerateLegalCheckingMoves(Board board, Move[] moves)
        {
            var scratch = GetScratchArray();
            int pseudoCount = GenerateMoves(board, scratch);
            int count = 0;

            bool sideThatMoved = board.WhiteToMove;
            bool opponentSide = !sideThatMoved;

            for (int i = 0; i < pseudoCount; i++)
            {
                Move move = scratch[i];
                if (move.IsCapture || move.IsPromotion)
                {
                    continue;
                }

                board.MakeMove(move);
                bool legal = !board.IsKingInCheck(sideThatMoved);
                bool givesCheck = legal && board.IsKingInCheck(opponentSide);
                board.UndoMove(move);

                if (legal && givesCheck)
                {
                    moves[count++] = move;
                }
            }

            if (count > 1)
            {
                SortCheckingMovesByPieceValue(board, moves, count);
            }

            return count;
        }

        private static void SortCheckingMovesByPieceValue(Board board, Move[] moves, int count)
        {
            for (int i = 0; i < count - 1; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    int valI = GetMovingPieceValue(board, moves[i]);
                    int valJ = GetMovingPieceValue(board, moves[j]);
                    if (valJ < valI)
                    {
                        Move temp = moves[i];
                        moves[i] = moves[j];
                        moves[j] = temp;
                    }
                }
            }
        }

        private static int GetMovingPieceValue(Board board, Move move)
        {
            return Evaluator.GetPieceValue(board.PieceAt(move.From));
        }

        public static int GenerateMoves(Board board, Move[] moves)
        {
            int count = 0;
            bool white = board.WhiteToMove;

            if (white)
            {
                count = GeneratePawnMoves(board, moves, count, white);
                count = GenerateKnightMoves(board, board.WN, moves, count, board.WhitePieces);
                count = GenerateSlidingMoves(board, board.WB, moves, count, board.WhitePieces, true);
                count = GenerateSlidingMoves(board, board.WR, moves, count, board.WhitePieces, false);
                count = GenerateQueenMoves(board, board.WQ, moves, count, board.WhitePieces);
                count = GenerateKingMoves(board, board.WK, moves, count, white);
            }
            else
            {
                count = GeneratePawnMoves(board, moves, count, white);
                count = GenerateKnightMoves(board, board.BN, moves, count, board.BlackPieces);
                count = GenerateSlidingMoves(board, board.BB, moves, count, board.BlackPieces, true);
                count = GenerateSlidingMoves(board, board.BR, moves, count, board.BlackPieces, false);
                count = GenerateQueenMoves(board, board.BQ, moves, count, board.BlackPieces);
                count = GenerateKingMoves(board, board.BK, moves, count, white);
            }

            return count;
        }

        public static int GenerateCaptures(Board board, Move[] moves)
        {
            int count = 0;
            bool white = board.WhiteToMove;
            ulong enemies = white ? board.BlackPieces : board.WhitePieces;

            if (white)
            {
                count = GeneratePawnCaptures(board, moves, count, white);
                count = GenerateKnightCaptures(board, board.WN, moves, count, enemies);
                count = GenerateSlidingCaptures(board, board.WB, moves, count, enemies, true);
                count = GenerateSlidingCaptures(board, board.WR, moves, count, enemies, false);
                count = GenerateQueenCaptures(board, board.WQ, moves, count, enemies);
                count = GenerateKingCaptures(board, board.WK, moves, count, white, enemies);
            }
            else
            {
                count = GeneratePawnCaptures(board, moves, count, white);
                count = GenerateKnightCaptures(board, board.BN, moves, count, enemies);
                count = GenerateSlidingCaptures(board, board.BB, moves, count, enemies, true);
                count = GenerateSlidingCaptures(board, board.BR, moves, count, enemies, false);
                count = GenerateQueenCaptures(board, board.BQ, moves, count, enemies);
                count = GenerateKingCaptures(board, board.BK, moves, count, white, enemies);
            }

            return count;
        }


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

        private static int GenerateSlidingMoves(Board board, ulong pieces, Move[] moves, int count, ulong friendly, bool isBishop)
        {
            while (pieces != 0)
            {
                int from = Bitboard.PopLsb(ref pieces);
                ulong attacks = isBishop
                    ? MagicBitboards.GetBishopAttacks(from, board.AllPieces)
                    : MagicBitboards.GetRookAttacks(from, board.AllPieces);
                attacks &= ~friendly;

                while (attacks != 0)
                {
                    int to = Bitboard.PopLsb(ref attacks);
                    MoveFlags flags = Bitboard.HasBit(board.AllPieces, to) ? MoveFlags.Capture : MoveFlags.None;
                    moves[count++] = new Move(from, to, Piece.Empty, flags);
                }
            }
            return count;
        }

        private static int GenerateSlidingCaptures(Board board, ulong pieces, Move[] moves, int count, ulong enemies, bool isBishop)
        {
            while (pieces != 0)
            {
                int from = Bitboard.PopLsb(ref pieces);
                ulong attacks = isBishop
                    ? MagicBitboards.GetBishopAttacks(from, board.AllPieces)
                    : MagicBitboards.GetRookAttacks(from, board.AllPieces);
                attacks &= enemies;

                while (attacks != 0)
                {
                    int to = Bitboard.PopLsb(ref attacks);
                    moves[count++] = new Move(from, to, Piece.Empty, MoveFlags.Capture);
                }
            }
            return count;
        }

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

        private static int GenerateQueenCaptures(Board board, ulong queens, Move[] moves, int count, ulong enemies)
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


        private static int GenerateKingMoves(Board board, ulong king, Move[] moves, int count, bool white)
        {
            if (king == 0)
            {
                return count;
            }

            int from = Bitboard.BitScanForward(king);
            ulong friendly = white ? board.WhitePieces : board.BlackPieces;
            ulong attacks = Bitboard.KingAttacks[from] & ~friendly;

            while (attacks != 0)
            {
                int to = Bitboard.PopLsb(ref attacks);
                if (board.IsSquareAttacked(to, !white))
                {
                    continue;
                }

                MoveFlags flags = Bitboard.HasBit(board.AllPieces, to) ? MoveFlags.Capture : MoveFlags.None;
                moves[count++] = new Move(from, to, Piece.Empty, flags);
            }

            count = GenerateCastling(board, moves, count, white);
            return count;
        }

        private static int GenerateKingCaptures(Board board, ulong king, Move[] moves, int count, bool white, ulong enemies)
        {
            if (king == 0)
            {
                return count;
            }

            int from = Bitboard.BitScanForward(king);
            ulong attacks = Bitboard.KingAttacks[from] & enemies;

            while (attacks != 0)
            {
                int to = Bitboard.PopLsb(ref attacks);
                if (board.IsSquareAttacked(to, !white))
                {
                    continue;
                }
                moves[count++] = new Move(from, to, Piece.Empty, MoveFlags.Capture);
            }
            return count;
        }

        private static int GenerateCastling(Board board, Move[] moves, int count, bool white)
        {
            if (white)
            {
                if ((board.Castling & CastlingRights.WhiteKingSide) != 0 &&
                    (board.AllPieces & WhiteKingsidePath) == 0 &&
                    !board.IsSquareAttacked(4, false) &&
                    !board.IsSquareAttacked(5, false) &&
                    !board.IsSquareAttacked(6, false))
                {
                    moves[count++] = new Move(4, 6, Piece.Empty, MoveFlags.Castling);
                }

                if ((board.Castling & CastlingRights.WhiteQueenSide) != 0 &&
                    (board.AllPieces & WhiteQueensidePath) == 0 &&
                    !board.IsSquareAttacked(4, false) &&
                    !board.IsSquareAttacked(3, false) &&
                    !board.IsSquareAttacked(2, false))
                {
                    moves[count++] = new Move(4, 2, Piece.Empty, MoveFlags.Castling);
                }
            }
            else
            {
                if ((board.Castling & CastlingRights.BlackKingSide) != 0 &&
                    (board.AllPieces & BlackKingsidePath) == 0 &&
                    !board.IsSquareAttacked(60, true) &&
                    !board.IsSquareAttacked(61, true) &&
                    !board.IsSquareAttacked(62, true))
                {
                    moves[count++] = new Move(60, 62, Piece.Empty, MoveFlags.Castling);
                }

                if ((board.Castling & CastlingRights.BlackQueenSide) != 0 &&
                    (board.AllPieces & BlackQueensidePath) == 0 &&
                    !board.IsSquareAttacked(60, true) &&
                    !board.IsSquareAttacked(59, true) &&
                    !board.IsSquareAttacked(58, true))
                {
                    moves[count++] = new Move(60, 58, Piece.Empty, MoveFlags.Castling);
                }
            }
            return count;
        }
    }
}
