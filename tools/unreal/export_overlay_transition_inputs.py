"""Read Overlay notify consumers and generated notify identities without saving UE assets."""
import hashlib
import json
import os
import tempfile
from pathlib import Path

import unreal

SOURCE = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP"


def native_text(obj, directory):
    if obj is None:
        raise RuntimeError("Missing Overlay transition object")
    task = unreal.AssetExportTask()
    task.object, task.exporter = obj, unreal.ObjectExporterT3D()
    task.filename = str(directory / "object.t3d")
    task.automated, task.prompt = True, False
    if not unreal.Exporter.run_asset_export_task(task):
        raise RuntimeError("Cannot export " + obj.get_path_name())
    data = Path(task.filename).read_bytes()
    return data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")


root = Path(os.environ["ALS_OVERLAY_REPOSITORY"])
output = Path(os.environ["ALS_OVERLAY_TRANSITION_OUTPUT"])
if not root.is_absolute() or not output.is_absolute():
    raise ValueError("Absolute repository/output required")
with tempfile.TemporaryDirectory(prefix="als-overlay-transition-") as temporary:
    directory = Path(temporary)
    request, result = directory / "request.json", directory / "result.json"
    request.write_text(json.dumps({"schemaVersion": 1, "traces": []}), encoding="utf-8")
    if not unreal.AlsAnimationGraphLibrary.export_overlay_state_trace(str(request), str(result)):
        raise RuntimeError("Cannot read generated Overlay notify identities")
    trace = json.loads(result.read_text(encoding="utf-8"))
    graphs = []
    for name in ("CanOverlayTransition", "PlayTransition", "EventGraph"):
        obj = unreal.load_object(None, SOURCE + ":" + name)
        graphs.append({"name": name, "path": obj.get_path_name(), "nativeText": native_text(obj, directory)})
    payload = {"schemaVersion": 1, "source": SOURCE,
               "layeringSha256": hashlib.sha256((root / "assets/config/v4_layering_inputs.json").read_bytes()).hexdigest(),
               "overlaySha256": hashlib.sha256((root / "assets/config/v4_overlay_inputs.json").read_bytes()).hexdigest(),
               "notifyDefinitions": trace["notifies"], "graphs": graphs}
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(payload, indent=2, allow_nan=False) + "\n", encoding="utf-8")
unreal.log("ALS_OVERLAY_TRANSITION_INPUTS_OK graphs=3 notifies=" + str(len(payload["notifyDefinitions"])) + " assets_saved=0")
if os.environ.get("ALS_OVERLAY_QUIT") == "1":
    unreal.SystemLibrary.quit_editor()
