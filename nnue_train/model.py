from __future__ import annotations

from dataclasses import asdict
from typing import Any, Dict

import torch
from torch import nn

from .config import ModelConfig


def clipped_relu(x: torch.Tensor, max_value: float) -> torch.Tensor:
    return torch.clamp(x, 0.0, float(max_value))


class NnueModel(nn.Module):
    """
    Sparse NNUE-style evaluation network:

        EmbeddingBag(num_features → 256) → clipped ReLU
        Linear(256 → 32) → clipped ReLU
        Linear(32 → 1)

    The input is encoded as:
    - indices: LongTensor of feature indices concatenated across the batch.
    - offsets: LongTensor of starting positions for each sample (EmbeddingBag-style).
    """

    def __init__(self, cfg: ModelConfig) -> None:
        super().__init__()
        self.cfg = cfg

        self.input = nn.EmbeddingBag(
            num_embeddings=cfg.num_features,
            embedding_dim=cfg.hidden_dim,
            mode="sum",
        )
        self.bias0 = nn.Parameter(torch.zeros(cfg.hidden_dim))

        self.fc1 = nn.Linear(cfg.hidden_dim, cfg.hidden_dim2)
        self.fc2 = nn.Linear(cfg.hidden_dim2, 1)

        self._init_weights()

    def _init_weights(self) -> None:
        # Simple Xavier/He-style initialisation is sufficient here.
        nn.init.kaiming_uniform_(self.input.weight, a=0.0, nonlinearity="relu")
        nn.init.zeros_(self.bias0)
        nn.init.kaiming_uniform_(self.fc1.weight, a=0.0, nonlinearity="relu")
        nn.init.zeros_(self.fc1.bias)
        nn.init.kaiming_uniform_(self.fc2.weight, a=0.0, nonlinearity="linear")
        nn.init.zeros_(self.fc2.bias)

    def forward(self, indices: torch.Tensor, offsets: torch.Tensor) -> torch.Tensor:
        x = self.input(indices, offsets)
        x = clipped_relu(x + self.bias0, self.cfg.clip_max)
        x = clipped_relu(self.fc1(x), self.cfg.clip_max)
        out = self.fc2(x)
        return out.squeeze(-1)

    def metadata(self) -> Dict[str, Any]:
        """
        Export basic architectural metadata for use by the engine loader.
        """

        return {
            "model_config": asdict(self.cfg),
            "layers": {
                "input": {
                    "num_embeddings": self.input.num_embeddings,
                    "embedding_dim": self.input.embedding_dim,
                },
                "fc1": {
                    "in_features": self.fc1.in_features,
                    "out_features": self.fc1.out_features,
                },
                "fc2": {
                    "in_features": self.fc2.in_features,
                    "out_features": self.fc2.out_features,
                },
            },
        }

