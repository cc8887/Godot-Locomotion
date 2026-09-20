"""Read grounded control defaults and native direction graphs; never save assets."""
import json
import os
import tempfile
from pathlib import Path

import unreal


def native_text(asset):
    with tempfile.TemporaryDirectory(prefix="als-ground-control-") as directory:
        task = unreal.AssetExportTask()
        task.object = asset
        task.exporter = unreal.ObjectExporterT3D()
        task.filename = str(Path(directory) / "graph.t3d")
        task.automated = True
        task.prompt = False
        if not unreal.Exporter.run_asset_export_task(task):
            raise RuntimeError("Native export failed: " + asset.get_path_name())
        data = Path(task.filename).read_bytes()
        return data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")


source = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP"
output = Path(os.environ["ALS_GROUNDED_CONTROL_OUTPUT"])
if not output.is_absolute():
    raise ValueError("ALS_GROUNDED_CONTROL_OUTPUT must be absolute")
defaults = unreal.get_default_object(unreal.load_class(None, source + "_C"))
values = {name: defaults.get_editor_property(name) for name in
          ("ElapsedDelayTime", "Rotate_L", "Rotate_R", "RotateRate", "RotationScale")}
values["MovementDirection"] = defaults.get_editor_property("MovementDirection").value
graphs = []
for name in ("CalculateMovementDirection", "UpdateRotationValues"):
    graph = unreal.load_object(None, source + ":" + name)
    if graph is None:
        raise RuntimeError("Missing grounded control graph: " + name)
    readable = unreal.BlueprintLispPythonBridge.export_graph_to_text(source, name)
    if not readable.success:
        raise RuntimeError(readable.message)
    graphs.append({"name": name, "path": graph.get_path_name(), "nativeText": native_text(graph), "readableGraph": readable.dsl_text})
payload = {"schemaVersion": 1, "source": source, "defaults": values, "graphs": graphs}
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
unreal.log("ALS_GROUNDED_CONTROL_INPUTS_OK defaults=6 graphs=2 assets_saved=0")
if os.environ.get("ALS_GROUNDED_CONTROL_QUIT") == "1":
    unreal.SystemLibrary.quit_editor()
