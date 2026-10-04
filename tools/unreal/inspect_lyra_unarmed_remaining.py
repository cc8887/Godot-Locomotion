"""Inventory CDO-bound Unarmed assets absent from the ALS export catalogs."""

import json
import os
from pathlib import Path

import unreal


root = Path(os.environ["LYRA_OUTPUT_ROOT"])
inventory = json.loads((root / "linked_layer_inventory.json").read_text(encoding="utf-8"))
if inventory["schemaVersion"] != 1:
    raise RuntimeError("Unsupported Lyra linked-layer inventory")
known = set()
for filename in ("unarmed_catalog.json", "unarmed_aux_catalog.json",
                 "unarmed_crouch_transitions_catalog.json"):
    catalog = json.loads((root / filename).read_text(encoding="utf-8"))
    known.update(clip["source"] for clip in catalog["clips"])

bindings = {}
profile = inventory["classes"]["unarmed"]
for name, path in profile["assets"].items():
    if path:
        bindings.setdefault(path, []).append("asset:" + name)
for group, directions in profile["cardinals"].items():
    for direction, path in directions.items():
        if path:
            bindings.setdefault(path, []).append("cardinal:" + group + ":" + direction)
missing = sorted(set(bindings) - known)
if len(missing) != 19:
    raise RuntimeError("Unexpected Unarmed remaining asset count: " + str(len(missing)))

rows = []
for path in missing:
    asset = unreal.load_asset(path)
    if not asset or asset.get_path_name() != path:
        raise RuntimeError("Missing Unarmed layer asset: " + path)
    row = {"source": path, "bindings": sorted(bindings[path]),
           "assetClass": asset.get_class().get_name()}
    if isinstance(asset, unreal.AnimSequence):
        row["metadata"] = json.loads(
            unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(asset))
        row["skeleton"] = asset.get_editor_property("skeleton").get_path_name()
    else:
        try:
            row["skeleton"] = asset.get_editor_property("skeleton").get_path_name()
        except Exception:
            row["skeleton"] = None
    rows.append(row)
    unreal.log("LYRA_UNARMED_REMAINING_CLIP_OK class=" + row["assetClass"] +
               " source=" + path)

payload = {"schemaVersion": 1, "knownSequenceCount": len(known), "assets": rows}
output = root / "unarmed_remaining_inventory.json"
if output.exists():
    if json.loads(output.read_text(encoding="utf-8")) != payload:
        raise RuntimeError("Existing Unarmed remaining inventory changed: " + str(output))
else:
    output.write_text(json.dumps(payload, indent=2), encoding="utf-8")
unreal.log("LYRA_UNARMED_REMAINING_OK assets=" + str(len(rows)) +
           " sequences=" + str(sum(row["assetClass"] == "AnimSequence" for row in rows)))
