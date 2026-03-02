using System;
using System.Runtime.CompilerServices;
using System.Threading;

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
        private const int LockCount = 4096;
        private static readonly int LockMask = LockCount - 1;

        private TTEntry[] _table = Array.Empty<TTEntry>();
        private readonly object[] _locks = new object[LockCount];
        private ulong _mask;
        private int _entries;

        public const int MateScore = 30000;
        public const int MateThreshold = MateScore - 1000;

        public TranspositionTable(int sizeMB = 64)
        {
            for (int i = 0; i < LockCount; i++)
            {
                _locks[i] = new object();
            }
            Resize(sizeMB);
        }

        public void Resize(int sizeMB)
        {
            int entrySize = Unsafe.SizeOf<TTEntry>();
            long totalBytes = (long)sizeMB * 1024 * 1024;
            long numSlots = totalBytes / entrySize;
            long numBuckets = numSlots / 2;

            _entries = 1;
            while (_entries * 2 <= numBuckets)
            {
                _entries *= 2;
            }

            _mask = (ulong)(_entries - 1);
            _table = new TTEntry[_entries * 2];
        }

        public void Clear()
        {
            for (int i = 0; i < LockCount; i++)
            {
                Monitor.Enter(_locks[i]);
            }
            try
            {
                Array.Clear(_table, 0, _table.Length);
            }
            finally
            {
                for (int i = 0; i < LockCount; i++)
                {
                    Monitor.Exit(_locks[i]);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Store(ulong hash, int depth, int score, TTFlag flag, Move bestMove, int ply)
        {
            int bucket = (int)(hash & _mask);
            int baseIdx = bucket * 2;
            object bucketLock = _locks[bucket & LockMask];
            lock (bucketLock)
            {
                ref TTEntry e0 = ref _table[baseIdx];
                ref TTEntry e1 = ref _table[baseIdx + 1];

                ref TTEntry target = ref e0;
                if (e0.Hash == hash)
                {
                    if (depth < e0.Depth)
                    {
                        return;
                    }
                }
                else if (e1.Hash == hash)
                {
                    if (depth < e1.Depth)
                    {
                        return;
                    }
                    target = ref e1;
                }
                else
                {
                    int d0 = e0.Flag == TTFlag.None ? -1 : e0.Depth;
                    int d1 = e1.Flag == TTFlag.None ? -1 : e1.Depth;
                    if (d1 < d0)
                    {
                        target = ref e1;
                    }
                }

                if (score > MateThreshold)
                {
                    score += ply;
                }
                else if (score < -MateThreshold)
                {
                    score -= ply;
                }
                target.Hash = hash;
                target.Score = (short)score;
                target.Depth = (byte)depth;
                target.Flag = flag;
                target.BestMove = TTEntry.EncodeMove(bestMove);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Probe(ulong hash, int depth, int alpha, int beta, int ply, out int score, out Move ttMove)
        {
            int bucket = (int)(hash & _mask);
            int baseIdx = bucket * 2;
            object bucketLock = _locks[bucket & LockMask];
            lock (bucketLock)
            {
                ref TTEntry e0 = ref _table[baseIdx];
                ref TTEntry e1 = ref _table[baseIdx + 1];

                score = 0;
                ttMove = default;

                ref TTEntry entry = ref e0;
                if (e0.Hash != hash)
                {
                    if (e1.Hash != hash)
                    {
                        return false;
                    }
                    entry = ref e1;
                }
                else if (e1.Hash == hash && e1.Depth > e0.Depth)
                {
                    entry = ref e1;
                }

                ttMove = TTEntry.DecodeMove(entry.BestMove);
                if (entry.Depth < depth)
                {
                    return false;
                }

                int ttScore = entry.Score;
                if (ttScore > MateThreshold)
                {
                    ttScore -= ply;
                }
                else if (ttScore < -MateThreshold)
                {
                    ttScore += ply;
                }

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
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Move GetTTMove(ulong hash)
        {
            int bucket = (int)(hash & _mask);
            int baseIdx = bucket * 2;
            object bucketLock = _locks[bucket & LockMask];
            lock (bucketLock)
            {
                ref TTEntry e0 = ref _table[baseIdx];
                ref TTEntry e1 = ref _table[baseIdx + 1];
                TTEntry best = default;
                if (e0.Hash == hash)
                {
                    best = e0;
                }
                if (e1.Hash == hash && (best.Hash == 0 || e1.Depth > best.Depth))
                {
                    best = e1;
                }
                return best.Hash == hash ? TTEntry.DecodeMove(best.BestMove) : default;
            }
        }

        /// <summary>
        /// Probe for singular extension - returns true if we have a useful TT entry
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ProbeForSingular(ulong hash, int depth, out int score, out TTFlag flag)
        {
            int bucket = (int)(hash & _mask);
            int baseIdx = bucket * 2;
            object bucketLock = _locks[bucket & LockMask];
            lock (bucketLock)
            {
                ref TTEntry e0 = ref _table[baseIdx];
                ref TTEntry e1 = ref _table[baseIdx + 1];

                score = 0;
                flag = TTFlag.None;

                TTEntry entry = default;
                if (e0.Hash == hash)
                {
                    entry = e0;
                }
                if (e1.Hash == hash && (entry.Hash == 0 || e1.Depth > entry.Depth))
                {
                    entry = e1;
                }
                if (entry.Hash != hash)
                {
                    return false;
                }

                if (entry.Depth < depth - 3)
                {
                    return false;
                }

                score = entry.Score;
                flag = entry.Flag;
                return true;
            }
        }

        /// <summary>
        /// Returns the hash table usage in permille (0-1000).
        /// Samples a portion of the table for performance.
        /// </summary>
        public int Hashfull()
        {
            if (_entries == 0)
            {
                return 0;
            }

            int sampleBuckets = Math.Min(1000, _entries);
            int used = 0;
            for (int i = 0; i < sampleBuckets; i++)
            {
                if (_table[i * 2].Flag != TTFlag.None) used++;
                if (_table[i * 2 + 1].Flag != TTFlag.None) used++;
            }
            return used * 1000 / (sampleBuckets * 2);
        }
    }
}
