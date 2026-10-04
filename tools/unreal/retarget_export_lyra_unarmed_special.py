"""Retarget CDO-bound Lyra additive sequences to the ALS mannequin."""

import hashlib
import json
import math
import os
from pathlib import Path

import unreal


root = Path(os.environ["LYRA_OUTPUT_ROOT"])
mode = os.environ["LYRA_SPECIAL_MODE"]
profile = os.environ.get("LYRA_SPECIAL_PROFILE", "unarmed")
if (not root.is_absolute() or not root.is_dir() or mode not in ("aim", "jump") or
        profile not in ("unarmed", "pistol", "rifle")):
    raise ValueError("An existing absolute output, profile and aim/jump mode are required")
inventory_path = root / (profile + "_special_inventory.json")
inventory_bytes = inventory_path.read_bytes()
inventory = json.loads(inventory_bytes)
if inventory["schemaVersion"] != 1:
    raise RuntimeError("Lyra special inventory changed")

source_mesh = unreal.load_asset("/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny")
target_mesh = unreal.load_asset("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/Mannequin")
retargeter = unreal.load_asset("/Game/Characters/Heroes/Mannequin_UE4/Meshes/RTG_UE5Manny_UE4Manny")
if (not isinstance(source_mesh, unreal.SkeletalMesh) or
        not isinstance(target_mesh, unreal.SkeletalMesh) or
        not isinstance(retargeter, unreal.IKRetargeter)):
    raise RuntimeError("Missing Manny/ALS meshes or IK retargeter")
source_skeleton = source_mesh.get_editor_property("skeleton").get_path_name()
target_skeleton = target_mesh.get_editor_property("skeleton").get_path_name()
if (source_skeleton != inventory["sourceSkeleton"] or target_skeleton !=
        "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton"):
    raise RuntimeError("Unexpected source or target skeleton")

directory = "/Game/GodotLyraRetarget/" + profile.title() + (
    "Aim" if mode == "aim" else "JumpAdditive")
source_rows = inventory["aimOffset"]["samples"] if mode == "aim" else [inventory["jumpRecovery"]]
if mode == "aim" and (len(source_rows) < 3 or profile == "unarmed" and len(source_rows) != 15):
    raise RuntimeError("Lyra AimOffset sample count changed")
center = next((row for row in inventory["aimOffset"]["samples"] if row["point"] == [0, 0]), None)
if center is None:
    raise RuntimeError("Lyra AimOffset center sample is missing")
target_center = directory + "/LY_" + center["source"].rsplit("/", 1)[1].split(".")[0]
targets = []
for row in source_rows:
    path = row["source"]
    source = unreal.load_asset(path)
    if (not isinstance(source, unreal.AnimSequence) or
            source.get_editor_property("skeleton").get_path_name() != source_skeleton):
        raise RuntimeError("Invalid additive source: " + path)
    package = Path(unreal.Paths.project_content_dir()) / (path.removeprefix("/Game/").split(".")[0] + ".uasset")
    if not package.is_file() or hashlib.sha256(package.read_bytes()).hexdigest() != row["sourceUassetSha256"]:
        raise RuntimeError("Additive source package changed: " + path)
    name = "LY_" + source.get_name()
    target_path = directory + "/" + name
    targets.append((row, source, name, target_path))
if len({item[3] for item in targets}) != len(targets):
    raise RuntimeError("Duplicate additive target paths")

existing = {item[3]: unreal.EditorAssetLibrary.does_asset_exist(item[3]) for item in targets}
missing = [item for item in targets if not existing[item[3]]]
if missing:
    inputs = unreal.IKRetargetBatchOperationInputs()
    inputs.set_editor_property("assets_to_retarget", [
        unreal.EditorAssetLibrary.find_asset_data(row["source"]) for row, _, _, _ in missing])
    inputs.set_editor_property("source_mesh", source_mesh)
    inputs.set_editor_property("target_mesh", target_mesh)
    inputs.set_editor_property("ik_retarget_asset", retargeter)
    inputs.set_editor_property("target_path", directory)
    inputs.set_editor_property("prefix", "LY_")
    inputs.set_editor_property("include_referenced_assets", False)
    inputs.set_editor_property("overwrite_existing_files", False)
    results = unreal.IKRetargetBatchOperation.run_batch_retarget(inputs)
    actual = {asset.get_asset().get_path_name() for asset in results}
    expected = {path + "." + name for _, _, name, path in missing}
    if actual != expected:
        raise RuntimeError("Additive batch retarget produced unexpected assets: " + str(actual ^ expected))

center_asset = unreal.load_asset(target_center) if mode == "aim" else None
if mode == "aim" and not isinstance(center_asset, unreal.AnimSequence):
    raise RuntimeError("ALS AimOffset center sample was not retargeted")
skeleton_data = json.loads(unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(
    target_mesh.get_editor_property("skeleton")))
logical_names = skeleton_data["logicalBoneNames"]
logical_to_physical = skeleton_data["logicalToPhysical"]
if (len(logical_names) != 79 or len(logical_to_physical) != 79 or
        sorted(index for index in logical_to_physical if index >= 0) != list(range(68))):
    raise RuntimeError("ALS additive target is not the expected 68+11-bone rig")
