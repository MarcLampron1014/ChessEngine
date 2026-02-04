using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace ChessEngine
{
    public static class Uci
    {
        private const string EngineName = "ChessEngine";
        private const string EngineAuthor = "marcl";
        private const int DefaultHashSizeMB = 64;
        private const int DefaultThreads = 4;

        private static readonly TimeManager _timeManager = new TimeManager();
        private static Thread? _searchThread;
        private static Board? _goBoard;
        private static int _numThreads = DefaultThreads;
        private static bool _inPonderMode;

        public static void Run()
        {
            Console.Out.Flush();
            var board = new Board();

            while (true)
            {
                string? line = Console.ReadLine();
                if (line == null) break;

                line = line.Trim();
                if (line.Length == 0) continue;

                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                string cmd = parts[0].ToLowerInvariant();

                switch (cmd)
                {
                    case "uci":
                        WriteLineFlush($"id name {EngineName}");
                        WriteLineFlush($"id author {EngineAuthor}");
                        WriteLineFlush($"option name Hash type spin default {DefaultHashSizeMB} min 1 max 1024");
                        WriteLineFlush($"option name Threads type spin default {DefaultThreads} min 1 max 512");
                        WriteLineFlush("uciok");
                        break;
                    case "isready":
                        if (_searchThread?.IsAlive == true)
                        {
                            Search.RequestStop();
                            _searchThread.Join(5000);
                        }
                        WriteLineFlush("readyok");
                        break;
                    case "ucinewgame":
                        if (_searchThread?.IsAlive == true)
                        {
                            Search.RequestStop();
                            _searchThread.Join(5000);
                        }
                        board = new Board();
                        Search.ClearHash();
                        Evaluator.ClearCache();
                        break;
                    case "setoption":
                        HandleSetOption(parts);
                        break;
                    case "position":
                        if (_searchThread?.IsAlive == true)
                        {
                            Search.RequestStop();
                            _searchThread.Join(5000);
                        }
                        HandlePosition(board, parts);
                        break;
                    case "go":
                        if (_searchThread?.IsAlive == true)
                        {
                            Search.RequestStop();
                            _searchThread.Join(5000);
                        }
                        HandleGo(board, parts);
                        break;
                    case "stop":
                        Search.RequestStop();
                        if (_searchThread?.IsAlive == true)
                            _searchThread.Join(10000);
                        if (_inPonderMode)
                        {
                            _inPonderMode = false;
                            SendBestMoveFromLastResult(_goBoard, Search.GetLastResult());
                        }
                        break;
                    case "ponderhit":
                        _inPonderMode = false;
                        SendBestMoveFromLastResult(_goBoard, Search.GetLastResult());
                        break;
                    case "quit":
                        Search.RequestStop();
                        if (_searchThread?.IsAlive == true)
                            _searchThread.Join(2000);
                        return;
                }
            }
        }

        private static void HandleSetOption(string[] parts)
        {
            if (parts.Length < 5) return;

            int nameIdx = -1, valueIdx = -1;
            for (int i = 1; i < parts.Length; i++)
            {
                if (parts[i].Equals("name", StringComparison.OrdinalIgnoreCase))
                    nameIdx = i + 1;
                else if (parts[i].Equals("value", StringComparison.OrdinalIgnoreCase))
                    valueIdx = i + 1;
            }

            if (nameIdx < 0 || valueIdx < 0 || nameIdx >= parts.Length || valueIdx >= parts.Length)
                return;

            string optionName = parts[nameIdx];
            string optionValue = parts[valueIdx];

            if (optionName.Equals("Hash", StringComparison.OrdinalIgnoreCase) &&
                TryParseInt(optionValue, out int sizeMB))
            {
                Search.SetHashSize(Math.Max(1, Math.Min(1024, sizeMB)));
            }
            else if (optionName.Equals("Threads", StringComparison.OrdinalIgnoreCase) &&
                TryParseInt(optionValue, out int threads))
            {
                _numThreads = Math.Max(1, Math.Min(512, threads));
            }
        }

        private static void HandlePosition(Board board, string[] parts)
        {
            if (parts.Length < 2) return;

            int idx = 1;
            if (parts[idx] == "startpos")
            {
                board.Reset();
                idx++;
            }
            else if (parts[idx] == "fen")
            {
                if (parts.Length < idx + 1 + 6) return;
                string fen = string.Join(' ', parts, idx + 1, 6);
                Fen.Load(board, fen);
                idx += 7;
            }
            else
            {
                return;
            }

            if (idx < parts.Length && parts[idx] == "moves")
            {
                idx++;
                for (; idx < parts.Length; idx++)
                {
                    if (!TryApplyUciMove(board, parts[idx]))
                        break;
                }
            }
        }

        private static void HandleGo(Board board, string[] parts)
        {
            int movetime = -1, wtime = -1, btime = -1, winc = 0, binc = 0, movestogo = 0;
            bool infinite = false;
            bool ponder = false;

            for (int i = 1; i < parts.Length; i++)
            {
                string t = parts[i].ToLowerInvariant();
                if (t == "ponder")
                    ponder = true;
                if (i + 1 < parts.Length)
                {
                    if (t == "movetime" && TryParseInt(parts[i + 1], out int mt)) { movetime = mt; i++; }
                    else if (t == "wtime" && TryParseInt(parts[i + 1], out int wt)) { wtime = wt; i++; }
                    else if (t == "btime" && TryParseInt(parts[i + 1], out int bt)) { btime = bt; i++; }
                    else if (t == "winc" && TryParseInt(parts[i + 1], out int wi)) { winc = wi; i++; }
                    else if (t == "binc" && TryParseInt(parts[i + 1], out int bi)) { binc = bi; i++; }
                    else if (t == "movestogo" && TryParseInt(parts[i + 1], out int mtg)) { movestogo = mtg; i++; }
                }
                if (t == "infinite")
                    infinite = true;
            }

            _inPonderMode = ponder;
            _goBoard = board;
            InitializeTimeManager(board, movetime, wtime, btime, winc, binc, movestogo, infinite);

            _searchThread = new Thread(() =>
            {
                try
                {
                    var result = Search.FindBestMove(board, _timeManager, maxDepth: 64, numThreads: _numThreads);
                    if (!_inPonderMode)
                        SendBestMoveFromLastResult(board, result);
                }
                catch (Search.SearchTimeoutException)
                {
                    if (!_inPonderMode)
                        SendBestMoveFromLastResult(board, Search.GetLastResult());
                }
                catch (Exception)
                {
                    if (!_inPonderMode)
                        SendBestMoveFromLastResult(board, Search.GetLastResult());
                }
            });
            _searchThread.IsBackground = true;
            _searchThread.Start();
        }

        private static void SendBestMoveFromLastResult(Board? board, Search.SearchResult? result = null)
        {
            var r = result ?? Search.GetLastResult();
            Move best = r.BestMove;

            if (board != null)
            {
                var legal = MoveGenerator.GenerateLegalMoves(board);
                bool found = false;
                foreach (var m in legal)
                {
                    if (m.From == best.From && m.To == best.To && m.Promotion == best.Promotion)
                    {
                        found = true;
                        best = m;
                        break;
                    }
                }
                if (!found && legal.Count > 0)
                    best = legal[0];
            }

            string bestStr = best.From == best.To && best.Promotion == Piece.Empty ? "0000" : best.ToString();
            if (r.PonderMove.From != r.PonderMove.To)
            {
                string ponderStr = r.PonderMove.ToString();
                WriteLineFlush($"bestmove {bestStr} ponder {ponderStr}");
            }
            else
            {
                WriteLineFlush($"bestmove {bestStr}");
            }
        }

        private static void InitializeTimeManager(Board board, int movetime, int wtime, int btime,
                                                   int winc, int binc, int movestogo, bool infinite = false)
        {
            if (infinite)
            {
                _timeManager.InitializeFixedTime(300_000);
                return;
            }
            if (movetime > 0)
            {
                _timeManager.InitializeFixedTime(movetime);
                return;
            }

            int remaining = board.WhiteToMove ? wtime : btime;
            int increment = board.WhiteToMove ? winc : binc;

            if (remaining <= 0)
            {
                _timeManager.InitializeFixedTime(100);
                return;
            }

            GamePhase phase = TimeManager.DetectGamePhase(board);
            int rootMoveCount = MoveGenerator.GenerateLegalMoves(board).Count;
            bool isInCheck = board.IsKingInCheck(board.WhiteToMove);

            _timeManager.Initialize(remaining, increment, movestogo, phase, rootMoveCount, isInCheck);
        }

        private static void WriteLineFlush(string line)
        {
            Console.WriteLine(line);
            Console.Out.Flush();
        }

        private static bool TryApplyUciMove(Board board, string uciMove)
        {
            if (!TryParseUciMove(uciMove, board.WhiteToMove, out int from, out int to, out Piece promo))
                return false;

            List<Move> legal = MoveGenerator.GenerateLegalMoves(board);
            foreach (Move m in legal)
            {
                if (m.From == from && m.To == to && m.Promotion == promo)
                {
                    board.MakeMove(m);
                    return true;
                }
            }
            return false;
        }

        private static bool TryParseUciMove(string text, bool whiteToMove, out int from, out int to, out Piece promotion)
        {
            from = 0;
            to = 0;
            promotion = Piece.Empty;

            if (text.Length < 4) return false;
            if (!TryParseSquare(text.AsSpan(0, 2), out from)) return false;
            if (!TryParseSquare(text.AsSpan(2, 2), out to)) return false;

            if (text.Length >= 5)
            {
                promotion = char.ToLowerInvariant(text[4]) switch
                {
                    'q' => whiteToMove ? Piece.WQ : Piece.BQ,
                    'r' => whiteToMove ? Piece.WR : Piece.BR,
                    'b' => whiteToMove ? Piece.WB : Piece.BB,
                    'n' => whiteToMove ? Piece.WN : Piece.BN,
                    _ => Piece.Empty
                };
            }
            return true;
        }

        private static bool TryParseSquare(ReadOnlySpan<char> sq, out int square)
        {
            square = 0;
            if (sq.Length != 2) return false;

            char fileChar = sq[0];
            char rankChar = sq[1];

            if (fileChar < 'a' || fileChar > 'h') return false;
            if (rankChar < '1' || rankChar > '8') return false;

            square = (rankChar - '1') * 8 + (fileChar - 'a');
            return true;
        }

        private static bool TryParseInt(string s, out int value)
        {
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }
}
