"""Read the original V4 Roll notify and its Blueprint implementation, without saving assets."""
import json
import os
import tempfile
from pathlib import Path

import unreal


def native_text(obj):
    if obj is None:
        raise RuntimeError("Missing native object")
    with tempfile.TemporaryDirectory(prefix="als-notify-semantics-") as directory:
        task = unreal.AssetExportTask()
        task.object = obj
        task.exporter = unreal.ObjectExporterT3D()
        task.filename = str(Path(directory) / "object.t3d")
        task.automated = True
        task.prompt = False
        if not unreal.Exporter.run_asset_export_task(task):
            raise RuntimeError("Cannot export " + obj.get_path_name())
        data = Path(task.filename).read_bytes()
        return data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")


def export():
    output = Path(os.environ["ALS_GROUNDED_NOTIFY_OUTPUT"])
    if not output.is_absolute():
        raise ValueError("ALS_GROUNDED_NOTIFY_OUTPUT must be absolute")
    sequence = unreal.load_asset("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Actions/ALS_N_LandRoll_F")
    rows = []
    for index, event in enumerate(unreal.AnimationLibrary.get_animation_notify_events(sequence)):
        obj = event.get_editor_property("notify")
        if obj is None or obj.get_class().get_path_name() != "/Game/AdvancedLocomotionV4/Blueprints/AnimNotifys/GroundedEntryState_AnimNotify.GroundedEntryState_AnimNotify_C":
            continue
        rows.append({"sourceIndex": index, "objectPath": obj.get_path_name(),
                     "classPath": obj.get_class().get_path_name(), "nativeText": native_text(obj),
                     "eventText": event.export_text()})
    if len(rows) != 1:
        raise RuntimeError("Expected one original Roll grounded entry notify")
    paths = ["/Game/AdvancedLocomotionV4/Blueprints/AnimNotifys/GroundedEntryState_AnimNotify.GroundedEntryState_AnimNotify",
             "/Game/AdvancedLocomotionV4/Blueprints/AnimNotifys/MovementAction_NotifyState.MovementAction_NotifyState"]
    graphs = []
    for path in paths:
        asset = unreal.load_asset(path)
        for graph in unreal.ObjectIterator(unreal.EdGraph):
            if graph.get_outer() == asset and not graph.get_name().endswith("_MERGED"):
                graphs.append({"path": graph.get_path_name(), "name": graph.get_name(), "nativeText": native_text(graph)})
    enum = unreal.load_asset("/Game/AdvancedLocomotionV4/Data/Enums/GroundedEntryState.GroundedEntryState")
    payload = {"schemaVersion": 1, "source": sequence.get_path_name(), "notifies": rows,
               "enumPath": enum.get_path_name(), "enumText": native_text(enum),
               "graphs": sorted(graphs, key=lambda g: g["path"])}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    unreal.log("ALS_GROUNDED_NOTIFY_SEMANTICS_OK assets_saved=0")


if __name__ == "__main__":
    export()
