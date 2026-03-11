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
        public int Phase;
        public int MaterialMG;
        public int MaterialEG;
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

        // Cached king squares for fast IsKingInCheck
        private int _whiteKingSquare;
        private int _blackKingSquare;

        // Mailbox array for O(1) PieceAt lookup
        private readonly Piece[] _pieces = new Piece[64];

        // Incremental game phase (0 = endgame, 24 = opening)
        private int _phase;
        public int Phase => _phase;

        // Incremental material scores (middlegame and endgame)
        // Positive = white advantage, negative = black advantage
        private int _materialMG;
        private int _materialEG;
        public int MaterialMG => _materialMG;
        public int MaterialEG => _materialEG;

        // Phase values for each piece type
        private const int KnightPhaseValue = 1;
        private const int BishopPhaseValue = 1;
        private const int RookPhaseValue = 2;
        private const int QueenPhaseValue = 4;

        private readonly Stack<UndoInfo> _history = new Stack<UndoInfo>();
        private readonly List<ulong> _positionHistory = new List<ulong>();

        public Board()
        {
            Reset();
        }

        public void Reset()
        {
            WP = WN = WB = WR = WQ = WK = 0;
            BP = BN = BB = BR = BQ = BK = 0;

            // Clear piece array
            Array.Clear(_pieces, 0, 64);

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

            // Initialize piece array for starting position
            _pieces[0] = Piece.WR; _pieces[1] = Piece.WN; _pieces[2] = Piece.WB; _pieces[3] = Piece.WQ;
            _pieces[4] = Piece.WK; _pieces[5] = Piece.WB; _pieces[6] = Piece.WN; _pieces[7] = Piece.WR;
            for (int i = 8; i < 16; i++) _pieces[i] = Piece.WP;
            for (int i = 48; i < 56; i++) _pieces[i] = Piece.BP;
            _pieces[56] = Piece.BR; _pieces[57] = Piece.BN; _pieces[58] = Piece.BB; _pieces[59] = Piece.BQ;
            _pieces[60] = Piece.BK; _pieces[61] = Piece.BB; _pieces[62] = Piece.BN; _pieces[63] = Piece.BR;

            UpdateOccupancy();

            // Cache king squares
            _whiteKingSquare = 4;  // e1
            _blackKingSquare = 60; // e8

            // Initialize phase: 2N + 2B + 2R + Q = 2*1 + 2*1 + 2*2 + 4 = 12 per side = 24 total
            _phase = 24;

            // Initialize material (starting position is equal, so 0)
            _materialMG = 0;
            _materialEG = 0;

            WhiteToMove = true;
            EnPassantSquare = -1;
            Castling = CastlingRights.WhiteKingSide | CastlingRights.WhiteQueenSide |
                       CastlingRights.BlackKingSide | CastlingRights.BlackQueenSide;
            HalfMoveClock = 0;
            FullMoveNumber = 1;

            _history.Clear();
            _positionHistory.Clear();
            ComputeHash();
            _positionHistory.Add(ZobristHash);
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
        public Piece PieceAt(int sq) => _pieces[sq];

        public void SetPiece(int sq, Piece p)
        {
            ClearPiece(sq);
            if (p == Piece.Empty) return;

            _pieces[sq] = p;
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
            _pieces[sq] = Piece.Empty;
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
            _pieces[from] = Piece.Empty;
            _pieces[to] = p;
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
            _pieces[sq] = Piece.Empty;
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
            _pieces[sq] = p;
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
            
            // Rebuild piece array from bitboards (for FEN loading)
            RebuildPieceArray();
            
            // Cache king squares
            _whiteKingSquare = WK != 0 ? Bitboard.BitScanForward(WK) : -1;
            _blackKingSquare = BK != 0 ? Bitboard.BitScanForward(BK) : -1;

            // Calculate phase from piece counts
            _phase = Bitboard.PopCount(WN | BN) * KnightPhaseValue +
                     Bitboard.PopCount(WB | BB) * BishopPhaseValue +
                     Bitboard.PopCount(WR | BR) * RookPhaseValue +
                     Bitboard.PopCount(WQ | BQ) * QueenPhaseValue;
            if (_phase > 24) _phase = 24;

            // Compute material balance from piece counts
            ComputeMaterial();

            WhiteToMove = whiteToMove;
            Castling = castling;
            EnPassantSquare = enPassantSquare;
            HalfMoveClock = halfMoveClock;
            FullMoveNumber = fullMoveNumber;
            _history.Clear();
            _positionHistory.Clear();
            ComputeHash();
            _positionHistory.Add(ZobristHash);
        }

        private void RebuildPieceArray()
        {
            Array.Clear(_pieces, 0, 64);
            for (int sq = 0; sq < 64; sq++)
            {
                ulong mask = 1UL << sq;
                if ((WP & mask) != 0) _pieces[sq] = Piece.WP;
                else if ((WN & mask) != 0) _pieces[sq] = Piece.WN;
                else if ((WB & mask) != 0) _pieces[sq] = Piece.WB;
                else if ((WR & mask) != 0) _pieces[sq] = Piece.WR;
                else if ((WQ & mask) != 0) _pieces[sq] = Piece.WQ;
                else if ((WK & mask) != 0) _pieces[sq] = Piece.WK;
                else if ((BP & mask) != 0) _pieces[sq] = Piece.BP;
                else if ((BN & mask) != 0) _pieces[sq] = Piece.BN;
                else if ((BB & mask) != 0) _pieces[sq] = Piece.BB;
                else if ((BR & mask) != 0) _pieces[sq] = Piece.BR;
                else if ((BQ & mask) != 0) _pieces[sq] = Piece.BQ;
                else if ((BK & mask) != 0) _pieces[sq] = Piece.BK;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetPhaseValue(Piece p)
        {
            return p switch
            {
                Piece.WN or Piece.BN => KnightPhaseValue,
                Piece.WB or Piece.BB => BishopPhaseValue,
                Piece.WR or Piece.BR => RookPhaseValue,
                Piece.WQ or Piece.BQ => QueenPhaseValue,
                _ => 0
            };
        }

        /// <summary>
        /// Gets the middlegame material value for a piece (positive for white, negative for black).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetMaterialMG(Piece p)
        {
            var ep = EvalParams.Instance;
            return p switch
            {
                Piece.WP => ep.PawnValueMG,
                Piece.WN => ep.KnightValueMG,
                Piece.WB => ep.BishopValueMG,
                Piece.WR => ep.RookValueMG,
                Piece.WQ => ep.QueenValueMG,
                Piece.BP => -ep.PawnValueMG,
                Piece.BN => -ep.KnightValueMG,
                Piece.BB => -ep.BishopValueMG,
                Piece.BR => -ep.RookValueMG,
                Piece.BQ => -ep.QueenValueMG,
                _ => 0
            };
        }

        /// <summary>
        /// Gets the endgame material value for a piece (positive for white, negative for black).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetMaterialEG(Piece p)
        {
            var ep = EvalParams.Instance;
            return p switch
            {
                Piece.WP => ep.PawnValueEG,
                Piece.WN => ep.KnightValueEG,
                Piece.WB => ep.BishopValueEG,
                Piece.WR => ep.RookValueEG,
                Piece.WQ => ep.QueenValueEG,
                Piece.BP => -ep.PawnValueEG,
                Piece.BN => -ep.KnightValueEG,
                Piece.BB => -ep.BishopValueEG,
                Piece.BR => -ep.RookValueEG,
                Piece.BQ => -ep.QueenValueEG,
                _ => 0
            };
        }

        /// <summary>
        /// Computes material balance from scratch based on piece bitboards.
        /// Call after loading from FEN.
        /// </summary>
        public void ComputeMaterial()
        {
            var ep = EvalParams.Instance;
            _materialMG = 0;
            _materialEG = 0;

            // White pieces
            _materialMG += Bitboard.PopCount(WP) * ep.PawnValueMG;
            _materialMG += Bitboard.PopCount(WN) * ep.KnightValueMG;
            _materialMG += Bitboard.PopCount(WB) * ep.BishopValueMG;
            _materialMG += Bitboard.PopCount(WR) * ep.RookValueMG;
            _materialMG += Bitboard.PopCount(WQ) * ep.QueenValueMG;

            _materialEG += Bitboard.PopCount(WP) * ep.PawnValueEG;
            _materialEG += Bitboard.PopCount(WN) * ep.KnightValueEG;
            _materialEG += Bitboard.PopCount(WB) * ep.BishopValueEG;
            _materialEG += Bitboard.PopCount(WR) * ep.RookValueEG;
            _materialEG += Bitboard.PopCount(WQ) * ep.QueenValueEG;

            // Black pieces (negative contribution)
            _materialMG -= Bitboard.PopCount(BP) * ep.PawnValueMG;
            _materialMG -= Bitboard.PopCount(BN) * ep.KnightValueMG;
            _materialMG -= Bitboard.PopCount(BB) * ep.BishopValueMG;
            _materialMG -= Bitboard.PopCount(BR) * ep.RookValueMG;
            _materialMG -= Bitboard.PopCount(BQ) * ep.QueenValueMG;

            _materialEG -= Bitboard.PopCount(BP) * ep.PawnValueEG;
            _materialEG -= Bitboard.PopCount(BN) * ep.KnightValueEG;
            _materialEG -= Bitboard.PopCount(BB) * ep.BishopValueEG;
            _materialEG -= Bitboard.PopCount(BR) * ep.RookValueEG;
            _materialEG -= Bitboard.PopCount(BQ) * ep.QueenValueEG;
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
                ZobristHash = ZobristHash,
                Phase = _phase,
                MaterialMG = _materialMG,
                MaterialEG = _materialEG
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
                // Decrement phase for captured piece
                _phase -= GetPhaseValue(capturedPiece);
                // Update material (remove captured piece's value)
                _materialMG -= GetMaterialMG(capturedPiece);
                _materialEG -= GetMaterialEG(capturedPiece);
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
                // Increment phase for promoted piece (pawn has no phase, promoted piece does)
                _phase += GetPhaseValue(move.Promotion);
                // Update material (remove pawn value, add promoted piece value)
                _materialMG -= GetMaterialMG(piece);
                _materialMG += GetMaterialMG(move.Promotion);
                _materialEG -= GetMaterialEG(piece);
                _materialEG += GetMaterialEG(move.Promotion);
            }

            if ((move.Flags & MoveFlags.EnPassant) != 0)
            {
                int capSq = piece == Piece.WP ? to - 8 : to + 8;
                Piece enemyPawn = piece == Piece.WP ? Piece.BP : Piece.WP;
                if (capturedPiece == enemyPawn)
                {
                    RemovePiece(capSq, capturedPiece);
                    hash ^= Zobrist.PieceKeys[(int)capturedPiece, capSq];
                    // Update material for en passant capture (remove enemy pawn value)
                    _materialMG -= GetMaterialMG(capturedPiece);
                    _materialEG -= GetMaterialEG(capturedPiece);
                }
            }

            if (piece == Piece.WP && to - from == 16)
                EnPassantSquare = from + 8;
            else if (piece == Piece.BP && from - to == 16)
                EnPassantSquare = from - 8;

            if (piece == Piece.WK)
            {
                _whiteKingSquare = to;
                Castling &= ~(CastlingRights.WhiteKingSide | CastlingRights.WhiteQueenSide);
            }
            else if (piece == Piece.BK)
            {
                _blackKingSquare = to;
                Castling &= ~(CastlingRights.BlackKingSide | CastlingRights.BlackQueenSide);
            }

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

            _positionHistory.Add(ZobristHash);
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

            // Restore king square if king was moved
            if (movedPiece == Piece.WK)
                _whiteKingSquare = from;
            else if (movedPiece == Piece.BK)
                _blackKingSquare = from;

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
            _phase = undo.Phase;
            _materialMG = undo.MaterialMG;
            _materialEG = undo.MaterialEG;

            if (_positionHistory.Count > 0)
                _positionHistory.RemoveAt(_positionHistory.Count - 1);
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


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsKingInCheck(bool white)
        {
            int kingSquare = white ? _whiteKingSquare : _blackKingSquare;
            return kingSquare >= 0 && IsSquareAttacked(kingSquare, !white);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int KingSquare(bool white) => white ? _whiteKingSquare : _blackKingSquare;

        /// <summary>
        /// Check if the current position is a repetition (has occurred before).
        /// Returns true if position occurred at least once before (2-fold).
        /// For search, we treat 2-fold as draw since we're about to make it 3-fold.
        /// </summary>
        public bool IsRepetition()
        {
            if (_positionHistory.Count < 4)
                return false;

            // Only need to check positions where same side was to move
            // and within the last HalfMoveClock moves (no captures/pawn moves reset this)
            int limit = Math.Min(_positionHistory.Count - 1, HalfMoveClock);
            
            for (int i = _positionHistory.Count - 3; i >= _positionHistory.Count - 1 - limit; i -= 2)
            {
                if (i < 0) break;
                if (_positionHistory[i] == ZobristHash)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Check if we've reached the 50-move rule (100 half-moves without capture or pawn move).
        /// </summary>
        public bool IsFiftyMoveRule() => HalfMoveClock >= 100;
    }
}
