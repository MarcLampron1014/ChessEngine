## SPRT testing for engine changes

This document describes how to run Sequential Probability Ratio Tests (SPRT) to decide whether a change (search, eval, time management, etc.) is an Elo improvement.

The examples use **`cutechess-cli`** on Windows, but the same ideas apply on other platforms.

### 1. Build two engine binaries

Always compare **exactly one change** at a time:

- **A (baseline)**: known-good version.
- **B (test)**: identical except for the change you want to evaluate.

Build two executables, e.g.:

- `.\publish\ChessEngine_base.exe`
- `.\publish\ChessEngine_test.exe`

using:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -o publish
```

You can distinguish A and B by:

- Compiling from different commits, or
- Using different `eval_params.json` files (e.g. `eval_params_base.json` vs `eval_params_test.json`).

### 2. Recommended cutechess-cli command

Basic template (adapt paths / options as needed):

```bash
cutechess-cli ^
  -engine cmd=".\publish\ChessEngine_base.exe" name=Base proto=uci ^
  -engine cmd=".\publish\ChessEngine_test.exe" name=Test proto=uci ^
  -each tc=40/5+0.05 option.Hash=128 option.Threads=1 ^
  -openings file=book.pgn format=pgn order=random plies=12 ^
  -games 2000 ^
  -sprt elo0=0 elo1=10 alpha=0.05 beta=0.05 ^
  -concurrency 4
```

Guidelines:

- Use **identical options** (`Hash`, `Threads`, book, tablebases, contempt, etc.) for both engines.
- Use a **small but non-trivial time control** (e.g. `40/5+0.05`, `5+0.05`, or similar).
- Use a **fixed opening book** (`book.pgn`) with random order and both colors.

### 3. Choosing SPRT parameters

A good default for most tests:

- `elo0 = 0` — null hypothesis: no improvement.
- `elo1 = 10` — alternative: at least +10 Elo.
- `alpha = 0.05` — at most 5% chance to accept a bad change.
- `beta  = 0.05` — at most 5% chance to reject a good change.

For very small expected gains, you can use `elo1=5` at the cost of more games.

`cutechess-cli` will stop early when the result is statistically clear, printing either:

- `SPRT: H1 accepted` — keep the change (B is stronger).
- `SPRT: H0 accepted` — reject the change (no evidence B is better).

### 4. Testing workflow

For each candidate change:

1. **Verify correctness** (perft, regression tests, no crashes).
2. Build `Base` and `Test` binaries.
3. Run an SPRT match as above.
4. Only accept the change if **H1 is accepted**.
5. Log:
   - Commit / change description.
   - SPRT parameters (TC, book, `elo0/elo1`, `alpha/beta`).
   - Final Elo estimate and game count.

Over time this gives you a history of **proven Elo improvements** on top of your classical search and evaluation.

