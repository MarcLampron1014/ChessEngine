from huggingface_hub import snapshot_download

snapshot_download(
    repo_id="official-stockfish/fishtest_pgns",
    repo_type="dataset",
    allow_patterns="26-01-*/*/*.pgn.gz",
    local_dir="C:\\Users\\marcl\\Downloads\\fishtest_2026_01"
)