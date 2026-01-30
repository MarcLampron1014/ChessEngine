using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ChessEngine
{
    public enum Piece
    {
        Empty = 0,
        WP, WN, WB, WR, WQ, WK,
        BP, BN, BB, BR, BQ, BK
    }

    public static class PieceExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsWhite(this Piece p)
        {
            return p >= Piece.WP && p <= Piece.WK;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsBlack(this Piece p)
        {
            return p >= Piece.BP && p <= Piece.BK;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsEmpty(this Piece p)
        {
            return p == Piece.Empty;
        }
    }
    
    [Flags]
    public enum CastlingRights
    {
        None = 0,
        WhiteKingSide = 1,
        WhiteQueenSide = 2,
        BlackKingSide = 4,
        BlackQueenSide = 8,
    }

    public struct UndoInfo
    {
        public Piece CapturedPiece;
        public int EnPassantSquare;
        public CastlingRights CastlingRights;
        public int HalfMoveClock;
    }

    public class Board
    {
        // Piece bitboards
        public ulong WP, WN, WB, WR, WQ, WK;
        public ulong BP, BN, BB, BR, BQ, BK;

        // Occupancy bitboards
        public ulong WhitePieces;
        public ulong BlackPieces;
        public ulong AllPieces;

        // Game state
        public bool WhiteToMove { get; private set; }
        public int EnPassantSquare { get; private set; } // -1 if none
        public CastlingRights Castling { get; private set; }
        public int HalfMoveClock { get; private set; }
        public int FullMoveNumber { get; private set; }

        private Stack<UndoInfo> history = new Stack<UndoInfo>();

        public Board()
        {
            Reset();
        }

        public void Reset()
        {
            // Clear all bitboards
            WP = WN = WB = WR = WQ = WK = 0;
            BP = BN = BB = BR = BQ = BK = 0;

            // White pieces
            WR = Bitboard.SquareBB[0] | Bitboard.SquareBB[7];
            WN = Bitboard.SquareBB[1] | Bitboard.SquareBB[6];
            WB = Bitboard.SquareBB[2] | Bitboard.SquareBB[5];
            WQ = Bitboard.SquareBB[3];
            WK = Bitboard.SquareBB[4];
            WP = Bitboard.Rank2; // Pawns on rank 2

            // Black pieces
            BR = Bitboard.SquareBB[56] | Bitboard.SquareBB[63];
            BN = Bitboard.SquareBB[57] | Bitboard.SquareBB[62];
            BB = Bitboard.SquareBB[58] | Bitboard.SquareBB[61];
            BQ = Bitboard.SquareBB[59];
            BK = Bitboard.SquareBB[60];
            BP = Bitboard.Rank7; // Pawns on rank 7

            UpdateOccupancy();

            WhiteToMove = true;
            EnPassantSquare = -1;
            Castling = CastlingRights.WhiteKingSide |
                       CastlingRights.WhiteQueenSide |
                       CastlingRights.BlackKingSide |
                       CastlingRights.BlackQueenSide;
            HalfMoveClock = 0;
            FullMoveNumber = 1;

            history.Clear();
        }

        /// <summary>
        /// Update occupancy bitboards from piece bitboards.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateOccupancy()
        {
            WhitePieces = WP | WN | WB | WR | WQ | WK;
            BlackPieces = BP | BN | BB | BR | BQ | BK;
            AllPieces = WhitePieces | BlackPieces;
        }

        /// <summary>
        /// Get the piece at a given square.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Piece PieceAt(int sq)
        {
            ulong mask = 1UL << sq;

            if ((AllPieces & mask) == 0) return Piece.Empty;

            if ((WP & mask) != 0) return Piece.WP;
            if ((WN & mask) != 0) return Piece.WN;
            if ((WB & mask) != 0) return Piece.WB;
            if ((WR & mask) != 0) return Piece.WR;
            if ((WQ & mask) != 0) return Piece.WQ;
            if ((WK & mask) != 0) return Piece.WK;

            if ((BP & mask) != 0) return Piece.BP;
            if ((BN & mask) != 0) return Piece.BN;
            if ((BB & mask) != 0) return Piece.BB;
            if ((BR & mask) != 0) return Piece.BR;
            if ((BQ & mask) != 0) return Piece.BQ;
            if ((BK & mask) != 0) return Piece.BK;

            return Piece.Empty;
        }

        /// <summary>
        /// Set a piece at a given square (updates relevant bitboard).
        /// </summary>
        public void SetPiece(int sq, Piece p)
        {
            ClearPiece(sq); // Remove any existing piece first

            if (p == Piece.Empty) return;

            ulong mask = 1UL << sq;

            switch (p)
            {
                case Piece.WP: WP |= mask; break;
                case Piece.WN: WN |= mask; break;
                case Piece.WB: WB |= mask; break;
                case Piece.WR: WR |= mask; break;
                case Piece.WQ: WQ |= mask; break;
                case Piece.WK: WK |= mask; break;
                case Piece.BP: BP |= mask; break;
                case Piece.BN: BN |= mask; break;
                case Piece.BB: BB |= mask; break;
                case Piece.BR: BR |= mask; break;
                case Piece.BQ: BQ |= mask; break;
                case Piece.BK: BK |= mask; break;
            }

            UpdateOccupancy();
        }

        /// <summary>
        /// Clear any piece from a given square.
        /// </summary>
        public void ClearPiece(int sq)
        {
            ulong mask = ~(1UL << sq);

            WP &= mask; WN &= mask; WB &= mask;
            WR &= mask; WQ &= mask; WK &= mask;
            BP &= mask; BN &= mask; BB &= mask;
            BR &= mask; BQ &= mask; BK &= mask;

            UpdateOccupancy();
        }

        /// <summary>
        /// Move a piece from one square to another (internal helper).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void MovePiece(int from, int to, Piece p)
        {
            ulong fromTo = (1UL << from) | (1UL << to);

            switch (p)
            {
                case Piece.WP: WP ^= fromTo; break;
                case Piece.WN: WN ^= fromTo; break;
                case Piece.WB: WB ^= fromTo; break;
                case Piece.WR: WR ^= fromTo; break;
                case Piece.WQ: WQ ^= fromTo; break;
                case Piece.WK: WK ^= fromTo; break;
                case Piece.BP: BP ^= fromTo; break;
                case Piece.BN: BN ^= fromTo; break;
                case Piece.BB: BB ^= fromTo; break;
                case Piece.BR: BR ^= fromTo; break;
                case Piece.BQ: BQ ^= fromTo; break;
                case Piece.BK: BK ^= fromTo; break;
            }
        }

        /// <summary>
        /// Remove a piece from a square (internal helper for captures).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemovePiece(int sq, Piece p)
        {
            ulong mask = ~(1UL << sq);

            switch (p)
            {
                case Piece.WP: WP &= mask; break;
                case Piece.WN: WN &= mask; break;
                case Piece.WB: WB &= mask; break;
                case Piece.WR: WR &= mask; break;
                case Piece.WQ: WQ &= mask; break;
                case Piece.WK: WK &= mask; break;
                case Piece.BP: BP &= mask; break;
                case Piece.BN: BN &= mask; break;
                case Piece.BB: BB &= mask; break;
                case Piece.BR: BR &= mask; break;
                case Piece.BQ: BQ &= mask; break;
                case Piece.BK: BK &= mask; break;
            }
        }

        /// <summary>
        /// Add a piece to a square (internal helper).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddPiece(int sq, Piece p)
        {
            ulong mask = 1UL << sq;

            switch (p)
            {
                case Piece.WP: WP |= mask; break;
                case Piece.WN: WN |= mask; break;
                case Piece.WB: WB |= mask; break;
                case Piece.WR: WR |= mask; break;
                case Piece.WQ: WQ |= mask; break;
                case Piece.WK: WK |= mask; break;
                case Piece.BP: BP |= mask; break;
                case Piece.BN: BN |= mask; break;
                case Piece.BB: BB |= mask; break;
                case Piece.BR: BR |= mask; break;
                case Piece.BQ: BQ |= mask; break;
                case Piece.BK: BK |= mask; break;
            }
        }

        public void LoadPosition(
            bool whiteToMove,
            CastlingRights castling,
            int enPassantSquare,
            int halfMoveClock,
            int fullMoveNumber)
        {
            // Piece bitboards should already be set via SetPiece calls
            UpdateOccupancy();

            WhiteToMove = whiteToMove;
            Castling = castling;
            EnPassantSquare = enPassantSquare;
            HalfMoveClock = halfMoveClock;
            FullMoveNumber = fullMoveNumber;

            history.Clear();
        }
        
        public void MakeMove(Move move)
        {
            int from = move.From;
            int to = move.To;
            Piece piece = PieceAt(from);

            // Determine captured piece
            Piece capturedPiece = PieceAt(to);
            if ((move.Flags & MoveFlags.EnPassant) != 0)
            {
                // For en passant, captured pawn is one rank behind the destination
                int capSq = piece == Piece.WP ? to - 8 : to + 8;
                capturedPiece = PieceAt(capSq);
            }

            UndoInfo undo = new UndoInfo
            {
                CapturedPiece = capturedPiece,
                EnPassantSquare = EnPassantSquare,
                CastlingRights = Castling,
                HalfMoveClock = HalfMoveClock
            };

            history.Push(undo);

            // Reset EP
            EnPassantSquare = -1;

            // Halfmove clock
            if (piece == Piece.WP || piece == Piece.BP || capturedPiece != Piece.Empty)
                HalfMoveClock = 0;
            else
                HalfMoveClock++;

            // Handle capture (remove captured piece)
            if (capturedPiece != Piece.Empty && (move.Flags & MoveFlags.EnPassant) == 0)
            {
                RemovePiece(to, capturedPiece);
            }

            // Move piece
            MovePiece(from, to, piece);

            // Promotion - remove pawn, add promoted piece
            if (move.Promotion != Piece.Empty)
            {
                RemovePiece(to, piece);
                AddPiece(to, move.Promotion);
            }

            // En passant capture
            if ((move.Flags & MoveFlags.EnPassant) != 0)
            {
                int capSq = piece == Piece.WP ? to - 8 : to + 8;
                RemovePiece(capSq, capturedPiece);
            }

            // Set en passant square
            if (piece == Piece.WP && to - from == 16)
                EnPassantSquare = from + 8;
            else if (piece == Piece.BP && from - to == 16)
                EnPassantSquare = from - 8;

            // Remove castling rights: king move
            if (piece == Piece.WK)
                Castling &= ~(CastlingRights.WhiteKingSide | CastlingRights.WhiteQueenSide);
            else if (piece == Piece.BK)
                Castling &= ~(CastlingRights.BlackKingSide | CastlingRights.BlackQueenSide);

            // Remove castling rights: rook move
            if (from == 0) Castling &= ~CastlingRights.WhiteQueenSide;
            else if (from == 7) Castling &= ~CastlingRights.WhiteKingSide;
            else if (from == 56) Castling &= ~CastlingRights.BlackQueenSide;
            else if (from == 63) Castling &= ~CastlingRights.BlackKingSide;

            // Remove castling rights: rook captured
            if (capturedPiece == Piece.WR)
            {
                if (to == 0) Castling &= ~CastlingRights.WhiteQueenSide;
                else if (to == 7) Castling &= ~CastlingRights.WhiteKingSide;
            }
            else if (capturedPiece == Piece.BR)
            {
                if (to == 56) Castling &= ~CastlingRights.BlackQueenSide;
                else if (to == 63) Castling &= ~CastlingRights.BlackKingSide;
            }

            // Castling rook move
            if ((move.Flags & MoveFlags.Castling) != 0)
            {
                if (piece == Piece.WK)
                {
                    if (to == 6) { MovePiece(7, 5, Piece.WR); }      // Kingside
                    else if (to == 2) { MovePiece(0, 3, Piece.WR); } // Queenside
                }
                else if (piece == Piece.BK)
                {
                    if (to == 62) { MovePiece(63, 61, Piece.BR); }     // Kingside
                    else if (to == 58) { MovePiece(56, 59, Piece.BR); } // Queenside
                }
            }

            UpdateOccupancy();

            WhiteToMove = !WhiteToMove;
            if (WhiteToMove)
                FullMoveNumber++;
        }

        public void UndoMove(Move move)
        {
            UndoInfo undo = history.Pop();

            WhiteToMove = !WhiteToMove;
            if (!WhiteToMove)
                FullMoveNumber--;

            int from = move.From;
            int to = move.To;

            Piece movedPiece = PieceAt(to);

            // Undo castling rook move
            if ((move.Flags & MoveFlags.Castling) != 0)
            {
                if (movedPiece == Piece.WK)
                {
                    if (to == 6) { MovePiece(5, 7, Piece.WR); }      // Kingside
                    else if (to == 2) { MovePiece(3, 0, Piece.WR); } // Queenside
                }
                else if (movedPiece == Piece.BK)
                {
                    if (to == 62) { MovePiece(61, 63, Piece.BR); }     // Kingside
                    else if (to == 58) { MovePiece(59, 56, Piece.BR); } // Queenside
                }
            }

            // Undo promotion
            if (move.Promotion != Piece.Empty)
            {
                RemovePiece(to, movedPiece);
                movedPiece = WhiteToMove ? Piece.WP : Piece.BP;
                AddPiece(to, movedPiece);
            }

            // Move piece back
            MovePiece(to, from, movedPiece);

            // Restore captured piece
            if (undo.CapturedPiece != Piece.Empty)
            {
                if ((move.Flags & MoveFlags.EnPassant) != 0)
                {
                    int capSq = WhiteToMove ? to - 8 : to + 8;
                    AddPiece(capSq, undo.CapturedPiece);
                }
                else
                {
                    AddPiece(to, undo.CapturedPiece);
                }
            }

            UpdateOccupancy();

            EnPassantSquare = undo.EnPassantSquare;
            Castling = undo.CastlingRights;
            HalfMoveClock = undo.HalfMoveClock;
        }

        public Piece GetPiece(int square)
        {
            return PieceAt(square);
        }

        public static bool IsWhite(Piece p)
        {
            return p >= Piece.WP && p <= Piece.WK;
        }

        public static bool IsBlack(Piece p)
        {
            return p >= Piece.BP && p <= Piece.BK;
        }

        public bool HasCastlingRight(CastlingRights right)
        {
            return (Castling & right) != 0;
        }

        /// <summary>
        /// Get bitboard of all attackers to a given square.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong AttackersTo(int sq, ulong occupied)
        {
            return (Bitboard.PawnAttacks[1][sq] & WP)    // White pawn attacks (looking from black's perspective)
                 | (Bitboard.PawnAttacks[0][sq] & BP)    // Black pawn attacks (looking from white's perspective)
                 | (Bitboard.KnightAttacks[sq] & (WN | BN))
                 | (MagicBitboards.GetRookAttacks(sq, occupied) & (WR | BR | WQ | BQ))
                 | (MagicBitboards.GetBishopAttacks(sq, occupied) & (WB | BB | WQ | BQ))
                 | (Bitboard.KingAttacks[sq] & (WK | BK));
        }

        /// <summary>
        /// Check if a square is attacked by a given side.
        /// </summary>
        public bool IsSquareAttacked(int square, bool byWhite)
        {
            if (byWhite)
            {
                // Check if white attacks this square
                if ((Bitboard.PawnAttacks[1][square] & WP) != 0) return true;
                if ((Bitboard.KnightAttacks[square] & WN) != 0) return true;
                if ((Bitboard.KingAttacks[square] & WK) != 0) return true;
                if ((MagicBitboards.GetBishopAttacks(square, AllPieces) & (WB | WQ)) != 0) return true;
                if ((MagicBitboards.GetRookAttacks(square, AllPieces) & (WR | WQ)) != 0) return true;
            }
            else
            {
                // Check if black attacks this square
                if ((Bitboard.PawnAttacks[0][square] & BP) != 0) return true;
                if ((Bitboard.KnightAttacks[square] & BN) != 0) return true;
                if ((Bitboard.KingAttacks[square] & BK) != 0) return true;
                if ((MagicBitboards.GetBishopAttacks(square, AllPieces) & (BB | BQ)) != 0) return true;
                if ((MagicBitboards.GetRookAttacks(square, AllPieces) & (BR | BQ)) != 0) return true;
            }

            return false;
        }

        /// <summary>
        /// Check if the king of a given color is in check.
        /// </summary>
        public bool IsKingInCheck(bool white)
        {
            ulong kingBB = white ? WK : BK;
            int kingSquare = Bitboard.BitScanForward(kingBB);
            return IsSquareAttacked(kingSquare, !white);
        }

        /// <summary>
        /// Get the king square for a given color.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int KingSquare(bool white)
        {
            return Bitboard.BitScanForward(white ? WK : BK);
        }
    }
}
