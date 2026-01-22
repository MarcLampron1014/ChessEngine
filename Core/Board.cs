using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ChessEngine.Core
{
    public enum Piece
    {
        Empty = 0,
        WP, WN, WB, WR, WQ, WK,
        BP, BN, BB, BR, BQ, BK
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

        public void MakeMove()
        {
            UndoInfo undo = new UndoInfo
            {
                CapturedPiece = Squares[move.To],
                EnPassantSquare = EnPassantSquare,
                CastlingRights = Castling,
                HalfMoveClock = HalfMoveClock
            };

            history.Push(undo);
            Piece movingPiece = Squares[move.From];
            EnPassantSquare = -1;

            // Halfmove clock
            if (movingPiece == Piece.WP || movingPiece == Piece.BP || undo.CapturedPiece != Piece.Empty)
                HalfMoveClock = 0;
            else
                HalfMoveClock++;

            //Move Piece
            Squares[move.To] = movingPiece;
            Squares[move.From] = Piece.Empty;

            // Promotion
            if (move.Promotion != Piece.Empty)
            {
                Squares[move.To] = move.Promotion;
            }

            // TODO: En passant capture
            
            

            //Castling rights
            if (piece == Piece.WK)
                Castling &= ~(CastlingRights.WhiteKingSide | CastlingRights.WhiteQueenSide);

            if (piece == Piece.BK)
                Castling &= ~(CastlingRights.BlackKingSide | CastlingRights.BlackQueenSide);
            
            // White rooks
            if (from == 0)
                Castling &= ~CastlingRights.WhiteQueenSide;
            else if (from == 7)
                Castling &= ~CastlingRights.WhiteKingSide;

            // Black rooks
            else if (from == 56)
                Castling &= ~CastlingRights.BlackQueenSide;
            else if (from == 63)
                Castling &= ~CastlingRights.BlackKingSide;

            //Removed rights when rook is captured
            if (capturedPiece == Piece.WR)
            {
                if (to == 0)
                    Castling &= ~CastlingRights.WhiteQueenSide;
                else if (to == 7)
                    Castling &= ~CastlingRights.WhiteKingSide;
            }
            else if (capturedPiece == Piece.BR)
            {
                if (to == 56)
                    Castling &= ~CastlingRights.BlackQueenSide;
                else if (to == 63)
                    Castling &= ~CastlingRights.BlackKingSide;
            }
            

            //Castling rook move
            if ((move.Flags & MoveFlags.Castling) != 0)
            {
                if (piece == Piece.WK)
                {
                    // White king side
                    if (move.To == 6)
                    {
                        Squares[5] = Piece.WR; // rook f1
                        Squares[7] = Piece.Empty;
                    }
                    // White queen side
                    else if (move.To == 2)
                    {
                        Squares[3] = Piece.WR; // rook d1
                        Squares[0] = Piece.Empty;
                    }
                }
                else if (piece == Piece.BK)
                {
                    // Black king side
                    if (move.To == 62)
                    {
                        Squares[61] = Piece.BR; // rook f8
                        Squares[63] = Piece.Empty;
                    }
                    // Black queen side
                    else if (move.To == 58)
                    {
                        Squares[59] = Piece.BR; // rook d8
                        Squares[56] = Piece.Empty;
                    }
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

            Piece movedPiece = Squares[move.To];

            // Undo promotion
            if (move.Promotion != Piece.Empty)
            {
                movedPiece = WhiteToMove ? Piece.WP : Piece.BP;
            }

            Squares[move.From] = movedPiece;
            Squares[move.To] = undo.CapturedPiece;

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
    }

}