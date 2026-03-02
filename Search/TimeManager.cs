using System;

namespace ChessEngine
{
    public enum GamePhase { Opening, Middlegame, Endgame }

    public class TimeManager
    {
        private const int StabilityThreshold = 75;
        private const int StableIterationsForEarlyExit = 5;
        private const int MinDepthForEarlyExit = 8;
        private const double MaxExtension = 2.0;
        private const int SafetyMarginMs = 20;
        private const int MinTimeMs = 500;
        private const int HardCapMs = 10000;

        public int BaseTimeMs { get; private set; }
        public int MaxTimeMs { get; private set; }
        public int EffectiveBaseTime => (int)(BaseTimeMs * _timeExtension);

        private int _lastScore;
        private int _stableIterations;
        private int _lastCompletedDepth;
        private double _timeExtension = 1.0;

        public void Initialize(int remainingMs, int incrementMs, int movesToGo,
                              GamePhase phase, int rootMoveCount, bool isInCheck)
        {
            _lastScore = 0;
            _stableIterations = 0;
            _lastCompletedDepth = 0;
            _timeExtension = 1.0;
            _mateFound = false;

            int estimatedMovesLeft = movesToGo > 0 ? movesToGo : 20;
            int baseTime = remainingMs / estimatedMovesLeft + (int)(incrementMs * 0.9);
            baseTime = Math.Max(baseTime, incrementMs > 0 ? incrementMs : remainingMs / 40);

            double phaseMultiplier = phase switch
            {
                GamePhase.Opening => 0.9,
                GamePhase.Middlegame => 1.0,
                GamePhase.Endgame => 1.2,
                _ => 1.0
            };
            baseTime = (int)(baseTime * phaseMultiplier);

            if (isInCheck)
                baseTime = (int)(baseTime * 1.1);

            baseTime -= SafetyMarginMs;
            baseTime = Math.Min(baseTime, HardCapMs);
            baseTime = Math.Max(baseTime, MinTimeMs);

            BaseTimeMs = baseTime;

            MaxTimeMs = Math.Min(baseTime * 4, remainingMs * 2 / 3);
            MaxTimeMs = Math.Min(MaxTimeMs, HardCapMs);
            MaxTimeMs = Math.Max(MaxTimeMs, baseTime);
        }

        public void InitializeFixedTime(int moveTimeMs)
        {
            _lastScore = 0;
            _stableIterations = 0;
            _lastCompletedDepth = 0;
            _timeExtension = 1.0;
            _mateFound = false;

            int time = Math.Max(moveTimeMs - SafetyMarginMs, MinTimeMs);
            BaseTimeMs = time;
            MaxTimeMs = time;
        }

        private const int WinningScoreThreshold = 180; // centipawns; score is from side-to-move perspective
        private const int LosingScoreThreshold = 180;  // when losing, extend time to find best defense or a hold
        private const int MateScoreThreshold = 99000;  // scores above this indicate a forced mate

        private bool _mateFound;

        public void OnIterationComplete(int depth, int score)
        {
            _lastCompletedDepth = depth;

            if (depth == 1)
            {
                _lastScore = score;
                _stableIterations = 0;
                return;
            }

            if (Math.Abs(score) >= MateScoreThreshold && depth >= 3)
            {
                _mateFound = true;
                _stableIterations = StableIterationsForEarlyExit;
                _lastScore = score;
                return;
            }

            int scoreDelta = Math.Abs(score - _lastScore);

            if (scoreDelta <= StabilityThreshold)
            {
                _stableIterations++;
                if (score >= WinningScoreThreshold && _stableIterations >= 2)
                    _timeExtension = Math.Max(_timeExtension * 0.9, 0.5);
                if (score <= -LosingScoreThreshold && _stableIterations >= 1)
                    ExtendTime(1.15);
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

            if (_mateFound && elapsedMs >= MinTimeMs)
                return true;

            // Require minimum depth before allowing stability-based early exit
            if (_lastCompletedDepth < MinDepthForEarlyExit)
                return false;

            // When winning or losing, require a higher minimum fraction of base time before allowing early exit
            bool winning = _lastScore >= WinningScoreThreshold;
            bool losing = _lastScore <= -LosingScoreThreshold;
            long minTimeBeforeEarlyExit = (winning || losing) ? (BaseTimeMs * 4) / 5 : (BaseTimeMs * 3) / 4;
            return _stableIterations >= StableIterationsForEarlyExit && elapsedMs >= minTimeBeforeEarlyExit;
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
