"""Read the source skeleton's slot groups and all eight turn sequence settings."""
import json
import os
import tempfile
from pathlib import Path

import unreal


base = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/"
assets = []
skeleton = None
for stance in ("N", "CLF"):
    for direction in ("L90", "R90", "L180", "R180"):
        name = "ALS_" + stance + "_TurnIP_" + direction
        asset = unreal.load_asset(base + name)
        if asset is None:
            raise RuntimeError("Missing sequence: " + name)
        current = asset.get_editor_property("skeleton")
        if skeleton is not None and current != skeleton:
            raise RuntimeError("Turn skeletons differ")
        skeleton = current
        assets.append({"path": asset.get_path_name(), "rateScale": asset.get_editor_property("rate_scale"),
                       "rootMotionEnabled": asset.get_editor_property("enable_root_motion")})
with tempfile.TemporaryDirectory(prefix="als-turn-skeleton-") as directory:
    task = unreal.AssetExportTask()
    task.object = skeleton
    task.exporter = unreal.ObjectExporterT3D()
    task.filename = str(Path(directory) / "skeleton.t3d")
    task.automated = True
    task.prompt = False
    if not unreal.Exporter.run_asset_export_task(task):
        raise RuntimeError("Skeleton native export failed")
    data = Path(task.filename).read_bytes()
    skeleton_text = data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")
output = Path(os.environ["ALS_TURN_MONTAGE_OUTPUT"])
if not output.is_absolute():
    raise ValueError("ALS_TURN_MONTAGE_OUTPUT must be absolute")
payload = {"schemaVersion": 1, "skeleton": skeleton.get_path_name(), "skeletonText": skeleton_text, "assets": assets}
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
unreal.log("ALS_TURN_MONTAGE_INPUTS_OK assets=8 skeleton_native_text=1 assets_saved=0")
if os.environ.get("ALS_TURN_MONTAGE_QUIT") == "1":
    unreal.SystemLibrary.quit_editor()
