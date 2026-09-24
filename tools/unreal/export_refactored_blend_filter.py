"""Native original FilterInput followed by cached triangle evaluation."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(__file__).parents[2] / "assets/config"
inputs_bytes = (root / "refactored_triangulation_inputs.json").read_bytes()
spaces = json.loads(inputs_bytes)["spaces"]
rows = []
for space in spaces:
    runs = []
    for hz in (30, 60, 120):
        frames = []
        for frame in range(hz * 4):
            time = frame / hz
            x = -.2 if time < .5 else .9 if time < 1.7 else .137
            y = 1.2 if time < 1 else -.3 if time < 2.5 else .773
            delta = 0 if frame % 47 == 0 else .00005 if frame % 53 == 0 else 1/hz
            frames.append([x, y, delta, frame == hz * 2])
        value = json.loads(unreal.AlsSourceAnimationLibrary.read_blend_space_triangulation_reference(
            unreal.load_asset(space["source"]), json.dumps({"filter": True, "inputs": frames})))
        runs.append({"hz": hz, "cases": value["cases"]})
    rows.append({"source": space["source"], "runs": runs})
output = Path(os.environ["ALS_REFACTORED_BLEND_FILTER_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps({"schemaVersion": 1, "inputsSha256": hashlib.sha256(inputs_bytes).hexdigest(), "spaces": rows},
                            separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log("ALS_REFACTORED_BLEND_FILTER_OK spaces=8 frames=6720 assets_saved=0")
if os.environ.get("ALS_REFACTORED_BLEND_FILTER_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)
