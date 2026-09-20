"""Tick the actual Refactored character in a transient physical world; no saves."""
import json
import os
import time
from pathlib import Path

import unreal

for item in json.loads(os.environ["ALS_CHARACTER_PLATFORM_REPLAYS"]):
    source, output = Path(item["input"]), Path(item["output"])
    if not source.is_absolute() or not source.is_file() or not output.is_absolute() or output.exists():
        raise ValueError("Existing absolute input and fresh absolute output required")
    if not unreal.AlsAnimationGraphLibrary.replay_refactored_character_platform(str(source), str(output)):
        raise RuntimeError("Original Character/AnimBP/physics replay failed")
    expected = json.loads(source.read_text(encoding="utf-8"))
    result = json.loads(output.read_text(encoding="utf-8"))
    assert result["schemaVersion"] == 1 and len(result["frames"]) == len(expected) == 360
    assert all(a["frame"] == b["Frame"] for a, b in zip(result["frames"], expected))
    assert all(len(row["components"]) == len(result["names"]) for row in result["frames"])
    assert sum(row["onExpectedBase"] for row in result["frames"]) >= 300
    assert sum(row["valid"] and row["left"]["amount"] > .999 and row["right"]["amount"] > .999
               for row in result["frames"]) >= 60
    unreal.log(f"ALS_CHARACTER_PLATFORM_OK frames=360 bones={len(result['names'])} assets_saved=0 output={output}")

if os.environ.get("ALS_CHARACTER_PLATFORM_QUIT") == "1":
    _deadline = time.monotonic() + 10
    def _finish(_delta):
        if time.monotonic() >= _deadline:
            unreal.unregister_slate_post_tick_callback(_handle)
            unreal.SystemLibrary.quit_editor()
    _handle = unreal.register_slate_post_tick_callback(_finish)
