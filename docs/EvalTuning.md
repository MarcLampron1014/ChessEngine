## Evaluation tuning (Texel-style)

This engine supports Texel-style tuning of evaluation parameters using datasets of positions with game results.

### 1. Prepare a dataset

You can use:

- A **CSV file** with at least FEN and result columns (e.g. `fen,result` where result is `1`, `0.5`, or `0`), or
- A text file with lines like `FEN;result` or `FEN [result]` or `FEN 1-0`.

For very large CSVs, convert once to a compact positions file:

```bash
dotnet run -c Release -- convert tuning_dataset.csv positions.txt 500000
```

### 2. Run tuning

Use the `tune` command; the format (CSV vs text) is auto-detected:

```bash
dotnet run -c Release -- tune <dataset_file> [iterations] [max_positions] [tune_subset_size]
```

- `iterations`: maximum tuning iterations (default `100`).
- `max_positions`: optional cap for very large datasets.
- `tune_subset_size`: optional fast subset for parameter updates (full set still used for error reporting).

The tuner writes:

- `eval_params_tuned.json` — final tuned parameters.
- `eval_params_tuning.json` — last-iteration backup during tuning.

### 3. Measure eval quality

Use `eval-error` on any compatible dataset to track MSE and accuracy:

```bash
dotnet run -c Release -- eval-error positions.txt
```

You can use this both before and after tuning to see how much the error improved.

### 4. Apply tuned parameters

After tuning, either:

1. Copy tuned params over the default file:
   ```bash
   copy eval_params_tuned.json eval_params.json
   ```
2. Or load and re-save as the default:
   ```bash
   dotnet run -c Release -- load-params eval_params_tuned.json
   dotnet run -c Release -- save-params eval_params.json
   ```

On engine startup (UCI mode), if `eval_params.json` exists, the engine loads it automatically.

### 5. Combine with SPRT

For structural eval changes (adding/removing terms), use this workflow:

1. Generate or reuse a dataset and tune both **baseline** and **modified** evals.
2. Use `eval-error` to confirm the modified eval is not obviously worse.
3. Run **SPRT A/B matches** (see `docs/SPRT.md`) between the two parameter sets to confirm Elo gains.

