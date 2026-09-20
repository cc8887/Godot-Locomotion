"""Execute the original CR_Als VM with real collision and captured pre-rig inputs."""
import json
import os
import time
from pathlib import Path
import unreal

for item in json.loads(os.environ["ALS_FOOT_RIG_REPLAYS"]):
    source, output = Path(item["input"]), Path(item["output"])
    if not source.is_absolute() or not source.is_file() or not output.is_absolute() or output.exists():
        raise ValueError("Existing absolute input and fresh absolute output required")
    closed = bool(item.get("closedFeedback", False))
    if not unreal.AlsAnimationGraphLibrary.replay_refactored_foot_rig(str(source), str(output), closed):
        raise RuntimeError("Original CR_Als VM replay failed")
    expected = json.loads(source.read_text(encoding="utf-8"))
    result = json.loads(output.read_text(encoding="utf-8"))
    assert result["schemaVersion"] == 2 and len(result["frames"]) == len(expected)
    assert result["closedFeedback"] == closed
    if closed:
        assert all("feedback" in row and "finalCurves" in row and "sole" in row for row in result["frames"])
    assert all(len(row["inputComponents"]) == len(result["names"]) for row in result["frames"])
    assert all(a["frame"] == b["Frame"] for a, b in zip(result["frames"], expected))
    unreal.log(f"ALS_FOOT_RIG_REPLAY_OK frames={len(expected)} bones={len(result['names'])} assets_saved=0 output={output}")

if os.environ.get("ALS_FOOT_RIG_REPLAY_QUIT") == "1":
    _deadline = time.monotonic() + 10
    def _finish(_delta):
        if time.monotonic() >= _deadline:
            unreal.unregister_slate_post_tick_callback(_handle)
            unreal.SystemLibrary.quit_editor()
    _handle = unreal.register_slate_post_tick_callback(_finish)
