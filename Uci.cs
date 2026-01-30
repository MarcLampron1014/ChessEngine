using System;
using System.Collections.Generic;
using System.Globalization;

namespace ChessEngine
{
    public static class Uci
    {
        private const string EngineName = "ChessEngine";
        private const string EngineAuthor = "marcl";
        private const int DefaultHashSizeMB = 64;

        private static readonly TimeManager _timeManager = new TimeManager();

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
                string cmd = parts[0];

                switch (cmd)
                {
                    case "uci":
                        WriteLineFlush($"id name {EngineName}");
                        WriteLineFlush($"id author {EngineAuthor}");
                        WriteLineFlush($"option name Hash type spin default {DefaultHashSizeMB} min 1 max 1024");
                        WriteLineFlush("uciok");
                        break;
                    case "isready":
                        WriteLineFlush("readyok");
                        break;
                    case "ucinewgame":
                        board = new Board();
                        Search.ClearHash();
                        break;
                    case "setoption":
                        HandleSetOption(parts);
                        break;
                    case "position":
                        HandlePosition(board, parts);
                        break;
                    case "go":
                        HandleGo(board, parts);
                        break;
                    case "quit":
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

            for (int i = 1; i < parts.Length; i++)
            {
                if (i + 1 >= parts.Length) continue;

                string t = parts[i];
                if (t == "movetime" && TryParseInt(parts[i + 1], out int mt)) { movetime = mt; i++; }
                else if (t == "wtime" && TryParseInt(parts[i + 1], out int wt)) { wtime = wt; i++; }
                else if (t == "btime" && TryParseInt(parts[i + 1], out int bt)) { btime = bt; i++; }
                else if (t == "winc" && TryParseInt(parts[i + 1], out int wi)) { winc = wi; i++; }
                else if (t == "binc" && TryParseInt(parts[i + 1], out int bi)) { binc = bi; i++; }
                else if (t == "movestogo" && TryParseInt(parts[i + 1], out int mtg)) { movestogo = mtg; i++; }
            }

            InitializeTimeManager(board, movetime, wtime, btime, winc, binc, movestogo);
            var result = Search.FindBestMove(board, _timeManager, maxDepth: 64);

            if (!result.BestMove.Equals(default(Move)))
                board.MakeMove(result.BestMove);

            string best = result.BestMove.Equals(default(Move)) ? "0000" : result.BestMove.ToString();
            WriteLineFlush($"bestmove {best}");
        }

        private static void InitializeTimeManager(Board board, int movetime, int wtime, int btime,
                                                   int winc, int binc, int movestogo)
        {
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
