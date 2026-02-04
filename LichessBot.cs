using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ChessEngine
{
    public static class LichessBot
    {
        private const string EventStreamUrl = "https://lichess.org/api/stream/event";
        private const string BaseUrl = "https://lichess.org";

        public static string? LoadToken()
        {
            var token = Environment.GetEnvironmentVariable("LICHESS_BOT_TOKEN");
            if (!string.IsNullOrWhiteSpace(token))
                token = token.Trim();
            else
            {
                var searchDirs = GetTokenSearchDirectories();

                foreach (var dir in searchDirs)
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    var path = Path.Combine(dir, "lichess_token.txt");
                    if (File.Exists(path))
                    {
                        try
                        {
                            var line = File.ReadAllText(path).Trim();
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                token = line.Split('\n')[0].Trim();
                                break;
                            }
                        }
                        catch { /* ignore */ }
                    }
                }
            }

            return IsValidToken(token) ? token : null;
        }

        private static string[] GetTokenSearchDirectories()
        {
            var list = new List<string>();

            var current = Directory.GetCurrentDirectory();
            if (!string.IsNullOrEmpty(current))
                list.Add(current);

            var baseDir = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDir))
            {
                var exeDir = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!list.Contains(exeDir))
                    list.Add(exeDir);

                for (var dir = Path.GetDirectoryName(exeDir); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
                {
                    var normalized = dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (!list.Contains(normalized))
                        list.Add(normalized);
                }
            }

            return list.ToArray();
        }

        private static bool IsValidToken(string? token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            if (token.Contains(' ')) return false;
            if (token.Contains("Paste", StringComparison.OrdinalIgnoreCase) ||
                token.Contains("Save as", StringComparison.OrdinalIgnoreCase) ||
                token.Contains("lichess.org/account", StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }

        public static void Run(string token)
        {
            RunAsync(token).GetAwaiter().GetResult();
        }

        public static async Task RunAsync(string token)
        {
            using var client = new HttpClient();
            client.BaseAddress = new Uri(BaseUrl);
            client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);

            Console.WriteLine("Lichess bot: connected. Waiting for events...");

            while (true)
            {
                try
                {
                    await StreamEventsAsync(client, token);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Event stream error: {ex.Message}. Reconnecting in 5s...");
                    await Task.Delay(5000);
                }
            }
        }

        private static async Task StreamEventsAsync(HttpClient client, string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, EventStreamUrl);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    var type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

                    switch (type)
                    {
                        case "challenge":
                            if (root.TryGetProperty("challenge", out var ch) && ch.TryGetProperty("id", out var chId))
                                _ = Task.Run(() => AcceptChallengeAsync(client, chId.GetString()!));
                            break;
                        case "gameStart":
                            if (root.TryGetProperty("game", out var game) && game.TryGetProperty("id", out var gameId))
                                await PlayGameAsync(client, gameId.GetString()!, game);
                            break;
                        case "gameFinish":
                            break;
                    }
                }
                catch (JsonException) { /* skip malformed line */ }
            }
        }

        private static async Task AcceptChallengeAsync(HttpClient client, string challengeId)
        {
            try
            {
                var res = await client.PostAsync($"/api/challenge/{challengeId}/accept", null);
                if (res.IsSuccessStatusCode)
                    Console.WriteLine($"Accepted challenge {challengeId}");
                else
                    Console.WriteLine($"Accept challenge {challengeId}: {res.StatusCode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Accept challenge error: {ex.Message}");
            }
        }

        private static async Task PlayGameAsync(HttpClient client, string gameId, JsonElement gameStartGame)
        {
            var ourColor = gameStartGame.TryGetProperty("color", out var c) ? c.GetString() : "white";
            var isWhite = string.Equals(ourColor, "white", StringComparison.OrdinalIgnoreCase);

            var enginePath = GetEnginePath();
            if (string.IsNullOrEmpty(enginePath) || !File.Exists(enginePath))
            {
                Console.WriteLine("Engine executable not found. Run the published exe from bin/Release/.../publish/.");
                return;
            }

            var workingDir = Path.GetDirectoryName(enginePath) ?? Directory.GetCurrentDirectory();
            var psi = new ProcessStartInfo
            {
                FileName = enginePath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDir
            };

            Process? process = null;
            try
            {
                process = Process.Start(psi);
                if (process == null) return;

                var stdin = process.StandardInput;
                var stdout = process.StandardOutput;

                stdin.WriteLine("uci");
                await ReadUntilAsync(stdout, "uciok");
                stdin.WriteLine("isready");
                await ReadUntilAsync(stdout, "readyok");

                var pendingBestLock = new object();
                var pendingBestHolder = new TaskCompletionSource<(string best, string? ponder)>?[1];
                var readerTask = Task.Run(() => EngineReader(stdout, pendingBestLock, pendingBestHolder));

                string? positionFen = null;
                string moves = "";
                int wtime = 60000, btime = 60000, winc = 0, binc = 0;
                string? status = null;
                bool weArePondering = false;
                string? expectedPonderMove = null;

                var streamUrl = $"/api/bot/game/stream/{gameId}";
                using var request = new HttpRequestMessage(HttpMethod.Get, streamUrl);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);

                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;

                        bool lineHadPositionUpdate = false;
                        if (root.TryGetProperty("state", out var state))
                        {
                            if (state.TryGetProperty("moves", out var m)) { moves = m.GetString() ?? ""; lineHadPositionUpdate = true; }
                            if (state.TryGetProperty("fen", out var f)) { positionFen = f.GetString(); lineHadPositionUpdate = true; }
                            if (state.TryGetProperty("wtime", out var wt)) wtime = wt.GetInt32();
                            if (state.TryGetProperty("btime", out var bt)) btime = bt.GetInt32();
                            if (state.TryGetProperty("winc", out var wi)) winc = wi.GetInt32();
                            if (state.TryGetProperty("binc", out var bi)) binc = bi.GetInt32();
                        }
                        else
                        {
                            if (root.TryGetProperty("moves", out var m)) { moves = m.GetString() ?? ""; lineHadPositionUpdate = true; }
                            if (root.TryGetProperty("fen", out var f)) { positionFen = f.GetString(); lineHadPositionUpdate = true; }
                            if (root.TryGetProperty("wtime", out var wt)) wtime = wt.GetInt32();
                            if (root.TryGetProperty("btime", out var bt)) btime = bt.GetInt32();
                            if (root.TryGetProperty("winc", out var wi)) winc = wi.GetInt32();
                            if (root.TryGetProperty("binc", out var bi)) binc = bi.GetInt32();
                        }

                        if (root.TryGetProperty("status", out var st)) status = st.GetString();

                        if (!string.IsNullOrEmpty(status) && (status == "mate" || status == "resign" || status == "draw" || status == "stalemate" || status == "finished"))
                            break;

                        var moveTokens = moves.Trim().Length == 0 ? Array.Empty<string>() : moves.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var moveCount = moveTokens.Length;
                        var ourTurn = isWhite ? (moveCount % 2 == 0) : (moveCount % 2 == 1);

                        if (ourTurn && lineHadPositionUpdate)
                        {
                            string? bestMove;
                            string? ponderMove;

                            if (weArePondering && moveCount > 0)
                            {
                                var lastMove = moveTokens[moveTokens.Length - 1];
                                if (lastMove == expectedPonderMove)
                                {
                                    lock (pendingBestLock)
                                    {
                                        pendingBestHolder[0] = new TaskCompletionSource<(string, string?)>();
                                    }
                                    stdin.WriteLine("ponderhit");
                                    (bestMove, ponderMove) = await pendingBestHolder[0]!.Task;
                                }
                                else
                                {
                                    lock (pendingBestLock)
                                    {
                                        pendingBestHolder[0] = new TaskCompletionSource<(string, string?)>();
                                    }
                                    stdin.WriteLine("stop");
                                    await pendingBestHolder[0]!.Task;
                                    var posCmd = BuildPositionCommand(positionFen, moves);
                                    stdin.WriteLine(posCmd);
                                    var goCmd = $"go wtime {wtime} btime {btime} winc {winc} binc {binc}";
                                    stdin.WriteLine(goCmd);
                                    lock (pendingBestLock)
                                    {
                                        pendingBestHolder[0] = new TaskCompletionSource<(string, string?)>();
                                    }
                                    (bestMove, ponderMove) = await pendingBestHolder[0]!.Task;
                                }
                            }
                            else
                            {
                                var posCmd = BuildPositionCommand(positionFen, moves);
                                stdin.WriteLine(posCmd);
                                var goCmd = $"go wtime {wtime} btime {btime} winc {winc} binc {binc}";
                                stdin.WriteLine(goCmd);
                                lock (pendingBestLock)
                                {
                                    pendingBestHolder[0] = new TaskCompletionSource<(string, string?)>();
                                }
                                (bestMove, ponderMove) = await pendingBestHolder[0]!.Task;
                            }

                            if (string.IsNullOrEmpty(bestMove) || bestMove == "(none)" || bestMove == "0000")
                            {
                                await ResignAsync(client, gameId);
                                break;
                            }

                            if (!Uci.IsMoveLegalInPosition(positionFen, moves, bestMove))
                            {
                                Console.WriteLine($"Lichess: skipping illegal move {bestMove} in position (desync or engine bug)");
                                break;
                            }

                            var moveOk = await SendMoveAsync(client, gameId, bestMove);
                            if (!moveOk)
                                break;

                            if (!string.IsNullOrEmpty(ponderMove) && ponderMove != "(none)" && ponderMove != "0000")
                            {
                                var movesWithOurs = string.IsNullOrEmpty(moves) ? bestMove : moves + " " + bestMove;
                                var movesWithPonder = movesWithOurs + " " + ponderMove;
                                var ponderPosCmd = BuildPositionCommand(positionFen, movesWithPonder);
                                stdin.WriteLine(ponderPosCmd);
                                var ponderGoCmd = $"go ponder wtime {wtime} btime {btime} winc {winc} binc {binc}";
                                stdin.WriteLine(ponderGoCmd);
                                expectedPonderMove = ponderMove;
                                weArePondering = true;
                            }
                            else
                            {
                                weArePondering = false;
                                expectedPonderMove = null;
                            }
                        }
                    }
                    catch (JsonException) { /* skip */ }
                }
            }
            finally
            {
                try
                {
                    process?.Kill();
                }
                catch { /* ignore */ }
            }
        }

        private static void EngineReader(StreamReader stdout, object pendingBestLock, TaskCompletionSource<(string best, string? ponder)>?[] pendingBestHolder)
        {
            string? line;
            while ((line = stdout.ReadLine()) != null)
            {
                var t = line?.Trim() ?? "";
                if (t.StartsWith("bestmove ", StringComparison.OrdinalIgnoreCase))
                {
                    var rest = t.Substring(9).Trim();
                    var space = rest.IndexOf(' ');
                    string best = space >= 0 ? rest.Substring(0, space) : rest;
                    string? ponder = null;
                    if (space >= 0)
                    {
                        var after = rest.Substring(space + 1).Trim();
                        if (after.StartsWith("ponder ", StringComparison.OrdinalIgnoreCase))
                            ponder = after.Substring(7).Trim();
                    }
                    lock (pendingBestLock)
                    {
                        var tcs = pendingBestHolder[0];
                        pendingBestHolder[0] = null;
                        tcs?.TrySetResult((best, string.IsNullOrEmpty(ponder) ? null : ponder));
                    }
                }
            }
        }

        private static async Task ReadUntilAsync(StreamReader reader, string until)
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (line.Trim().StartsWith(until, StringComparison.OrdinalIgnoreCase))
                    return;
            }
        }

        private static async Task<bool> SendMoveAsync(HttpClient client, string gameId, string uciMove)
        {
            for (int retry = 0; retry < 3; retry++)
            {
                try
                {
                    var res = await client.PostAsync($"/api/bot/game/{gameId}/move/{uciMove}", null);
                    if (res.IsSuccessStatusCode)
                        return true;
                    if ((int)res.StatusCode == 429)
                    {
                        await Task.Delay(1000 * (retry + 1));
                        continue;
                    }
                    return false;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Send move error: {ex.Message}");
                    if (retry == 2) return false;
                    await Task.Delay(500);
                }
            }
            return false;
        }

        private static async Task ResignAsync(HttpClient client, string gameId)
        {
            try
            {
                await client.PostAsync($"/api/bot/game/{gameId}/resign", null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Resign error: {ex.Message}");
            }
        }

        private static string BuildPositionCommand(string? fen, string movesStr)
        {
            var moves = movesStr.Trim();
            if (string.IsNullOrEmpty(fen) || string.Equals(fen, "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", StringComparison.Ordinal))
            {
                if (string.IsNullOrEmpty(moves))
                    return "position startpos";
                return "position startpos moves " + moves;
            }
            if (string.IsNullOrEmpty(moves))
                return "position fen " + fen;
            return "position fen " + fen + " moves " + moves;
        }

        private static string? GetEnginePath()
        {
            try
            {
                var module = Process.GetCurrentProcess().MainModule;
                if (module?.FileName != null)
                {
                    var path = module.FileName;
                    if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        return path;
                }
            }
            catch { /* ignore */ }

            var baseDir = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDir))
            {
                var exeInBase = Path.Combine(baseDir, "ChessEngine.exe");
                if (File.Exists(exeInBase))
                    return exeInBase;
            }

            return null;
        }
    }
}
