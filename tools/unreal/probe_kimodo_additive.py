"""Probe Kimodo ALS_Final additive metadata and skeleton identity for the raw source export."""
import json
import os
from pathlib import Path

import unreal

# original additive -> baseAsset from the original raw sequence files
ORIG_BASE = {
    "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/AimOffsets/ALS_N_Look_D_Sweep.ALS_N_Look_D_Sweep": "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/BasePoses/ALS_N_Pose.ALS_N_Pose",
    "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/AimOffsets/ALS_N_Look_U_Sweep.ALS_N_Look_U_Sweep": "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/BasePoses/ALS_N_Pose.ALS_N_Pose",
    "/Game/AdvancedLocomotionV4/CharacterAssets/Mankeleton/AnimationExamples/AimOffsets/ALS_N_Look_F_Sweep.ALS_N_Look_F_Sweep": "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/BasePoses/ALS_N_Pose.ALS_N_Pose",
}
ORIG_BASE["/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/AimOffsets/ALS_N_Look_F_Sweep.ALS_N_Look_F_Sweep"] = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/BasePoses/ALS_N_Pose.ALS_N_Pose"

OUT = Path(os.environ["KIMODO_ADDITIVE_PROBE_OUTPUT"])
manifest_path = Path(r"F:/Github/Godot-Locomotion/assets/generated/kimodo_als/als_manifest.json")
manifest = json.loads(manifest_path.read_text(encoding="utf-8"))

def decode(text):
    return json.loads(text, parse_int=lambda token: -0.0 if token == "-0" else int(token))

rows = []
skeletons = {}
for entry in manifest["animations"]:
    source = entry.get("kimodoSource")
    if not source:
        continue
    retargeted = source["retargetedAssetPath"]
    animation = unreal.load_asset(retargeted)
    if animation is None:
        raise RuntimeError("Missing Kimodo asset: " + retargeted)
    evaluation = decode(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(animation))
    skeleton = animation.get_editor_property("skeleton")
    skeleton_source = skeleton.get_path_name()
    if skeleton_source not in skeletons:
        skeletons[skeleton_source] = decode(unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(skeleton))
    rows.append({
        "retargeted": retargeted,
        "original": entry["objectPath"],
        "baseAsset": evaluation["baseAsset"],
        "additiveAnimType": evaluation.get("additiveAnimType"),
        "skeleton": skeleton_source,
        "tracks": None,
    })

# sampled detail: pose keys for the additive set that matters for dependency recursion
sample_targets = [
    "/Game/KimodoMocara/ALS_Final/AS_ALS_N_Look_D_Sweep.AS_ALS_N_Look_D_Sweep",
    "/Game/KimodoMocara/ALS_Final/AS_ALS_N_LeanPose_L.AS_ALS_N_LeanPose_L",
    "/Game/KimodoMocara/ALS_Final/AS_ALS_Props_Bow_Aim_Sweep.AS_ALS_Props_Bow_Aim_Sweep",
    "/Game/KimodoMocara/ALS_Final/AS_ALS_N_Stop_L_Down.AS_ALS_N_Stop_L_Down",
]
samples = []
for target in sample_targets:
    animation = unreal.load_asset(target)
    if animation is None:
        raise RuntimeError("Missing sample target: " + target)
    evaluation = decode(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(animation))
    row = decode(unreal.AlsSourcePoseKeysLibrary.read_source_pose_keys(animation))
    samples.append({
        "retargeted": target,
        "baseAsset": evaluation["baseAsset"],
        "enableRootMotion": evaluation["enableRootMotion"],
        "forceRootLock": evaluation["forceRootLock"],
        "additiveAnimType": evaluation.get("additiveAnimType"),
        "tracks": len(row["tracks"]),
        "keyCount": row["sampledKeyCount"],
        "frameRate": [row["frameRateNumerator"], row["frameRateDenominator"]],
    })

result = {"schemaVersion": 1, "animationCount": len(rows), "skeletons": skeletons, "additives": rows, "samples": samples}
OUT.parent.mkdir(parents=True, exist_ok=True)
OUT.write_text(json.dumps(result, indent=2, allow_nan=False) + "\n", encoding="utf-8")

additive_count = sum(1 for row in rows if row["baseAsset"])
unreal.log("KIMODO_ADDITIVE_PROBE_OK animations=" + str(len(rows)) + " additive=" + str(additive_count) +
           " skeletons=" + str(len(skeletons)) + " samples=" + str(len(samples)))
