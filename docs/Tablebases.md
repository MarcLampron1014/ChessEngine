## Endgame tablebases integration (Syzygy stub)

This engine includes a **hook** for Syzygy-style endgame tablebases via the `Tablebases` helper, but does not yet ship with a concrete probing implementation.

### 1. Configuring a tablebase path

The engine exposes a UCI option:

- `SyzygyPath` (string): root directory containing tablebase files (e.g. `.rtbw` / `.rtbz`).

Example (UCI command):

```text
setoption name SyzygyPath value C:\tb\syzygy
```

Internally this calls:

- `Tablebases.SetSyzygyPath(...)` and sets `Tablebases.IsAvailable` when the directory exists.

### 2. Search integration

`Search.AlphaBeta` contains an early-out hook for low-material positions:

- If `Tablebases.IsAvailable` and `board.PieceCount <= 7`, it calls:
  - `Tablebases.TryProbeWdl(board, out int wdl, out int dtz)`
  - On success, converts `wdl` to a mate/zero score (from the side to move) and returns it immediately.

The `Tablebases.TryProbeWdl` method currently always returns `false` and is intended to be wired to a real Syzygy probing library.

### 3. Wiring a Syzygy library

To fully enable tablebases you can:

1. Add a Syzygy probing implementation (e.g. by vendoring or referencing an existing .NET-compatible library).
2. Implement `Tablebases.TryProbeWdl(Board board, out int wdl, out int dtz)` to:
   - Map `Board` state to the tablebase probe API.
   - Convert WDL/DTZ back into the expected `wdl` / `dtz` outputs.
3. Optionally refine the search hook:
   - Use DTZ to distinguish winning vs drawing moves in the root.
   - Restrict probing to very low-material positions for performance.

Until then, the presence of `SyzygyPath` and `Tablebases` is harmless: when no implementation is provided, the search behaves as before but is ready for tablebases once you add a probe.

