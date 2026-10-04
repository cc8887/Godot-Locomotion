"""Export the actual CDO-bound left-hand evaluators on the ALS skeleton."""
import hashlib
import json
import math
import os
from pathlib import Path

import unreal


root = Path(os.environ["LYRA_OUTPUT_ROOT"])
if not root.is_absolute() or not root.is_dir():
    raise ValueError("An existing absolute LYRA_OUTPUT_ROOT is required")
inventory_bytes = (root / "linked_layer_inventory.json").read_bytes()
inventory = json.loads(inventory_bytes)
source_mesh = unreal.load_asset("/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny")
target_mesh = unreal.load_asset("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/Mannequin")
retargeter = unreal.load_asset("/Game/Characters/Heroes/Mannequin_UE4/Meshes/RTG_UE5Manny_UE4Manny")
target_skeleton = target_mesh.get_editor_property("skeleton")
layout = json.loads(unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(target_skeleton))
if len(layout["rawBoneNames"]) != 68 or len(layout["logicalBoneNames"]) != 79:
    raise RuntimeError("Unexpected ALS left-hand skeleton")


def digest(path):
    package = Path(unreal.Paths.project_content_dir()) / (path.removeprefix("/Game/").split(".")[0] + ".uasset")
    return hashlib.sha256(package.read_bytes()).hexdigest()


clips = []
directory = "/Game/GodotLyraRetarget/LeftHandPoses"
for profile, row in inventory["classes"].items():
    if not row["scalars"]["EnableLeftHandPoseOverride"]:
        continue
    source_path = row["assets"]["LeftHandPose_Override"]
    source = unreal.load_asset(source_path)
    if (not isinstance(source, unreal.AnimSequence) or
            source.get_editor_property("skeleton") != source_mesh.get_editor_property("skeleton") or
            source.get_editor_property("additive_anim_type") != unreal.AdditiveAnimationType.AAT_NONE):
        raise RuntimeError("Invalid left-hand evaluator source: " + str(source_path))
    source_hash = digest(source_path)
    name = "LY_" + source.get_name()
    target_path = directory + "/" + name
    if not unreal.EditorAssetLibrary.does_asset_exist(target_path):
        inputs = unreal.IKRetargetBatchOperationInputs()
        inputs.set_editor_property("assets_to_retarget", [unreal.EditorAssetLibrary.find_asset_data(source_path)])
        inputs.set_editor_property("source_mesh", source_mesh)
        inputs.set_editor_property("target_mesh", target_mesh)
        inputs.set_editor_property("ik_retarget_asset", retargeter)
        inputs.set_editor_property("target_path", directory)
        inputs.set_editor_property("prefix", "LY_")
        inputs.set_editor_property("include_referenced_assets", False)
        inputs.set_editor_property("overwrite_existing_files", False)
        results = unreal.IKRetargetBatchOperation.run_batch_retarget(inputs)
        if {item.get_asset().get_path_name() for item in results} != {target_path + "." + name}:
            raise RuntimeError("Unexpected left-hand retarget output")
        target = unreal.load_asset(target_path)
        if not unreal.EditorAssetLibrary.save_loaded_asset(target, only_if_is_dirty=False):
            raise RuntimeError("Could not save left-hand target")
    target = unreal.load_asset(target_path)
    if target.get_editor_property("skeleton") != target_skeleton:
        raise RuntimeError("Left-hand target skeleton differs")
    unreal.AlsSourceAnimationLibrary.finish_source_compression(target)
    pose = json.loads(unreal.AlsSourceAnimationLibrary.read_raw_animation_pose(target, 0, True, True, False))
    if (pose["names"] != layout["logicalBoneNames"] or len(pose["pose"]) != 79 or
            any(not all(math.isfinite(x) for x in atom[channel])
                for atom in pose["pose"] for channel in ("position", "rotation", "scale")) or
            digest(source_path) != source_hash):
        raise RuntimeError("Invalid left-hand native pose or source changed")
    clips.append({"profile": profile, "source": source_path, "sourceUassetSha256": source_hash,
                  "target": target.get_path_name(), "targetUassetSha256": digest(target.get_path_name()),
                  "timeSeconds": pose["timeSeconds"], "pose": pose["pose"], "curves": pose["curves"]})
if len(clips) != 2:
    raise RuntimeError("Left-hand evaluator inventory changed")
payload = {"schemaVersion": 1, "inventorySha256": hashlib.sha256(inventory_bytes).hexdigest(),
           "targetSkeleton": target_skeleton.get_path_name(), "logicalBoneNames": layout["logicalBoneNames"],
           "logicalToPhysical": layout["logicalToPhysical"], "clips": clips}
output = root / "left_hand_pose_catalog.json"
if output.exists():
    if json.loads(output.read_text(encoding="utf-8")) != payload:
        raise RuntimeError("Existing left-hand pose catalog differs")
else:
    output.write_text(json.dumps(payload, indent=2), encoding="utf-8")
unreal.log("LYRA_LEFT_HAND_POSES_OK clips=2 physical=68 logical=79")
