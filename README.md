# ChessEngine
This is my attempt at making a chess engine from scratch, with the help of a lot of research.

## Roadmap / TODO

- [ ] Improve pruning heuristics and search tuning
- [ ] Improve evaluation (feature coverage, tuning quality)
- [ ] Endgame databases
- [ ] Critical positions / contempt handling
- [ ] SPRT testing

Useful sources:
https://www.chessprogramming.org/Main_Page
https://www.chessprogramming.org/Perft_Results
See also:
- `docs/SearchTuning.md` for an overview of the engine’s pruning and search parameters and how to tune them safely.
- `docs/EvalTuning.md` for the full Texel-style evaluation tuning workflow.
- `docs/SPRT.md` for setting up cutechess-cli based SPRT testing to measure Elo gains from changes.

## How to use the engine

### Requirements
- .NET SDK (10.0 or later)

### Build and run

**1. Build the project** (required before running any command):

```bash
cd path\to\ChessEngine
dotnet build -c Release
```

**2. Run the engine** — choose one:

- **From source** (no publish step): run with `dotnet run -c Release --` followed by a command. Examples:
  ```bash
  dotnet run -c Release --                    # UCI mode
  dotnet run -c Release -- perft             # perft tests
  dotnet run -c Release -- tune mydata.csv 50
  ```
- **From a published executable**: build once, then run the exe:
  ```bash
  dotnet publish -c Release -r win-x64 --self-contained true -o publish
  .\publish\ChessEngine.exe                  # UCI mode
  .\publish\ChessEngine.exe perft           # perft tests
  .\publish\ChessEngine.exe tune mydata.csv 50
  ```

All commands below use `dotnet run -c Release --` for “run from source”. If you use the published exe, replace that with `.\publish\ChessEngine.exe` (Windows) or `./publish/ChessEngine` (Linux/macOS).

### Command-line commands

Run everything from the **project directory** (where `ChessEngine.csproj` is).

| Command | Description |
|--------|-------------|
| *(no args)* | Start the engine in UCI mode. If `eval_params.json` exists in the current directory, evaluation parameters are loaded from it. |
| `perft` | Run built-in perft tests. |
| `convert` | Convert a CSV dataset to FEN;result format for tuning. |
| `tune` | Run Texel-style evaluation tuning on a dataset. |
| `eval-error` | Report mean squared error and prediction accuracy on a dataset. |
| `save-params` | Save current evaluation parameters to a JSON file. |
| `load-params` | Load evaluation parameters from a JSON file and print them. |

**Perft**
```bash
dotnet run -c Release -- perft
```

**Convert (CSV → positions file)**  
Converts a CSV file (with FEN and result columns) into `FEN;result` lines. Output defaults to `positions.txt`.
```bash
dotnet run -c Release -- convert <input.csv> [output.txt] [max_positions]
# Examples:
dotnet run -c Release -- convert tuning_dataset_16m.csv
dotnet run -c Release -- convert tuning_dataset_16m.csv positions.txt 500000
```

**Tune**  
Runs evaluation tuning on a dataset. Format is auto-detected: CSV (`fen,result`) or FEN;result text. Writes tuned parameters to `eval_params_tuned.json` (and a backup to `eval_params_tuning.json`).
```bash
dotnet run -c Release -- tune <dataset_file> [iterations] [max_positions]
# Examples:
dotnet run -c Release -- tune tuning_dataset_16m.csv 50
dotnet run -c Release -- tune tuning_dataset_16m.csv 50 500000
dotnet run -c Release -- tune positions.txt 100
```

**Eval-error**  
Measures how well the current evaluation matches game results on a dataset (same file formats as `tune`).
```bash
dotnet run -c Release -- eval-error <dataset_file>
# Example:
dotnet run -c Release -- eval-error positions.txt
```

**Save / load parameters**
```bash
# Save current parameters (default: eval_params.json)
dotnet run -c Release -- save-params [path]
dotnet run -c Release -- save-params eval_params.json

# Load parameters from a file (prints values; does not start UCI)
dotnet run -c Release -- load-params <params_file>
dotnet run -c Release -- load-params eval_params_tuned.json
```

**Using tuned parameters in the engine**  
After tuning, parameters are in `eval_params_tuned.json`. The engine only loads `eval_params.json` at startup. To use the tuned values:

