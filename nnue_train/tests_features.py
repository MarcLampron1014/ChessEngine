from __future__ import annotations

"""
Simple sanity checks for HalfKP feature mapping.

These tests are intended to be run manually, e.g.:

    python -m nnue_train.tests_features
"""

from .features_halfkp import FEATURES_PER_KING, TOTAL_FEATURES, PLANE_ORDER, golden_fen_indices


def run_tests() -> None:
    assert len(PLANE_ORDER) == 10, "Expected 10 piece planes for HalfKP."
    assert FEATURES_PER_KING == 64 * len(PLANE_ORDER) * 64, "Unexpected FEATURES_PER_KING."
    assert TOTAL_FEATURES == 2 * FEATURES_PER_KING, "Unexpected TOTAL_FEATURES."

    indices = golden_fen_indices()
    assert indices == sorted(indices), "Golden indices should be sorted."
    assert len(indices) % 2 == 0, "Each piece should contribute two indices (dual-king)."
    assert all(0 <= i < TOTAL_FEATURES for i in indices), "Indices out of range."

    print("All HalfKP feature tests passed.")


if __name__ == "__main__":
    run_tests()

