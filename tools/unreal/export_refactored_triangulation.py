"""Read actual 2D topology and cached native weight queries; never save assets."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(__file__).parents[2] / "assets/config"
index_bytes = (root / "refactored_animation_sources.json").read_bytes()
index = json.loads(index_bytes)
rows = []
for entry in index["assets"]:
    if entry["class"] != "BlendSpace":
        continue
    lean = "/Lean/" in entry["source"]
    # Corners, perimeter, triangles, near-edge tolerance and zero-weight threshold.
    values = [-.2, 0, .000001, .00001, .0001, .137, .25, .49999, .5, .50001, .75, .863, .9999, .99999, 1, 1.2]
    inputs = [[2*x-1 if lean else x, 2*y-1 if lean else y] for y in values for x in values]
    inputs += list(reversed(inputs))
    result = json.loads(unreal.AlsSourceAnimationLibrary.read_blend_space_triangulation_reference(
        unreal.load_asset(entry["source"]), json.dumps({"inputs": inputs})))
    rows.append(result)
output = Path(os.environ["ALS_REFACTORED_TRIANGULATION_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
output.parent.mkdir(parents=True, exist_ok=True)
inputs = {"schemaVersion": 1, "catalogSha256": hashlib.sha256(index_bytes).hexdigest(),
          "spaces": [{"source": r["source"], "data": r["data"]} for r in rows]}
input_bytes = (json.dumps(inputs, separators=(",", ":"), allow_nan=False) + "\n").encode("utf-8")
output.with_name("refactored_triangulation_inputs.json").write_bytes(input_bytes)
reference = {"schemaVersion": 1, "inputsSha256": hashlib.sha256(input_bytes).hexdigest(),
             "spaces": [{"source": r["source"], "cases": r["cases"]} for r in rows]}
output.write_text(json.dumps(reference, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log(f"ALS_REFACTORED_TRIANGULATION_OK spaces={len(rows)} cases={sum(len(r['cases']) for r in rows)} assets_saved=0")
if os.environ.get("ALS_REFACTORED_TRIANGULATION_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)
