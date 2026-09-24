"""Read original rich-curve keys and separate raw-pose curve reference samples. Never saves assets."""
import hashlib
import json
import os
from pathlib import Path
import unreal

repository = Path(__file__).parents[2]
output = Path(os.environ["ALS_MANTLE_CURVES_OUTPUT"])
oracle = Path(os.environ["ALS_MANTLE_CURVES_REFERENCE"])
if not output.is_absolute() or not oracle.is_absolute() or output == oracle:
    raise ValueError("Distinct absolute curve output paths required")
source_bytes = (repository / "assets/config/refactored_mantle_animation_inputs.json").read_bytes()
source = json.loads(source_bytes)
curves, references = [], []
for row in source["sequences"]:
    asset = unreal.load_asset(row["raw"]["source"])
    if not isinstance(asset, unreal.AnimSequence):
        raise ValueError("Missing mantle sequence")
    curves.append(json.loads(unreal.AlsSourceAnimationLibrary.read_source_float_curves(asset)))
    samples = []
    for index in range(121):
        time = row["raw"]["playLength"] * index / 120
        pose = json.loads(unreal.AlsSourceAnimationLibrary.read_raw_bone_pose(asset, time, True, False, False))
        samples.append({"requestedTime": time, "timeSeconds": pose["timeSeconds"], "curves": pose["curves"]})
    references.append({"source": asset.get_path_name(), "samples": samples})

def write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")

write(output, {"schemaVersion": 1, "animationInputsSha256": hashlib.sha256(source_bytes).hexdigest(), "sequences": curves})
write(oracle, {"schemaVersion": 1, "curvesSha256": hashlib.sha256(output.read_bytes()).hexdigest(), "sequences": references})
unreal.log("ALS_MANTLE_CURVES_OK sources=3 samples=363 assets_saved=0")
if os.environ.get("ALS_MANTLE_CURVES_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)
