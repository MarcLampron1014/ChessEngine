using System;

namespace ChessEngine
{
    public static partial class Search
    {
        private struct MovePicker
        {
            private enum Stage : byte { TTMove, GenerateMoves, GoodCaptures, Killers, Counters, BadCaptures, Quiets, Done }

            private Stage _stage;
            private readonly Board _board;
            private readonly Move _ttMove;
            private readonly Move _prevMove;
            private readonly int _ply;
            private Move[] _moves;
            private int _moveCount;
            private int _currentIdx;
            private int _captureEnd;
            private int _badCaptureStart;

            public MovePicker(Board board, Move[] moveBuffer, Move ttMove, int ply, Move prevMove = default)
            {
                _board = board;
                _ttMove = ttMove;
                _prevMove = prevMove;
                _ply = ply;
                _moves = moveBuffer;
                _moveCount = 0;
                _currentIdx = 0;
                _captureEnd = 0;
                _badCaptureStart = 0;
                _stage = ttMove.From != ttMove.To ? Stage.TTMove : Stage.GenerateMoves;
            }

            public bool NextMove(out Move move, out int moveIndex)
            {
                while (_stage != Stage.Done)
                {
                    switch (_stage)
                    {
                        case Stage.TTMove:
                            _stage = Stage.GenerateMoves;
                            if (IsLegalMove(_board, _ttMove))
                            {
                                move = _ttMove;
                                moveIndex = 0;
                                return true;
                            }
                            continue;

                        case Stage.GenerateMoves:
                            GenerateAndScoreMoves();
                            _stage = Stage.GoodCaptures;
                            continue;

                        case Stage.GoodCaptures:
                            while (_currentIdx < _captureEnd)
                            {
                                int bestIdx = SelectBest(_currentIdx, _captureEnd);
                                move = _moves[bestIdx];
                                
                                if (MovesEqual(move, _ttMove))
                                {
                                    SwapMoves(bestIdx, _currentIdx);
                                    _currentIdx++;
                                    continue;
                                }

                                if (SEE(_board, move) < 0)
                                {
                                    SwapMoves(bestIdx, --_captureEnd);
                                    continue;
                                }

                                SwapMoves(bestIdx, _currentIdx);
                                moveIndex = _currentIdx++;
                                return true;
                            }
                            _badCaptureStart = _captureEnd;
                            _stage = Stage.Killers;
                            continue;

                        case Stage.Killers:
                            _stage = Stage.Counters;
                            if (_ply < MaxPly)
                            {
                                for (int k = 0; k < 2; k++)
                                {
                                    Move killer = Killers[_ply, k];
                                    if (killer.From == killer.To) continue;
                                    if (MovesEqual(killer, _ttMove)) continue;
                                    if (killer.IsCapture) continue;

                                    for (int i = _captureEnd; i < _moveCount; i++)
                                    {
                                        if (MovesEqual(_moves[i], killer))
                                        {
                                            move = _moves[i];
                                            moveIndex = i;
                                            SwapMoves(i, _captureEnd);
                                            _captureEnd++;
                                            return true;
                                        }
                                    }
                                }
                            }
                            continue;

                        case Stage.Counters:
                            _stage = Stage.BadCaptures;
                            if (PreviousMove.From != PreviousMove.To)
                            {
                                Piece prevPiece = _board.PieceAt(PreviousMove.To);
                                if (prevPiece != Piece.Empty)
                                {
                                    Move counter = Counters[(int)prevPiece, PreviousMove.To];
                                    if (counter.From != counter.To && !MovesEqual(counter, _ttMove) && !counter.IsCapture)
                                    {
                                        for (int i = _captureEnd; i < _moveCount; i++)
                                        {
                                            if (MovesEqual(_moves[i], counter))
                                            {
                                                move = _moves[i];
                                                moveIndex = i;
                                                SwapMoves(i, _captureEnd);
                                                _captureEnd++;
                                                return true;
                                            }
                                        }
                                    }
                                }
                            }
                            continue;

                        case Stage.BadCaptures:
                            while (_badCaptureStart < _moveCount && _moves[_badCaptureStart].IsCapture)
                            {
                                move = _moves[_badCaptureStart];
                                if (!MovesEqual(move, _ttMove))
                                {
                                    moveIndex = _badCaptureStart++;
                                    return true;
                                }
                                _badCaptureStart++;
                            }
                            _stage = Stage.Quiets;
                            _currentIdx = _captureEnd;
                            continue;

                        case Stage.Quiets:
                            while (_currentIdx < _moveCount)
                            {
                                int bestIdx = SelectBest(_currentIdx, _moveCount);
                                move = _moves[bestIdx];
                                
                                if (MovesEqual(move, _ttMove) || 
                                    (_ply < MaxPly && (MovesEqual(move, Killers[_ply, 0]) || MovesEqual(move, Killers[_ply, 1]))))
                                {
                                    SwapMoves(bestIdx, _currentIdx);
                                    _currentIdx++;
                                    continue;
                                }

                                if (PreviousMove.From != PreviousMove.To)
                                {
                                    Piece prevPiece = _board.PieceAt(PreviousMove.To);
                                    if (prevPiece != Piece.Empty && MovesEqual(move, Counters[(int)prevPiece, PreviousMove.To]))
                                    {
                                        SwapMoves(bestIdx, _currentIdx);
                                        _currentIdx++;
                                        continue;
                                    }
                                }

                                SwapMoves(bestIdx, _currentIdx);
                                moveIndex = _currentIdx++;
                                return true;
                            }
                            _stage = Stage.Done;
                            continue;
                    }
                }

                move = default;
                moveIndex = -1;
                return false;
            }

