"""Read all original Refactored montages and skeleton Slot groups; never save assets."""
import json
import os
from pathlib import Path
import re
import tempfile
import unreal

output = Path(os.environ["ALS_REFACTORED_SLOT_INVENTORY"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
registry = unreal.AssetRegistryHelpers.get_asset_registry()
registry.scan_paths_synchronous(["/ALS"], force_rescan=True)
rows = registry.get_assets_by_path("/ALS", recursive=True)
montages, skeletons = [], {}
for row in sorted(rows, key=lambda item: str(item.package_name)):
    if str(row.asset_class_path.asset_name) != "AnimMontage":
        continue
    asset = row.get_asset()
    skeleton = asset.get_editor_property("skeleton")
    source = skeleton.get_path_name()
    if source not in skeletons:
        with tempfile.TemporaryDirectory(prefix="als-slot-inventory-") as directory:
            task = unreal.AssetExportTask()
            task.object, task.exporter = skeleton, unreal.ObjectExporterT3D()
            task.filename = str(Path(directory) / "skeleton.t3d")
            task.automated, task.prompt = True, False
            if not unreal.Exporter.run_asset_export_task(task):
                raise RuntimeError("Skeleton text export failed")
            data = Path(task.filename).read_bytes()
            native = data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")
        groups = []
        for line in native.splitlines():
            match = re.match(r'\s+SlotGroups\((\d+)\)=\((?:GroupName="([^"]+)",)?SlotNames=\((.*)\)\)', line)
            if match:
                groups.append({"index": int(match[1]), "name": match[2] or "DefaultGroup", "slots": re.findall(r'"([^"]+)"', match[3])})
        if not groups:
            raise ValueError("Missing original Slot groups")
        skeletons[source] = {"groups": groups, "nativeText": native}
    tracks = []
    for track in asset.get_editor_property("slot_anim_tracks"):
        segments = []
        for segment in track.get_editor_property("anim_track").get_editor_property("anim_segments"):
            animation = segment.get_editor_property("anim_reference")
            segments.append({"animation": animation.get_path_name() if animation else None})
        tracks.append({"slot": str(track.get_editor_property("slot_name")), "segments": segments})
    montages.append({"source": asset.get_path_name(), "skeleton": source, "tracks": tracks})
if not montages:
    raise ValueError("No Refactored montages found")
result = {"schemaVersion": 1, "searchRoot": "/ALS", "montages": montages, "skeletons": skeletons}
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(result, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log(f"ALS_REFACTORED_SLOT_INVENTORY_OK montages={len(montages)} skeletons={len(skeletons)} assets_saved=0")
if os.environ.get("ALS_REFACTORED_SLOT_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)
