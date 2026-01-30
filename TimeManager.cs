using System;

namespace ChessEngine
{
    public enum GamePhase { Opening, Middlegame, Endgame }

    public class TimeManager
    {
        private const int StabilityThreshold = 50;
        private const int StableIterationsForEarlyExit = 3;
        private const double MaxExtension = 2.0;
        private const int SafetyMarginMs = 20;
        private const int MinTimeMs = 10;
        private const int HardCapMs = 10000;

        public int BaseTimeMs { get; private set; }
        public int MaxTimeMs { get; private set; }
        public int EffectiveBaseTime => (int)(BaseTimeMs * _timeExtension);

        private int _lastScore;
        private int _stableIterations;
        private double _timeExtension = 1.0;

        public void Initialize(int remainingMs, int incrementMs, int movesToGo,
                              GamePhase phase, int rootMoveCount, bool isInCheck)
        {
            _lastScore = 0;
            _stableIterations = 0;
            _timeExtension = 1.0;

            int baseTime = movesToGo > 0
                ? remainingMs / movesToGo + (int)(incrementMs * 0.8)
                : remainingMs / 30 + (int)(incrementMs * 0.8);

            double phaseMultiplier = phase switch
            {
                GamePhase.Opening => 0.8,
                GamePhase.Middlegame => 1.0,
                GamePhase.Endgame => 1.2,
                _ => 1.0
            };
            baseTime = (int)(baseTime * phaseMultiplier);

            double complexityMultiplier = rootMoveCount switch
            {
                > 35 => 1.2,
                < 5 => 0.5,
                < 15 => 0.7,
                _ => 1.0
            };
            baseTime = (int)(baseTime * complexityMultiplier);

            if (isInCheck)
                baseTime = (int)(baseTime * 1.1);

            baseTime -= SafetyMarginMs;
            baseTime = Math.Min(baseTime, remainingMs / 5);
            baseTime = Math.Min(baseTime, HardCapMs);
            baseTime = Math.Max(baseTime, MinTimeMs);

            BaseTimeMs = baseTime;

            MaxTimeMs = Math.Min(baseTime * 3, remainingMs / 2);
            MaxTimeMs = Math.Min(MaxTimeMs, HardCapMs);
            MaxTimeMs = Math.Max(MaxTimeMs, baseTime);
        }

        public void InitializeFixedTime(int moveTimeMs)
        {
            _lastScore = 0;
            _stableIterations = 0;
            _timeExtension = 1.0;

            int time = Math.Max(moveTimeMs - SafetyMarginMs, MinTimeMs);
            BaseTimeMs = time;
            MaxTimeMs = time;
        }

        public void OnIterationComplete(int depth, int score)
        {
            if (depth == 1)
            {
                _lastScore = score;
                _stableIterations = 0;
                return;
            }

            int scoreDelta = Math.Abs(score - _lastScore);

            if (scoreDelta <= StabilityThreshold)
            {
                _stableIterations++;
            }
            else
            {
                _stableIterations = 0;
                if (scoreDelta > StabilityThreshold * 2)
                    ExtendTime(1.3);
            }

            _lastScore = score;
        }

        public void ExtendTime(double factor)
        {
            _timeExtension = Math.Min(_timeExtension * factor, MaxExtension);
        }

        public bool ShouldStop(long elapsedMs)
        {
            if (elapsedMs >= EffectiveBaseTime)
                return true;

            return _stableIterations >= StableIterationsForEarlyExit && elapsedMs >= BaseTimeMs / 2;
        }

        public bool MustStop(long elapsedMs) => elapsedMs >= MaxTimeMs;

        public static GamePhase DetectGamePhase(Board board)
        {
            int whitePieces = Bitboard.PopCount(board.WN) + Bitboard.PopCount(board.WB) +
                              Bitboard.PopCount(board.WR) + Bitboard.PopCount(board.WQ) * 2;
            int blackPieces = Bitboard.PopCount(board.BN) + Bitboard.PopCount(board.BB) +
                              Bitboard.PopCount(board.BR) + Bitboard.PopCount(board.BQ) * 2;
            int totalMinorMajor = whitePieces + blackPieces;
            int moveNumber = board.FullMoveNumber;

            if (moveNumber <= 10 && totalMinorMajor >= 12)
                return GamePhase.Opening;
            if (totalMinorMajor <= 6)
                return GamePhase.Endgame;
            return GamePhase.Middlegame;
        }
    }
}