            private void GenerateAndScoreMoves()
            {
                _moveCount = MoveGenerator.GenerateLegalMoves(_board, _moves);
                
                int captureIdx = 0;
                int quietIdx = _moveCount - 1;

                while (captureIdx <= quietIdx)
                {
                    if (_moves[captureIdx].IsCapture || _moves[captureIdx].IsPromotion)
                    {
                        MoveScores[captureIdx] = ScoreMoveInternal(_moves[captureIdx]);
                        captureIdx++;
                    }
                    else
                    {
                        (_moves[captureIdx], _moves[quietIdx]) = (_moves[quietIdx], _moves[captureIdx]);
                        MoveScores[quietIdx] = ScoreMoveInternal(_moves[quietIdx]);
                        quietIdx--;
                    }
                }

                _captureEnd = captureIdx;
                _currentIdx = 0;

                for (int i = _captureEnd; i < _moveCount; i++)
                {
                    MoveScores[i] = ScoreMoveInternal(_moves[i]);
                }
            }

            private int ScoreMoveInternal(Move move)
            {
                int score = 0;

                if (move.IsPromotion)
                    score += 1_000_000 + Evaluator.GetPieceValue(move.Promotion);

                if (move.IsCapture)
                {
                    Piece attacker = _board.PieceAt(move.From);
                    Piece victim = move.IsEnPassant
                        ? (_board.WhiteToMove ? Piece.BP : Piece.WP)
                        : _board.PieceAt(move.To);

                    int victimValue = Evaluator.GetPieceValue(victim);
                    int attackerValue = Evaluator.GetPieceValue(attacker);
                    score += 500_000 + (victimValue * 10) - attackerValue;
                    
                    int capturedType = GetPieceType(victim);
                    score += CaptureHistory[(int)attacker, move.To, capturedType];
                }
                else
                {
                    Piece piece = _board.PieceAt(move.From);
                    if (piece != Piece.Empty)
                    {
                        score += History[(int)piece, move.To];
                        
                        if (_prevMove.From != _prevMove.To)
                        {
                            Piece prevPiece = _board.PieceAt(_prevMove.To);
                            if (prevPiece != Piece.Empty)
                                score += ContinuationHistory[(int)prevPiece, _prevMove.To, (int)piece, move.To];
                        }
                    }
                }

                if (move.IsCastling)
                    score += 50;

                return score;
            }

