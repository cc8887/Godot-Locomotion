"""Native absolute/local-additive 2D blended poses, using actual original assets."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(__file__).parents[2] / "assets/config"
inputs_bytes = (root / "refactored_triangulation_inputs.json").read_bytes()
inputs = json.loads(inputs_bytes)
rows = []
for entry in inputs["spaces"]:
    path = entry["source"]
    asset = unreal.load_asset(path)
    lean = "/Lean/" in path
    points = [(0, 0), (1, 0), (0, 1), (1, 1), (.5, .5), (.137, .773), (.25, .25), (.75, .75), (-.2, .5), (.5, 1.2)]
    poses = []
    for x, y in points:
        for time in (0, .137, .5, .773, 1):
            poses.append(json.loads(unreal.AlsSourceAnimationLibrary.read_raw_blend_space_pose2d(
                asset, x*2-1 if lean else x, y*2-1 if lean else y, time)))
    rows.append({"source": path, "poses": poses})
output = Path(os.environ["ALS_REFACTORED_BLEND_POSES_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps({"schemaVersion": 1, "inputsSha256": hashlib.sha256(inputs_bytes).hexdigest(), "spaces": rows},
                            separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log(f"ALS_REFACTORED_BLEND_POSES_OK spaces={len(rows)} poses={sum(len(r['poses']) for r in rows)} assets_saved=0")
if os.environ.get("ALS_REFACTORED_BLEND_POSES_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)
