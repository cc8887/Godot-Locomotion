"""Read source weapon/VB channels and actual native AimOffset -> CopyBone poses.

No source or target asset is saved. Ordinary keys and interval midpoints are
recorded before any graph blend; additive channels retain their native meaning.
"""
import hashlib
import json
import math
import os
from pathlib import Path

import unreal

root = Path(os.environ["LYRA_OUTPUT_ROOT"])
if not root.is_absolute() or not root.is_dir():
    raise ValueError("Existing absolute LYRA_OUTPUT_ROOT required")
catalog_names = ("unarmed_catalog.json", "unarmed_aux_catalog.json",
                 "unarmed_remaining_catalog.json", "unarmed_crouch_transitions_catalog.json",
                 "pistol_catalog.json", "rifle_catalog.json")
dependencies = {}


def read(name):
    data = (root / name).read_bytes()
    dependencies[name] = hashlib.sha256(data).hexdigest()
    return json.loads(data)


def package_hash(path):
    package = path.split(".", 1)[0]
    if not package.startswith("/Game/"):
        raise ValueError("Expected a project asset: " + path)
    return hashlib.sha256((Path(unreal.Paths.project_content_dir()) /
                           (package[6:] + ".uasset")).read_bytes()).hexdigest()


def save(name, payload):
    output = root / name
    if output.exists():
        if json.loads(output.read_text(encoding="utf-8")) != payload:
            raise RuntimeError("Existing weapon-space export differs: " + name)
    else:
        output.write_text(json.dumps(payload, separators=(",", ":")), encoding="utf-8")
    return hashlib.sha256(output.read_bytes()).hexdigest()


ordinary = [(name, row) for name in catalog_names for row in read(name)["clips"]]
aim_clips = [(profile, row) for profile in ("unarmed", "pistol", "rifle")
             for row in read(profile + "_aim_samples_catalog.json")["clips"]]
inventories = {profile: read(profile + "_special_inventory.json")
               for profile in ("unarmed", "pistol", "rifle")}
defaults = read("skeletal_control_defaults.json")
read("pose_layer_contracts.json")
read("unarmed_layer_masks.json")
if len(ordinary) != 189 or len(aim_clips) != 45:
    raise RuntimeError("Expected all 189 ordinary and 45 AimOffset clips")

assets = {}
for _, row in ordinary + aim_clips:
    for key in ("source", "target"):
        value = package_hash(row[key])
        if value != row[key + "UassetSha256"]:
            raise RuntimeError("Asset changed: " + row[key])
        assets[row[key]] = value
for inventory in inventories.values():
    aim = inventory["aimOffset"]
    value = package_hash(aim["source"])
    if value != aim["sourceUassetSha256"]:
        raise RuntimeError("AimOffset changed")
    assets[aim["source"]] = value

samples = []
names = ["hand_r", "hand_l", "weapon_r", "VB IK_Hand_L_weaponSpace"]
for category, row in ordinary + aim_clips:
    sequence = unreal.load_asset(row["source"])
    data = json.loads(unreal.AlsHandControlLibrary.read_weapon_space_samples(sequence))
    additive = "index" in row
    if (data["source"] != row["source"] or data["names"] != names or
            data["additive"] != additive or len(data["samples"]) != 2 * data["keyCount"] - 1):
        raise RuntimeError("Incomplete control samples: " + row["source"])
    for sample in data["samples"]:
        atoms = sample["local"] + sample.get("component", [])
        if (len(sample["local"]) != 4 or
                any(not math.isfinite(v) for atom in atoms for values in atom.values() for v in values)):
            raise RuntimeError("Invalid control atom")
    data["category"] = category
    data["slot"] = row.get("slot", "aim_" + str(row.get("index")))
    samples.append(data)
    unreal.log("LYRA_WEAPON_SPACE_CLIP " + data["slot"] + " samples=" + str(len(data["samples"])))

source_payload = {"schemaVersion": 1, "dependencySha256": dependencies.copy(),
                  "assetSha256": assets, "layout": defaults["skeletons"]["source"]["layout"],
                  "sampling": "GetAnimationPose RAW; every key and interval midpoint; no graph blend",
                  "clips": samples}
source_sha = save("weapon_space_sources.json", source_payload)

