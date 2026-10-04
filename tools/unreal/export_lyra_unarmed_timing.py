"""Export Lyra's authored distance curves and sync markers beside retargeted FBX."""

import json
import os
from pathlib import Path

import unreal


manifest_path = Path(os.environ["LYRA_CLIP_MANIFEST"])
output_root = Path(os.environ["LYRA_OUTPUT_ROOT"])
if not manifest_path.is_absolute() or not manifest_path.is_file() or not output_root.is_absolute():
    raise ValueError("Lyra timing inputs must be absolute existing paths")
manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
catalog_path = output_root / "unarmed_catalog.json"
catalog = json.loads(catalog_path.read_text(encoding="utf-8"))
if manifest["schemaVersion"] != 1 or catalog["schemaVersion"] != 1:
    raise ValueError("Unsupported Lyra resource schema")
by_slot = {item["slot"]: item for item in catalog["clips"]}
if len(by_slot) != len(manifest["clips"]):
    raise RuntimeError("Lyra timing/catalog clip counts differ")

records = []
curve_differences = 0
marker_differences = 0
for item in manifest["clips"]:
    slot = item["slot"]
    row = by_slot[slot]
    source = unreal.load_asset(manifest["sourceDirectory"] + "/" + item["name"])
    target = unreal.load_asset("/Game/GodotLyraRetarget/Unarmed/LY_" + item["name"])
    if not isinstance(source, unreal.AnimSequence) or not isinstance(target, unreal.AnimSequence):
        raise RuntimeError("Lyra timing asset missing: " + slot)
    if source.get_path_name() != row["source"] or target.get_path_name() != row["target"]:
        raise RuntimeError("Lyra timing asset path differs from catalog: " + slot)
    source_curves = json.loads(unreal.AlsSourceAnimationLibrary.read_source_float_curves(source))
    target_curves = json.loads(unreal.AlsSourceAnimationLibrary.read_source_float_curves(target))
    source_sync = json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(source))
    target_sync = json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(target))
    same_curves = source_curves["curves"] == target_curves["curves"]
    same_markers = source_sync["markers"] == target_sync["markers"]
    curve_differences += not same_curves
    marker_differences += not same_markers
    records.append({
        "slot": slot,
        "source": row["source"],
        "target": row["target"],
        "sourceCurves": source_curves["curves"],
        "targetCurves": target_curves["curves"],
        "sourceSyncMarkers": source_sync["markers"],
        "targetSyncMarkers": target_sync["markers"],
        "curvesUnchanged": same_curves,
        "syncMarkersUnchanged": same_markers,
    })
    unreal.log("LYRA_UNARMED_TIMING_CLIP_OK slot=" + slot +
               " curves=" + str(len(source_curves["curves"])) +
               " markers=" + str(len(source_sync["markers"])))

output = output_root / "unarmed_timing.json"
payload = {"schemaVersion": 1, "clips": records}
if output.exists():
    if json.loads(output.read_text(encoding="utf-8")) != payload:
        raise RuntimeError("Existing Lyra timing export differs: " + str(output))
else:
    output.write_text(json.dumps(payload, indent=2), encoding="utf-8")
unreal.log("LYRA_UNARMED_TIMING_OK clips=" + str(len(records)) +
           " curveDifferences=" + str(curve_differences) +
           " markerDifferences=" + str(marker_differences))
