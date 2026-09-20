"""Read actual Refactored settings/curve and probe RefreshInAir in a temporary world."""
import json
import os
from pathlib import Path
import unreal

output = Path(os.environ["ALS_GROUND_PREDICTION_OUTPUT"])
if not output.is_absolute() or output.exists():
    raise ValueError("A fresh absolute output directory is required")
output.mkdir(parents=True)


def native_text(asset, name):
    target = output / name
    task = unreal.AssetExportTask()
    task.object = asset
    task.exporter = unreal.ObjectExporterT3D()
    task.filename = str(target)
    task.automated = True
    task.prompt = False
    if not unreal.Exporter.run_asset_export_task(task):
        raise RuntimeError("Native export failed: " + asset.get_path_name())
    data = target.read_bytes()
    return data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")


animation_class = unreal.load_class(None, "/ALS/ALS/Character/AB_Als.AB_Als_C")
settings = unreal.get_default_object(animation_class).get_editor_property("settings")
air_settings = settings.get_editor_property("InAir")
curve = air_settings.get_editor_property("GroundPredictionAmountCurve")
curve_text = native_text(curve, "curve.native.txt")
samples = [dict(input=i / 1000, value=curve.get_float_value(i / 1000)) for i in range(1001)]
payload = dict(schemaVersion=1, animationClass=animation_class.get_path_name(), settings=settings.get_path_name(),
               settingsNativeText=native_text(settings, "settings.native.txt"),
               sweepChannel=str(air_settings.get_editor_property("GroundPredictionSweepChannel")),
               responseChannels=[str(c) for c in air_settings.get_editor_property("GroundPredictionResponseChannels")],
               curve=dict(path=curve.get_path_name(), nativeText=curve_text, verification=samples))
(output / "settings.json").write_text(json.dumps(payload, indent=2, allow_nan=False) + "\n", encoding="utf-8")

# The native helper creates its own game world. No viewport, PIE subsystem or
# temporary map is required, and the real RefreshInAir game-world guard remains.
if not unreal.AlsAnimationGraphLibrary.export_ground_prediction(str(output / "native_refresh_in_air.json")):
    raise RuntimeError("Actual game-world ground prediction export failed")
rows = json.loads((output / "native_refresh_in_air.json").read_text(encoding="utf-8"))["rows"]
assert len(rows) == 1260
assert any(row["result"] == 0 for row in rows) and any(row["result"] > 0 for row in rows)
unreal.log("ALS_GROUND_PREDICTION_OK curve_samples=%d native_frames=%d assets_saved=0" % (len(samples), len(rows)))
