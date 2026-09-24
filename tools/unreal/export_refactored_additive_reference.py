"""Original local/mesh additive sequence poses and curves for the source catalog."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(__file__).parents[2] / "assets/config"
index_bytes = (root / "refactored_animation_sources.json").read_bytes()
index = json.loads(index_bytes)
output = Path(os.environ["ALS_REFACTORED_ADDITIVE_REFERENCE"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
rows = []
for entry in index["assets"]:
    if entry["class"] != "AnimSequence":
        continue
    data = (root / entry["file"]).read_bytes()
    if hashlib.sha256(data).hexdigest() != entry["sha256"]:
        raise ValueError("Source payload digest differs")
    payload = json.loads(data)
    if payload["evaluation"]["additiveType"] == "AAT_None":
        continue
    sequence = unreal.load_asset(entry["source"])
    length = payload["raw"]["playLength"]
    poses = [json.loads(unreal.AlsSourceAnimationLibrary.read_raw_animation_pose(
        sequence, length * fraction, True, False, False)) for fraction in (0, .137, .5, .773, 1)]
    rows.append({"source": entry["source"], "poses": poses})
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps({"schemaVersion": 1, "catalogSha256": hashlib.sha256(index_bytes).hexdigest(), "sequences": rows},
                            separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log(f"ALS_REFACTORED_ADDITIVE_REFERENCE_OK sequences={len(rows)} poses={sum(len(r['poses']) for r in rows)} assets_saved=0")
if os.environ.get("ALS_REFACTORED_ADDITIVE_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)
