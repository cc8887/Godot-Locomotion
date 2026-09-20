"""Read original character rotation/gameplay graphs and mesh defaults; never save assets."""
import json
import os
import re
import tempfile
from pathlib import Path

import unreal


def native_text(asset):
    if asset is None:
        raise RuntimeError("Cannot export a missing object")
    with tempfile.TemporaryDirectory(prefix="als-character-bridge-") as directory:
        task = unreal.AssetExportTask()
        task.object = asset
        task.exporter = unreal.ObjectExporterT3D()
        task.filename = str(Path(directory) / "object.t3d")
        task.automated = True
        task.prompt = False
        if not unreal.Exporter.run_asset_export_task(task):
            raise RuntimeError("Native export failed: " + asset.get_path_name())
        data = Path(task.filename).read_bytes()
        return data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")


output = Path(os.environ["ALS_CHARACTER_BRIDGE_OUTPUT"])
if not output.is_absolute():
    raise ValueError("ALS_CHARACTER_BRIDGE_OUTPUT must be absolute")
source = "/Game/AdvancedLocomotionV4/Blueprints/CharacterLogic/ALS_Base_CharacterBP.ALS_Base_CharacterBP"
asset = unreal.load_asset(source)
defaults = unreal.get_default_object(unreal.load_class(None, source + "_C"))
mesh = defaults.get_editor_property("Mesh")
graphs = []
rotation_inputs = os.environ.get("ALS_CHARACTER_ROTATION_INPUTS") == "1"
# ObjectIterator also sees generated interface/event graphs, whose lifetime and
# contents differ between a commandlet and the Editor. Resolve authored graphs
# by exact path and export only through the native read-only exporter.
graph_names = ("AddToCharacterRotation", "CalculateGroundedRotationRate", "CanUpdateMovingRotation",
             "GetAnimCurveValue", "LimitRotation", "OnMovementActionChanged", "OnMovementStateChanged",
             "OnRotationModeChanged", "SmoothCharacterRotation", "UpdateGroudedRotation", "UpdateInAirRotation", "TickGraph")
if rotation_inputs:
    graph_names += ("GetMappedSpeed", "GetTargetMovementSettings", "SetEssentialValues", "GetActualGait", "UpdateCharacterMovement",
                    "UpdateDynamicMovementSettings", "CacheValues")
for name in graph_names:
    graph = unreal.load_object(None, source + ":" + name)
    if graph is None:
        raise RuntimeError("Missing authored character graph: " + name)
    graphs.append({"name": name, "path": graph.get_path_name(), "nativeText": native_text(graph)})
payload = {"schemaVersion": 1, "source": source,
           "graphs": sorted(graphs, key=lambda g: g["name"]), "defaultsText": native_text(defaults),
           "meshPath": mesh.get_path_name(), "meshText": native_text(mesh),
           "teleportDistanceThresholdCm": mesh.get_editor_property("TeleportDistanceThreshold")}
if rotation_inputs:
    history = []
    for graph in unreal.ObjectIterator(unreal.EdGraph):
        if graph.get_outer() != asset or graph.get_name().endswith("_MERGED"):
            continue
        text = native_text(graph)
        if any('MemberName="' + name + '"' in text for name in ("TargetRotation", "InAirRotation", "SetMovementModel")):
            history.append({"name": graph.get_name(), "path": graph.get_path_name(), "nativeText": text})
    payload["rotationHistoryGraphs"] = sorted(history, key=lambda g: g["name"])
    model = defaults.get_editor_property("MovementModel")
    table = model.get_editor_property("DataTable")
    table_text = unreal.DataTableFunctionLibrary.export_data_table_to_json_string(table)
    if not table_text:
        raise RuntimeError("Movement model table export failed")
    payload["movementModel"] = {"path": table.get_path_name(), "row": str(model.get_editor_property("RowName")),
                                "rows": json.loads(table_text)}
    curve_paths = set()
    def find_curves(value):
        if isinstance(value, dict):
            for key, item in value.items():
                if re.sub(r"[^a-z]", "", key.lower()).startswith("rotationratecurve") and isinstance(item, str) and item != "None":
                    match = re.search(r"(/Game/[^']+)", item)
                    if not match:
                        raise RuntimeError("Unsupported rotation curve reference: " + item)
                    curve_paths.add(match.group(1))
                else:
                    find_curves(item)
        elif isinstance(value, list):
            for item in value:
                find_curves(item)
    find_curves(payload["movementModel"]["rows"])
    if not curve_paths:
        raise RuntimeError("Movement settings have no rotation rate curves: " + table_text)
    curves = []
    for path in sorted(curve_paths):
        curve = unreal.load_asset(path)
        if not isinstance(curve, unreal.CurveFloat):
            raise RuntimeError("Missing rotation rate curve: " + path)
        samples = [{"input": step / 100, "value": curve.get_float_value(step / 100)} for step in range(-50, 351)]
        curves.append({"path": curve.get_path_name(), "nativeText": native_text(curve), "verification": samples})
    payload["rotationCurves"] = curves
    enum_paths = set()
    for graph in graphs:
        enum_paths.update(re.findall(r'Enum="/Script/Engine.UserDefinedEnum\x27([^\x27]+)\x27"', graph["nativeText"]))
    payload["enums"] = [{"path": path, "nativeText": native_text(unreal.load_asset(path))} for path in sorted(enum_paths)]
    interpolation = []
    for current, target in ((0, 90), (90, 0), (179, -179), (-179, 179), (0, 180), (0, -180),
                            (40, 40.00001), (-100, 700)):
        for delta in (0, 1 / 30, 1 / 60, 1 / 120, .5):
            for rate in (0, 2, 5, 15, 20, 500, 800, 1000):
                a = unreal.Rotator(yaw=current)
                b = unreal.Rotator(yaw=target)
                interpolation.append({"current": current, "target": target, "delta": delta, "rate": rate,
                    "constant": unreal.MathLibrary.r_interp_to_constant(a, b, delta, rate).yaw,
                    "smooth": unreal.MathLibrary.r_interp_to(a, b, delta, rate).yaw})
    payload["interpolationVerification"] = interpolation
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
unreal.log("ALS_CHARACTER_ANIMATION_BRIDGE_OK graphs=" + str(len(graphs)) + " assets_saved=0")
