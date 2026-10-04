"""Evaluate the actual UE HandIKRetargeting node and its native local blend."""
import hashlib
import json
import os
from pathlib import Path

import unreal

root = Path(os.environ["LYRA_OUTPUT_ROOT"])
if not root.is_absolute() or not root.is_dir():
    raise ValueError("An existing absolute LYRA_OUTPUT_ROOT is required")
catalogs = {}
clips = []
for profile, slot in (("unarmed", "jog_fwd_cycle"), ("pistol", "pistol_jog_fwd_cycle"),
                      ("rifle", "rifle_jog_fwd_cycle")):
    data = (root / (profile + "_catalog.json")).read_bytes()
    catalogs[profile] = hashlib.sha256(data).hexdigest()
    clips.append(next(row for row in json.loads(data)["clips"] if row["slot"] == slot))
rows = []
for clip in clips:
    animation = unreal.load_asset(clip["target"])
    unreal.AlsSourceAnimationLibrary.finish_source_compression(animation)
    for time in (0, clip["playLength"] * 0.37, clip["playLength"] * 0.75):
        for weight in (-0.1, 0, 0.00001, 0.25, 0.5, 0.75, 0.99999, 1, 1.1):
            for alpha in (0, 0.25, 0.5, 1):
                row = json.loads(unreal.AlsHandControlLibrary.read_hand_retarget_pose(animation, time, weight, alpha))
                if (row["source"] != clip["target"] or len(row["names"]) != 79 or
                        len(row["before"]) != 79 or len(row["after"]) != 79):
                    raise RuntimeError("Incomplete native hand-retarget output")
                row["slot"] = clip["slot"]
                rows.append(row)
layout = json.loads(unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(animation.get_editor_property("skeleton")))
payload = {"schemaVersion": 1, "catalogSha256": catalogs, "layout": layout, "rows": rows,
           "nativeOperator": "FAnimNode_HandIKRetargeting + FCSPose.LocalBlendCSBoneTransforms"}
output = root / "hand_retarget_native.json"
if output.exists():
    if json.loads(output.read_text(encoding="utf-8")) != payload:
        raise RuntimeError("Existing native hand-retarget oracle differs")
else:
    output.write_text(json.dumps(payload, separators=(",", ":")), encoding="utf-8")
unreal.log("LYRA_HAND_RETARGET_NATIVE_OK clips=3 cases=" + str(len(rows)) + " physical=68 logical=79 assets_saved=0")
