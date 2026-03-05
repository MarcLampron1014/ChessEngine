# continue_train_part2.py
from __future__ import annotations

from pathlib import Path
import math
import time
from typing import Tuple

import torch
from torch.amp import GradScaler, autocast
from torch.utils.data import DataLoader

from nnue_train.config import TrainingConfig, ModelConfig
from nnue_train.data import FenCpTextDataset, collate_embedding_bag, build_or_load_index
from nnue_train.export import export_txt
from nnue_train.model import NnueModel


def _create_dataloaders(cfg: TrainingConfig) -> Tuple[DataLoader, DataLoader]:
    # Ensure index files for this new data_path exist
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


def main() -> None:
    # 1) Load checkpoint from part 1 (use full epoch checkpoint, not nnue_best.pt)
    ckpt_path = Path("nnue_checkpoints/nnue_epoch3.pt")
    ckpt = torch.load(ckpt_path, map_location="cpu")

    # 2) Build TrainingConfig for part 2 using original config + overrides
    cfg_dict = ckpt["training_config"].copy()
    # Remove helper flag not part of TrainingConfig signature
    cfg_dict.pop("small_model", None)
    cfg_dict["data_path"] = Path(
        r"C:\Users\marcl\ChessEngine\datasets\cleaned\positions_fishtest_part2.txt"
    )
    # How many epochs you want to run on part 2:
    cfg_dict["num_epochs"] = 3   # or more/less as you like

    cfg = TrainingConfig(**cfg_dict)

    torch.manual_seed(cfg.seed)

    use_cuda = torch.cuda.is_available() and cfg.device == "cuda"
    device = torch.device("cuda" if use_cuda else "cpu")

    # 3) Recreate model and load weights
    model_cfg = ModelConfig(clip_max=cfg.clip_max)
    if getattr(cfg, "small_model", False):
        model_cfg.hidden_dim = 192
        model_cfg.hidden_dim2 = 24

    model = NnueModel(model_cfg).to(device)
    model.load_state_dict(ckpt["model_state"])

    # 4) Recreate optimizer and scaler, resume their states
    optimizer = torch.optim.Adam(
        model.parameters(),
        lr=cfg.learning_rate,
        weight_decay=cfg.weight_decay,
    )
    optimizer.load_state_dict(ckpt["optimizer_state"])

    scaler = GradScaler("cuda") if use_cuda else None
    if use_cuda and "scaler_state" in ckpt and ckpt["scaler_state"] is not None:
        scaler.load_state_dict(ckpt["scaler_state"])

    # 5) Dataloader setup (same logic as in train.py)
    if not use_cuda and cfg.num_workers > 0:
        cfg.num_workers = 0

    train_loader, val_loader = _create_dataloaders(cfg)
    total_train_samples = len(train_loader.dataset)

    cfg.out_dir.mkdir(parents=True, exist_ok=True)
    best_val_loss = math.inf
    global_step = 0
    start_epoch = 1  # fresh count for part 2; you can also store/use ckpt["epoch"] if you want

    for epoch in range(start_epoch, cfg.num_epochs + 1):
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
                assert scaler is not None
                with autocast(device_type="cuda", enabled=True):
                    preds = model(indices, offsets)
                    loss = torch.mean((preds - targets) ** 2)
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

        # Validation on part 2
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

        # Save checkpoint for part 2 training
        ckpt_path_out = cfg.out_dir / f"nnue_part2_epoch{epoch}.pt"
        torch.save(
            {
                "epoch": epoch,
                "model_state": model.state_dict(),
                "optimizer_state": optimizer.state_dict(),
                "scaler_state": scaler.state_dict() if scaler is not None else None,
                "training_config": cfg.__dict__,
            },
            ckpt_path_out,
        )

        if mean_val_loss < best_val_loss:
            best_val_loss = mean_val_loss
            best_path = cfg.out_dir / "nnue_part2_best.pt"
            torch.save(model.state_dict(), best_path)

    # Optionally export final model from part 2 training
    txt_path = cfg.out_dir / "nnue_part2_weights.txt"
    meta_path = cfg.out_dir / "nnue_part2_weights_meta.json"
    export_txt(model, txt_path, meta_path, cp_scale=cfg.cp_scale)


if __name__ == "__main__":
    main()