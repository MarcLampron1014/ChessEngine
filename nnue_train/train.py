from __future__ import annotations

import argparse
import math
import time
from pathlib import Path
from typing import Tuple

import torch
from torch.amp import GradScaler, autocast
from torch.utils.data import DataLoader

from .config import ModelConfig, TrainingConfig
from .data import FenCpTextDataset, build_or_load_index, collate_embedding_bag
from .export import export_txt
from .model import NnueModel


def _parse_args() -> TrainingConfig:
    parser = argparse.ArgumentParser(description="Train NNUE (HalfKP dual-king) for ChessEngine.")
    parser.add_argument("data_path", type=Path, help="Path to training data file (FEN | cp).")
    parser.add_argument("--batch-size", type=int, default=8192)
    parser.add_argument("--epochs", type=int, default=5)
    parser.add_argument("--lr", type=float, default=1e-3)
    parser.add_argument("--weight-decay", type=float, default=0.0)
    parser.add_argument("--cp-clamp", type=int, default=1500)
    parser.add_argument("--cp-scale", type=float, default=400.0)
    parser.add_argument("--val-permille", type=int, default=50, help="Validation share in permille (50 => 5%).")
    parser.add_argument("--num-workers", type=int, default=4)
    parser.add_argument("--device", type=str, default="cuda")
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--log-interval", type=int, default=100)
    parser.add_argument("--out-dir", type=Path, default=Path("nnue_checkpoints"))
    parser.add_argument("--clip-max", type=float, default=1.0, help="Clipped ReLU maximum.")
    parser.add_argument(
        "--small-model",
        action="store_true",
        help="Use a smaller NNUE (reduced hidden dims) for faster experiments.",
    )

    args = parser.parse_args()

    cfg = TrainingConfig(
        data_path=args.data_path,
        batch_size=args.batch_size,
        num_epochs=args.epochs,
        learning_rate=args.lr,
        weight_decay=args.weight_decay,
        cp_clamp=args.cp_clamp,
        cp_scale=args.cp_scale,
        val_permille=args.val_permille,
        num_workers=args.num_workers,
        device=args.device,
        seed=args.seed,
        clip_max=args.clip_max,
        log_interval=args.log_interval,
        out_dir=args.out_dir,
    )

    # Attach non-TrainingConfig flags used for model size tuning.
    # These are stored on the config object so they are visible in checkpoints.
    cfg.small_model = args.small_model  # type: ignore[attr-defined]

    return cfg


def _create_dataloaders(cfg: TrainingConfig) -> Tuple[DataLoader, DataLoader]:
    # Ensure index .npy files exist before creating datasets (dataset only stores paths).
    build_or_load_index(cfg.data_path, cfg.val_permille)

    train_ds = FenCpTextDataset(
        data_path=cfg.data_path,
        split="train",
        cp_clamp=cfg.cp_clamp,
        cp_scale=cfg.cp_scale,
        val_permille=cfg.val_permille,
    )
    val_ds = FenCpTextDataset(
        data_path=cfg.data_path,
        split="val",
        cp_clamp=cfg.cp_clamp,
        cp_scale=cfg.cp_scale,
        val_permille=cfg.val_permille,
    )

    persistent = cfg.num_workers > 0

    train_loader = DataLoader(
        train_ds,
        batch_size=cfg.batch_size,
        shuffle=True,
        num_workers=cfg.num_workers,
        pin_memory=True,
        collate_fn=collate_embedding_bag,
        persistent_workers=persistent,
    )
    val_loader = DataLoader(
        val_ds,
        batch_size=cfg.batch_size,
        shuffle=False,
        num_workers=cfg.num_workers,
        pin_memory=True,
        collate_fn=collate_embedding_bag,
        persistent_workers=persistent,
    )
    return train_loader, val_loader


