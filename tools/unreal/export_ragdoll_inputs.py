"""Read authored Ragdoll animation/gameplay graphs and baked state policy. Save no assets."""
import json
import os
import tempfile
from pathlib import Path

import unreal


def native_text(obj):
    with tempfile.TemporaryDirectory(prefix="als-ragdoll-") as directory:
        task = unreal.AssetExportTask()
        task.object = obj
        task.exporter = unreal.ObjectExporterT3D()
        task.filename = str(Path(directory) / "object.t3d")
        task.automated, task.prompt = True, False
        if not unreal.Exporter.run_asset_export_task(task):
            raise RuntimeError("Cannot export " + obj.get_path_name())
        data = Path(task.filename).read_bytes()
        return data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")


def export():
    source = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP"
    character = "/Game/AdvancedLocomotionV4/Blueprints/CharacterLogic/ALS_Base_CharacterBP.ALS_Base_CharacterBP"
    output = Path(os.environ["ALS_RAGDOLL_OUTPUT"])
    if not output.is_absolute():
        raise ValueError("ALS_RAGDOLL_OUTPUT must be absolute")
    anim_asset, character_asset = unreal.load_asset(source), unreal.load_asset(character)
    if anim_asset is None or character_asset is None:
        raise RuntimeError("Missing original ALS assets")
    metadata = json.loads(unreal.AlsAnimationGraphLibrary.read_baked_state_machines(anim_asset))
    machines = [m for m in metadata["bakedMachines"] if m["machineName"] == "Ragdoll States"]
    if metadata["source"] != source or len(machines) != 1:
        raise RuntimeError("Incomplete Ragdoll state machine metadata")
    graphs, character_graphs = [], []
    for graph in unreal.ObjectIterator(unreal.EdGraph):
        path = graph.get_path_name()
        if "_MERGED" in path:
            continue
        if path.startswith(source + ":"):
            suffix = path[len(source) + 1:]
            if suffix.startswith("AnimGraph.AnimGraphNode_StateMachine_10.Ragdoll States") or suffix in ("AnimGraph", "EventGraph", "UpdateGraph", "UpdateRagdollValues"):
                graphs.append({"name": graph.get_name(), "path": path, "nativeText": native_text(graph)})
        elif path.startswith(character + ":") and path[len(character) + 1:] in ("RagdollStart", "RagdollEnd", "RagdollUpdate", "SetActorLocationDuringRagdoll"):
            character_graphs.append({"name": graph.get_name(), "path": path, "nativeText": native_text(graph)})
    if not any(g["name"] == "UpdateRagdollValues" for g in graphs) or not character_graphs:
        raise RuntimeError("Missing Ragdoll input or character functions")
    def native_function(name):
        candidates = [n for n in dir(unreal.MathLibrary) if n.replace("_", "").lower() == name.lower()]
        if len(candidates) != 1:
            raise RuntimeError("Ambiguous native math binding: " + name + " " + str(candidates))
        return getattr(unreal.MathLibrary, candidates[0])
    length, mapped = native_function("VSize"), native_function("MapRangeClamped")
    velocity_cases = []
    for velocity in ((0, 0, 0), (100, 0, 0), (-500, 0, 0), (0, 0, 1000), (0, 1001, 0),
                     (300, 400, 0), (-300, 0, -400), (500, 500, 500), (1000, 1000, 1000)):
        speed = length(unreal.Vector(*velocity))
        velocity_cases.append({"velocityCm": velocity, "speedCm": speed, "mappedRate": mapped(speed, 0, 1000, 0, 1)})
    result = {"schemaVersion": 1, "source": source, "characterSource": character,
              "nativeFlailRateCases": velocity_cases,
              "graphs": sorted(graphs, key=lambda g: g["path"]),
              "characterGraphs": sorted(character_graphs, key=lambda g: g["path"]),
              "bakedMachines": machines,
              "editorStateNodes": [n for n in metadata["editorStateNodes"] if "Ragdoll States" in n["path"]],
              "defaultsText": native_text(unreal.get_default_object(unreal.load_class(None, source + "_C"))),
              "characterDefaultsText": native_text(unreal.get_default_object(unreal.load_class(None, character + "_C")))}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    unreal.log("ALS_RAGDOLL_INPUTS_OK graphs=" + str(len(graphs)) + " character_graphs=" + str(len(character_graphs)) + " assets_saved=0")


export()
