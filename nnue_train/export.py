from __future__ import annotations

import json
from pathlib import Path
from typing import Dict, Tuple

import torch

from .model import NnueModel


def _flatten_tensor(t: torch.Tensor) -> Tuple[int, int, torch.Tensor]:
    if t.ndim == 1:
        rows, cols = 1, t.shape[0]
    elif t.ndim == 2:
        rows, cols = int(t.shape[0]), int(t.shape[1])
    else:
        raise ValueError(f"Only 1D or 2D tensors are supported for export, got {t.shape}")
    return rows, cols, t.detach().cpu().reshape(-1)


def export_txt(model: NnueModel, txt_path: Path, meta_path: Path, cp_scale: float) -> None:
    """
    Export model weights to a flat text file plus a small JSON metadata file.

    The text file layout is:

        # Header (single line, space-separated):
        num_features hidden_dim hidden_dim2 clip_max cp_scale
        # Followed by tensors in this exact order, row-major:
        W0  (input.embedding: [num_features, hidden_dim])
        b0  (bias0: [hidden_dim])
        W1  (fc1.weight: [hidden_dim2, hidden_dim])
        b1  (fc1.bias: [hidden_dim2])
        W2  (fc2.weight: [1, hidden_dim2])
        b2  (fc2.bias: [1])
    """

    cfg = model.cfg

    with txt_path.open("w", encoding="utf-8") as f:
        # Header
        f.write(
            f"{cfg.num_features} {cfg.hidden_dim} {cfg.hidden_dim2} "
            f"{cfg.clip_max} {cp_scale}\n"
        )

        tensors = [
            ("W0", model.input.weight),
            ("b0", model.bias0),
            ("W1", model.fc1.weight),
            ("b1", model.fc1.bias),
            ("W2", model.fc2.weight),
            ("b2", model.fc2.bias),
        ]

        for name, tensor in tensors:
            rows, cols, flat = _flatten_tensor(tensor)
            f.write(f"# {name} {rows} {cols}\n")
            f.write(" ".join(f"{x:.8f}" for x in flat.tolist()))
            f.write("\n")

    meta: Dict[str, object] = {
        "header": {
            "num_features": cfg.num_features,
            "hidden_dim": cfg.hidden_dim,
            "hidden_dim2": cfg.hidden_dim2,
            "clip_max": cfg.clip_max,
            "cp_scale": float(cp_scale),
        },
        "tensors": {
            "W0": [cfg.num_features, cfg.hidden_dim],
            "b0": [cfg.hidden_dim],
            "W1": [cfg.hidden_dim2, cfg.hidden_dim],
            "b1": [cfg.hidden_dim2],
            "W2": [1, cfg.hidden_dim2],
            "b2": [1],
        },
        "notes": "Values are stored in row-major order. The engine must apply the same "
        "HalfKP feature mapping and clipped ReLU activation with clip_max.",
    }

    with meta_path.open("w", encoding="utf-8") as f_meta:
        json.dump(meta, f_meta, indent=2)

