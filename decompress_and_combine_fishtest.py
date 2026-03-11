"""
Decompress all .pgn.gz under a fishtest download and optionally combine into one .pgn.

Usage:
  python decompress_and_combine_fishtest.py C:\\Users\\marcl\\Downloads\\fishtest_2026_01 --out combined_202601.pgn
  python decompress_and_combine_fishtest.py C:\\Users\\marcl\\Downloads\\fishtest_2026_01 --out-dir pgn_decompressed
"""

from __future__ import annotations

import argparse
import gzip
from pathlib import Path


def find_all_pgn_gz(root: Path):
    """Yield all .pgn.gz files under root."""
    return sorted(root.rglob("*.pgn.gz"))


def decompress_and_combine(source_dir: Path, output_pgn: Path) -> int:
    """
    Decompress every .pgn.gz under source_dir and append contents to one output .pgn file.
    Returns total number of .pgn.gz files processed.
    """
    files = find_all_pgn_gz(source_dir)
    if not files:
        print(f"No .pgn.gz files under {source_dir}")
        return 0

    output_pgn.parent.mkdir(parents=True, exist_ok=True)
    count = 0
    with output_pgn.open("wb") as out:
        for p in files:
            try:
                with gzip.open(p, "rb") as f:
                    data = f.read()
            except Exception as e:
                print(f"Skip {p}: {e}")
                continue
            if count > 0:
                out.write(b"\n\n")
            out.write(data)
            count += 1
            if count % 100 == 0:
                print(f"Processed {count} files...")
    print(f"Wrote {count} archives into {output_pgn}")
    return count


def decompress_to_folder(source_dir: Path, out_dir: Path) -> int:
    """
    Decompress every .pgn.gz into out_dir with unique names from relative path.
    Returns number of files written.
    """
    files = find_all_pgn_gz(source_dir)
    if not files:
        print(f"No .pgn.gz files under {source_dir}")
        return 0

    out_dir.mkdir(parents=True, exist_ok=True)
    count = 0
    for p in files:
        try:
            rel = p.relative_to(source_dir)
            # e.g. 26-01-01/test_id/test_id.pgn.gz -> 26-01-01_test_id.pgn
            unique_name = "_".join(rel.parts[:-1]) + "_" + rel.stem + ".pgn"
            unique_name = unique_name.replace(".pgn.pgn", ".pgn")
            dest = out_dir / unique_name
        except Exception as e:
            print(f"Skip {p}: {e}")
            continue
        try:
            with gzip.open(p, "rb") as f:
                data = f.read()
            dest.write_bytes(data)
            count += 1
            if count % 100 == 0:
                print(f"Decompressed {count} files...")
        except Exception as e:
            print(f"Skip {p}: {e}")
    print(f"Decompressed {count} files to {out_dir}")
    return count


def main():
    parser = argparse.ArgumentParser(
        description="Decompress fishtest .pgn.gz and optionally combine or flatten."
    )
    parser.add_argument(
        "source_dir",
        type=Path,
        help="Root folder containing 26-01-XX/test-id/*.pgn.gz",
    )
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument(
        "--out",
        type=Path,
        metavar="FILE",
        help="Write one combined .pgn file (all games concatenated).",
    )
    group.add_argument(
        "--out-dir",
        type=Path,
        metavar="DIR",
        help="Decompress each .pgn.gz into this folder with unique names.",
    )
    args = parser.parse_args()

    if not args.source_dir.exists():
        raise SystemExit(f"Source directory not found: {args.source_dir}")

    if args.out is not None:
        decompress_and_combine(args.source_dir, args.out)
    else:
        decompress_to_folder(args.source_dir, args.out_dir)


if __name__ == "__main__":
    main()
