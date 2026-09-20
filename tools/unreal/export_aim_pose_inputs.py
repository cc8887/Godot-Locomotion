"""Read the actual baked Aim machines, authored graphs and curve assets; save no UE assets."""
import json
import os
import tempfile
from pathlib import Path

import unreal

SOURCE = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP"
MACHINES = ("Aim Offset Behavior States", "Look Towards Input States", "Look Towards Camera States")


def native_text(obj):
    with tempfile.TemporaryDirectory(prefix="als-aim-pose-") as directory:
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
    output = Path(os.environ["ALS_AIM_POSE_OUTPUT"])
    if not output.is_absolute():
        raise ValueError("ALS_AIM_POSE_OUTPUT must be absolute")
    metadata = json.loads(unreal.AlsAnimationGraphLibrary.read_baked_state_machines(unreal.load_asset(SOURCE)))
    if metadata["schemaVersion"] != 1 or metadata["source"] != SOURCE:
        raise RuntimeError("Unexpected native baked machine metadata")
    machines = [machine for machine in metadata["bakedMachines"] if machine["machineName"] in MACHINES]
    if {machine["machineName"] for machine in machines} != set(MACHINES):
        raise RuntimeError("Incomplete native Aim state machine closure")
    curve_paths = {edge["customCurve"] for machine in machines for edge in machine["transitions"] if edge["customCurve"]}
    profile_paths = {edge["blendProfile"] for machine in machines for edge in machine["transitions"] if edge["blendProfile"]}
    curves = [curve for curve in metadata["curves"] if curve["path"] in curve_paths]
    if {curve["path"] for curve in curves} != curve_paths:
        raise RuntimeError("Incomplete native Aim transition curve closure")
    graphs = [{"name": graph.get_name(), "path": graph.get_path_name(), "nativeText": native_text(graph)}
              for graph in unreal.ObjectIterator(unreal.EdGraph)
              if (graph.get_path_name() == SOURCE + ":AimOffsetBehaviors" or
                  graph.get_path_name().startswith(SOURCE + ":AimOffsetBehaviors.")) and "_MERGED" not in graph.get_path_name()]
    graphs.sort(key=lambda graph: graph["path"])
    space = unreal.load_asset("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/AimOffsets/ALS_N_Look")
    result = {"schemaVersion": 1, "source": SOURCE, "bakedMachines": machines, "curves": curves, "graphs": graphs,
              "blendProfiles": [profile for profile in metadata["blendProfiles"] if profile["path"] in profile_paths],
              "editorStateNodes": [node for node in metadata["editorStateNodes"] if node["path"].startswith(SOURCE + ":AimOffsetBehaviors.")],
              "blendSpace": {"path": space.get_path_name(), "nativeText": native_text(space)}}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    unreal.log("ALS_AIM_POSE_INPUTS_OK machines=" + str(len(machines)) + " graphs=" + str(len(graphs)) +
               " curves=" + str(len(curves)) + " assets_saved=0")


export()
