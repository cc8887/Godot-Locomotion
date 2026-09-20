"""Export authored upper-body graph connections and input functions without saving assets."""
import json
import os
import struct
import tempfile
from pathlib import Path

import unreal


def native_text(asset):
    if asset is None:
        raise RuntimeError("Cannot export a missing Unreal object")
    with tempfile.TemporaryDirectory(prefix="als-layering-") as directory:
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
    # Release native Python wrappers and reflected callables while the interpreter
    # and engine modules are still alive, rather than during editor shutdown.
    source = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP"
    output = Path(os.environ["ALS_LAYERING_OUTPUT"])
    if not output.is_absolute():
        raise ValueError("ALS_LAYERING_OUTPUT must be absolute")
    asset = unreal.load_asset(source)
    defaults = unreal.get_default_object(unreal.load_class(None, source + "_C"))
    roots = ("LayerBlending", "BasePoses", "AimOffsetBehaviors", "OverlayLayer", "AnimGraph")
    functions = ("UpdateLayerValues", "GetAnimCurve_Compact", "GetAnimCurve_Clamped", "UpdateAimingValues", "UpdateGraph", "UpdateCharacterInfo", "EventGraph")
    graphs = []
    for graph in unreal.ObjectIterator(unreal.EdGraph):
        path = graph.get_path_name()
        if not path.startswith(source + ":") or "_MERGED" in path:
            continue
        suffix = path[len(source) + 1:]
        if suffix in functions or any(suffix == root or suffix.startswith(root + ".") for root in roots):
            graphs.append({"name": graph.get_name(), "path": path, "nativeText": native_text(graph)})
    graphs.sort(key=lambda graph: graph["path"])
    for required in roots + functions:
        if not any(graph["path"] == source + ":" + required for graph in graphs):
            raise RuntimeError("Missing authored layering dependency: " + required)
    floor_cases = []
    floor_names = [name for name in dir(unreal.MathLibrary) if name.replace("_", "").lower() == "ffloor"]
    if len(floor_names) != 1:
        raise RuntimeError("Expected exactly one Python binding for KismetMathLibrary.FFloor: " + str(floor_names))
    native_floor = getattr(unreal.MathLibrary, floor_names[0])
    for value in (-3.4028234663852886e38, -2147483904.0, -2147483648.0, -2.1, -1.0, -0.1,
                  0.0, 0.99, 1.0, 1.1, 2.0, 2147483520.0, 2147483648.0, 3.4028234663852886e38):
        curve_value = struct.unpack("f", struct.pack("f", value))[0]
        floor_cases.append({"curveValue": curve_value, "floorValue": native_floor(curve_value)})
    inventory = json.loads(Path(os.environ["ALS_LAYERING_INVENTORY"]).read_text(encoding="utf-8-sig"))
    if inventory["source"] != source or inventory["inventorySchemaVersion"] != 1 or inventory["cacheSchemaVersion"] != 1:
        raise RuntimeError("Layering requires a matching native compiled-node/cache inventory")
    graph_paths = {graph["path"] for graph in graphs}
    skeleton = asset.get_editor_property("target_skeleton")
    payload = {"schemaVersion": 1, "source": source, "defaultsText": native_text(defaults), "graphs": graphs,
               "floorVerification": floor_cases, "skeletonSource": skeleton.get_path_name(), "skeletonText": native_text(skeleton),
               "inventorySchemaVersion": 1, "cacheSchemaVersion": 1,
               "compiledPropertyCount": inventory["compiledPropertyCount"],
               "compiledNodeInventory": [node for node in inventory["compiledNodeInventory"] if node["path"].rsplit(".", 1)[0] in graph_paths],
               "orderedSavedPoseNodes": [order for order in inventory["orderedSavedPoseNodes"] if order["root"] in roots]}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    unreal.log("ALS_LAYERING_INPUTS_OK graphs=" + str(len(graphs)) + " assets_saved=0")


export()
