using System;
using System.Collections.Generic;
using System.Globalization;

namespace ChessEngine
{
    public static class Uci
    {
        private const string EngineName = "ChessEngine";
        private const string EngineAuthor = "marcl";

        public static void Run()
        {
            var board = new Board();

            while (true)
            {
                string? line = Console.ReadLine();
                if (line == null)
                    break;

                line = line.Trim();
                if (line.Length == 0)
                    continue;

                // Tokenize (UCI is whitespace-delimited).
                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string cmd = parts[0];

                switch (cmd)
                {
                    case "uci":
                        Console.WriteLine($"id name {EngineName}");
                        Console.WriteLine($"id author {EngineAuthor}");
                        // No options yet
                        Console.WriteLine("uciok");
                        break;

                    case "isready":
                        Console.WriteLine("readyok");
                        break;

                    case "ucinewgame":
                        board = new Board();
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

        private static void HandlePosition(Board board, string[] parts)
        {
            // position startpos [moves ...]
            // position fen <fen> [moves ...]

            if (parts.Length < 2)
                return;

            int idx = 1;
            if (parts[idx] == "startpos")
            {
                board.Reset();
                idx++;
            }
            else if (parts[idx] == "fen")
            {
                // FEN has 6 space-separated fields.
                // We'll rebuild them from the token stream.
                if (parts.Length < idx + 1 + 6)
                    return;

                string fen = string.Join(' ', parts, idx + 1, 6);
                Fen.Load(board, fen);
                idx += 7; // fen + 6 fields
            }
            else
            {
                // Unknown position format
                return;
            }

            // Optional moves
            if (idx < parts.Length && parts[idx] == "moves")
            {
                idx++;
                for (; idx < parts.Length; idx++)
                {
                    string uciMove = parts[idx];
                    if (!TryApplyUciMove(board, uciMove))
                    {
                        // If a move can't be applied, stop applying further moves.
                        break;
                    }
                }
            }
        }

        private static void HandleGo(Board board, string[] parts)
        {
            // Common Arena patterns:
            // go movetime 1000
            // go wtime 300000 btime 300000 winc 0 binc 0
            // (may include extra tokens like depth, nodes, etc. which we ignore)

            int movetime = -1;
            int wtime = -1, btime = -1, winc = 0, binc = 0;

            for (int i = 1; i < parts.Length; i++)
            {
                string t = parts[i];
                if (i + 1 >= parts.Length)
                    continue;

                if (t == "movetime" && TryParseInt(parts[i + 1], out int mt))
                {
                    movetime = mt;
                    i++;
                }
                else if (t == "wtime" && TryParseInt(parts[i + 1], out int wt))
                {
                    wtime = wt;
                    i++;
                }
                else if (t == "btime" && TryParseInt(parts[i + 1], out int bt))
                {
                    btime = bt;
                    i++;
                }
                else if (t == "winc" && TryParseInt(parts[i + 1], out int wi))
                {
                    winc = wi;
                    i++;
                }
                else if (t == "binc" && TryParseInt(parts[i + 1], out int bi))
                {
                    binc = bi;
                    i++;
                }
            }

            int timeMs = ComputeTimeBudgetMs(board.WhiteToMove, movetime, wtime, btime, winc, binc);
            var result = Search.FindBestMove(board, timeMs, maxDepth: 64);

            // Apply best move to internal board state (engine assumes it plays the side-to-move).
            if (!result.BestMove.Equals(default(Move)))
                board.MakeMove(result.BestMove);

            // UCI requires a bestmove line even if we have none (use 0000 as null move).
            string best = result.DepthReached == 0 ? "0000" : result.BestMove.ToString();
            Console.WriteLine($"bestmove {best}");
        }

        private static int ComputeTimeBudgetMs(bool whiteToMove, int movetime, int wtime, int btime, int winc, int binc)
        {
            // Safety margin to avoid flagging on time.
            const int safety = 20;

            if (movetime > 0)
                return Math.Max(1, movetime - safety);

            int remaining = whiteToMove ? wtime : btime;
            int inc = whiteToMove ? winc : binc;

            if (remaining <= 0)
                return 100; // fallback

            // Very simple allocation:
            // use ~1/30th of remaining + a chunk of increment, capped.
            int slice = remaining / 30;
            int budget = slice + (inc * 8 / 10);

            // Cap to a reasonable fraction of remaining time.
            int cap = Math.Max(50, remaining / 5);
            budget = Math.Min(budget, cap);

            return Math.Max(1, budget - safety);
        }

        private static bool TryApplyUciMove(Board board, string uciMove)
        {
            if (!TryParseUciMove(uciMove, board.WhiteToMove, out int from, out int to, out Piece promo))
                return false;

            List<Move> legal = MoveGenerator.GenerateLegalMoves(board);
            for (int i = 0; i < legal.Count; i++)
            {
                Move m = legal[i];
                if (m.From != from || m.To != to)
                    continue;

                // Promotions must match exactly.
                if (m.Promotion != promo)
                    continue;

                board.MakeMove(m);
                return true;
            }

            return false;
        }

        private static bool TryParseUciMove(string text, bool whiteToMove, out int from, out int to, out Piece promotion)
        {
            from = 0;
            to = 0;
            promotion = Piece.Empty;

            if (text.Length < 4)
                return false;

            if (!TryParseSquare(text.AsSpan(0, 2), out from))
                return false;
            if (!TryParseSquare(text.AsSpan(2, 2), out to))
                return false;

            if (text.Length >= 5)
            {
                char pc = char.ToLowerInvariant(text[4]);
                promotion = pc switch
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
            if (sq.Length != 2)
                return false;

            char fileChar = sq[0];
            char rankChar = sq[1];

            if (fileChar < 'a' || fileChar > 'h')
                return false;
            if (rankChar < '1' || rankChar > '8')
                return false;

            int file = fileChar - 'a';
            int rank = rankChar - '1';
            square = rank * 8 + file;
            return true;
        }

        private static bool TryParseInt(string s, out int value)
        {
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }
}