            private int SelectBest(int start, int end)
            {
                int bestIdx = start;
                int bestScore = MoveScores[start];
                for (int i = start + 1; i < end; i++)
                {
                    if (MoveScores[i] > bestScore)
                    {
                        bestScore = MoveScores[i];
                        bestIdx = i;
                    }
                }
                return bestIdx;
            }

            private void SwapMoves(int a, int b)
            {
                if (a != b)
                {
                    (_moves[a], _moves[b]) = (_moves[b], _moves[a]);
                    (MoveScores[a], MoveScores[b]) = (MoveScores[b], MoveScores[a]);
                }
            }

            private static bool IsLegalMove(Board board, Move move)
            {
                Piece p = board.PieceAt(move.From);
                if (p == Piece.Empty) return false;
                
                bool white = board.WhiteToMove;
                bool isPieceWhite = (int)p <= 6;
                if (white != isPieceWhite) return false;

                board.MakeMove(move);
                bool legal = !board.IsKingInCheck(white);
                board.UndoMove(move);
                return legal;
            }

        }

        private static bool MovesEqual(Move a, Move b)
        {
            return a.From == b.From && a.To == b.To && a.Promotion == b.Promotion;
        }

        private static int GetPieceType(Piece piece)
        {
            if (piece == Piece.Empty) return 0;
            int p = (int)piece;
            return p <= 6 ? p : p - 6;
        }

        private static void OrderMoves(Board board, Move[] moves, int moveCount, Move ttMove, int ply)
        {
            for (int i = 0; i < moveCount; i++)
                MoveScores[i] = ScoreMove(board, moves[i], ttMove, ply);

            int sortLimit = moveCount - 1;
            for (int i = 0; i < sortLimit; i++)
            {
                int bestIdx = i;
                int bestScore = MoveScores[i];
                for (int j = i + 1; j < moveCount; j++)
                {
                    if (MoveScores[j] > bestScore)
                    {
                        bestScore = MoveScores[j];
                        bestIdx = j;
                    }
                }
                if (bestIdx != i)
                {
                    (moves[i], moves[bestIdx]) = (moves[bestIdx], moves[i]);
                    (MoveScores[i], MoveScores[bestIdx]) = (MoveScores[bestIdx], MoveScores[i]);
                }
            }
        }

        private static int ScoreMove(Board board, Move move, Move ttMove, int ply)
        {
            if (MovesEqual(move, ttMove))
                return 10_000_000;

            int score = 0;

            if (move.IsPromotion)
                score += 1_000_000 + Evaluator.GetPieceValue(move.Promotion);

            if (move.IsCapture)
            {
                Piece attacker = board.PieceAt(move.From);
                Piece victim = move.IsEnPassant
                    ? (board.WhiteToMove ? Piece.BP : Piece.WP)
                    : board.PieceAt(move.To);

                int victimValue = Evaluator.GetPieceValue(victim);
                int attackerValue = Evaluator.GetPieceValue(attacker);
                score += 500_000 + (victimValue * 10) - attackerValue;
            }
            else
            {
                if (ply < MaxPly)
                {
                    if (MovesEqual(move, Killers[ply, 0]))
                        return 400_000;
                    if (MovesEqual(move, Killers[ply, 1]))
                        return 300_000;
                }

                if (PreviousMove.From != PreviousMove.To)
                {
                    Piece prevPiece = board.PieceAt(PreviousMove.To);
                    if (prevPiece != Piece.Empty && MovesEqual(move, Counters[(int)prevPiece, PreviousMove.To]))
                        return 200_000;
                }

                Piece piece = board.PieceAt(move.From);
                if (piece != Piece.Empty)
                {
                    score += History[(int)piece, move.To];
                    
                    if (PreviousMove.From != PreviousMove.To)
                    {
                        Piece prevPiece = board.PieceAt(PreviousMove.To);
                        if (prevPiece != Piece.Empty)
                            score += ContinuationHistory[(int)prevPiece, PreviousMove.To, (int)piece, move.To];
                    }
                }
            }

            if (move.IsCastling)
                score += 50;

            return score;
        }
    }
}