def train() -> None:
    cfg = _parse_args()
    torch.manual_seed(cfg.seed)

    use_cuda = torch.cuda.is_available() and cfg.device == "cuda"
    device = torch.device("cuda" if use_cuda else "cpu")

    model_cfg = ModelConfig(clip_max=cfg.clip_max)
    # Optionally shrink the model for faster experiments.
    if getattr(cfg, "small_model", False):
        model_cfg.hidden_dim = 192
        model_cfg.hidden_dim2 = 24

    model = NnueModel(model_cfg).to(device)

    optimizer = torch.optim.Adam(
        model.parameters(),
        lr=cfg.learning_rate,
        weight_decay=cfg.weight_decay,
    )
    scaler = GradScaler("cuda") if use_cuda else None

    # On pure CPU training, multiple workers often bring limited benefit and can
    # interact poorly with some Python versions. In that case it's safer to use
    # a single worker.
    if not use_cuda and cfg.num_workers > 0:
        cfg.num_workers = 0

    train_loader, val_loader = _create_dataloaders(cfg)
    total_train_samples = len(train_loader.dataset)

    cfg.out_dir.mkdir(parents=True, exist_ok=True)
    best_val_rmse = math.inf

    global_step = 0
    for epoch in range(1, cfg.num_epochs + 1):
        epoch_start = time.perf_counter()
        model.train()
        running_loss = 0.0
        running_count = 0

        for batch_idx, (indices, offsets, targets) in enumerate(train_loader, start=1):
            indices = indices.to(device, non_blocking=True)
            offsets = offsets.to(device, non_blocking=True)
            targets = targets.to(device, non_blocking=True)

            optimizer.zero_grad(set_to_none=True)
            if use_cuda:
                with autocast(device_type="cuda", enabled=True):
                    preds = model(indices, offsets)
                    loss = torch.mean((preds - targets) ** 2)
                assert scaler is not None
                scaler.scale(loss).backward()
                scaler.step(optimizer)
                scaler.update()
            else:
                preds = model(indices, offsets)
                loss = torch.mean((preds - targets) ** 2)
                loss.backward()
                optimizer.step()

            batch_size = targets.shape[0]
            running_loss += float(loss.item()) * batch_size
            running_count += batch_size
            global_step += 1

            if batch_idx % cfg.log_interval == 0:
                mean_loss = running_loss / max(1, running_count)
                rmse_norm = math.sqrt(mean_loss)
                rmse_cp = rmse_norm * cfg.cp_scale
                samples_done = (batch_idx - 1) * cfg.batch_size + batch_size
                pct_epoch = 100.0 * min(samples_done, total_train_samples) / total_train_samples
                print(
                    f"Epoch {epoch} Step {global_step} "
                    f"Train MSE={mean_loss:.6f} RMSE_cp={rmse_cp:.2f} "
                    f"({pct_epoch:.1f}% of epoch)"
                )
                running_loss = 0.0
                running_count = 0

        # Validation
        model.eval()
        val_loss = 0.0
        val_count = 0
        with torch.no_grad():
            for indices, offsets, targets in val_loader:
                indices = indices.to(device, non_blocking=True)
                offsets = offsets.to(device, non_blocking=True)
                targets = targets.to(device, non_blocking=True)

                preds = model(indices, offsets)
                loss = torch.mean((preds - targets) ** 2)

                batch_size = targets.shape[0]
                val_loss += float(loss.item()) * batch_size
                val_count += batch_size

        epoch_seconds = time.perf_counter() - epoch_start

        mean_val_loss = val_loss / max(1, val_count)
        rmse_norm = math.sqrt(mean_val_loss)
        rmse_cp = rmse_norm * cfg.cp_scale
        print(
            f"Epoch {epoch} "
            f"Validation MSE={mean_val_loss:.6f} RMSE_cp={rmse_cp:.2f} "
            f"(epoch_time={epoch_seconds:.1f}s)"
        )

        # Checkpoint
        ckpt_path = cfg.out_dir / f"nnue_epoch{epoch}.pt"
        torch.save(
            {
                "epoch": epoch,
                "model_state": model.state_dict(),
                "optimizer_state": optimizer.state_dict(),
                "scaler_state": scaler.state_dict(),
                "training_config": cfg.__dict__,
            },
            ckpt_path,
        )

        if mean_val_loss < best_val_rmse:
            best_val_rmse = mean_val_loss
            best_path = cfg.out_dir / "nnue_best.pt"
            torch.save(model.state_dict(), best_path)

    # Final export of the last model
    txt_path = cfg.out_dir / "nnue_weights.txt"
    meta_path = cfg.out_dir / "nnue_weights_meta.json"
    export_txt(model, txt_path, meta_path, cp_scale=cfg.cp_scale)


if __name__ == "__main__":
    train()

