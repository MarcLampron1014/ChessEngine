## NNUE evaluation roadmap (design only)

This document outlines a future path for adding an NNUE-style evaluation to the engine. It is a **design and planning document only**; NNUE is intentionally not implemented yet.

### 1. Goals and constraints

- Keep the current **classical evaluation** as a strong baseline and fallback.
- Add NNUE as a **drop-in alternative** behind a feature flag or UCI option.
- Preserve existing tuning and SPRT workflows for validating NNUE vs classical eval.

### 2. Feature representation

Use a standard king-relative feature representation, e.g.:

- Side-to-move king square and enemy king square.
- Piece–square occupancy for:
  - White/black pawns, knights, bishops, rooks, queens.
- Encode as indexed features (e.g. `piece * 64 + square`) suitable for incremental updates.

The board class already exposes bitboards for each piece type, which can be mapped into feature indices efficiently.

### 3. Network architecture

A typical NNUE layout:

- Input layer: sparse encoded piece–square features.
- One or two large fully-connected hidden layers (e.g. 256–1024 units).
- Output layer: single scalar score in centipawns.

Design considerations:

- Use **integer weights and activations** (e.g. 16-bit) to match NNUE-style efficient inference.
- Choose layer sizes that fit L3 cache and SIMD widths on your target CPUs.

### 4. Incremental update path

To keep search fast:

- Maintain an NNUE state per node or per thread.
- On each move/undo:
  - Update only the affected feature indices (pieces moved, captured, promoted, castled).
  - Reuse most of the accumulated hidden-layer activations.

This mirrors how the existing classical eval reuses material/phase information and pawn hashes.

### 5. Integration into the engine

Planned integration points:

- Add a new evaluation entry point, e.g. `EvaluatorNNUE.Evaluate(Board board)`.
- In the main search:
  - Gate between classical and NNUE eval via a configuration flag or UCI option (e.g. `UseNNUE`).
  - Optionally keep classical quick-eval for pruning and NNUE only for full-node evaluation.
- Use existing tuning and SPRT infrastructure:
  - Train NNUE on the same or extended datasets as the classical eval.
  - Run SPRT matches `Classical vs NNUE` to validate strength gains.

### 6. Training pipeline (high level)

1. **Data generation**
   - Self-play games at fixed time control, or imported high-quality engine/human games.
   - Extract positions with results and optionally search scores.
2. **Dataset format**
   - Store FEN + result (and/or centipawn targets) in a compact binary or text format.
3. **Training loop**
   - Implement in an external training script (e.g. Python with PyTorch) or in C# using a suitable ML library.
   - Optimize NNUE weights with standard optimizers (SGD, Adam) against the chosen loss (e.g. Texel, MSE).
4. **Export weights**
   - Save trained weights in a compact binary layout read by the engine at startup (similar to `eval_params.json` today).

With this roadmap, the engine is prepared for a future NNUE implementation while continuing to rely on (and improve) the existing classical evaluation and search for now.

