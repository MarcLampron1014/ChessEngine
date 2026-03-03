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

```bash
python -m nnue_train.train path/to/dataset.txt \
  --batch-size 8192 \
  --epochs 5 \
  --lr 1e-3 \
  --cp-clamp 1500 \
  --cp-scale 400.0 \
  --clip-max 1.0 \
  --val-permille 50 \
  --num-workers 4 \
  --device cuda \
  --out-dir nnue_checkpoints
```

This will:

- Build (or load) a cached index for the dataset:
  - Byte offsets per non-empty line.
  - Deterministic train/validation split via a hash of the FEN string.
- Train the NNUE model with mixed precision (when CUDA is available).
- Save checkpoints and export a flat text weight file:
  - `nnue_checkpoints/nnue_weights.txt`
  - `nnue_checkpoints/nnue_weights_meta.json`

### Golden FEN test

The module `features_halfkp.py` includes a `golden_fen_indices()` helper and a
small CLI when run directly. Use this to verify that your C# feature extractor
produces exactly the same feature indices for the same FEN before wiring NNUE
into the search.

