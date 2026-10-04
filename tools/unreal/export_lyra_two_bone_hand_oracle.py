"""Read actual TwoBoneIK + native local blend on the ALS target skeleton."""
import hashlib
import json
import os
from pathlib import Path

import unreal

root = Path(os.environ["LYRA_OUTPUT_ROOT"])
if not root.is_absolute() or not root.is_dir():
    raise ValueError("An existing absolute LYRA_OUTPUT_ROOT is required")
dependencies = {}
clips = []
for profile, slot in (("unarmed", "jog_fwd_cycle"), ("pistol", "pistol_jog_fwd_cycle"),
                      ("rifle", "rifle_jog_fwd_cycle")):
    path = root / (profile + "_catalog.json")
    data = path.read_bytes()
    dependencies[path.name] = hashlib.sha256(data).hexdigest()
    clips.append(next(row for row in json.loads(data)["clips"] if row["slot"] == slot))
for name in ("pose_layer_contracts.json", "skeletal_control_defaults.json", "linked_layer_inventory.json"):
    dependencies[name] = hashlib.sha256((root / name).read_bytes()).hexdigest()

def package_hash(asset):
    package = asset.split(".")[0]
    if not package.startswith("/Game/"):
        raise ValueError("Expected a project asset")
    return hashlib.sha256((Path(unreal.Paths.project_content_dir()) / (package[6:] + ".uasset")).read_bytes()).hexdigest()

assets = {clip[key]: package_hash(clip[key]) for clip in clips for key in ("source", "target")}
rows = []
for clip in clips:
    animation = unreal.load_asset(clip["target"])
    unreal.AlsSourceAnimationLibrary.finish_source_compression(animation)
    for time in (0, clip["playLength"] * 0.37, clip["playLength"] * 0.75):
        for right in (False, True):
            for retarget in (False, True):
                for alpha in (0, 0.000001, 0.25, 0.5, 0.999995, 1):
                    for offset in ((0, 0, 0), (0, 10, 5), (150, -50, 20)):
                        row = json.loads(unreal.AlsHandControlLibrary.read_two_bone_hand_pose(
                            animation, time, right, retarget, alpha, unreal.Vector(*offset)))
                        if (row["source"] != clip["target"] or len(row["names"]) != 79 or
                                len(row["before"]) != 79 or len(row["after"]) != 79 or
                                row["changedBones"] != 3 or row["takeEffectorRotation"] == right or
                                row["allowStretching"] or not row["allowTwist"] or
                                row["maintainEffectorRelativeRotation"]):
                            raise RuntimeError("Incomplete or changed native TwoBoneIK output")
                        row["slot"] = clip["slot"]
                        rows.append(row)
if len(rows) != 648 or any(package_hash(path) != value for path, value in assets.items()):
    raise RuntimeError("Unexpected case count or mutated asset")
layout = json.loads(unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(animation.get_editor_property("skeleton")))
payload = {"schemaVersion": 1, "dependencySha256": dependencies, "assetSha256": assets,
           "layout": layout, "rows": rows,
           "nativeOperator": "FAnimNode_TwoBoneIK + FCSPose.LocalBlendCSBoneTransforms"}
output = root / "two_bone_hand_native.json"
if output.exists():
    if json.loads(output.read_text(encoding="utf-8")) != payload:
        raise RuntimeError("Existing TwoBoneIK oracle differs")
else:
    output.write_text(json.dumps(payload, separators=(",", ":")), encoding="utf-8")
unreal.log("LYRA_TWO_BONE_HAND_NATIVE_OK clips=3 cases=648 physical=68 logical=79 assets_saved=0")
