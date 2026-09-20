"""Export exact native YawOffset curve properties and rotation graph pins without saving assets."""
import json
import os
import tempfile
from pathlib import Path

import unreal


def native_text(asset):
    with tempfile.TemporaryDirectory(prefix="als-yaw-") as directory:
        task = unreal.AssetExportTask()
        task.object = asset
        task.exporter = unreal.ObjectExporterT3D()
        task.filename = str(Path(directory) / "asset.t3d")
        task.automated = True
        task.prompt = False
        if not unreal.Exporter.run_asset_export_task(task):
            raise RuntimeError("Native property export failed: " + asset.get_path_name())
        data = Path(task.filename).read_bytes()
        return data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")


output = Path(os.environ["ALS_YAW_INPUTS_OUTPUT"])
if not output.is_absolute():
    raise ValueError("ALS_YAW_INPUTS_OUTPUT must be absolute")
curves = []
for name in ("YawOffset_FB", "YawOffset_LR"):
    curve = unreal.load_asset("/Game/AdvancedLocomotionV4/Data/Curves/AnimationBlendCurves/" + name)
    if not isinstance(curve, unreal.CurveVector):
        raise RuntimeError("Missing CurveVector: " + name)
    samples = []
    for step in range(1441):
        angle = -180 + step * .25
        value = curve.get_vector_value(angle)
        samples.append({"angle": angle, "x": value.x, "y": value.y, "z": value.z})
    curves.append({"name": name, "path": curve.get_path_name(), "nativeText": native_text(curve), "verification": samples})
blueprint = unreal.load_asset("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP")
graph = unreal.load_object(None, blueprint.get_path_name() + ":UpdateRotationValues")
if graph is None:
    raise RuntimeError("Missing UpdateRotationValues graph")
event_graph = unreal.load_object(None, blueprint.get_path_name() + ":EventGraph")
if event_graph is None:
    raise RuntimeError("Missing animation EventGraph")
update_graph = unreal.load_object(None, blueprint.get_path_name() + ":UpdateGraph")
if update_graph is None:
    raise RuntimeError("Missing animation UpdateGraph")
macro = unreal.load_object(None, "/Game/AdvancedLocomotionV4/Blueprints/Libraries/ALS_MacroLibrary.ALS_MacroLibrary:ML_DoWhile(TrueFalse)")
movement_enum = unreal.load_asset("/Game/AdvancedLocomotionV4/Data/Enums/ALS_MovementState")
defaults = unreal.get_default_object(unreal.load_class(None, blueprint.get_path_name() + "_C"))
payload = {"schemaVersion": 1, "source": blueprint.get_path_name(), "curves": curves,
           "rotationGraphPath": graph.get_path_name(), "rotationGraphText": native_text(graph),
           "eventGraphPath": event_graph.get_path_name(), "eventGraphText": native_text(event_graph),
           "updateGraphPath": update_graph.get_path_name(), "updateGraphText": native_text(update_graph),
           "movementEnumText": native_text(movement_enum), "moveMacroText": native_text(macro),
           "yawDefaults": [defaults.get_editor_property(name) for name in ("FYaw", "BYaw", "LYaw", "RYaw")]}
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
unreal.log("ALS_YAW_INPUTS_OK curves=2 samples=2882 graphs=3 assets_saved=0")
if os.environ.get("ALS_YAW_QUIT_EDITOR") == "1":
    unreal.SystemLibrary.quit_editor()
