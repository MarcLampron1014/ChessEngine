"""
Process all PGN (or .pgn.gz) files in a directory into one FEN | cp output file.

Usage:
  python batch_convert_pgn_to_nnue.py C:\\Users\\marcl\\Downloads\\combinedFishTest --out positions_fishtest.txt --min-depth 18
  python batch_convert_pgn_to_nnue.py ... --jobs 8   # use 8 parallel workers (faster)
"""

from __future__ import annotations

import argparse
import multiprocessing
import shutil
from pathlib import Path

from convert_pgn_to_nnue import process_pgn


def _process_chunk(
    pgn_paths: list[Path],
    out_path: Path,
    min_depth: int,
    cp_clamp: int,
    max_positions: int | None,
) -> tuple[int, int]:
    """Process a list of PGN files into one temp file. Returns (positions_written, skipped_count)."""
    total = 0
    skipped = 0
    remaining = max_positions
    with out_path.open("w", encoding="utf-8", buffering=65536) as out:
        for pgn_path in pgn_paths:
            if remaining is not None and remaining <= 0:
                break
            try:
                n = process_pgn(
                    pgn_path,
                    out,
                    max_positions=remaining,
                    min_depth=min_depth,
                    cp_clamp=cp_clamp,
                )
                total += n
                if remaining is not None:
                    remaining -= n
            except Exception:
                skipped += 1
    return total, skipped


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Convert all PGN files in a directory to one FEN | cp file."
    )
    parser.add_argument(
        "input_dir",
        type=Path,
        help="Directory containing .pgn or .pgn.gz files",
    )
    parser.add_argument(
        "--out",
        type=Path,
        required=True,
        help="Output path for combined FEN | cp file",
    )
    parser.add_argument("--max-positions", type=int, default=None)
    parser.add_argument("--min-depth", type=int, default=0)
    parser.add_argument("--cp-clamp", type=int, default=1500)
    parser.add_argument(
        "--jobs",
        "-j",
        type=int,
        default=1,
        help="Number of parallel workers (default 1). Use 4–12 for multi-core.",
    )
    parser.add_argument(
        "--recursive",
        action="store_true",
        help="Search input_dir recursively for PGN files",
    )
    parser.add_argument(
        "--small-first",
        action="store_true",
        help="Process smallest files first (get output sooner; good if some files are huge)",
    )
    args = parser.parse_args()

    if not args.input_dir.is_dir():
        raise SystemExit(f"Not a directory: {args.input_dir}")

    if args.recursive:
        files = sorted(args.input_dir.rglob("*.pgn")) + sorted(
            args.input_dir.rglob("*.pgn.gz")
        )
    else:
        files = sorted(args.input_dir.glob("*.pgn")) + sorted(
            args.input_dir.glob("*.pgn.gz")
        )

    if args.small_first:
        files = sorted(files, key=lambda p: p.stat().st_size)

    if not files:
        print(f"No .pgn or .pgn.gz files in {args.input_dir}")
        return

    out_path = args.out
    if out_path.exists() and out_path.is_dir():
        out_path = out_path / "positions_fen_cp.txt"
        print(f"Output path is a directory; writing to {out_path}")

    out_path.parent.mkdir(parents=True, exist_ok=True)
    jobs = max(1, args.jobs)

    if jobs == 1:
        # Sequential: same as before, with progress
        print(f"Found {len(files)} PGN files. Writing to {out_path}")
        total_positions = 0
        max_positions_remaining = args.max_positions
        skipped = 0
        with out_path.open("w", encoding="utf-8", buffering=65536) as out:
            for i, pgn_path in enumerate(files, 1):
                if max_positions_remaining is not None and max_positions_remaining <= 0:
                    break
                print(f"  [{i}/{len(files)}] {pgn_path.name} ...", flush=True)
                try:
                    n = process_pgn(
                        pgn_path,
                        out,
                        max_positions=max_positions_remaining,
                        min_depth=args.min_depth,
                        cp_clamp=args.cp_clamp,
                    )
                except Exception as e:
                    print(f"      SKIP ({e})", flush=True)
                    skipped += 1
                    continue
                total_positions += n
                if max_positions_remaining is not None:
                    max_positions_remaining -= n
                if total_positions == 1 or total_positions % 1000 == 0:
                    out.flush()
                print(f"      -> {n} positions (total: {total_positions})", flush=True)
        print(
            f"Done. Wrote {total_positions} positions to {out_path}"
            + (f" (skipped {skipped} files)" if skipped else "")
        )
        return

    # Parallel: split work across workers, then merge
    print(f"Found {len(files)} PGN files. Using {jobs} workers. Output: {out_path}")
    max_pos = args.max_positions
    per_worker = (max_pos // jobs) if max_pos is not None else None
    # Round-robin so each worker gets a mix of small/large if --small-first was used
    chunks: list[list[Path]] = [[] for _ in range(jobs)]
    for idx, p in enumerate(files):
        chunks[idx % jobs].append(p)
    chunks = [c for c in chunks if c]

    tmp_dir = out_path.parent / (out_path.stem + "_batch_tmp")
    tmp_dir.mkdir(parents=True, exist_ok=True)
    try:
        task_args = [
            (
                chunk,
                tmp_dir / f"part_{i}.txt",
                args.min_depth,
                args.cp_clamp,
                per_worker,
            )
            for i, chunk in enumerate(chunks)
        ]
        with multiprocessing.Pool(jobs) as pool:
            results = pool.starmap(_process_chunk, task_args)
        total_positions = sum(r[0] for r in results)
        skipped = sum(r[1] for r in results)
        # Merge part files into final output
        with out_path.open("w", encoding="utf-8", buffering=65536) as out:
            for i in range(len(chunks)):
                part = tmp_dir / f"part_{i}.txt"
                if part.exists():
                    with part.open("r", encoding="utf-8") as f:
                        shutil.copyfileobj(f, out)
        print(
            f"Done. Wrote {total_positions} positions to {out_path}"
            + (f" (skipped {skipped} files)" if skipped else "")
        )
    finally:
        shutil.rmtree(tmp_dir, ignore_errors=True)


if __name__ == "__main__":
    main()
