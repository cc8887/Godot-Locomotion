"""Read ALS V4 input functions, authored defaults and referenced curves; never save assets."""
import json
import os
import tempfile
from pathlib import Path

import unreal


def native_text(asset):
    with tempfile.TemporaryDirectory(prefix="als-movement-input-") as directory:
        target = Path(directory) / "asset.t3d"
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


output = Path(os.environ["ALS_MOVEMENT_INPUT_FUNCTIONS_OUTPUT"])
if not output.is_absolute():
    raise ValueError("ALS_MOVEMENT_INPUT_FUNCTIONS_OUTPUT must be absolute")
blueprint = unreal.load_asset("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP")
source = blueprint.get_path_name()
names = (
    "UpdateMovementValues", "UpdateInAirValues", "CalculateVelocityBlend", "CalculateDiagonalScaleAmount",
    "CalculateRelativeAccelerationAmount", "CalculateWalkRunBlend", "CalculateStrideBlend",
    "CalculateStandingPlayRate", "CalculateCrouchingPlayRate", "CalculateLandPrediction",
    "CalculateInAirLeanAmount", "InterpVelocityBlend", "InterpLeanAmount", "EventGraph", "UpdateGraph",
)
graphs = []
for name in names:
    graph = unreal.load_object(None, source + ":" + name)
    if graph is None:
        raise RuntimeError("Missing input function: " + name)
    readable = unreal.BlueprintLispPythonBridge.export_graph_to_text(source, name)
    if not readable.success:
        raise RuntimeError("Readable graph export failed: " + name + ": " + readable.message)
    graphs.append({"name": name, "path": graph.get_path_name(), "nativeText": native_text(graph), "readableGraph": readable.dsl_text})

defaults = unreal.get_default_object(unreal.load_class(None, source + "_C"))
values = {}
for name in (
    "VelocityBlendInterpSpeed", "GroundedLeanInterpSpeed", "InAirLeanInterpSpeed",
    "AnimatedWalkSpeed", "AnimatedRunSpeed", "AnimatedSprintSpeed", "AnimatedCrouchSpeed",
    "JumpPlayRate", "FallSpeed", "LandPrediction", "WalkRunBlend", "StrideBlend", "CrouchingPlayRate",
):
    values[name] = defaults.get_editor_property(name)

curve_bindings = {name: defaults.get_editor_property(name).get_path_name() for name in (
    "StrideBlend_N_Walk", "StrideBlend_N_Run", "StrideBlend_C_Walk",
    "DiagonalScaleAmountCurve", "LeanInAirCurve", "LandPredictionCurve",
)}
curve_paths = sorted(set(curve_bindings.values()))
curves = []
for path in curve_paths:
    curve = unreal.load_asset(path)
    if not isinstance(curve, unreal.CurveFloat):
        raise RuntimeError("Referenced input curve is not CurveFloat: " + path)
    low, high = curve.get_time_range()
    samples = []
    for step in range(201):
        value = low + (high - low) * step / 200
        samples.append({"input": value, "value": curve.get_float_value(value)})
    curves.append({"name": curve.get_name(), "path": curve.get_path_name(), "nativeText": native_text(curve),
                   "verification": samples})

payload = {"schemaVersion": 1, "source": source, "graphs": graphs, "defaults": values,
           "curveBindings": curve_bindings, "curves": curves}
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
unreal.log("ALS_MOVEMENT_INPUT_FUNCTIONS_OK graphs=%d curves=%d defaults=%d assets_saved=0" %
           (len(graphs), len(curves), len(values)))
if os.environ.get("ALS_MOVEMENT_INPUT_QUIT_EDITOR") == "1":
    unreal.SystemLibrary.quit_editor()
