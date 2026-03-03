from __future__ import annotations

from dataclasses import dataclass
from typing import List, Optional


FILES = "abcdefgh"
RANKS = "12345678"


@dataclass
class PieceOnSquare:
    color: str  # "w" or "b"
    kind: str  # "P", "N", "B", "R", "Q", "K"
    square: int  # 0..63, using A1 = 0, H8 = 63


@dataclass
class ParsedFen:
    pieces: List[PieceOnSquare]
    white_king_sq: int
    black_king_sq: int
    side_to_move: str  # "w" or "b"


def _square_index(file_idx: int, rank_idx: int) -> int:
    """
    Convert (file, rank) to 0..63 index using A1 = 0, H8 = 63.

    file_idx: 0..7 (a..h)
    rank_idx: 0..7 (1..8), where 0 corresponds to rank 1.
    """

    return rank_idx * 8 + file_idx


def parse_fen(fen: str) -> ParsedFen:
    """
    Parse a FEN string into a simple piece list representation.

    Only fields relevant for NNUE features are extracted:
    - Piece placement
    - Side to move
    """

    parts = fen.strip().split()
    if len(parts) < 4:
        raise ValueError(f"Invalid FEN (expected at least 4 fields): {fen!r}")

    board_part, stm_part = parts[0], parts[1]
    side_to_move = stm_part.lower()
    if side_to_move not in ("w", "b"):
        raise ValueError(f"Invalid side to move in FEN: {fen!r}")

    pieces: List[PieceOnSquare] = []
    white_king_sq: Optional[int] = None
    black_king_sq: Optional[int] = None

    ranks = board_part.split("/")
    if len(ranks) != 8:
        raise ValueError(f"Invalid FEN (expected 8 ranks): {fen!r}")

    # FEN ranks go from 8 down to 1.
    for fen_rank_idx, rank_str in enumerate(ranks):
        rank_from_top = 7 - fen_rank_idx  # 7..0, where 0 is rank 1
        file_idx = 0
        for ch in rank_str:
            if ch.isdigit():
                file_idx += int(ch)
                continue

            if file_idx < 0 or file_idx > 7:
                raise ValueError(f"Invalid file index while parsing FEN: {fen!r}")

            color = "w" if ch.isupper() else "b"
            kind = ch.upper()
            if kind not in ("P", "N", "B", "R", "Q", "K"):
                raise ValueError(f"Unexpected piece kind {kind!r} in FEN: {fen!r}")

            sq = _square_index(file_idx, rank_from_top)
            piece = PieceOnSquare(color=color, kind=kind, square=sq)
            pieces.append(piece)

            if kind == "K":
                if color == "w":
                    white_king_sq = sq
                else:
                    black_king_sq = sq

            file_idx += 1

        if file_idx != 8:
            raise ValueError(f"Rank does not have exactly 8 files in FEN: {fen!r}")

    if white_king_sq is None or black_king_sq is None:
        raise ValueError(f"Both kings must be present in FEN: {fen!r}")

    return ParsedFen(
        pieces=pieces,
        white_king_sq=white_king_sq,
        black_king_sq=black_king_sq,
        side_to_move=side_to_move,
    )


def main() -> None:
    """
    Small manual test when running this module directly.
    """

    example = "r1bqkbnr/pppp1ppp/2n5/4p3/3PP3/5N2/PPP2PPP/RNBQKB1R b KQkq - 0 4"
    parsed = parse_fen(example)
    print("Parsed FEN:")
    print(f"  Side to move: {parsed.side_to_move}")
    print(f"  White king square: {parsed.white_king_sq}")
    print(f"  Black king square: {parsed.black_king_sq}")
    print(f"  Pieces ({len(parsed.pieces)}):")
    for p in parsed.pieces:
        print(f"    {p.color}{p.kind} @ {p.square}")


if __name__ == "__main__":
    main()

