from __future__ import annotations

import argparse
from multiprocessing import Pool, cpu_count
from pathlib import Path
from typing import List, Sequence, Tuple

import numpy as np

from nnue_train.data import _index_paths, fnv1a_64


def _compute_chunks(data_path: Path, chunk_size: int) -> List[Tuple[int, int]]:
    """
    Compute (start, end) byte ranges that align to newline boundaries.

    Each chunk covers a contiguous region of the file, and all chunks
    together cover [0, file_size).
    """

    file_size = data_path.stat().st_size
    if file_size == 0:
        return []

    chunks: List[Tuple[int, int]] = []
    with data_path.open("rb") as f:
        pos = 0
        while pos < file_size:
            start = pos
            tentative_end = start + chunk_size
            if tentative_end >= file_size:
                end = file_size
            else:
                f.seek(tentative_end)
                _ = f.readline()
                end = f.tell()
            chunks.append((start, end))
            pos = end

    return chunks


def _process_chunk(
    args: Tuple[Path, int, int, int]
) -> Tuple[List[int], List[int], List[int], int]:
    """
    Worker function: process a byte range [start, end) of the dataset file.

    Returns:
        offsets_chunk: list of byte offsets (from file start) for non-empty lines
                       whose first byte lies within [start, end).
        train_idx_chunk: local line indices assigned to the training split.
        val_idx_chunk: local line indices assigned to the validation split.
        line_count: number of non-empty lines in this chunk.
    """

    data_path, start, end, val_permille = args
    bucket_threshold = val_permille * 10  # permille of 10000

    offsets_chunk: List[int] = []
    train_idx_chunk: List[int] = []
    val_idx_chunk: List[int] = []

    with data_path.open("rb") as f:
        f.seek(start)
        line_idx = 0

        while f.tell() < end:
            offset = f.tell()
            line = f.readline()
            if not line:
                break
            if not line.strip():
                continue

            offsets_chunk.append(offset)

            fen_bytes = line.split(b"|", 1)[0].strip()
            h = fnv1a_64(fen_bytes)
            bucket = h % 10000
            if bucket < bucket_threshold:
                val_idx_chunk.append(line_idx)
            else:
                train_idx_chunk.append(line_idx)

            line_idx += 1

    return offsets_chunk, train_idx_chunk, val_idx_chunk, line_idx


def build_index_parallel(
    data_path: Path, val_permille: int, jobs: int, chunk_size: int
) -> None:
    """
    Build NNUE index files for a large positions text file using multiple processes.

    This writes the same .npy files as nnue_train.data.build_or_load_index would:
    - <data_path>.nnue.offsets.npy
    - <data_path>.nnue.train_idx.npy
    - <data_path>.nnue.val_idx.npy
    """

    chunks = _compute_chunks(data_path, chunk_size)
    if not chunks:
        raise ValueError(f"Empty dataset file: {data_path}")

    print(f"Indexing {data_path} in {len(chunks)} chunks using {jobs} workers...")

    args: Sequence[Tuple[Path, int, int, int]] = [
        (data_path, start, end, val_permille) for (start, end) in chunks
    ]

    with Pool(processes=jobs) as pool:
        results = pool.map(_process_chunk, args)

    global_offsets: List[int] = []
    global_train_idx: List[int] = []
    global_val_idx: List[int] = []

    line_base = 0
    for offsets_chunk, train_idx_chunk, val_idx_chunk, line_count in results:
        global_offsets.extend(offsets_chunk)
        global_train_idx.extend([idx + line_base for idx in train_idx_chunk])
        global_val_idx.extend([idx + line_base for idx in val_idx_chunk])
        line_base += line_count

    offsets_arr = np.asarray(global_offsets, dtype=np.int64)
    train_idx_arr = np.asarray(global_train_idx, dtype=np.int64)
    val_idx_arr = np.asarray(global_val_idx, dtype=np.int64)

    idx_files = _index_paths(data_path)
    np.save(idx_files.offsets_path, offsets_arr)
    np.save(idx_files.train_idx_path, train_idx_arr)
    np.save(idx_files.val_idx_path, val_idx_arr)

    print(f"Saved offsets to {idx_files.offsets_path}")
    print(f"Saved train indices to {idx_files.train_idx_path}")
    print(f"Saved val indices to {idx_files.val_idx_path}")
    print(f"Total non-empty lines indexed: {line_base}")


def _parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Build NNUE index .npy files for a large positions_nnue.txt file "
            "using multiple CPU processes."
        )
    )
    parser.add_argument(
        "data_path",
        type=Path,
        help="Path to positions text file (FEN | cp per line).",
    )
    parser.add_argument(
        "--val-permille",
        type=int,
        default=50,
        help="Validation share in permille (50 => 5%%). Must match training.",
    )
    parser.add_argument(
        "--jobs",
        type=int,
        default=min(12, cpu_count()),
        help="Number of worker processes to use.",
    )
    parser.add_argument(
        "--chunk-size",
        type=int,
        default=256 * 1024 * 1024,
        help="Approximate chunk size in bytes (default: 256 MiB).",
    )
    return parser.parse_args()


def main() -> None:
    args = _parse_args()
    build_index_parallel(
        data_path=args.data_path,
        val_permille=args.val_permille,
        jobs=args.jobs,
        chunk_size=args.chunk_size,
    )


if __name__ == "__main__":
    main()

