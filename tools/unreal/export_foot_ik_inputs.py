"""Read authored Foot IK graphs and defaults; never modify or save UE assets."""
import json
import os
import tempfile
from pathlib import Path

import unreal


def native_text(asset):
    with tempfile.TemporaryDirectory(prefix="als-foot-ik-") as directory:
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


def export():
    source = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP"
    output = Path(os.environ["ALS_FOOT_IK_OUTPUT"])
    if not output.is_absolute():
        raise ValueError("ALS_FOOT_IK_OUTPUT must be absolute")
    asset = unreal.load_asset(source)
    if asset is None:
        raise RuntimeError("Missing original ALS AnimBP")
    defaults = unreal.get_default_object(unreal.load_class(None, source + "_C"))
    roots = ("Foot IK", "UpdateFootIK", "SetFootLocking", "SetFootLockOffsets",
             "SetFootOffsets", "SetPelvisIKOffset", "ResetIKOffsets", "EventGraph",
             "UpdateGraph", "GetAnimCurve_Compact", "GetAnimCurve_Clamped")
    graphs = []
    for graph in unreal.ObjectIterator(unreal.EdGraph):
        path = graph.get_path_name()
        if not path.startswith(source + ":") or "_MERGED" in path:
            continue
        suffix = path[len(source) + 1:]
        root = suffix.split(".", 1)[0]
        if root in roots:
            graphs.append({"name": graph.get_name(), "path": path, "nativeText": native_text(graph)})
    graphs.sort(key=lambda graph: graph["path"])
    for required in roots:
        if not any(graph["path"] == source + ":" + required for graph in graphs):
            raise RuntimeError("Missing original Foot IK dependency: " + required)
    interp_names = [name for name in dir(unreal.MathLibrary) if name.replace("_", "").lower() == "vinterpto"]
    if len(interp_names) != 1:
        raise RuntimeError("Expected one native VInterpTo binding: " + str(interp_names))
    interpolate = getattr(unreal.MathLibrary, interp_names[0])
    cases = []
    for hz in (30, 60, 120):
        for current, target in (((0, 0, 0), (3, -5, -20)), ((4, -2, -30), (1, 7, -5)),
                                ((0, 0, 0), (0.009, 0, 0)), ((0, 0, 0), (0.011, 0, 0))):
            for speed in (10.0, 15.0):
                result = interpolate(unreal.Vector(*current), unreal.Vector(*target), 1.0 / hz, speed)
                cases.append({"current": current, "target": target, "delta": 1.0 / hz, "speed": speed,
                              "result": [result.x, result.y, result.z]})
    payload = {"schemaVersion": 1, "source": source, "defaultsText": native_text(defaults),
               "graphs": graphs, "nativeVectorInterpolation": cases}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    unreal.log("ALS_FOOT_IK_INPUTS_OK graphs=" + str(len(graphs)) + " assets_saved=0")


export()
