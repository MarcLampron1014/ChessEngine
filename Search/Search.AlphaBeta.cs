using System;
using System.Threading;

namespace ChessEngine
{
    public static partial class Search
    {
        private const int RazorBaseMargin = 150;
        private const int RazorDepthMargin = 100;
        private const int NullMoveVerifyPhaseThreshold = 12;
        private const int ProbCutMargin = 100;
        private const int SingularExtensionMargin = 50;
        private const int QuiesceSEEThreshold = -150;
        private const int MaxCheckingMovesInQuiesce = 3;
        private static readonly int[] FutilityMargins = { 0, 200, 400, 600, 900 };

        private static int AlphaBeta(Board board, int depth, int alpha, int beta, int ply, bool isNullMove, Move excludeMove = default)
        {
            CheckTime();

            if (ply > 0)
            {
                if (board.IsRepetition() || board.IsFiftyMoveRule())
                {
                    return 0;
                }
                if (Evaluator.IsInsufficientMaterial(board))
                {
                    return 0;
                }
            }

            int matingScore = MateScore - ply;
            if (matingScore < beta)
            {
                beta = matingScore;
                if (alpha >= matingScore)
                {
                    return matingScore;
                }
            }
            int matedScore = -MateScore + ply;
            if (matedScore > alpha)
            {
                alpha = matedScore;
                if (beta <= matedScore)
                {
                    return matedScore;
                }
            }

            bool inCheck = board.IsKingInCheck(board.WhiteToMove);
            if (inCheck)
            {
                depth++;
            }

            if (depth <= 0)
            {
                return Quiesce(board, alpha, beta, ply);
            }

            bool isPV = beta - alpha > 1;

            if (_tt.Probe(board.ZobristHash, depth, alpha, beta, ply, out int ttScore, out Move ttMove))
            {
                if (!isPV)
                {
                    Interlocked.Increment(ref _ttCutoffs);
                    return ttScore;
                }
            }

            int staticEval = Evaluator.Evaluate(board);

            const int LosingMargin = 200;
            const int ReverseFutilityMarginPerDepth = 80;

            if (!isPV && !inCheck && depth <= 7
                && staticEval - ReverseFutilityMarginPerDepth * depth >= beta
                && Math.Abs(beta) < MateScore - 1000)
            {
                return staticEval;
            }

            // Razoring
            if (!isPV && !inCheck && depth <= 3)
            {
                int razorMargin = RazorBaseMargin + RazorDepthMargin * depth;
                if (staticEval + razorMargin < alpha)
                {
                    int razorScore = Quiesce(board, alpha, beta, ply);
                    if (razorScore < alpha)
                    {
                        return razorScore;
                    }
                }
            }

            // Null move pruning
            if (!isNullMove && !inCheck && depth >= 3 && HasNonPawnMaterial(board))
            {
                Interlocked.Increment(ref _nullMoveAttempts);
                int R = 3 + depth / 5;
                R = Math.Min(R, depth - 1);

                board.MakeNullMove();
                int nullScore = -AlphaBeta(board, depth - R, -beta, -beta + 1, ply + 1, true);
                board.UndoNullMove();

                if (nullScore >= beta)
                {
                    if (board.Phase <= NullMoveVerifyPhaseThreshold && depth >= 4)
                    {
                        int verifyScore = AlphaBeta(board, depth - R, beta - 1, beta, ply, false);
                        if (verifyScore < beta)
                        {
                            goto SkipNullMoveCutoff;
                        }
                    }
                    Interlocked.Increment(ref _nullMoveCutoffs);
                    return beta;
                }
            }
            SkipNullMoveCutoff:

            // ProbCut
            if (!isPV && !inCheck && depth >= 5 && Math.Abs(beta) < MateScore - ProbCutMargin)
            {
                int probCutBeta = beta + ProbCutMargin;
                var probPicker = new MovePicker(board, MoveStacks[ply], ttMove, ply);
                while (probPicker.NextMove(out Move pcMove, out _))
                {
                    if (!pcMove.IsCapture)
                    {
                        continue;
                    }
                    if (SEE(board, pcMove) < 0)
                    {
                        continue;
                    }

                    board.MakeMove(pcMove);
                    int pcScore = -AlphaBeta(board, depth - 4, -probCutBeta, -probCutBeta + 1, ply + 1, false);
                    board.UndoMove(pcMove);

                    if (pcScore >= probCutBeta)
                    {
                        return beta;
                    }
                }
            }

            // Internal Iterative Reduction
            if (ttMove.From == ttMove.To && depth >= 4)
            {
                depth--;
            }

            // Singular extension
            int singularExtension = 0;
            bool singularSearchInProgress = excludeMove.From != excludeMove.To;
            if (!singularSearchInProgress && depth >= 8 && isPV
                && ttMove.From != ttMove.To
                && _tt.ProbeForSingular(board.ZobristHash, depth, out int seScore, out TTFlag seFlag)
                && (seFlag == TTFlag.Beta || seFlag == TTFlag.Exact))
            {
                int seBeta = seScore - SingularExtensionMargin;
                int seDepth = depth / 2;
                int seResult = AlphaBeta(board, seDepth, seBeta - 1, seBeta, ply, false, ttMove);
                if (seResult < seBeta)
                {
                    singularExtension = 1;
                }
            }

            int originalAlpha = alpha;
            Move bestMove = default;
            int movesSearched = 0;

            const int ImprovingMargin = 50;
            bool improving = staticEval >= alpha - ImprovingMargin;
            bool losing = staticEval < alpha - LosingMargin;

            bool canFutilityPrune = !inCheck && !isPV && !losing && depth <= 4 && depth < FutilityMargins.Length && staticEval + FutilityMargins[depth] < alpha;

            Move prevMove = PreviousMove;
            var picker = new MovePicker(board, MoveStacks[ply], ttMove, ply, prevMove);

            var triedQuiets = TriedQuietsBuffer;
            int triedQuietCount = 0;

            while (picker.NextMove(out Move move, out _))
            {
                if (singularSearchInProgress && MovesEqual(move, excludeMove))
                {
                    continue;
                }

                bool isQuiet = !move.IsCapture && !move.IsPromotion;
                bool isTTMove = MovesEqual(move, ttMove);

                // LMP
                if (!isPV && !inCheck && depth <= 6 && movesSearched > 0 && isQuiet && !improving && !losing)
                {
                    if (depth < LMPThresholds.Length && movesSearched >= LMPThresholds[depth])
                    {
                        continue;
                    }
                }

                if (canFutilityPrune && movesSearched > 0 && isQuiet)
                {
                    continue;
                }

                // SEE pruning
                if (move.IsCapture && !move.IsPromotion && movesSearched > 0 && depth <= 1)
                {
                    if (SEE(board, move) < 0)
                    {
                        continue;
                    }
                }
                if (move.IsCapture && !move.IsPromotion && movesSearched > 0 && depth >= 2 && depth <= 3)
                {
                    if (SEE(board, move) < -100)
                    {
                        continue;
                    }
                }

                int extension = (isTTMove && singularExtension > 0) ? singularExtension : 0;
                bool isRecapture = prevMove.From != prevMove.To && prevMove.IsCapture && move.IsCapture && move.To == prevMove.To;
                if (isRecapture)
                {
                    extension++;
                }
                if (extension == 0 && move.IsCapture && !move.IsPromotion && depth >= 8 && SEE(board, move) >= 200)
                {
                    extension++;
                }
                
                // Passed pawn extension
                if (extension == 0 && !move.IsCapture && !move.IsPromotion)
                {
                    Piece movePiece = board.PieceAt(move.From);
                    if (movePiece == Piece.WP || movePiece == Piece.BP)
                    {
                        bool isWhitePawn = movePiece == Piece.WP;
                        int toRank = isWhitePawn ? Bitboard.RankOf(move.To) : 7 - Bitboard.RankOf(move.To);
                        if (toRank >= 5 && Evaluator.IsPassedPawn(board, move.From, isWhitePawn))
                        {
                            extension = 1;
                        }
                    }
                }
                int newDepth = depth - 1 + extension;

                bool isBadCapture = move.IsCapture && !move.IsPromotion && SEE(board, move) < 0;

                if (isQuiet && triedQuietCount < TriedQuietsMax)
                {
                    triedQuiets[triedQuietCount++] = move;
                }

                PreviousMove = move;
                board.MakeMove(move);
                bool givesCheck = board.IsKingInCheck(board.WhiteToMove);

                // LMR: apply to quiet moves and also to bad captures
                int reduction = 0;
                if (depth >= 3 && movesSearched >= 3 && !isPV && (isQuiet || isBadCapture) && !inCheck && !givesCheck && !isRecapture)
                {
                    reduction = 1 + (depth / 5) + (movesSearched / 8);
                    if (isBadCapture)
                        reduction = Math.Max(1, reduction - 1);
                    reduction = Math.Min(reduction, depth - 2);
                }

                int score;
                if (movesSearched == 0)
                {
                    score = -AlphaBeta(board, newDepth, -beta, -alpha, ply + 1, false);
                }
                else
                {
                    score = -AlphaBeta(board, newDepth - reduction, -alpha - 1, -alpha, ply + 1, false);
                    if (reduction > 0 && score > alpha)
                    {
                        Interlocked.Increment(ref _lmrResearches);
                        score = -AlphaBeta(board, newDepth, -alpha - 1, -alpha, ply + 1, false);
                    }
                    if (score > alpha && score < beta)
                    {
                        score = -AlphaBeta(board, newDepth, -beta, -alpha, ply + 1, false);
                    }
                }

                board.UndoMove(move);
                movesSearched++;

                if (score >= beta)
                {
                    Interlocked.Increment(ref _betaCutoffs);
                    _tt.Store(board.ZobristHash, depth, beta, TTFlag.Beta, move, ply);
                    if (isQuiet && ply < MaxPly)
                    {
                        if (!MovesEqual(Killers[ply, 0], move))
                        {
                            Killers[ply, 1] = Killers[ply, 0];
                            Killers[ply, 0] = move;
                        }
                    }
                    if (prevMove.From != prevMove.To && isQuiet)
                    {
                        Piece prevPiece = board.PieceAt(prevMove.To);
                        if (prevPiece != Piece.Empty)
                        {
                            Counters[(int)prevPiece, prevMove.To] = move;
                        }
                    }
                    if (isQuiet)
                    {
                        Piece piece = board.PieceAt(move.From);
                        if (piece == Piece.Empty)
                            piece = board.PieceAt(move.To);
                        History[(int)piece, move.To] += depth * depth;
                        
                        if (prevMove.From != prevMove.To)
                        {
                            Piece prevPiece = board.PieceAt(prevMove.To);
                            if (prevPiece != Piece.Empty)
                            {
                                ContinuationHistory[(int)prevPiece, prevMove.To, (int)piece, move.To] += depth * depth;
                            }
                        }

                        int malus = -(depth * depth);
                        for (int qi = 0; qi < triedQuietCount - 1; qi++)
                        {
                            Move tried = triedQuiets[qi];
                            Piece triedPiece = board.PieceAt(tried.From);
                            if (triedPiece == Piece.Empty)
                                triedPiece = board.PieceAt(tried.To);
                            if (triedPiece != Piece.Empty)
                            {
                                History[(int)triedPiece, tried.To] += malus;

                                if (prevMove.From != prevMove.To)
                                {
                                    Piece pp = board.PieceAt(prevMove.To);
                                    if (pp != Piece.Empty)
                                        ContinuationHistory[(int)pp, prevMove.To, (int)triedPiece, tried.To] += malus;
                                }
                            }
                        }
                    }
                    else if (move.IsCapture)
                    {
                        Piece attacker = board.PieceAt(move.From);
                        Piece victim = move.IsEnPassant
                            ? (board.WhiteToMove ? Piece.BP : Piece.WP)
                            : board.PieceAt(move.To);
                        int capturedType = GetPieceType(victim);
                        CaptureHistory[(int)attacker, move.To, capturedType] += depth * depth;
                    }
                    PreviousMove = prevMove;
                    return beta;
                }

                if (score > alpha)
                {
                    alpha = score;
                    bestMove = move;
                    if (isQuiet)
                    {
                        Piece piece = board.PieceAt(move.From);
                        if (piece == Piece.Empty)
                        {
                            piece = board.PieceAt(move.To);
                        }
                        History[(int)piece, move.To] += depth;
                        
                        if (prevMove.From != prevMove.To)
                        {
                            Piece prevPiece = board.PieceAt(prevMove.To);
                            if (prevPiece != Piece.Empty)
                            {
                                ContinuationHistory[(int)prevPiece, prevMove.To, (int)piece, move.To] += depth;
                            }
                        }
                    }
                    else if (move.IsCapture)
                    {
                        Piece attacker = board.PieceAt(move.From);
                        Piece victim = move.IsEnPassant
                            ? (board.WhiteToMove ? Piece.BP : Piece.WP)
                            : board.PieceAt(move.To);
                        int capturedType = GetPieceType(victim);
                        CaptureHistory[(int)attacker, move.To, capturedType] += depth;
                    }
                }
            }

            PreviousMove = prevMove;
            if (movesSearched == 0)
            {
                return inCheck ? -MateScore + ply : 0;
            }
            TTFlag flag = alpha > originalAlpha ? TTFlag.Exact : TTFlag.Alpha;
            _tt.Store(board.ZobristHash, depth, alpha, flag, bestMove, ply);
            return alpha;
        }

