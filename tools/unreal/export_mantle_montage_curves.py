"""Read original Montage-owned curves and native evaluations; never save assets."""
import hashlib
import json
import os
from pathlib import Path
import unreal

repository = Path(__file__).parents[2]
output = Path(os.environ["ALS_MANTLE_MONTAGE_CURVES_OUTPUT"])
oracle = Path(os.environ["ALS_MANTLE_MONTAGE_CURVES_REFERENCE"])
if not output.is_absolute() or not oracle.is_absolute() or output == oracle:
    raise ValueError("Distinct absolute output paths required")
source_bytes = (repository / "assets/config/refactored_mantle_animation_inputs.json").read_bytes()
source = json.loads(source_bytes)
curves, references = [], []
for row in source["montages"]:
    asset = unreal.load_asset(row["path"])
    if not isinstance(asset, unreal.AnimMontage):
        raise ValueError("Missing mantle montage")
    curves.append(json.loads(unreal.AlsSourceAnimationLibrary.read_source_float_curves(asset)))
    samples = [json.loads(unreal.AlsSourceAnimationLibrary.read_asset_float_curve_values(
        asset, row["length"] * index / 120)) for index in range(121)]
    references.append({"source": asset.get_path_name(), "samples": samples})

def write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")

write(output, {"schemaVersion": 1, "animationInputsSha256": hashlib.sha256(source_bytes).hexdigest(), "montages": curves})
write(oracle, {"schemaVersion": 1, "curvesSha256": hashlib.sha256(output.read_bytes()).hexdigest(), "montages": references})
unreal.log("ALS_MANTLE_MONTAGE_CURVES_OK montages=6 samples=726 assets_saved=0")
if os.environ.get("ALS_MANTLE_MONTAGE_CURVES_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)