inputs = [(x, y) for x in (-180, 0, 180) for y in (-90, 0, 90)] + [
    (-135, -45), (-45, 45), (45, -45), (135, 45), (22.5, 30), (-22.5, -30), (0, 0)]
rows = []
for profile, idle_slot, cycle_slot in (("unarmed", "idle", "jog_fwd_cycle"),
                                      ("pistol", "pistol_idle_hipfire", "pistol_jog_fwd_cycle"),
                                      ("rifle", "rifle_idle_hipfire", "rifle_jog_fwd_cycle")):
    blend_space = unreal.load_asset(inventories[profile]["aimOffset"]["source"])
    for slot, ratio in ((idle_slot, 0), (cycle_slot, 0.37)):
        base_row = next(row for _, row in ordinary if row["slot"] == slot)
        sequence = unreal.load_asset(base_row["source"])
        skeleton = sequence.get_editor_property("skeleton")
        time = base_row["playLength"] * ratio
        for x, y in inputs:
            pose = json.loads(unreal.AlsSourceAnimationLibrary.read_raw_aiming_pose2d(
                blend_space, sequence, time, x, y, 0.5))
            if (len(pose["appliedPose"]) != 164 or
                    pose["names"] != source_payload["layout"]["logicalBoneNames"]):
                raise RuntimeError("Source AimOffset has an unexpected layout")
            before = pose["appliedPose"]
            for alpha in (0, 0.25, 0.5, 1):
                copy = json.loads(unreal.AlsHandControlLibrary.read_weapon_space_copy_pose(
                    skeleton, json.dumps({"pose": before}, separators=(",", ":")), alpha))
                if copy["changedBones"] != 1 or len(copy["after"]) != 164:
                    raise RuntimeError("Native CopyBone is incomplete")
                rows.append({"profile": profile, "slot": slot, "time": time,
                             "input": [x, y], "alpha": alpha, "before": before,
                             "after": copy["after"], "additiveControls": [pose["pose"][i] for i in (88, 163)]})
if len(rows) != 384 or any(package_hash(path) != value for path, value in assets.items()):
    raise RuntimeError("Unexpected native case count or mutated source/target package")
native = {"schemaVersion": 1, "sourceDataSha256": source_sha,
          "layout": source_payload["layout"], "rows": rows,
          "nativeOperator": "FAnimNode_CopyBone ComponentSpace translation+rotation + FCSPose.LocalBlendCSBoneTransforms"}
save("weapon_space_native.json", native)
# A unit-scale oracle cannot prove bCopyScale=false. Feed the real node
# independently scaled source/target channels and a nonuniform target parent.
# These are controlled pose fixtures, not modifications to animation assets.
scaled_rows = []
bone_names = native["layout"]["logicalBoneNames"]
for row in rows:
    if row["alpha"] != 1 or row["input"] not in ([-180, -90], [22.5, 30]):
        continue
    before = json.loads(json.dumps(row["before"]))
    for name, scale in (("weapon_r", [1.1, 0.9, 1.05]), ("ik_hand_l", [0.8, 1.2, 1.1]),
                        ("ik_hand_gun", [1.3, 0.7, 1.1])):
        before[bone_names.index(name)]["scale"] = scale
    for alpha in (0, 0.25, 0.5, 1):
        copy = json.loads(unreal.AlsHandControlLibrary.read_weapon_space_copy_pose(
            skeleton, json.dumps({"pose": before}, separators=(",", ":")), alpha))
        if copy["changedBones"] != 1 or len(copy["after"]) != 164:
            raise RuntimeError("Scaled native CopyBone is incomplete")
        scaled_rows.append({"profile": row["profile"], "slot": row["slot"], "input": row["input"],
                            "alpha": alpha, "before": before, "after": copy["after"]})
if len(scaled_rows) != 48 or any(package_hash(path) != value for path, value in assets.items()):
    raise RuntimeError("Incomplete scaled oracle or mutated asset")
save("weapon_space_native_scaled.json", {"schemaVersion": 1, "sourceDataSha256": source_sha,
     "layout": native["layout"], "rows": scaled_rows, "nativeOperator": native["nativeOperator"],
     "fixture": "Nonuniform weapon/IK-left/IK-gun scales; original copyScale=false"})
unreal.log("LYRA_WEAPON_SPACE_OK ordinary=189 additive=45 native=384 scaled=48 assets_saved=0")
