using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

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
        public static bool IsWhite(this Piece p)
        {
            return p >= Piece.WP && p <= Piece.WK;
        }

        public static bool IsBlack(this Piece p)
        {
            return p >= Piece.BP && p <= Piece.BK;
        }

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
        public Piece[] Squares { get; private set; } = new Piece[64];
        public bool WhiteToMove{ get; private set; }
        public int EnPassantSquare { get; private set; } // -1 if none
        public CastlingRights Castling { get; private set; }
        public int HalfMoveClock { get; private set; }
        public int FullMoveNumber { get; private set; }

        private Stack<UndoInfo> history = new Stack<UndoInfo>();

        public Board(){
            Reset();
        }

        public void Reset()
        {
            Array.Fill(Squares, Piece.Empty);

            // White pieces
            Squares[0] = Piece.WR;
            Squares[1] = Piece.WN;
            Squares[2] = Piece.WB;
            Squares[3] = Piece.WQ;
            Squares[4] = Piece.WK;
            Squares[5] = Piece.WB;
            Squares[6] = Piece.WN;
            Squares[7] = Piece.WR;

            for (int i = 8; i < 16; i++)
                Squares[i] = Piece.WP;

            // Black pieces
            Squares[56] = Piece.BR;
            Squares[57] = Piece.BN;
            Squares[58] = Piece.BB;
            Squares[59] = Piece.BQ;
            Squares[60] = Piece.BK;
            Squares[61] = Piece.BB;
            Squares[62] = Piece.BN;
            Squares[63] = Piece.BR;

            for (int i = 48; i < 56; i++)
                Squares[i] = Piece.BP;

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
        
        public void MakeMove(Move move)
        {
            int from = move.From;
            int to = move.To;
            Piece piece = Squares[from];

            // Determine captured piece
            Piece capturedPiece = Squares[to];
            if ((move.Flags & MoveFlags.EnPassant) != 0)
            {
                int capSq = WhiteToMove ? to - 8 : to + 8;
                capturedPiece = Squares[capSq];
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

            // Move piece
            Squares[to] = piece;
            Squares[from] = Piece.Empty;

            // Promotion
            if (move.Promotion != Piece.Empty)
                Squares[to] = move.Promotion;

            // En passant capture
            if ((move.Flags & MoveFlags.EnPassant) != 0)
            {
                int capSq = WhiteToMove ? to - 8 : to + 8;
                Squares[capSq] = Piece.Empty;
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
                    if (to == 6)      { Squares[5] = Piece.WR; Squares[7] = Piece.Empty; }
                    else if (to == 2) { Squares[3] = Piece.WR; Squares[0] = Piece.Empty; }
                }
                else if (piece == Piece.BK)
                {
                    if (to == 62)     { Squares[61] = Piece.BR; Squares[63] = Piece.Empty; }
                    else if (to == 58){ Squares[59] = Piece.BR; Squares[56] = Piece.Empty; }
                }
            }

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

            Piece movedPiece = Squares[to];

            // Undo castling rook move
            if ((move.Flags & MoveFlags.Castling) != 0)
            {
                if (movedPiece == Piece.WK)
                {
                    if (to == 6)      { Squares[7] = Piece.WR; Squares[5] = Piece.Empty; }
                    else if (to == 2) { Squares[0] = Piece.WR; Squares[3] = Piece.Empty; }
                }
                else if (movedPiece == Piece.BK)
                {
                    if (to == 62)     { Squares[63] = Piece.BR; Squares[61] = Piece.Empty; }
                    else if (to == 58){ Squares[56] = Piece.BR; Squares[59] = Piece.Empty; }
                }
            }

            // Undo promotion
            if (move.Promotion != Piece.Empty)
                movedPiece = WhiteToMove ? Piece.WP : Piece.BP;

            Squares[from] = movedPiece;
            Squares[to] = undo.CapturedPiece;

            // Restore en passant capture
            if ((move.Flags & MoveFlags.EnPassant) != 0)
            {
                int capSq = WhiteToMove ? to - 8 : to + 8;
                Squares[to] = Piece.Empty;
                Squares[capSq] = undo.CapturedPiece;
            }

            EnPassantSquare = undo.EnPassantSquare;
            Castling = undo.CastlingRights;
            HalfMoveClock = undo.HalfMoveClock;
        }

        public Piece GetPiece(int square)
        {
            return Squares[square];
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


        public bool IsSquareAttacked(int square, bool byWhite)
        {
            
            int file = square % 8;
            int rank = square / 8;

            // ==========================
            // Pawn attacks
            // ==========================
            if (byWhite)
            {
                if (file > 0 && square >= 9 && Squares[square - 9] == Piece.WP)
                    return true;
                if (file < 7 && square >= 7 && Squares[square - 7] == Piece.WP)
                    return true;
            }
            else
            {
                if (file > 0 && square <= 55 && Squares[square + 7] == Piece.BP)
                    return true;
                if (file < 7 && square <= 54 && Squares[square + 9] == Piece.BP)
                    return true;
            }

            // ==========================
            // Knight attacks
            // ==========================
            int[] knightOffsets = { 15, 17, 10, 6, -15, -17, -10, -6 };

            foreach (int offset in knightOffsets)
            {
                int to = square + offset;
                if (to < 0 || to >= 64)
                    continue;

                int toFile = to % 8;
                int toRank = to / 8;

                if (Math.Abs(toFile - file) + Math.Abs(toRank - rank) != 3)
                    continue;

                Piece p = Squares[to];
                if (byWhite && p == Piece.WN) return true;
                if (!byWhite && p == Piece.BN) return true;
            }

            // ==========================
            // Diagonals
            // ==========================
            int[] bishopDirs = { 7, 9, -7, -9 };

            foreach (int dir in bishopDirs)
            {
                int to = square;

                while (true)
                {
                    int prev = to;
                    to += dir;

                    if (to < 0 || to >= 64)
                        break;

                    if (Math.Abs((to % 8) - (prev % 8)) != 1)
                        break;

                    Piece p = Squares[to];
                    if (p == Piece.Empty)
                        continue;

                    if (byWhite && (p == Piece.WB || p == Piece.WQ))
                        return true;

                    if (!byWhite && (p == Piece.BB || p == Piece.BQ))
                        return true;

                    break;
                }
            }

            // ==========================
            // Sliding Horizontally and Vertically
            // ==========================
            int[] rookDirs = { 8, -8, 1, -1 };

            foreach (int dir in rookDirs)
            {
                int to = square;

                while (true)
                {
                    int prev = to;
                    to += dir;

                    if (to < 0 || to >= 64)
                        break;

                    if ((dir == 1 || dir == -1) &&
                        Math.Abs((to % 8) - (prev % 8)) != 1)
                        break;

                    Piece p = Squares[to];
                    if (p == Piece.Empty)
                        continue;

                    if (byWhite && (p == Piece.WR || p == Piece.WQ))
                        return true;

                    if (!byWhite && (p == Piece.BR || p == Piece.BQ))
                        return true;

                    break;
                }
            }

            // ==========================
            // King attacks
            // ==========================
            int[] kingOffsets = { 8, -8, 1, -1, 9, 7, -9, -7 };

            foreach (int offset in kingOffsets)
            {
                int to = square + offset;
                if (to < 0 || to >= 64)
                    continue;

                int toFile = to % 8;
                int toRank = to / 8;

                if (Math.Abs(toFile - file) > 1 ||
                    Math.Abs(toRank - rank) > 1)
                    continue;

                Piece p = Squares[to];
                if (byWhite && p == Piece.WK) return true;
                if (!byWhite && p == Piece.BK) return true;
            }

            return false;
        }


        public bool IsKingInCheck(bool white)
        {
            int kingSquare = -1;

            for (int i = 0; i < 64; i++)
            {
                if (Squares[i] == (white ? Piece.WK : Piece.BK))
                {
                    kingSquare = i;
                    break;
                }
            }

            if (kingSquare == -1)
                return false; // King not found - shouldn't happen in legal positions

            return IsSquareAttacked(kingSquare, !white);
        }


    }

}