from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path


@dataclass
class ModelConfig:
    """
    Configuration for the NNUE network architecture.

    The feature mapping is fixed to dual-king HalfKP with:
    - 64 king squares
    - 10 piece planes (WP, WN, WB, WR, WQ, BP, BN, BB, BR, BQ)
    - 64 board squares
    => 64 * 10 * 64 = 40_960 features per king, 81_920 total.
    """

    num_features: int = 64 * 10 * 64 * 2
    hidden_dim: int = 256
    hidden_dim2: int = 32

    # Clipped ReLU maximum. Can be tuned (e.g. 1.0 or 127.0).
    clip_max: float = 1.0


@dataclass
class TrainingConfig:
    """
    Configuration for NNUE training.

    All paths are resolved relative to the project root at runtime.
    """

    data_path: Path

    # Optimization
    batch_size: int = 8192
    num_epochs: int = 5
    learning_rate: float = 1e-3
    weight_decay: float = 0.0

    # Target scaling: clamp centipawns and divide by this factor.
    cp_clamp: int = 1500
    cp_scale: float = 400.0

    # Validation split: permille out of 1000 (e.g. 50 => 5%).
    val_permille: int = 50

    # Data loading / hardware
    num_workers: int = 4
    device: str = "cuda"
    seed: int = 42

    # Activation
    clip_max: float = 1.0

    # Logging / checkpoints
    log_interval: int = 100
    out_dir: Path = Path("nnue_checkpoints")

