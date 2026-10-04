"""Read authored timing, notify and root metadata for retargeted Unarmed layers."""

import hashlib
import json
import math
import os
from pathlib import Path

import unreal


output_root = Path(os.environ["LYRA_OUTPUT_ROOT"])
if not output_root.is_absolute() or not output_root.is_dir():
    raise ValueError("LYRA_OUTPUT_ROOT must be an existing absolute directory")

project_content = Path(unreal.Paths.project_content_dir())
mode = os.environ.get("LYRA_METADATA_MODE", "aux")
groups = {
    "aux": (("unarmed_aux_catalog.json", "unarmed_crouch_transitions_catalog.json"), 24),
    "remaining": (("unarmed_remaining_catalog.json",), 17),
}
if mode not in groups:
    raise ValueError("Unsupported Lyra metadata mode: " + mode)
catalog_names, expected_count = groups[mode]
rows = []
for catalog_name in catalog_names:
    catalog = json.loads((output_root / catalog_name).read_text(encoding="utf-8"))
    if catalog["schemaVersion"] != 1 or len(catalog["clips"]) != expected_count // len(catalog_names):
        raise RuntimeError("Invalid Lyra auxiliary catalog: " + catalog_name)
    rows.extend(catalog["clips"])
if len(rows) != expected_count or len({row["slot"] for row in rows}) != expected_count:
    raise RuntimeError("Duplicate Lyra auxiliary slot")

timing, notifies, playback, roots = [], [], [], []
event_count = distance_count = marker_count = 0
for row in rows:
    slot = row["slot"]
    assets = []
    for label in ("source", "target"):
        path = row[label]
        package = project_content / (path.split(".", 1)[0].removeprefix("/Game/") + ".uasset")
        if not package.is_file() or hashlib.sha256(package.read_bytes()).hexdigest() != row[label + "UassetSha256"]:
            raise RuntimeError("Lyra auxiliary package differs from catalog: " + slot + "/" + label)
        asset = unreal.load_asset(path)
        if not isinstance(asset, unreal.AnimSequence) or asset.get_path_name() != path:
            raise RuntimeError("Missing Lyra auxiliary sequence: " + slot + "/" + label)
        assets.append(asset)
    source, target = assets
    source_curves = json.loads(unreal.AlsSourceAnimationLibrary.read_source_float_curves(source))["curves"]
    target_curves = json.loads(unreal.AlsSourceAnimationLibrary.read_source_float_curves(target))["curves"]
    source_markers = json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(source))["markers"]
    target_markers = json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(target))["markers"]
    source_notify = json.loads(unreal.AlsSourceAnimationLibrary.read_source_notify_metadata(source))
    target_notify = json.loads(unreal.AlsSourceAnimationLibrary.read_source_notify_metadata(target))
    source_rate = float(source.get_editor_property("rate_scale"))
    target_rate = float(target.get_editor_property("rate_scale"))
    source_root = json.loads(unreal.AlsSourceAnimationLibrary.read_source_root_motion_range(
        source, 0.0, row["playLength"]))
    target_root = json.loads(unreal.AlsSourceAnimationLibrary.read_source_root_motion_range(
        target, 0.0, row["playLength"]))
    if (source_curves != target_curves or source_markers != target_markers or
            source_notify["events"] != target_notify["events"] or
            any(abs(data["length"] - row["playLength"]) > 1e-4
                for data in (source_notify, target_notify)) or
            not all(math.isfinite(rate) and rate > 0 for rate in (source_rate, target_rate)) or
            any(root["source"] != row[label] or
                abs(root["playLength"] - row["playLength"]) > 1e-4 or
                not math.isfinite(root["planarDistanceCm"]) or root["planarDistanceCm"] < 0
                for root, label in ((source_root, "source"), (target_root, "target")))):
        raise RuntimeError("Lyra auxiliary source/target metadata differs: " + slot)
    timing.append({
        "slot": slot, "source": row["source"], "target": row["target"],
        "sourceCurves": source_curves, "targetCurves": target_curves,
        "sourceSyncMarkers": source_markers, "targetSyncMarkers": target_markers,
        "curvesUnchanged": True, "syncMarkersUnchanged": True,
    })
    notifies.append({
        "slot": slot, "source": row["source"], "target": row["target"],
        "sourceEvents": source_notify["events"], "targetEvents": target_notify["events"],
        "eventsUnchanged": True,
    })
    playback.append({
        "slot": slot, "source": row["source"], "target": row["target"],
        "sourceRateScale": source_rate, "targetRateScale": target_rate,
    })
    roots.append({"slot": slot, "source": source_root, "target": target_root})
    event_count += len(source_notify["events"])
    marker_count += len(source_markers)
    distance_count += sum(curve["name"] == "Distance" for curve in source_curves)
    unreal.log("LYRA_UNARMED_" + mode.upper() + "_METADATA_CLIP_OK slot=" + slot +
               " events=" + str(len(source_notify["events"])) +
               " curves=" + str(len(source_curves)) +
               " markers=" + str(len(source_markers)))

outputs = {
    "unarmed_" + mode + "_timing.json": timing,
    "unarmed_" + mode + "_notifies.json": notifies,
    "unarmed_" + mode + "_playback.json": playback,
    "unarmed_" + mode + "_root_motion.json": roots,
}
for filename, clips in outputs.items():
    path = output_root / filename
    payload = {"schemaVersion": 1, "clips": clips}
    if path.exists():
        if json.loads(path.read_text(encoding="utf-8")) != payload:
            raise RuntimeError("Existing Lyra auxiliary metadata differs: " + str(path))
    else:
        path.write_text(json.dumps(payload, indent=2), encoding="utf-8")
unreal.log("LYRA_UNARMED_" + mode.upper() + "_METADATA_OK clips=" + str(expected_count) +
           " events=" + str(event_count) +
           " distance=" + str(distance_count) + " markers=" + str(marker_count))
