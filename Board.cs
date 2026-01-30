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

    [Flags]
    public enum CastlingRights
    {
        None = 0,
        WhiteKingSide = 1,
        WhiteQueenSide = 2,
        BlackKingSide = 4,
        BlackQueenSide = 8,
    }

    /// <summary>
    /// Zobrist hashing keys for unique position identification.
    /// </summary>
    public static class Zobrist
    {
        public static readonly ulong[,] PieceKeys = new ulong[13, 64];
        public static readonly ulong[] CastlingKeys = new ulong[16];
        public static readonly ulong[] EnPassantKeys = new ulong[8];
        public static readonly ulong SideToMoveKey;

        static Zobrist()
        {
            var rng = new Random(1234);

            for (int piece = 1; piece <= 12; piece++)
                for (int sq = 0; sq < 64; sq++)
                    PieceKeys[piece, sq] = NextUInt64(rng);

            for (int i = 0; i < 16; i++)
                CastlingKeys[i] = NextUInt64(rng);

            for (int file = 0; file < 8; file++)
                EnPassantKeys[file] = NextUInt64(rng);

            SideToMoveKey = NextUInt64(rng);
        }

        private static ulong NextUInt64(Random rng)
        {
            byte[] buffer = new byte[8];
            rng.NextBytes(buffer);
            return BitConverter.ToUInt64(buffer, 0);
        }
    }

    public static class PieceExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsWhite(this Piece p) => p >= Piece.WP && p <= Piece.WK;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsBlack(this Piece p) => p >= Piece.BP && p <= Piece.BK;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsEmpty(this Piece p) => p == Piece.Empty;
    }

    public struct UndoInfo
    {
        public Piece CapturedPiece;
        public int EnPassantSquare;
        public CastlingRights CastlingRights;
        public int HalfMoveClock;
        public ulong ZobristHash;
    }

    public class Board
    {
        public ulong WP, WN, WB, WR, WQ, WK;
        public ulong BP, BN, BB, BR, BQ, BK;

        public ulong WhitePieces;
        public ulong BlackPieces;
        public ulong AllPieces;

        public bool WhiteToMove { get; private set; }
        public int EnPassantSquare { get; private set; }
        public CastlingRights Castling { get; private set; }
        public int HalfMoveClock { get; private set; }
        public int FullMoveNumber { get; private set; }
        public ulong ZobristHash { get; private set; }

        private readonly Stack<UndoInfo> _history = new Stack<UndoInfo>();

        public Board()
        {
            Reset();
        }

        public void Reset()
        {
            WP = WN = WB = WR = WQ = WK = 0;
            BP = BN = BB = BR = BQ = BK = 0;

            WR = Bitboard.SquareBB[0] | Bitboard.SquareBB[7];
            WN = Bitboard.SquareBB[1] | Bitboard.SquareBB[6];
            WB = Bitboard.SquareBB[2] | Bitboard.SquareBB[5];
            WQ = Bitboard.SquareBB[3];
            WK = Bitboard.SquareBB[4];
            WP = Bitboard.Rank2;

            BR = Bitboard.SquareBB[56] | Bitboard.SquareBB[63];
            BN = Bitboard.SquareBB[57] | Bitboard.SquareBB[62];
            BB = Bitboard.SquareBB[58] | Bitboard.SquareBB[61];
            BQ = Bitboard.SquareBB[59];
            BK = Bitboard.SquareBB[60];
            BP = Bitboard.Rank7;

            UpdateOccupancy();

            WhiteToMove = true;
            EnPassantSquare = -1;
            Castling = CastlingRights.WhiteKingSide | CastlingRights.WhiteQueenSide |
                       CastlingRights.BlackKingSide | CastlingRights.BlackQueenSide;
            HalfMoveClock = 0;
            FullMoveNumber = 1;

            _history.Clear();
            ComputeHash();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateOccupancy()
        {
            WhitePieces = WP | WN | WB | WR | WQ | WK;
            BlackPieces = BP | BN | BB | BR | BQ | BK;
            AllPieces = WhitePieces | BlackPieces;
        }

        public void ComputeHash()
        {
            ulong hash = 0;

            for (int sq = 0; sq < 64; sq++)
            {
                Piece p = PieceAt(sq);
                if (p != Piece.Empty)
                    hash ^= Zobrist.PieceKeys[(int)p, sq];
            }

            hash ^= Zobrist.CastlingKeys[(int)Castling];

            if (EnPassantSquare >= 0)
                hash ^= Zobrist.EnPassantKeys[EnPassantSquare % 8];

            if (!WhiteToMove)
                hash ^= Zobrist.SideToMoveKey;

            ZobristHash = hash;
        }

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

        public void SetPiece(int sq, Piece p)
        {
            ClearPiece(sq);
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

        public void ClearPiece(int sq)
        {
            ulong mask = ~(1UL << sq);

            WP &= mask; WN &= mask; WB &= mask;
            WR &= mask; WQ &= mask; WK &= mask;
            BP &= mask; BN &= mask; BB &= mask;
            BR &= mask; BQ &= mask; BK &= mask;

            UpdateOccupancy();
        }

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

        public void LoadPosition(bool whiteToMove, CastlingRights castling, int enPassantSquare,
                                 int halfMoveClock, int fullMoveNumber)
        {
            UpdateOccupancy();
            WhiteToMove = whiteToMove;
            Castling = castling;
            EnPassantSquare = enPassantSquare;
            HalfMoveClock = halfMoveClock;
            FullMoveNumber = fullMoveNumber;
            _history.Clear();
            ComputeHash();
        }
        
        public void MakeMove(Move move)
        {
            int from = move.From;
            int to = move.To;
            Piece piece = PieceAt(from);

            Piece capturedPiece = PieceAt(to);
            if ((move.Flags & MoveFlags.EnPassant) != 0)
            {
                int capSq = piece == Piece.WP ? to - 8 : to + 8;
                capturedPiece = PieceAt(capSq);
            }

            _history.Push(new UndoInfo
            {
                CapturedPiece = capturedPiece,
                EnPassantSquare = EnPassantSquare,
                CastlingRights = Castling,
                HalfMoveClock = HalfMoveClock,
                ZobristHash = ZobristHash
            });

            ulong hash = ZobristHash;
            hash ^= Zobrist.CastlingKeys[(int)Castling];

            if (EnPassantSquare >= 0)
                hash ^= Zobrist.EnPassantKeys[EnPassantSquare % 8];

            EnPassantSquare = -1;

            if (piece == Piece.WP || piece == Piece.BP || capturedPiece != Piece.Empty)
                HalfMoveClock = 0;
            else
                HalfMoveClock++;

            if (capturedPiece != Piece.Empty && (move.Flags & MoveFlags.EnPassant) == 0)
            {
                RemovePiece(to, capturedPiece);
                hash ^= Zobrist.PieceKeys[(int)capturedPiece, to];
            }

            MovePiece(from, to, piece);
            hash ^= Zobrist.PieceKeys[(int)piece, from];
            hash ^= Zobrist.PieceKeys[(int)piece, to];

            if (move.Promotion != Piece.Empty)
            {
                RemovePiece(to, piece);
                AddPiece(to, move.Promotion);
                hash ^= Zobrist.PieceKeys[(int)piece, to];
                hash ^= Zobrist.PieceKeys[(int)move.Promotion, to];
            }

            if ((move.Flags & MoveFlags.EnPassant) != 0)
            {
                int capSq = piece == Piece.WP ? to - 8 : to + 8;
                Piece enemyPawn = piece == Piece.WP ? Piece.BP : Piece.WP;
                if (capturedPiece == enemyPawn)
                {
                    RemovePiece(capSq, capturedPiece);
                    hash ^= Zobrist.PieceKeys[(int)capturedPiece, capSq];
                }
            }

            if (piece == Piece.WP && to - from == 16)
                EnPassantSquare = from + 8;
            else if (piece == Piece.BP && from - to == 16)
                EnPassantSquare = from - 8;

            if (piece == Piece.WK)
                Castling &= ~(CastlingRights.WhiteKingSide | CastlingRights.WhiteQueenSide);
            else if (piece == Piece.BK)
                Castling &= ~(CastlingRights.BlackKingSide | CastlingRights.BlackQueenSide);

            if (from == 0) Castling &= ~CastlingRights.WhiteQueenSide;
            else if (from == 7) Castling &= ~CastlingRights.WhiteKingSide;
            else if (from == 56) Castling &= ~CastlingRights.BlackQueenSide;
            else if (from == 63) Castling &= ~CastlingRights.BlackKingSide;

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

            if ((move.Flags & MoveFlags.Castling) != 0)
            {
                if (piece == Piece.WK)
                {
                    if (to == 6)
                    {
                        MovePiece(7, 5, Piece.WR);
                        hash ^= Zobrist.PieceKeys[(int)Piece.WR, 7];
                        hash ^= Zobrist.PieceKeys[(int)Piece.WR, 5];
                    }
                    else if (to == 2)
                    {
                        MovePiece(0, 3, Piece.WR);
                        hash ^= Zobrist.PieceKeys[(int)Piece.WR, 0];
                        hash ^= Zobrist.PieceKeys[(int)Piece.WR, 3];
                    }
                }
                else if (piece == Piece.BK)
                {
                    if (to == 62)
                    {
                        MovePiece(63, 61, Piece.BR);
                        hash ^= Zobrist.PieceKeys[(int)Piece.BR, 63];
                        hash ^= Zobrist.PieceKeys[(int)Piece.BR, 61];
                    }
                    else if (to == 58)
                    {
                        MovePiece(56, 59, Piece.BR);
                        hash ^= Zobrist.PieceKeys[(int)Piece.BR, 56];
                        hash ^= Zobrist.PieceKeys[(int)Piece.BR, 59];
                    }
                }
            }

            UpdateOccupancy();

            hash ^= Zobrist.CastlingKeys[(int)Castling];
            if (EnPassantSquare >= 0)
                hash ^= Zobrist.EnPassantKeys[EnPassantSquare % 8];

            hash ^= Zobrist.SideToMoveKey;
            ZobristHash = hash;

            WhiteToMove = !WhiteToMove;
            if (WhiteToMove)
                FullMoveNumber++;
        }

        public void UndoMove(Move move)
        {
            UndoInfo undo = _history.Pop();

            WhiteToMove = !WhiteToMove;
            if (!WhiteToMove)
                FullMoveNumber--;

            int from = move.From;
            int to = move.To;
            Piece movedPiece = PieceAt(to);

            if ((move.Flags & MoveFlags.Castling) != 0)
            {
                if (movedPiece == Piece.WK)
                {
                    if (to == 6) MovePiece(5, 7, Piece.WR);
                    else if (to == 2) MovePiece(3, 0, Piece.WR);
                }
                else if (movedPiece == Piece.BK)
                {
                    if (to == 62) MovePiece(61, 63, Piece.BR);
                    else if (to == 58) MovePiece(59, 56, Piece.BR);
                }
            }

            if (move.Promotion != Piece.Empty)
            {
                RemovePiece(to, movedPiece);
                movedPiece = WhiteToMove ? Piece.WP : Piece.BP;
                AddPiece(to, movedPiece);
            }

            MovePiece(to, from, movedPiece);

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
            ZobristHash = undo.ZobristHash;
        }

        public bool HasCastlingRight(CastlingRights right) => (Castling & right) != 0;

        /// <summary>
        /// Makes a null move (pass turn to opponent without moving).
        /// Used for null move pruning in search.
        /// </summary>
        public void MakeNullMove()
        {
            _history.Push(new UndoInfo
            {
                CapturedPiece = Piece.Empty,
                EnPassantSquare = EnPassantSquare,
                CastlingRights = Castling,
                HalfMoveClock = HalfMoveClock,
                ZobristHash = ZobristHash
            });

            ulong hash = ZobristHash;

            // Clear en passant
            if (EnPassantSquare >= 0)
                hash ^= Zobrist.EnPassantKeys[EnPassantSquare % 8];
            EnPassantSquare = -1;

            // Switch side to move
            hash ^= Zobrist.SideToMoveKey;
            ZobristHash = hash;
            WhiteToMove = !WhiteToMove;
        }

        /// <summary>
        /// Undoes a null move.
        /// </summary>
        public void UndoNullMove()
        {
            UndoInfo undo = _history.Pop();
            WhiteToMove = !WhiteToMove;
            EnPassantSquare = undo.EnPassantSquare;
            ZobristHash = undo.ZobristHash;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong AttackersTo(int sq, ulong occupied)
        {
            return (Bitboard.PawnAttacks[1][sq] & WP)
                 | (Bitboard.PawnAttacks[0][sq] & BP)
                 | (Bitboard.KnightAttacks[sq] & (WN | BN))
                 | (MagicBitboards.GetRookAttacks(sq, occupied) & (WR | BR | WQ | BQ))
                 | (MagicBitboards.GetBishopAttacks(sq, occupied) & (WB | BB | WQ | BQ))
                 | (Bitboard.KingAttacks[sq] & (WK | BK));
        }

        public bool IsSquareAttacked(int square, bool byWhite)
        {
            if (square < 0 || square > 63)
                return false;

            if (byWhite)
            {
                if ((Bitboard.PawnAttacks[1][square] & WP) != 0) return true;
                if ((Bitboard.KnightAttacks[square] & WN) != 0) return true;
                if ((Bitboard.KingAttacks[square] & WK) != 0) return true;
                if ((MagicBitboards.GetBishopAttacks(square, AllPieces) & (WB | WQ)) != 0) return true;
                if ((MagicBitboards.GetRookAttacks(square, AllPieces) & (WR | WQ)) != 0) return true;
            }
            else
            {
                if ((Bitboard.PawnAttacks[0][square] & BP) != 0) return true;
                if ((Bitboard.KnightAttacks[square] & BN) != 0) return true;
                if ((Bitboard.KingAttacks[square] & BK) != 0) return true;
                if ((MagicBitboards.GetBishopAttacks(square, AllPieces) & (BB | BQ)) != 0) return true;
                if ((MagicBitboards.GetRookAttacks(square, AllPieces) & (BR | BQ)) != 0) return true;
            }

            return false;
        }

        public bool IsKingInCheck(bool white)
        {
            ulong kingBB = white ? WK : BK;
            if (kingBB == 0)
                return false;
            int kingSquare = Bitboard.BitScanForward(kingBB);
            return IsSquareAttacked(kingSquare, !white);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int KingSquare(bool white) => Bitboard.BitScanForward(white ? WK : BK);
    }
}
