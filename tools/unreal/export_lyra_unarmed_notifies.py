"""Export authored Lyra Unarmed notify events without changing existing timing files."""

import json
import os
from pathlib import Path

import unreal


manifest_path = Path(os.environ["LYRA_CLIP_MANIFEST"])
output_root = Path(os.environ["LYRA_OUTPUT_ROOT"])
if not manifest_path.is_absolute() or not manifest_path.is_file() or not output_root.is_absolute():
    raise ValueError("Lyra notify inputs must be absolute existing paths")
manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
catalog = json.loads((output_root / "unarmed_catalog.json").read_text(encoding="utf-8"))
if manifest["schemaVersion"] != 1 or catalog["schemaVersion"] != 1:
    raise ValueError("Unsupported Lyra resource schema")
by_slot = {item["slot"]: item for item in catalog["clips"]}
if len(by_slot) != len(manifest["clips"]):
    raise RuntimeError("Lyra notify/catalog clip counts differ")

records = []
source_count = 0
target_count = 0
different_count = 0
for item in manifest["clips"]:
    slot = item["slot"]
    row = by_slot[slot]
    source = unreal.load_asset(manifest["sourceDirectory"] + "/" + item["name"])
    target = unreal.load_asset("/Game/GodotLyraRetarget/Unarmed/LY_" + item["name"])
    if not isinstance(source, unreal.AnimSequence) or not isinstance(target, unreal.AnimSequence):
        raise RuntimeError("Lyra notify asset missing: " + slot)
    if source.get_path_name() != row["source"] or target.get_path_name() != row["target"]:
        raise RuntimeError("Lyra notify asset path differs from catalog: " + slot)
    source_data = json.loads(unreal.AlsSourceAnimationLibrary.read_source_notify_metadata(source))
    target_data = json.loads(unreal.AlsSourceAnimationLibrary.read_source_notify_metadata(target))
    if abs(source_data["length"] - row["playLength"]) > 1e-4 or abs(
        target_data["length"] - row["playLength"]
    ) > 1e-4:
        raise RuntimeError("Lyra notify sequence length differs from catalog: " + slot)
    source_events = source_data["events"]
    target_events = target_data["events"]
    for label, events in (("source", source_events), ("target", target_events)):
        for index, event in enumerate(events):
            if event["index"] != index or event["triggerTime"] < -1e-3 or event[
                "endTriggerTime"
            ] > row["playLength"] + 1e-3:
                raise RuntimeError("Invalid Lyra " + label + " notify: " + slot)
    unchanged = source_events == target_events
    source_count += len(source_events)
    target_count += len(target_events)
    different_count += not unchanged
    records.append({
        "slot": slot,
        "source": row["source"],
        "target": row["target"],
        "sourceEvents": source_events,
        "targetEvents": target_events,
        "eventsUnchanged": unchanged,
    })
    unreal.log("LYRA_UNARMED_NOTIFY_CLIP_OK slot=" + slot +
               " source=" + str(len(source_events)) +
               " target=" + str(len(target_events)) +
               " unchanged=" + str(unchanged))

payload = {"schemaVersion": 1, "clips": records}
output = output_root / "unarmed_notifies.json"
if output.exists():
    if json.loads(output.read_text(encoding="utf-8")) != payload:
        raise RuntimeError("Existing Lyra notify export differs: " + str(output))
else:
    output.write_text(json.dumps(payload, indent=2), encoding="utf-8")
unreal.log("LYRA_UNARMED_NOTIFY_OK clips=" + str(len(records)) +
           " source=" + str(source_count) + " target=" + str(target_count) +
           " differentClips=" + str(different_count))
