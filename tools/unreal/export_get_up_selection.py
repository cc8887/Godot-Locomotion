"""Read native V4 Get-up Blueprint graphs without saving assets."""
import json
import os
import tempfile
from pathlib import Path
import unreal

output = Path(os.environ["ALS_GET_UP_SELECTION_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
sources = ["/Game/AdvancedLocomotionV4/Blueprints/CharacterLogic/" + name + "." + name
           for name in ("ALS_Base_CharacterBP", "ALS_AnimMan_CharacterBP")]
notify_source = "/Game/AdvancedLocomotionV4/Blueprints/AnimNotifys/OverlayOverride_NotifyState.OverlayOverride_NotifyState"
unreal.load_asset(notify_source)
for source in sources:
    if unreal.load_asset(source) is None:
        raise RuntimeError("Missing source " + source)
graphs = []
with tempfile.TemporaryDirectory(prefix="als-getup-selection-") as temporary:
    for graph in unreal.ObjectIterator(unreal.EdGraph):
        if not any(graph.get_path_name().startswith(source + ":") for source in sources + [notify_source]):
            continue
        if graph.get_name() not in ("GetGetUpAnimation", "Received_NotifyBegin", "Received_NotifyEnd"):
            continue
        task = unreal.AssetExportTask()
        task.object, task.exporter = graph, unreal.ObjectExporterT3D()
        task.filename = str(Path(temporary) / "graph.t3d")
        task.automated, task.prompt = True, False
        if not unreal.Exporter.run_asset_export_task(task):
            raise RuntimeError("Graph export failed")
        data = Path(task.filename).read_bytes()
        text = data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")
        graphs.append({"path": graph.get_path_name(), "nativeText": text})
if not graphs:
    raise RuntimeError("No native Get-up graphs found")
generated = unreal.load_class(None, sources[1] + "_C")
subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
actor = subsystem.spawn_actor_from_class(generated, unreal.Vector(0, 0, -100000), transient=True)
if actor is None:
    raise RuntimeError("Cannot spawn Get-up selection reference")
cases = []
try:
    system = unreal.get_default_object(unreal.SystemLibrary)
    for overlay in range(13):
        system.call_method("SetBytePropertyByName", args=(actor, "OverlayState", overlay))
        if actor.get_editor_property("OverlayState").value != overlay:
            raise RuntimeError("Overlay assignment failed")
        for face_up in (False, True):
            montage = actor.call_method("GetGetUpAnimation", args=(face_up,))
            if not isinstance(montage, unreal.AnimMontage):
                raise RuntimeError("Get-up selection returned no montage")
            cases.append({"overlay": overlay, "facingUpward": face_up, "montage": montage.get_path_name()})
finally:
    subsystem.destroy_actor(actor)
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps({"schemaVersion": 1, "cases": cases,
    "graphs": sorted(graphs, key=lambda g: g["path"])}, indent=2) + "\n", encoding="utf-8")
unreal.log("ALS_GET_UP_SELECTION_OK graphs=" + str(len(graphs)) + " cases=" + str(len(cases)) + " assets_saved=0")
