"""Read locomotion direction curves and Blueprint functions without saving assets."""

import json
import os
from pathlib import Path

import unreal


output = Path(os.environ["ALS_DIRECTION_AUDIT_OUTPUT"])
if not output.is_absolute():
    raise ValueError("ALS_DIRECTION_AUDIT_OUTPUT must be absolute")
output.mkdir(parents=True, exist_ok=True)
curves = []
for name in ("YawOffset_FB", "YawOffset_LR"):
    path = "/Game/AdvancedLocomotionV4/Data/Curves/AnimationBlendCurves/" + name
    curve = unreal.load_asset(path)
    if not isinstance(curve, unreal.CurveVector):
        raise RuntimeError("Missing CurveVector: " + path)
    samples = []
    for angle in range(-180, 181):
        value = curve.get_vector_value(float(angle))
        samples.append({"angle": angle, "x": value.x, "y": value.y, "z": value.z})
    curves.append({"asset": path, "samples": samples})

blueprint_path = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP"
graphs = unreal.BlueprintLispPythonBridge.list_graphs(blueprint_path)
if not graphs.success:
    raise RuntimeError(graphs.message)
(output / "graphs.txt").write_text(graphs.dsl_text, encoding="utf-8")
for line in graphs.dsl_text.splitlines():
    graph = line.split("] ", 1)[1]
    if not any(word in graph.lower() for word in ("movement", "direction", "rotation", "layer", "hips")):
        continue
    result = unreal.BlueprintLispPythonBridge.export_graph_to_text(blueprint_path, graph)
    if result.success:
        safe_name = "".join(character if character.isalnum() else "_" for character in graph)
        (output / (safe_name + ".bplisp")).write_text(result.dsl_text, encoding="utf-8")
    else:
        unreal.log_warning("DIRECTION_GRAPH_UNAVAILABLE " + graph + ": " + result.message)

(output / "yaw-curves.json").write_text(json.dumps(curves, indent=2), encoding="utf-8")
unreal.log("ALS_DIRECTION_AUDIT_OK curves=2 samples=722 assets_saved=0")