1. Copy the tuned file over the default (from project directory):
   ```bash
   copy eval_params_tuned.json eval_params.json
   ```
   (PowerShell: `Copy-Item eval_params_tuned.json eval_params.json`)

2. Or load tuned params then save as default:
   ```bash
   dotnet run -c Release -- load-params eval_params_tuned.json
   dotnet run -c Release -- save-params eval_params.json
   ```

Then run the engine (with no arguments or from a GUI); it will use the tuned evaluation.

### Using with a GUI (UCI)
This engine implements the UCI (Universal Chess Interface) protocol. To use the engine with a GUI (Arena, CuteChess, etc.), add it as a UCI engine and point the GUI to the executable.

Example UCI command exchange (illustrative):
- GUI -> engine: `uci`
- Engine -> GUI: `id name ChessEngine`
- Engine -> GUI: `id author marcl`
- Engine -> GUI: `uciok`
- GUI -> engine: `isready`
- Engine -> GUI: `readyok`
- GUI -> engine: `position startpos moves e2e4 e7e5`
- GUI -> engine: `go movetime 1000`
- Engine -> GUI: `bestmove e2e4`

### Manual testing
Open a terminal and run the executable; then type UCI commands like `uci`, `isready`, `position`, `go`, and `quit` to interact with the engine. On Unix-like shells you can also pipe a small script of commands into the executable for automated tests.

### Notes
- The engine currently implements a basic UCI interface and a simple time allocation heuristic.
- For development or inspection, see source files such as `Uci.cs`, `Search.cs`, `MoveGenerator.cs`, and `Perft.cs`.

### Evaluation parameter files (JSON)

| File | Purpose |
|------|--------|
| **`eval_params.json`** | **The one the engine uses.** Loaded at startup when you run UCI (no args or from a GUI). Keep your chosen evaluation parameters here. Create or update it with `save-params` or by copying from a tuned file. |
| **`eval_params_tuned.json`** | Written at the **end** of each tuning run. This is the final tuned result. Use it by copying to `eval_params.json` (or run `load-params` then `save-params`) so the engine uses these values. |
| **`eval_params_tuning.json`** | Written **during** tuning (after each iteration) as a backup. You can ignore or delete it; it is only useful if a tuning run is interrupted and you want to recover the last iteration’s parameters. |

**Summary:** For normal use, only **`eval_params.json`** matters. The engine reads that file when it starts. The other two are tuning outputs; copy `eval_params_tuned.json` to `eval_params.json` when you want to apply new tuned values.

---

This readme was generated by AI,
Marc Lampron

--TODO (features currently in the engine):

**Search**
- [x] Transposition table (hash table, configurable size)
- [x] Iterative deepening with aspiration windows (PVS)
- [x] Move ordering (TT move, good captures, killers, countermoves, history)
- [x] Killer moves
- [x] Countermove heuristic
- [x] History heuristic
- [x] Quiescence search (captures, promotions, passed-pawn pushes; delta/SEE pruning)
- [x] Null move pruning (with endgame verification)
- [x] Late move reduction (LMR)
- [x] Late move pruning (LMP)
- [x] Futility pruning
- [x] Static Exchange Evaluation (SEE) for ordering and pruning
- [x] Internal iterative deepening (IID)
- [x] Check extension; recapture and capture extensions
- [x] Multi-threaded search (Lazy SMP)
- [x] Ponder (best move + ponder from TT)
- [x] Draw detection (repetition, fifty-move rule, insufficient material)

**Time management**
- [x] Variable time allocation (phase, complexity, stability-based early exit)

**Evaluation**
- [x] Phase-based (tapered) evaluation
- [x] Material and piece-square tables (MG/EG)
- [x] Pawn structure (doubled, isolated, passed pawns)
- [x] Bishop pair
- [x] Rooks on open/semi-open files
- [x] Eval cache; tunable parameters (JSON load/save)
- [x] Quick eval for pruning; insufficient material detection

**UCI & interfaces**
- [x] UCI protocol (options: Hash, Threads, BookFile, BookDepth, BookEvalLimit)
- [x] Opening book (Polyglot .bin)
- [x] Perft testing
- [x] Evaluation tuning (Texel-style: convert, tune, eval-error, save/load params)
- [x] Lichess bot mode
 - [x] Contempt option and stubbed SyzygyPath hook for future tablebases
