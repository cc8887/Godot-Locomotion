"""Evaluate the retargeted ALS AimOffset in a transient UE BlendSpace."""

import hashlib
import json
import math
import os
from pathlib import Path

import unreal


root = Path(os.environ["LYRA_OUTPUT_ROOT"])
mode = os.environ["LYRA_AIM_ORACLE_MODE"]
if not root.is_absolute() or mode not in ("grid", "interp", "apply-grid", "apply-interp"):
    raise ValueError("An absolute output directory and a valid AimOffset mode are required")

inventory_bytes = (root / "unarmed_special_inventory.json").read_bytes()
inventory = json.loads(inventory_bytes)
catalog_bytes = (root / "unarmed_aim_samples_catalog.json").read_bytes()
catalog = json.loads(catalog_bytes)
if (inventory["schemaVersion"] != 1 or catalog["schemaVersion"] != 1 or
        catalog["mode"] != "aim" or catalog["inventorySha256"] !=
        hashlib.sha256(inventory_bytes).hexdigest()):
    raise RuntimeError("ALS AimOffset inputs no longer match the exported inventory")

aim = inventory["aimOffset"]
source = unreal.load_asset(aim["source"])
skeleton = unreal.load_asset(catalog["targetSkeleton"])
if (not isinstance(source, unreal.AimOffsetBlendSpace) or
        not isinstance(skeleton, unreal.Skeleton) or len(catalog["clips"]) != 15):
    raise RuntimeError("ALS AimOffset source or target skeleton is missing")


def package_hash(path):
    package = Path(unreal.Paths.project_content_dir()) / (
        path.split(".", 1)[0].removeprefix("/Game/") + ".uasset")
    return hashlib.sha256(package.read_bytes()).hexdigest()


if package_hash(aim["source"]) != aim["sourceUassetSha256"]:
    raise RuntimeError("Original AimOffset asset changed")
targets = []
for index, row in enumerate(catalog["clips"]):
    if (row["index"] != index or row["point"] != aim["samples"][index]["point"] or
            row["source"] != aim["samples"][index]["source"] or
            package_hash(row["target"]) != row["targetUassetSha256"]):
        raise RuntimeError("Retargeted ALS AimOffset catalog changed at " + str(index))
    asset = unreal.load_asset(row["target"])
    if (not isinstance(asset, unreal.AnimSequence) or
            asset.get_editor_property("skeleton") != skeleton):
        raise RuntimeError("Missing retargeted ALS AimOffset sample " + str(index))
    targets.append(asset)

axes = aim["axes"]
base = None
base_row = None
base_catalog_bytes = None
if mode.startswith("apply-"):
    base_catalog_bytes = (root / "unarmed_catalog.json").read_bytes()
    base_catalog = json.loads(base_catalog_bytes)
    base_row = next((row for row in base_catalog["clips"] if row["slot"] == "idle"), None)
    if (base_catalog["targetSkeleton"] != catalog["targetSkeleton"] or
            base_row is None or package_hash(base_row["target"]) !=
            base_row["targetUassetSha256"]):
        raise RuntimeError("ALS Unarmed Idle base changed")
    base = unreal.load_asset(base_row["target"])
    if (not isinstance(base, unreal.AnimSequence) or
            base.get_editor_property("skeleton") != skeleton or
            base_row["additiveType"] != "AAT_None"):
        raise RuntimeError("ALS Unarmed Idle is not a non-additive base")

if mode.endswith("grid"):
    inputs = [[x, y] for x in (axes[0]["min"],
                                (axes[0]["min"] + axes[0]["max"]) / 2,
                                axes[0]["max"])
              for y in (axes[1]["min"],
                            (axes[1]["min"] + axes[1]["max"]) / 2,
                            axes[1]["max"])]
else:
    inputs = [[-135, -45], [-45, 45], [45, -45], [135, 45],
              [22.5, 30], [-22.5, -30], [0, 0]]

rows = []
for x, y in inputs:
    raw = (unreal.AlsSourceAnimationLibrary.read_retargeted_aiming_pose2d(
        source, skeleton, targets, base, 0.0, x, y, 0.5)
        if base is not None else
        unreal.AlsSourceAnimationLibrary.read_retargeted_blend_space_pose2d(
            source, skeleton, targets, x, y, 0.5))
    if not raw:
        raise RuntimeError("UE could not evaluate ALS AimOffset at " + str([x, y]))
    pose = json.loads(raw)
    pose.pop("source")
    if (pose["names"] != catalog["logicalBoneNames"] or
            len(pose["pose"]) != 79 or not 1 <= len(pose["samples"]) <= 3 or
            any(not all(math.isfinite(value) for value in atom[channel])
                for atom in pose["pose"] for channel in ("position", "rotation", "scale"))):
        raise RuntimeError("UE returned an incomplete ALS AimOffset pose")
    if base is not None and (len(pose["basePose"]) != 79 or
            len(pose["appliedPose"]) != 79 or pose["baseSource"] !=
            base_row["target"] or pose["baseTime"] != 0):
        raise RuntimeError("UE did not apply the ALS AimOffset to its base pose")
    rows.append(pose)

payload = {"schemaVersion": 1, "mode": mode,
           "inventorySha256": hashlib.sha256(inventory_bytes).hexdigest(),
           "catalogSha256": hashlib.sha256(catalog_bytes).hexdigest(),
           "source": aim["source"], "targetSkeleton": catalog["targetSkeleton"],
           "data": rows}
if base is not None:
    payload["baseCatalogSha256"] = hashlib.sha256(base_catalog_bytes).hexdigest()
    payload["baseUassetSha256"] = base_row["targetUassetSha256"]
    payload["baseSource"] = base_row["target"]
output = root / ("unarmed_aim_als_native_" + mode + "_poses.json")
if output.exists():
    if json.loads(output.read_text(encoding="utf-8")) != payload:
        raise RuntimeError("Existing ALS AimOffset oracle differs: " + str(output))
else:
    output.write_text(json.dumps(payload, indent=2), encoding="utf-8")
unreal.log("LYRA_ALS_AIM_ORACLE_OK mode=" + mode + " rows=" + str(len(rows)) +
           " assets_saved=0")
