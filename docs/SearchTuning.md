## Search tuning and pruning overview

This engine already implements a modern alpha–beta search with:

- Transposition table, iterative deepening, aspiration windows
- Null-move pruning with verification, razoring, reverse futility, probcut
- Late move pruning (LMP), late move reductions (LMR)
- Quiescence search with SEE-based pruning and extensions

### Where to tune search

Key parameters live in `Search.Search.AlphaBeta.cs` and `Search.Search.cs`:

- **Null-move pruning**
  - `NullMoveVerifyPhaseThreshold`
  - Null-move reduction `R = 3 + depth / 5` and verification search depth.
- **Razoring / reverse futility**
  - `RazorBaseMargin`, `RazorDepthMargin`
  - `ReverseFutilityMarginPerDepth`
  - `FutilityMargins[]` (used when `canFutilityPrune` is true).
- **Late move pruning (LMP)**
  - `LMPThresholds[]` in `Search.cs`.
- **Late move reductions (LMR)**
  - Reduction formula in `AlphaBeta` around `reduction = 1 + (depth / 5) + (movesSearched / 8)`.

When changing these values, keep all existing safety guards (e.g. checks for PV nodes, `inCheck`, large mate scores) intact to avoid tactical regressions.

### Safe workflow for search changes

1. **Verify correctness first**
   - Run the built-in perft tests:
     - `dotnet run -c Release -- perft`
   - Only proceed if all perft numbers match the expected values.
2. **Change a single heuristic at a time**
   - Example: adjust `LMPThresholds` or the LMR formula, but not both in the same test.
3. **Measure strength with SPRT**
   - Use the SPRT workflow described in `docs/SPRT.md` (cutechess-cli A/B testing).
   - A = current engine, B = engine with the proposed search tweak.
4. **Accept or reject the change**
   - Accept only if the SPRT test clearly favors the new version at your chosen Elo bounds.
   - If the result is inconclusive, revert or iterate with a smaller/more conservative change.

