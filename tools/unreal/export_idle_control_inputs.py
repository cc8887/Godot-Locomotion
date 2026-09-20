"""Export original idle control dependencies and CDO text without saving assets."""
import json
import os
import tempfile
from pathlib import Path

import unreal


def native_text(asset):
    if asset is None:
        raise RuntimeError("Cannot export a missing Unreal object")
    with tempfile.TemporaryDirectory(prefix="als-idle-control-") as directory:
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


source = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP"
output = Path(os.environ["ALS_IDLE_CONTROL_OUTPUT"])
if not output.is_absolute():
    raise ValueError("ALS_IDLE_CONTROL_OUTPUT must be absolute")
defaults = unreal.get_default_object(unreal.load_class(None, source + "_C"))
graphs = []
for name in ("CanRotateInPlace", "CanTurnInPlace", "RotateInPlaceCheck", "TurnInPlaceCheck",
             "TurnInPlace", "UpdateAimingValues", "UpdateCharacterInfo", "UpdateGraph", "DynamicTransitionCheck", "CanDynamicTransition"):
    graph = unreal.load_object(None, source + ":" + name)
    if graph is None:
        raise RuntimeError("Missing idle control graph: " + name)
    readable = unreal.BlueprintLispPythonBridge.export_graph_to_text(source, name)
    if not readable.success:
        raise RuntimeError(readable.message)
    graphs.append({"name": name, "path": graph.get_path_name(), "nativeText": native_text(graph),
                   "readableGraph": readable.dsl_text})
character = "/Game/AdvancedLocomotionV4/Blueprints/CharacterLogic/ALS_Base_CharacterBP.ALS_Base_CharacterBP"
character_graph = unreal.load_object(None, character + ":SetEssentialValues")
if character_graph is None:
    raise RuntimeError("Missing character essential values graph")
character_asset = unreal.load_asset(character)
history_graphs = []
for graph in unreal.ObjectIterator(unreal.EdGraph):
    if graph.get_outer() == character_asset and not graph.get_name().endswith("_MERGED"):
        text = native_text(graph)
        if any('MemberName="' + name + '"' in text for name in ("PreviousAimYaw", "CacheValues", "SetEssentialValues")):
            history_graphs.append({"path": graph.get_path_name(), "nativeText": text})
payload = {"schemaVersion": 1, "source": source, "defaultsText": native_text(defaults), "graphs": graphs,
           "characterSource": character, "essentialValuesText": native_text(character_graph),
           "characterHistoryGraphs": history_graphs,
           "previousAimYawDefault": unreal.get_default_object(unreal.load_class(None, character + "_C")).get_editor_property("PreviousAimYaw")}
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
unreal.log("ALS_IDLE_CONTROL_INPUTS_OK graphs=10 character_history_graphs=" + str(len(history_graphs)) + " assets_saved=0")
if os.environ.get("ALS_IDLE_CONTROL_QUIT") == "1":
    unreal.SystemLibrary.quit_editor()
