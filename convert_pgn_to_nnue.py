"""
Convert PGN files with move evals to FEN | cp training format.

Supports:
- Stockfish test style: {eval/depth time} e.g. {-0.13/21 3.5s}
- Lichess style: [%eval 0.17] (no depth filter for this format)

Output: one line per position, "FEN | centipawn_score" (white POV).
Requires: pip install python-chess
"""

from __future__ import annotations

import argparse
import gzip
import io
import re
from pathlib import Path
from typing import TextIO

import chess
import chess.pgn

# Stockfish test format: {eval/depth time} or eval/depth time (python-chess strips braces)
STOCKFISH_COMMENT_RE = re.compile(r"\{?([+-]?\d+\.?\d*)/(\d+)\s")

# Lichess/Chess.com format: [%eval 0.17] or [%eval -0.35]
LICHESS_EVAL_RE = re.compile(r"\[%eval\s+([^\]]+)\]")


def _parse_lichess_eval(token: str) -> int | None:
    """Parse [%eval ...] token to centipawns. Returns None for mate or invalid."""
    token = token.strip()
    if token.startswith("#"):
        return None
    try:
        pawns = float(token)
    except ValueError:
        return None
    return int(round(pawns * 100.0))


def _extract_lichess_cp(comment: str) -> int | None:
    m = LICHESS_EVAL_RE.search(comment)
    if not m:
        return None
    return _parse_lichess_eval(m.group(1))


def _extract_stockfish_cp_and_depth(comment: str) -> tuple[int | None, int | None]:
    """
    Parse Stockfish-style {eval/depth time}. Returns (cp, depth) or (None, None).
    Mate scores (#3, #-5) are skipped (return None).
    """
    m = STOCKFISH_COMMENT_RE.search(comment)
    if not m:
        return None, None
    eval_str, depth_str = m.group(1), m.group(2)
    if "#" in eval_str:
        return None, None
    try:
        pawns = float(eval_str)
        depth = int(depth_str)
    except ValueError:
        return None, None
    cp = int(round(pawns * 100.0))
    return cp, depth


def _open_pgn(path: Path):
    """Open a PGN file, decompressing if path ends with .gz."""
    if path.suffix == ".gz" or path.name.endswith(".pgn.gz"):
        return io.TextIOWrapper(
            gzip.open(path, "rb"), encoding="utf-8", errors="ignore"
        )
    return path.open("r", encoding="utf-8", errors="ignore")


def process_pgn(
    pgn_path: Path,
    out_file: TextIO,
    *,
    max_positions: int | None = None,
    min_depth: int = 0,
    cp_clamp: int = 1500,
) -> int:
    """
    Read PGN (or .pgn.gz), extract positions with evals, write "FEN | cp" lines.
    Tries Stockfish format first, then Lichess. Applies cp_clamp and optional min_depth.
    """
    num_positions = 0
    with _open_pgn(pgn_path) as f:
        while True:
            game = chess.pgn.read_game(f)
            if game is None:
                break

            board = game.board()
            for node in game.mainline():
                move = node.move
                board.push(move)
                comment = node.comment or ""

                cp = None
                depth = None

                cp, depth = _extract_stockfish_cp_and_depth(comment)
                if cp is not None and (min_depth <= 0 or (depth is not None and depth >= min_depth)):
                    pass
                elif cp is None or (min_depth > 0 and (depth is None or depth < min_depth)):
                    cp = _extract_lichess_cp(comment)
                    if cp is None:
                        continue

                if cp is None:
                    continue

                cp = max(-cp_clamp, min(cp_clamp, cp))
                fen = board.fen()
                out_file.write(f"{fen} | {cp}\n")
                num_positions += 1

                if max_positions is not None and num_positions >= max_positions:
                    return num_positions

    return num_positions


def _parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Convert PGN with move evals (Stockfish or Lichess style) to FEN | cp for NNUE training."
    )
    parser.add_argument("input_pgn", type=Path, help="Input PGN file")
    parser.add_argument("output_txt", type=Path, help="Output text file (FEN | cp per line)")
    parser.add_argument("--max-positions", type=int, default=None, help="Cap number of positions written")
    parser.add_argument("--min-depth", type=int, default=0, help="Only output positions with depth >= this (Stockfish format)")
    parser.add_argument("--cp-clamp", type=int, default=1500, help="Clamp centipawns to +/- this value")
    return parser.parse_args()


def main() -> None:
    args = _parse_args()
    if not args.input_pgn.exists():
        raise SystemExit(f"Input file not found: {args.input_pgn}")

    args.output_txt.parent.mkdir(parents=True, exist_ok=True)
    with args.output_txt.open("w", encoding="utf-8") as out:
        total = process_pgn(
            args.input_pgn,
            out,
            max_positions=args.max_positions,
            min_depth=args.min_depth,
            cp_clamp=args.cp_clamp,
        )
    print(f"Wrote {total} positions to {args.output_txt}")


if __name__ == "__main__":
    main()
