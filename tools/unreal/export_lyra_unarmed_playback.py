"""Read source and ALS-retargeted Lyra sequence rate scales without saving assets."""

import json
import math
import os
from pathlib import Path

import unreal


manifest_path = Path(os.environ["LYRA_CLIP_MANIFEST"])
output_root = Path(os.environ["LYRA_OUTPUT_ROOT"])
if not manifest_path.is_absolute() or not manifest_path.is_file() or not output_root.is_absolute():
    raise ValueError("Lyra playback inputs must be absolute existing paths")
manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
catalog = json.loads((output_root / "unarmed_catalog.json").read_text(encoding="utf-8"))
if manifest["schemaVersion"] != 1 or catalog["schemaVersion"] != 1:
    raise ValueError("Unsupported Lyra resource schema")
by_slot = {item["slot"]: item for item in catalog["clips"]}
if len(by_slot) != len(manifest["clips"]):
    raise RuntimeError("Lyra playback/catalog clip counts differ")

records = []
for item in manifest["clips"]:
    slot = item["slot"]
    row = by_slot[slot]
    source = unreal.load_asset(manifest["sourceDirectory"] + "/" + item["name"])
    target = unreal.load_asset("/Game/GodotLyraRetarget/Unarmed/LY_" + item["name"])
    if not isinstance(source, unreal.AnimSequence) or not isinstance(target, unreal.AnimSequence):
        raise RuntimeError("Lyra playback asset missing: " + slot)
    if source.get_path_name() != row["source"] or target.get_path_name() != row["target"]:
        raise RuntimeError("Lyra playback asset differs from catalog: " + slot)
    source_rate = float(source.get_editor_property("rate_scale"))
    target_rate = float(target.get_editor_property("rate_scale"))
    if not all(math.isfinite(rate) and rate > 0 for rate in (source_rate, target_rate)):
        raise RuntimeError("Invalid Lyra sequence rate scale: " + slot)
    records.append({
        "slot": slot,
        "source": row["source"],
        "target": row["target"],
        "sourceRateScale": source_rate,
        "targetRateScale": target_rate,
    })
    unreal.log("LYRA_UNARMED_PLAYBACK_CLIP_OK slot=" + slot +
               " sourceRate=" + str(source_rate) + " targetRate=" + str(target_rate))

payload = {"schemaVersion": 1, "clips": records}
output = output_root / "unarmed_playback.json"
if output.exists():
    if json.loads(output.read_text(encoding="utf-8")) != payload:
        raise RuntimeError("Existing Lyra playback export differs: " + str(output))
else:
    output.write_text(json.dumps(payload, indent=2), encoding="utf-8")
unreal.log("LYRA_UNARMED_PLAYBACK_OK clips=" + str(len(records)))
