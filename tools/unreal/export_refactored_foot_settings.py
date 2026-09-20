"""Inspect actual Refactored AnimBP/rig settings without changing any assets."""
import json
import os
from pathlib import Path
import unreal

output = Path(os.environ["ALS_REFACTORED_FOOT_SETTINGS_OUTPUT"])
if not output.is_absolute() or output.exists():
    raise ValueError("A fresh absolute settings audit directory is required")
output.mkdir(parents=True)
paths = (
    "/ALS/ALS/Data/AnimationInstance/AIS_Als_Default",
    "/ALS/ALS/Character/AB_Als",
    "/ALS/ALS/Character/CR_Als",
)
rows = []
for source in paths:
    asset = unreal.load_asset(source)
    if asset is None:
        raise RuntimeError("Missing exact Refactored source: " + source)
    task = unreal.AssetExportTask()
    task.object = asset
    task.exporter = unreal.ObjectExporterT3D()
    task.filename = str(output / (asset.get_name() + ".native.txt"))
    task.automated = True
    task.prompt = False
    if not unreal.Exporter.run_asset_export_task(task):
        raise RuntimeError("Native settings export failed: " + source)
    rows.append(dict(source=asset.get_path_name(), nativeFile=Path(task.filename).name))
animation_class = unreal.load_class(None, paths[1] + ".AB_Als_C")
defaults = unreal.get_default_object(animation_class)
settings = defaults.get_editor_property("settings")
general = settings.get_editor_property("general")
locking = settings.get_editor_property("foot_lock")
payload = dict(schemaVersion=1, sources=rows, animationClass=animation_class.get_path_name(),
               settings=settings.get_path_name(),
               general=dict(useFootIkBones=general.get_editor_property("use_foot_ik_bones"),
                            movingSmoothSpeedThreshold=general.get_editor_property("moving_smooth_speed_threshold")),
               locking=dict(allowFootLock=locking.get_editor_property("allow_foot_lock"),
                            thighAngleLimit=locking.get_editor_property("thigh_angle_limit"),
                            footAngleLimit=locking.get_editor_property("foot_angle_limit")))
(output / "settings.json").write_text(json.dumps(payload, indent=2, allow_nan=False) + "\n", encoding="utf-8")
unreal.log("ALS_REFACTORED_FOOT_SETTINGS_OK assets=3 assets_saved=0")
if os.environ.get("ALS_REFACTORED_FOOT_SETTINGS_QUIT") == "1":
    unreal.SystemLibrary.quit_editor()