        private static int Quiesce(Board board, int alpha, int beta, int ply)
        {
            CheckTime();

            if (board.IsRepetition() || board.IsFiftyMoveRule())
            {
                return 0;
            }
            if (Evaluator.IsInsufficientMaterial(board))
            {
                return 0;
            }

            bool isPV = beta - alpha > 1;

            if (_tt.Probe(board.ZobristHash, 0, alpha, beta, ply, out int ttScore, out Move ttMove))
            {
                if (!isPV)
                    return ttScore;
            }

            bool inCheck = board.IsKingInCheck(board.WhiteToMove);
            int standPat = Evaluator.Evaluate(board);

            if (inCheck)
            {
                int qPly = Math.Min(ply, MaxPly - 1);
                Move[] moves = MoveStacks[qPly];
                int moveCount = MoveGenerator.GenerateLegalMoves(board, moves);
                
                if (moveCount == 0)
                {
                    return -MateScore + ply;
                }
                
                OrderMoves(board, moves, moveCount, ttMove, qPly);

                int origAlpha = alpha;
                Move bestMove = default;

                for (int i = 0; i < moveCount; i++)
                {
                    Move move = moves[i];
                    board.MakeMove(move);
                    int score = -Quiesce(board, -beta, -alpha, ply + 1);
                    board.UndoMove(move);

                    if (score >= beta)
                    {
                        _tt.Store(board.ZobristHash, 0, beta, TTFlag.Beta, move, ply);
                        return beta;
                    }
                    if (score > alpha)
                    {
                        alpha = score;
                        bestMove = move;
                    }
                }
                TTFlag flag = alpha > origAlpha ? TTFlag.Exact : TTFlag.Alpha;
                _tt.Store(board.ZobristHash, 0, alpha, flag, bestMove, ply);
                return alpha;
            }

            if (standPat >= beta)
            {
                _tt.Store(board.ZobristHash, 0, beta, TTFlag.Beta, default, ply);
                return beta;
            }
            if (standPat > alpha)
            {
                alpha = standPat;
            }

            int origAlpha2 = alpha;
            Move bestCapture = default;

            int qPly2 = Math.Min(ply, MaxPly - 1);
            Move[] captureMoves = MoveStacks[qPly2];
            int noisyCount = MoveGenerator.GenerateLegalCaptures(board, captureMoves);

            if (noisyCount > 0)
            {
                OrderMoves(board, captureMoves, noisyCount, ttMove, qPly2);
            }

            const int DeltaMargin = 350;

            for (int i = 0; i < noisyCount; i++)
            {
                Move move = captureMoves[i];

                if (!move.IsPromotion)
                {
                    Piece victim = move.IsEnPassant
                        ? (board.WhiteToMove ? Piece.BP : Piece.WP)
                        : board.PieceAt(move.To);

                    int captureValue = Evaluator.GetPieceValue(victim);
                    if (standPat + captureValue + DeltaMargin < alpha)
                    {
                        continue;
                    }
                }

                if (move.IsCapture && !move.IsPromotion)
                {
                    int seeScore = SEE(board, move);
                    if (seeScore < QuiesceSEEThreshold)
                    {
                        continue;
                    }
                }

                board.MakeMove(move);
                int score = -Quiesce(board, -beta, -alpha, ply + 1);
                board.UndoMove(move);

                if (score >= beta)
                {
                    _tt.Store(board.ZobristHash, 0, beta, TTFlag.Beta, move, ply);
                    return beta;
                }
                if (score > alpha)
                {
                    alpha = score;
                    bestCapture = move;
                }
            }

            if (alpha > origAlpha2)
                _tt.Store(board.ZobristHash, 0, alpha, TTFlag.Exact, bestCapture, ply);

            return alpha;
        }
    }
}
