"""Read-only native AimOffset grid or pose oracle using the external plugin."""

import hashlib
import json
import os
from pathlib import Path

import unreal


root = Path(os.environ["LYRA_OUTPUT_ROOT"])
mode = os.environ["LYRA_AIM_NATIVE_MODE"]
profile = os.environ.get("LYRA_SPECIAL_PROFILE", "unarmed")
if (not root.is_absolute() or profile not in ("unarmed", "pistol", "rifle") or
        mode not in ("grid", "poses", "interp-grid", "interp-poses")):
    raise ValueError("An absolute Lyra output and valid native AimOffset mode are required")
inventory_path = root / (profile + "_special_inventory.json")
inventory_bytes = inventory_path.read_bytes()
inventory = json.loads(inventory_bytes)
aim = inventory["aimOffset"]
source = aim["source"]
space = unreal.load_asset(source)
if (inventory["schemaVersion"] != 1 or not isinstance(space, unreal.AimOffsetBlendSpace) or
        space.get_path_name() != source or len(aim["samples"]) < 3 or
        profile == "unarmed" and len(aim["samples"]) != 15):
    raise RuntimeError("Lyra AimOffset inventory changed")
axes = aim["axes"]
if mode.startswith("interp-"):
    inputs = [[-135, -45], [-45, 45], [45, -45], [135, 45],
              [22.5, 30], [-22.5, -30], [0, 0]]
else:
    inputs = [[x, y] for x in (axes[0]["min"],
                                (axes[0]["min"] + axes[0]["max"]) / 2, axes[0]["max"])
              for y in (axes[1]["min"],
                        (axes[1]["min"] + axes[1]["max"]) / 2, axes[1]["max"])]
if mode.endswith("grid"):
    result = json.loads(unreal.AlsSourceAnimationLibrary.read_blend_space_triangulation_reference(
        space, json.dumps({"inputs": inputs, "filter": False})))
    if result["source"] != source or len(result["cases"]) != len(inputs):
        raise RuntimeError("Native AimOffset triangulation is incomplete")
    rows = result
else:
    rows = []
    for x, y in inputs:
        pose = json.loads(unreal.AlsSourceAnimationLibrary.read_raw_blend_space_pose2d(
            space, x, y, 0.5))
        if (pose["source"] != source or len(pose["pose"]) != 164 or
                len(pose["names"]) != 164):
            raise RuntimeError("Native AimOffset pose is incomplete")
        rows.append(pose)

payload = {"schemaVersion": 1, "inventorySha256": hashlib.sha256(inventory_bytes).hexdigest(),
           "source": source, "mode": mode, "data": rows}
output = root / (profile + "_aim_native_" + mode.replace("-", "_") + ".json")
if output.exists():
    if json.loads(output.read_text(encoding="utf-8")) != payload:
        raise RuntimeError("Existing native AimOffset output differs: " + str(output))
else:
    output.write_text(json.dumps(payload, indent=2), encoding="utf-8")
unreal.log("LYRA_AIM_NATIVE_OK mode=" + mode + " rows=" +
           str(len(rows["cases"]) if mode.endswith("grid") else len(rows)) +
           " profile=" + profile + " assets_saved=0")
