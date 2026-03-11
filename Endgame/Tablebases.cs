using System.IO;

namespace ChessEngine
{
    /// <summary>
    /// Hook for Syzygy (or other) endgame tablebases.
    /// The probing implementation can be filled in later or delegated to an external library.
    /// </summary>
    public static class Tablebases
    {
        /// <summary>
        /// Root path where tablebase files (.rtbw/.rtbz, etc.) are stored.
        /// </summary>
        public static string? SyzygyPath { get; private set; }

        public static void SetSyzygyPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                SyzygyPath = null;
                return;
            }

            SyzygyPath = Directory.Exists(path) ? path : null;
        }

        /// <summary>
        /// Returns true if a non-empty tablebase directory has been configured.
        /// This does not guarantee that probing is implemented.
        /// </summary>
        public static bool IsAvailable =>
            !string.IsNullOrEmpty(SyzygyPath) && Directory.Exists(SyzygyPath!);

        /// <summary>
        /// Try to probe a WDL/DTZ result from tablebases.
        /// Current implementation is a stub that always returns false; it is
        /// intended to be wired to a Syzygy probing library.
        /// </summary>
        /// <param name="board">Current board.</param>
        /// <param name="wdl">
        /// Win/draw/loss code from the side to move's perspective:
        /// typically -2 (loss), -1 (loss), 0 (draw), +1 (win), +2 (win).
        /// </param>
        /// <param name="dtz">Distance-to-zeroing move, if available.</param>
        public static bool TryProbeWdl(Board board, out int wdl, out int dtz)
        {
            wdl = 0;
            dtz = 0;
            return false;
        }
    }
}

