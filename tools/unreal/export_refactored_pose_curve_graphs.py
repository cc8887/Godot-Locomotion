"""Read original linked AnimBPs to locate authored pose/foot curve producers."""
import json
import os
from pathlib import Path
import unreal

output = Path(os.environ["ALS_POSE_CURVE_GRAPH_OUTPUT"])
if not output.is_absolute() or output.exists():
    raise ValueError("A fresh absolute output directory is required")
output.mkdir(parents=True)
base = "/ALS/ALS/Character/AnimationInstances/"
paths = [base + name for name in [
    "AB_Als_Grounded", "AB_Als_Locomotion", "Stances/AB_Als_Standing", "Stances/AB_Als_Crouching",
    "AB_Als_Layering", "AB_Als_Head", "AB_Als_Ragdolling",
]]
rows = []
for source in paths:
    asset = unreal.load_asset(source)
    if asset is None:
        raise RuntimeError("Missing original source: " + source)
    task = unreal.AssetExportTask()
    task.object = asset
    task.exporter = unreal.ObjectExporterT3D()
    task.filename = str(output / (asset.get_name() + ".native.txt"))
    task.automated = True
    task.prompt = False
    if not unreal.Exporter.run_asset_export_task(task):
        raise RuntimeError("Native graph export failed: " + source)
    rows.append(dict(source=asset.get_path_name(), file=Path(task.filename).name))
(output / "sources.json").write_text(json.dumps(dict(schemaVersion=1, sources=rows), indent=2) + "\n", encoding="utf-8")
unreal.log("ALS_POSE_CURVE_GRAPHS_OK assets=7 assets_saved=0")
