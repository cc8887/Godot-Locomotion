"""Replay Godot platform frames through original ALS nodes without saving assets."""
import json
import os
import time
from pathlib import Path
import unreal

requests = json.loads(os.environ["ALS_FOOT_LOCATION_REPLAYS"])
for request in requests:
    source, output = Path(request["input"]), Path(request["output"])
    if not source.is_absolute() or not source.is_file() or not output.is_absolute() or output.exists():
        raise ValueError("Replay requires existing absolute input and a fresh absolute output")
    if not unreal.AlsAnimationGraphLibrary.replay_foot_offset_locations(str(source), str(output)):
        raise RuntimeError("Native foot location replay failed")
    expected = json.loads(source.read_text(encoding="utf-8"))
    actual = json.loads(output.read_text(encoding="utf-8"))
    assert actual["schemaVersion"] == 1 and len(actual["frames"]) == len(expected)
    assert all(a["frame"] == b["Frame"] for a, b in zip(actual["frames"], expected))
    unreal.log(f"ALS_FOOT_LOCATION_REPLAY_OK frames={len(expected)} output={output} assets_saved=0")

if os.environ.get("ALS_FOOT_LOCATION_REPLAY_QUIT") == "1":
    _deadline = time.monotonic() + 10
    def _finish(_delta):
        if time.monotonic() < _deadline:
            return
        unreal.unregister_slate_post_tick_callback(_handle)
        unreal.SystemLibrary.quit_editor()
    _handle = unreal.register_slate_post_tick_callback(_finish)
