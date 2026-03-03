from __future__ import annotations

from typing import List

from .fen import ParsedFen, parse_fen


# Plane order is fixed and must be kept in sync with the engine:
# 0: WP, 1: WN, 2: WB, 3: WR, 4: WQ,
# 5: BP, 6: BN, 7: BB, 8: BR, 9: BQ.
PLANE_ORDER = (
    ("w", "P"),
    ("w", "N"),
    ("w", "B"),
    ("w", "R"),
    ("w", "Q"),
    ("b", "P"),
    ("b", "N"),
    ("b", "B"),
    ("b", "R"),
    ("b", "Q"),
)

PLANE_INDEX = {key: idx for idx, key in enumerate(PLANE_ORDER)}

FEATURES_PER_KING = 64 * len(PLANE_ORDER) * 64  # 40_960
TOTAL_FEATURES = 2 * FEATURES_PER_KING  # 81_920


def _plane_index(color: str, kind: str) -> int:
    key = (color, kind)
    if kind == "K":
        raise ValueError("Kings are not encoded as HalfKP piece features.")
    try:
        return PLANE_INDEX[key]
    except KeyError as exc:
        raise ValueError(f"Unexpected piece for HalfKP features: {color}{kind}") from exc


def halfkp_indices_from_parsed(parsed: ParsedFen) -> List[int]:
    """
    Compute dual-king HalfKP feature indices from a parsed FEN.

    Conventions:
    - Uses engine-aligned square indices (A1 = 0, H8 = 63).
    - No board rotation is applied for the black king context.
    - For each non-king piece on square `sq`:
      - plane index: `plane = piece_plane * 64 + sq`
      - white-king index:
          idx_w = 0 * FEATURES_PER_KING + wk * (len(PLANE_ORDER) * 64) + plane
      - black-king index:
          idx_b = 1 * FEATURES_PER_KING + bk * (len(PLANE_ORDER) * 64) + plane
    """

    wk = parsed.white_king_sq
    bk = parsed.black_king_sq
    per_king_stride = len(PLANE_ORDER) * 64

    indices: List[int] = []
    for p in parsed.pieces:
        if p.kind == "K":
            continue
        plane = _plane_index(p.color, p.kind)
        ps = plane * 64 + p.square
        idx_w = 0 * FEATURES_PER_KING + wk * per_king_stride + ps
        idx_b = 1 * FEATURES_PER_KING + bk * per_king_stride + ps
        indices.append(idx_w)
        indices.append(idx_b)

    return indices


def halfkp_indices_from_fen(fen: str) -> List[int]:
    """
    Convenience wrapper: parse FEN and return HalfKP indices.
    """

    parsed = parse_fen(fen)
    return halfkp_indices_from_parsed(parsed)


def golden_fen_indices() -> List[int]:
    """
    Compute a deterministic set of indices for a single golden FEN.

    This function is intended as a cross-language reference: the C# NNUE
    feature extractor should reproduce exactly the same set of indices
    for this FEN before NNUE is trusted in search.
    """

    fen = "r1bqkbnr/pppp1ppp/2n5/4p3/3PP3/5N2/PPP2PPP/RNBQKB1R b KQkq - 0 4"
    return sorted(halfkp_indices_from_fen(fen))


def main() -> None:
    """
    Print golden FEN indices for manual inspection / copying into tests.
    """

    indices = golden_fen_indices()
    print(f"Golden FEN has {len(indices)} active indices.")
    print("First 64 indices:", indices[:64])
    print("Last 64 indices:", indices[-64:])


if __name__ == "__main__":
    main()