catalog = []
for row, source, name, path in targets:
    target = unreal.load_asset(path)
    if (not isinstance(target, unreal.AnimSequence) or
            target.get_editor_property("skeleton").get_path_name() != target_skeleton):
        raise RuntimeError("Additive target has the wrong skeleton: " + path)
    source_metadata = row["metadata"]
    corrected_reference = False
    if mode == "jump" and source_metadata["baseAsset"] not in (None, source.get_path_name()):
        raise RuntimeError("Unsupported Jump Recovery base asset: " + path)
    expected_reference = (center_asset if mode == "aim" else
                          target if source_metadata["baseAsset"] == source.get_path_name() else None)
    if target.get_editor_property("ref_pose_seq") != expected_reference:
        if existing[path]:
            raise RuntimeError("Existing ALS additive reference differs: " + path)
        target.set_editor_property("ref_pose_seq", expected_reference)
        corrected_reference = True
    target_metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(target))
    if (source_metadata["additiveType"] != target_metadata["additiveType"] or
            source_metadata["basePoseType"] != target_metadata["basePoseType"] or
            source_metadata["baseFrame"] != target_metadata["baseFrame"] or
            abs(source_metadata["sequencePlayLength"] - target_metadata["sequencePlayLength"]) > 1e-5 or
            source_metadata["enableRootMotion"] != target_metadata["enableRootMotion"]):
        raise RuntimeError("Additive policy changed during retarget: " + path)
    if mode == "aim":
        if (target_metadata["additiveType"] != "AAT_RotationOffsetMeshSpace" or
                target_metadata["basePoseType"] != "ABPT_AnimFrame" or
                target_metadata["baseAsset"] != target_center + "." + target_center.rsplit("/", 1)[1]):
            raise RuntimeError("AimOffset reference was not retargeted to ALS: " + path)
    elif (target_metadata["additiveType"] != "AAT_LocalSpaceBase" or
          target_metadata["basePoseType"] != source_metadata["basePoseType"] or
          target_metadata["baseAsset"] != (target.get_path_name() if expected_reference else None) or
          target_metadata["baseFrame"] != source_metadata["baseFrame"] or
          profile == "unarmed" and target_metadata["baseFrame"] != 28):
        raise RuntimeError("Jump Recovery base frame was not retained")
    if (not existing[path] or corrected_reference) and not unreal.EditorAssetLibrary.save_loaded_asset(
            target, only_if_is_dirty=False):
        raise RuntimeError("Could not save additive target: " + path)
    unreal.AlsSourceAnimationLibrary.finish_source_compression(target)
    times = ([target_metadata["sequencePlayLength"] * 0.5] if mode == "aim" else
             [target_metadata["sequencePlayLength"] * frame / 28 for frame in range(29)])
    poses = []
    for time in times:
        pose = json.loads(unreal.AlsSourceAnimationLibrary.read_raw_animation_pose(
            target, time, True, False, False))
        if (not pose.get("evaluatedAdditive") or pose["names"] != logical_names or
                len(pose["pose"]) != len(logical_names) or any(
                    not all(math.isfinite(value) for value in bone[channel])
                    for bone in pose["pose"] for channel in ("position", "rotation", "scale"))):
            raise RuntimeError("ALS additive pose is incomplete: " + path + " at " + str(time))
        poses.append({"timeSeconds": pose["timeSeconds"], "pose": pose["pose"],
                      "curves": pose["curves"]})
    target_package = Path(unreal.Paths.project_content_dir()) / (path.removeprefix("/Game/") + ".uasset")
    if not target_package.is_file():
        raise RuntimeError("Additive target package is missing: " + path)
    catalog.append({
        "index": row.get("index"), "point": row.get("point"),
        "source": source.get_path_name(), "sourceUassetSha256": row["sourceUassetSha256"],
        "target": target.get_path_name(),
        "targetUassetSha256": hashlib.sha256(target_package.read_bytes()).hexdigest(),
        "metadata": target_metadata,
        "evaluatedPoses": poses,
    })
    unreal.log("LYRA_SPECIAL_CLIP_OK mode=" + mode + " target=" + path)

payload = {"schemaVersion": 1, "mode": mode,
           "inventorySha256": hashlib.sha256(inventory_bytes).hexdigest(),
           "sourceSkeleton": source_skeleton, "targetSkeleton": target_skeleton,
           "targetDirectory": directory, "logicalBoneNames": logical_names,
           "logicalToPhysical": logical_to_physical,
           "clips": catalog}
catalog_path = root / (profile + ("_aim_samples_catalog.json" if mode == "aim" else
                                "_jump_additive_catalog.json"))
if catalog_path.exists():
    if json.loads(catalog_path.read_text(encoding="utf-8")) != payload:
        raise RuntimeError("Existing additive catalog differs: " + str(catalog_path))
else:
    catalog_path.write_text(json.dumps(payload, indent=2), encoding="utf-8")
unreal.log("LYRA_SPECIAL_EXPORT_OK mode=" + mode + " clips=" + str(len(catalog)) +
           " profile=" + profile)
