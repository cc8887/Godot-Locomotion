"""Retarget one Lyra locomotion sequence onto the ALS mannequin in GASP58."""

import json
import os
from pathlib import Path

__repository_root = Path(__file__).resolve().parents[2]

import unreal


SOURCE = os.environ.get("LYRA_RETARGET_SOURCE",
                        "/Game/Characters/Heroes/Mannequin/Animations/Locomotion/Unarmed/MM_Unarmed_Jog_Fwd")
SOURCE_MESH = "/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny"
TARGET_MESH = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/Mannequin"
RETARGETER = "/Game/Characters/Heroes/Mannequin_UE4/Meshes/RTG_UE5Manny_UE4Manny"
DESTINATION = "/Game/GodotLyraRetarget/Unarmed"
OUTPUT = (__repository_root / 'artifacts/lyra-analysis') / (
    "retarget-probe-" + SOURCE.rsplit("/", 1)[1] + ".json")


source = unreal.load_asset(SOURCE)
source_mesh = unreal.load_asset(SOURCE_MESH)
target_mesh = unreal.load_asset(TARGET_MESH)
retargeter = unreal.load_asset(RETARGETER)
if not isinstance(source, unreal.AnimSequence) or not isinstance(source_mesh, unreal.SkeletalMesh):
    raise RuntimeError("Missing Lyra source animation or mesh")
if not isinstance(target_mesh, unreal.SkeletalMesh) or not isinstance(retargeter, unreal.IKRetargeter):
    raise RuntimeError("Missing ALS target mesh or Lyra IK retargeter")

target_path = DESTINATION + "/LY_" + source.get_name()
if unreal.EditorAssetLibrary.does_asset_exist(target_path):
    raise RuntimeError("Refusing to overwrite existing retargeted asset: " + target_path)

inputs = unreal.IKRetargetBatchOperationInputs()
inputs.set_editor_property("assets_to_retarget", [unreal.EditorAssetLibrary.find_asset_data(SOURCE)])
inputs.set_editor_property("source_mesh", source_mesh)
inputs.set_editor_property("target_mesh", target_mesh)
inputs.set_editor_property("ik_retarget_asset", retargeter)
inputs.set_editor_property("target_path", DESTINATION)
inputs.set_editor_property("prefix", "LY_")
inputs.set_editor_property("include_referenced_assets", False)
inputs.set_editor_property("overwrite_existing_files", False)

results = unreal.IKRetargetBatchOperation.run_batch_retarget(inputs)
if len(results) != 1:
    raise RuntimeError("Expected one retargeted sequence, got " + str(len(results)))
target = results[0].get_asset()
if not isinstance(target, unreal.AnimSequence) or target.get_path_name() != target_path + "." + target_path.rsplit("/", 1)[1]:
    raise RuntimeError("Unexpected retarget output: " + str(target))
if target.get_editor_property("skeleton") != target_mesh.get_editor_property("skeleton"):
    raise RuntimeError("Retarget output is not bound to the ALS skeleton")
if not unreal.EditorAssetLibrary.save_loaded_asset(target, only_if_is_dirty=False):
    raise RuntimeError("Could not save retargeted asset")

source_metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(source))
target_metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(target))
target_skeleton = json.loads(unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(
    target.get_editor_property("skeleton")))
pose = json.loads(unreal.AlsSourceAnimationLibrary.read_raw_bone_pose(
    target, 0.0, True, False, False))
if pose["names"] != target_skeleton["logicalBoneNames"]:
    raise RuntimeError("Target pose layout does not match ALS skeleton")
unreal.AlsSourceAnimationLibrary.finish_source_compression(target)

OUTPUT.write_text(json.dumps({
    "source": source.get_path_name(),
    "target": target.get_path_name(),
    "targetSkeleton": target_skeleton["source"],
    "sourceMetadata": source_metadata,
    "targetMetadata": target_metadata,
    "poseBoneCount": len(pose["names"]),
}, indent=2), encoding="utf-8")
unreal.log("LYRA_ALS_RETARGET_PROBE_OK target=" + target.get_path_name() +
           " bones=" + str(len(pose["names"])))
