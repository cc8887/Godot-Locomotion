"""Actual FAnimSync tick trace for original Refactored assets."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(__file__).parents[2] / "assets/config"
output = Path(os.environ["ALS_REFACTORED_SYNC_TRACE_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
output.parent.mkdir(parents=True, exist_ok=True)
if not unreal.AlsSourceAnimationLibrary.export_refactored_sync_trace(str(output)):
    raise RuntimeError("Native Sync trace failed")
value = json.loads(output.read_text(encoding="utf-8"))
value["syncInputsSha256"] = hashlib.sha256((root / "refactored_sync_inputs.json").read_bytes()).hexdigest()
assets = value["assets"]
pose_count = 0
for trace in value["traces"]:
    for frame_index in (0, 16, 40, 63):
        frame = trace["frames"][frame_index]
        for row in frame["output"]:
            if not row["samples"]:
                continue
            path = assets[8 if trace["scenario"] == 6 else row["slot"]]["path"]
            row["poseReference"] = json.loads(unreal.AlsSourceAnimationLibrary.read_raw_blend_space_timed_pose(
                unreal.load_asset(path), json.dumps({"samples": row["samples"]})))
            pose_count += 1
output.write_text(json.dumps(value, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log(f"ALS_REFACTORED_SYNC_TRACE_OK assets=9 traces=21 frames=1344 poses={pose_count} assets_saved=0")
if os.environ.get("ALS_REFACTORED_SYNC_TRACE_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)
