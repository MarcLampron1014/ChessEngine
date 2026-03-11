"""
NNUE training package for ChessEngine.

This package implements:
- FEN parsing to a simple piece list representation.
- Dual-king HalfKP feature extraction.
- A sparse NNUE model using PyTorch EmbeddingBag.
- Dataset and dataloader utilities for large text datasets.
- A training loop with validation.
- Weight export to a flat text format for C#/C++ loading.
"""

from .config import ModelConfig, TrainingConfig
from .model import NnueModel

__all__ = ["ModelConfig", "TrainingConfig", "NnueModel"]

