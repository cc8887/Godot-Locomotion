"""Read full-range root motion used by Lyra's cycle play-rate calculation."""

import json
import math
import os
from pathlib import Path

import unreal


manifest_path = Path(os.environ["LYRA_CLIP_MANIFEST"])
output_root = Path(os.environ["LYRA_OUTPUT_ROOT"])
if not manifest_path.is_absolute() or not manifest_path.is_file() or not output_root.is_absolute():
    raise ValueError("Lyra root-motion inputs must be absolute existing paths")
manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
catalog = json.loads((output_root / "unarmed_catalog.json").read_text(encoding="utf-8"))
if manifest["schemaVersion"] != 1 or catalog["schemaVersion"] != 1:
    raise ValueError("Unsupported Lyra resource schema")
by_slot = {item["slot"]: item for item in catalog["clips"]}
if len(by_slot) != len(manifest["clips"]):
    raise RuntimeError("Lyra root-motion/catalog clip counts differ")

records = []
for item in manifest["clips"]:
    slot = item["slot"]
    row = by_slot[slot]
    source = unreal.load_asset(manifest["sourceDirectory"] + "/" + item["name"])
    target = unreal.load_asset("/Game/GodotLyraRetarget/Unarmed/LY_" + item["name"])
    if not isinstance(source, unreal.AnimSequence) or not isinstance(target, unreal.AnimSequence):
        raise RuntimeError("Lyra root-motion asset missing: " + slot)
    if source.get_path_name() != row["source"] or target.get_path_name() != row["target"]:
        raise RuntimeError("Lyra root-motion asset path differs from catalog: " + slot)
    source_range = json.loads(unreal.AlsSourceAnimationLibrary.read_source_root_motion_range(
        source, 0.0, row["playLength"]))
    target_range = json.loads(unreal.AlsSourceAnimationLibrary.read_source_root_motion_range(
        target, 0.0, row["playLength"]))
    for label, data in (("source", source_range), ("target", target_range)):
        if data["source"] != row[label] or abs(data["playLength"] - row["playLength"]) > 1e-4:
            raise RuntimeError("Lyra " + label + " root range differs from catalog: " + slot)
        if not math.isfinite(data["planarDistanceCm"]) or data["planarDistanceCm"] < 0:
            raise RuntimeError("Invalid Lyra " + label + " root distance: " + slot)
    records.append({"slot": slot, "source": source_range, "target": target_range})
    unreal.log("LYRA_UNARMED_ROOT_CLIP_OK slot=" + slot +
               " sourceCm=" + str(source_range["planarDistanceCm"]) +
               " targetCm=" + str(target_range["planarDistanceCm"]))

payload = {"schemaVersion": 1, "clips": records}
output = output_root / "unarmed_root_motion.json"
if output.exists():
    if json.loads(output.read_text(encoding="utf-8")) != payload:
        raise RuntimeError("Existing Lyra root-motion export differs: " + str(output))
else:
    output.write_text(json.dumps(payload, indent=2), encoding="utf-8")
unreal.log("LYRA_UNARMED_ROOT_OK clips=" + str(len(records)))
