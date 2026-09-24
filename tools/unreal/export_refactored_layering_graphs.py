"""Read the original linked layering graph chain without saving assets."""
import hashlib
import json
import os
from pathlib import Path
import tempfile
import unreal

output = Path(os.environ["ALS_REFACTORED_LAYERING_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
paths = ["/ALS/ALS/Character/AB_Als"] + [
    "/ALS/ALS/Character/AnimationInstances/" + name for name in
    ("AB_Als_Locomotion", "AB_Als_Layering", "AB_Als_Head")]
rows = []
for path in paths:
    asset = unreal.load_asset(path)
    if not isinstance(asset, unreal.AnimBlueprint):
        raise ValueError("Missing original AnimBlueprint: " + path)
    graph = unreal.find_object(None, asset.get_path_name() + ":AnimGraph")
    if not isinstance(graph, unreal.EdGraph):
        raise ValueError("Missing authored AnimGraph: " + path)
    with tempfile.TemporaryDirectory(prefix="als-refactored-layering-") as directory:
        task = unreal.AssetExportTask()
        task.object = graph
        task.exporter = unreal.ObjectExporterT3D()
        task.filename = str(Path(directory) / "graph.t3d")
        task.automated = True
        task.prompt = False
        if not unreal.Exporter.run_asset_export_task(task):
            raise RuntimeError("Graph export failed: " + path)
        data = Path(task.filename).read_bytes()
        text = data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")
    rows.append({"source": asset.get_path_name(), "graph": graph.get_path_name(), "nativeText": text,
                 "textSha256": hashlib.sha256(text.encode("utf-8")).hexdigest()})
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps({"schemaVersion": 1, "blueprints": rows}, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log("ALS_REFACTORED_LAYERING_GRAPHS_OK assets=4 assets_saved=0")
