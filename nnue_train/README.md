## nnue_train

Python/PyTorch training utilities for an NNUE-style evaluation network for `ChessEngine`.

### Overview

- Input: positions in text format:
  - One position per line: `FEN | centipawn_score`
  - Score is **white POV** (positive means White is better).
- Features: dual-king HalfKP:
  - Side-to-move is not used directly; scores are always white POV.
  - Square indexing matches the engine’s PST convention (A1 = 0, H8 = 63).
  - Piece planes (10 total, fixed order): `WP, WN, WB, WR, WQ, BP, BN, BB, BR, BQ`.
- Network:
  - `EmbeddingBag(num_features=81920, dim=256)` → clipped ReLU
  - `Linear(256 → 32)` → clipped ReLU
  - `Linear(32 → 1)`
- Targets:
  - Clamp at ±1500 centipawns.
  - Normalize by `cp_scale` (e.g. 400.0 or 600.0) and train with MSE.

### Installation

Create a virtual environment and install dependencies:

```bash
cd ChessEngine
python -m venv .venv
source .venv/bin/activate  # Windows: .venv\Scripts\activate
pip install -r nnue_train/requirements.txt
```

### Training

For a fast GPU setup (e.g. RTX-class card, dataset on SSD), start with:

```bash
python -m nnue_train.train C:\Users\marcl\ChessEngine\datasets\positions_nnue.txt ^
  --batch-size 16384 ^
  --epochs 3 ^
  --lr 1e-3 ^
  --cp-clamp 1500 ^
  --cp-scale 400.0 ^
  --clip-max 1.0 ^
  --val-permille 50 ^
  --num-workers 8 ^
  --device cuda ^
  --out-dir nnue_checkpoints
```

Notes:

- **Batch size**: Increase `--batch-size` (e.g. 16384, 32768) until you approach your GPU memory limit; larger batches improve throughput.
- **DataLoader workers**: Raise `--num-workers` to fully feed the GPU; the loader uses persistent workers and pinned memory when `num-workers > 0`.
- **Epochs**: Start with fewer epochs (e.g. 2–3) and only extend if validation RMSE keeps improving.

Copy Paste ca: 
python -m nnue_train.train C:\Users\marcl\ChessEngine\datasets\cleaned\positions_nnue_202601.txt --batch-size 32768 --epochs 3 --lr 1e-3 --cp-clamp 1500 --cp-scale 400.0 --clip-max 1.0 --val-permille 50 --num-workers 12 --device cuda --out-dir nnue_checkpoints

python -m nnue_train.train C:\Users\marcl\ChessEngine\datasets\cleaned\positions_nnue_202501.txt --batch-size 32768 --epochs 3 --lr 1e-3 --cp-clamp 1500 --cp-scale 400.0 --clip-max 1.0 --val-permille 50 --num-workers 12 --device cuda --out-dir nnue_checkpoints


This will:

- Build (or load) a cached index for the dataset:
  - Byte offsets per non-empty line.
  - Deterministic train/validation split via a hash of the FEN string.
- Train the NNUE model with mixed precision (when CUDA is available).
- Save checkpoints and export a flat text weight file:
  - `nnue_checkpoints/nnue_weights.txt`
  - `nnue_checkpoints/nnue_weights_meta.json`

For quick experiments on very large datasets, you can:

- Create a smaller subset of positions (e.g. by sampling lines from `positions_nnue.txt`) and train on that while tuning hyperparameters.
- Keep the full dataset text file and its `.nnue` index `.npy` files on a fast local SSD for best training throughput.

### Golden FEN test

The module `features_halfkp.py` includes a `golden_fen_indices()` helper and a
small CLI when run directly. Use this to verify that your C# feature extractor
produces exactly the same feature indices for the same FEN before wiring NNUE
into the search.

