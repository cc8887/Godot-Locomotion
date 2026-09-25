"""Export original skeleton text including curve LinkedBones; never save assets."""
import hashlib
import json
import os
from pathlib import Path
import tempfile
import unreal

output = Path(os.environ["ALS_SKELETON_CURVES_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
root = Path(__file__).parents[2]
catalog_path = root / "assets/config/refactored_animation_sources.json"
catalog = json.loads(catalog_path.read_text(encoding="utf-8"))
sequence = unreal.load_asset("/ALS/ALS/Animations/Base/A_Als_Stand_Pose.A_Als_Stand_Pose")
if not sequence:
    raise RuntimeError("Original standing reference missing")
skeleton = sequence.get_editor_property("skeleton")
source = skeleton.get_path_name()
if source != "/ALS/ALS/Character/SK_Als.SK_Als" or source not in catalog["skeletons"]:
    raise RuntimeError("Foreign original skeleton")
with tempfile.TemporaryDirectory(prefix="als-skeleton-curves-") as temporary:
    task = unreal.AssetExportTask()
    task.object, task.exporter = skeleton, unreal.ObjectExporterT3D()
    task.filename = str(Path(temporary) / "skeleton.t3d")
    task.automated, task.prompt = True, False
    if not unreal.Exporter.run_asset_export_task(task):
        raise RuntimeError("Skeleton native text export failed")
    data = Path(task.filename).read_bytes()
    native = data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")
if "CurveMetaData=" not in native:
    raise RuntimeError("Missing original curve metadata")
value = {
    "schemaVersion": 1,
    "catalogSha256": hashlib.sha256(catalog_path.read_bytes()).hexdigest(),
    "source": source,
    "referenceSequence": sequence.get_path_name(),
    "nativeText": native,
}
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(value, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log("ALS_SKELETON_CURVES_OK source=%s assets_saved=0" % source)
if os.environ.get("ALS_SKELETON_CURVES_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)
