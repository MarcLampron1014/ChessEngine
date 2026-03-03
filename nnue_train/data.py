from __future__ import annotations

import mmap
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, List, Sequence, Tuple

import numpy as np
import torch
from torch.utils.data import Dataset

from .features_halfkp import halfkp_indices_from_fen


def fnv1a_64(data: bytes) -> int:
    """
    FNV-1a 64-bit hash, used for deterministic train/val split.
    """

    h = 1469598103934665603
    for b in data:
        h ^= b
        h = (h * 1099511628211) & 0xFFFFFFFFFFFFFFFF
    return h


@dataclass
class DatasetIndexFiles:
    offsets_path: Path
    train_idx_path: Path
    val_idx_path: Path


def _index_paths(data_path: Path) -> DatasetIndexFiles:
    base = data_path.with_suffix(data_path.suffix + ".nnue")
    return DatasetIndexFiles(
        offsets_path=base.with_suffix(base.suffix + ".offsets.npy"),
        train_idx_path=base.with_suffix(base.suffix + ".train_idx.npy"),
        val_idx_path=base.with_suffix(base.suffix + ".val_idx.npy"),
    )


def build_or_load_index(
    data_path: Path, val_permille: int
) -> Tuple[np.ndarray, np.ndarray, np.ndarray]:
    """
    Build (or load) byte offsets and train/val line indices.

    For large datasets this is done once and cached as NumPy arrays.
    """

    idx_files = _index_paths(data_path)
    if (
        idx_files.offsets_path.exists()
        and idx_files.train_idx_path.exists()
        and idx_files.val_idx_path.exists()
    ):
        offsets = np.load(idx_files.offsets_path)
        train_idx = np.load(idx_files.train_idx_path)
        val_idx = np.load(idx_files.val_idx_path)
        return offsets, train_idx, val_idx

    offsets: List[int] = []
    train_idx: List[int] = []
    val_idx: List[int] = []

    bucket_threshold = val_permille * 10  # permille of 10000

    with data_path.open("rb") as f:
        line_idx = 0
        while True:
            offset = f.tell()
            line = f.readline()
            if not line:
                break
            if not line.strip():
                continue

            offsets.append(offset)

            # Hash based on FEN substring before '|'.
            fen_bytes = line.split(b"|", 1)[0].strip()
            h = fnv1a_64(fen_bytes)
            bucket = h % 10000
            if bucket < bucket_threshold:
                val_idx.append(line_idx)
            else:
                train_idx.append(line_idx)

            line_idx += 1

    offsets_arr = np.asarray(offsets, dtype=np.int64)
    train_idx_arr = np.asarray(train_idx, dtype=np.int64)
    val_idx_arr = np.asarray(val_idx, dtype=np.int64)

    np.save(idx_files.offsets_path, offsets_arr)
    np.save(idx_files.train_idx_path, train_idx_arr)
    np.save(idx_files.val_idx_path, val_idx_arr)

    return offsets_arr, train_idx_arr, val_idx_arr


class FenCpTextDataset(Dataset):
    """
    Dataset backed by a large text file with lines of the form:

        FEN | centipawn_score

    This implementation uses:
    - A cached array of byte offsets per line.
    - A memory-mapped view of the file for random access.
    - Pre-computed train/val index lists using a hash of the FEN string.
    """

    def __init__(
        self,
        data_path: Path,
        split: str,
        cp_clamp: int,
        cp_scale: float,
        val_permille: int,
    ) -> None:
        if split not in ("train", "val"):
            raise ValueError(f"split must be 'train' or 'val', got {split!r}")

        self.data_path = data_path
        self.split = split
        self.cp_clamp = int(cp_clamp)
        self.cp_scale = float(cp_scale)

        offsets, train_idx, val_idx = build_or_load_index(data_path, val_permille)
        self._offsets = offsets
        self._line_indices = train_idx if split == "train" else val_idx

        # Lazily initialised file handle / mmap, created per worker process.
        self._fp = None
        self._mm = None

    def _ensure_mmap(self) -> None:
        """
        Lazily open the dataset file and memory-map it.

        This avoids having an open file handle when the Dataset is pickled
        for DataLoader worker processes on Windows.
        """

        if self._mm is None or self._fp is None:
            fp = self.data_path.open("rb")
            mm = mmap.mmap(fp.fileno(), 0, access=mmap.ACCESS_READ)
            self._fp = fp
            self._mm = mm

    def __len__(self) -> int:
        return int(self._line_indices.shape[0])

    def __getitem__(self, idx: int) -> Tuple[List[int], float]:
        self._ensure_mmap()
        line_idx = int(self._line_indices[idx])
        start = int(self._offsets[line_idx])
        end = (
            int(self._offsets[line_idx + 1])
            if line_idx + 1 < len(self._offsets)
            else len(self._mm)
        )
        raw = self._mm[start:end]
        line = raw.rstrip(b"\r\n")
        if not line:
            raise IndexError(f"Empty line at index {idx}")

        try:
            fen_bytes, cp_bytes = line.split(b"|", 1)
        except ValueError as exc:
            raise ValueError(f"Invalid line format (expected 'FEN | cp'): {line!r}") from exc

        fen = fen_bytes.decode("utf-8").strip()
        cp_str = cp_bytes.decode("utf-8").strip()

        try:
            cp = int(cp_str)
        except ValueError as exc:
            raise ValueError(f"Invalid centipawn score {cp_str!r} in line {line!r}") from exc

        cp = max(-self.cp_clamp, min(self.cp_clamp, cp))
        target = cp / self.cp_scale

        indices = halfkp_indices_from_fen(fen)
        return indices, float(target)

    def close(self) -> None:
        if self._mm is not None:
            self._mm.close()
            self._mm = None
        if self._fp is not None:
            self._fp.close()
            self._fp = None


def collate_embedding_bag(
    batch: Sequence[Tuple[List[int], float]]
) -> Tuple[torch.Tensor, torch.Tensor, torch.Tensor]:
    """
    Collate function for EmbeddingBag with 'sum' mode.

    Converts a batch of variable-length index lists into:
    - indices_flat: 1-D tensor of all indices concatenated.
    - offsets: starting index in indices_flat for each sample.
    - targets: tensor of normalized scalar targets.
    """

    indices_lists: List[Iterable[int]] = []
    targets: List[float] = []
    for indices, target in batch:
        indices_lists.append(indices)
        targets.append(target)

    lengths = [len(x) for x in indices_lists]
    total_indices = sum(lengths)

    indices_flat = torch.empty(total_indices, dtype=torch.long)
    offsets = torch.empty(len(lengths), dtype=torch.long)

    cursor = 0
    for i, lst in enumerate(indices_lists):
        offsets[i] = cursor
        for v in lst:
            indices_flat[cursor] = int(v)
            cursor += 1

    targets_tensor = torch.tensor(targets, dtype=torch.float32)
    return indices_flat, offsets, targets_tensor

