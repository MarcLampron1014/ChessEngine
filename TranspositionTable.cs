using System;
using System.Runtime.CompilerServices;

namespace ChessEngine
{
    public enum TTFlag : byte
    {
        None = 0,
        Exact = 1,
        Alpha = 2,
        Beta = 3
    }

    public struct TTEntry
    {
        public ulong Hash;
        public short Score;
        public byte Depth;
        public TTFlag Flag;
        public ushort BestMove;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort EncodeMove(Move move)
        {
            return (ushort)(move.From | (move.To << 6) | ((int)move.Promotion << 12));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Move DecodeMove(ushort encoded)
        {
            int from = encoded & 0x3F;
            int to = (encoded >> 6) & 0x3F;
            Piece promotion = (Piece)((encoded >> 12) & 0xF);
            return new Move(from, to, promotion);
        }
    }

    public class TranspositionTable
    {
        private TTEntry[] _table = Array.Empty<TTEntry>();
        private ulong _mask;
        private int _entries;

        public const int MateScore = 30000;
        public const int MateThreshold = MateScore - 1000;

        public TranspositionTable(int sizeMB = 64)
        {
            Resize(sizeMB);
        }

        public void Resize(int sizeMB)
        {
            int entrySize = Unsafe.SizeOf<TTEntry>();
            long totalBytes = (long)sizeMB * 1024 * 1024;
            long numEntries = totalBytes / entrySize;

            _entries = 1;
            while (_entries * 2 <= numEntries)
                _entries *= 2;

            _mask = (ulong)(_entries - 1);
            _table = new TTEntry[_entries];
        }

        public void Clear() => Array.Clear(_table, 0, _table.Length);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Store(ulong hash, int depth, int score, TTFlag flag, Move bestMove, int ply)
        {
            int index = (int)(hash & _mask);
            ref TTEntry entry = ref _table[index];

            // Adjust mate scores for storage
            if (score > MateThreshold)
                score += ply;
            else if (score < -MateThreshold)
                score -= ply;

            entry.Hash = hash;
            entry.Score = (short)score;
            entry.Depth = (byte)depth;
            entry.Flag = flag;
            entry.BestMove = TTEntry.EncodeMove(bestMove);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Probe(ulong hash, int depth, int alpha, int beta, int ply, out int score, out Move ttMove)
        {
            int index = (int)(hash & _mask);
            ref TTEntry entry = ref _table[index];

            score = 0;
            ttMove = default;

            if (entry.Hash != hash)
                return false;

            ttMove = TTEntry.DecodeMove(entry.BestMove);

            if (entry.Depth < depth)
                return false;

            // Adjust mate scores for retrieval
            int ttScore = entry.Score;
            if (ttScore > MateThreshold)
                ttScore -= ply;
            else if (ttScore < -MateThreshold)
                ttScore += ply;

            switch (entry.Flag)
            {
                case TTFlag.Exact:
                    score = ttScore;
                    return true;
                case TTFlag.Alpha when ttScore <= alpha:
                    score = alpha;
                    return true;
                case TTFlag.Beta when ttScore >= beta:
                    score = beta;
                    return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Move GetTTMove(ulong hash)
        {
            int index = (int)(hash & _mask);
            ref TTEntry entry = ref _table[index];
            return entry.Hash == hash ? TTEntry.DecodeMove(entry.BestMove) : default;
        }
    }
}
