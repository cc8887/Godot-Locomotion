"""Retarget a pinned Lyra Unarmed set onto ALS and export Godot FBX sources."""

import hashlib
import json
import os
from pathlib import Path

import unreal


SOURCE_MESH = "/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny"
TARGET_MESH = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/Mannequin"
RETARGETER = "/Game/Characters/Heroes/Mannequin_UE4/Meshes/RTG_UE5Manny_UE4Manny"
TARGET_DIRECTORY = "/Game/GodotLyraRetarget/Unarmed"
EXPECTED_SKELETON = (
    "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/"
    "ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton"
)


def required_absolute_directory(name):
    path = Path(os.environ[name])
    if not path.is_absolute() or not path.is_dir():
        raise ValueError(name + " must name an existing absolute directory")
    return path


manifest_path = Path(os.environ["LYRA_CLIP_MANIFEST"])
if not manifest_path.is_absolute() or not manifest_path.is_file():
    raise ValueError("LYRA_CLIP_MANIFEST must name an existing absolute file")
output_root = required_absolute_directory("LYRA_OUTPUT_ROOT")
manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
if manifest["schemaVersion"] != 1 or not manifest["clips"]:
    raise ValueError("Unsupported or empty Lyra clip manifest")
clips = manifest["clips"]
if len({item["name"] for item in clips}) != len(clips) or len({item["slot"] for item in clips}) != len(clips):
    raise ValueError("Duplicate source clip or semantic slot")

source_mesh = unreal.load_asset(SOURCE_MESH)
target_mesh = unreal.load_asset(TARGET_MESH)
retargeter = unreal.load_asset(RETARGETER)
if not isinstance(source_mesh, unreal.SkeletalMesh) or not isinstance(target_mesh, unreal.SkeletalMesh):
    raise RuntimeError("Missing source or ALS target mesh")
if not isinstance(retargeter, unreal.IKRetargeter):
    raise RuntimeError("Missing Manny-to-UE4 retargeter")
if target_mesh.get_editor_property("skeleton").get_path_name() != EXPECTED_SKELETON:
    raise RuntimeError("ALS target mesh skeleton changed")

source_directory = manifest["sourceDirectory"]
project_content = Path(unreal.Paths.project_content_dir())
prepared = []
for clip in clips:
    name = clip["name"]
    if not name.startswith("MM_Unarmed_") or "/" in name or "\\" in name:
        raise ValueError("Unexpected clip name: " + name)
    source_path = source_directory + "/" + name
    target_path = TARGET_DIRECTORY + "/LY_" + name
    source = unreal.load_asset(source_path)
    if not isinstance(source, unreal.AnimSequence):
        raise RuntimeError("Missing Lyra AnimSequence: " + source_path)
    if source.get_editor_property("skeleton") != source_mesh.get_editor_property("skeleton"):
        raise RuntimeError("Source has the wrong skeleton: " + source_path)
    output = output_root / "animations" / ("LY_" + name + ".fbx")
    sidecar = output.with_suffix(".source.json")
    if output.exists() != sidecar.exists():
        raise RuntimeError("Partial existing FBX export: " + str(output))
    if output.exists():
        recorded = json.loads(sidecar.read_text(encoding="utf-8"))
        if (recorded.get("source") != target_path + ".LY_" + name or
                recorded.get("skeleton") != EXPECTED_SKELETON or
                recorded.get("fbxSha256", "").lower() != hashlib.sha256(output.read_bytes()).hexdigest()):
            raise RuntimeError("Existing FBX export is not the expected asset: " + str(output))
    prepared.append((clip, source, source_path, target_path, output, sidecar))

options = unreal.FbxExportOption()
options.set_editor_property("fbx_export_compatibility", unreal.FbxExportCompatibility.FBX_2020)
options.set_editor_property("ascii", True)
options.set_editor_property("force_front_x_axis", True)
options.set_editor_property("map_skeletal_motion_to_root", False)
catalog = []
for clip, source, source_path, target_path, output, sidecar in prepared:
    unreal.log("LYRA_UNARMED_START slot=" + clip["slot"])
    new_target = not unreal.EditorAssetLibrary.does_asset_exist(target_path)
    if new_target:
        inputs = unreal.IKRetargetBatchOperationInputs()
        inputs.set_editor_property("assets_to_retarget", [unreal.EditorAssetLibrary.find_asset_data(source_path)])
        inputs.set_editor_property("source_mesh", source_mesh)
        inputs.set_editor_property("target_mesh", target_mesh)
        inputs.set_editor_property("ik_retarget_asset", retargeter)
        inputs.set_editor_property("target_path", TARGET_DIRECTORY)
        inputs.set_editor_property("prefix", "LY_")
        inputs.set_editor_property("include_referenced_assets", False)
        inputs.set_editor_property("overwrite_existing_files", False)
        results = unreal.IKRetargetBatchOperation.run_batch_retarget(inputs)
        if len(results) != 1:
            raise RuntimeError("Retarget did not produce exactly one asset for " + source_path)
    target = unreal.load_asset(target_path)
    if not isinstance(target, unreal.AnimSequence) or target.get_editor_property("skeleton").get_path_name() != EXPECTED_SKELETON:
        raise RuntimeError("Retarget result does not use ALS skeleton: " + target_path)
    source_metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(source))
    target_metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(target))
    if abs(source_metadata["sequencePlayLength"] - target_metadata["sequencePlayLength"]) > 1e-5:
        raise RuntimeError("Retarget changed clip duration: " + target_path)
    if source_metadata["enableRootMotion"] != target_metadata["enableRootMotion"]:
        raise RuntimeError("Retarget changed root motion setting: " + target_path)
    if new_target and not unreal.EditorAssetLibrary.save_loaded_asset(target, only_if_is_dirty=False):
        raise RuntimeError("Could not save " + target_path)
    unreal.AlsSourceAnimationLibrary.finish_source_compression(target)

    if not output.exists():
        output.parent.mkdir(parents=True, exist_ok=True)
        task = unreal.AssetExportTask()
        task.set_editor_property("object", target)
        task.set_editor_property("exporter", unreal.AnimSequenceExporterFBX())
        task.set_editor_property("filename", str(output))
        task.set_editor_property("options", options)
        task.set_editor_property("automated", True)
        task.set_editor_property("prompt", False)
        task.set_editor_property("replace_identical", False)
        task.set_editor_property("use_file_archive", True)
        if not unreal.Exporter.run_asset_export_task(task) or not output.is_file():
            raise RuntimeError("FBX export failed: " + target_path)
        sidecar.write_text(json.dumps({
            "schemaVersion": 1,
            "source": target.get_path_name(),
            "skeleton": EXPECTED_SKELETON,
            "fbx": output.name,
            "fbxSha256": hashlib.sha256(output.read_bytes()).hexdigest(),
            "metadata": target_metadata,
        }, indent=2), encoding="utf-8")

    source_file = project_content / (source_path.removeprefix("/Game/") + ".uasset")
    target_file = project_content / (target_path.removeprefix("/Game/") + ".uasset")
    if not source_file.is_file() or not target_file.is_file():
        raise RuntimeError("Cannot hash source and retarget assets for " + clip["name"])
    catalog.append({
        "slot": clip["slot"],
        "source": source.get_path_name(),
        "sourceUassetSha256": hashlib.sha256(source_file.read_bytes()).hexdigest(),
        "target": target.get_path_name(),
        "targetUassetSha256": hashlib.sha256(target_file.read_bytes()).hexdigest(),
        "fbx": "animations/" + output.name,
        "fbxSha256": hashlib.sha256(output.read_bytes()).hexdigest(),
        "playLength": target_metadata["sequencePlayLength"],
        "enableRootMotion": target_metadata["enableRootMotion"],
        "forceRootLock": target_metadata["forceRootLock"],
        "rootMotionRootLock": target_metadata["rootMotionRootLock"],
        "additiveType": target_metadata["additiveType"],
        "floatCurveNames": target_metadata["floatCurveNames"],
    })
    unreal.log("LYRA_UNARMED_CLIP_OK slot=" + clip["slot"] + " target=" + target_path)

catalog_path = output_root / "unarmed_catalog.json"
if catalog_path.exists():
    old = json.loads(catalog_path.read_text(encoding="utf-8"))
    if old.get("clips") != catalog:
        raise RuntimeError("Existing Lyra catalog differs; refusing to overwrite " + str(catalog_path))
else:
    catalog_path.write_text(json.dumps({
        "schemaVersion": 1,
        "sourceProject": str(unreal.Paths.get_project_file_path()),
        "sourceMesh": SOURCE_MESH,
        "targetMesh": TARGET_MESH,
        "targetSkeleton": EXPECTED_SKELETON,
        "ikRetargeter": RETARGETER,
        "clips": catalog,
    }, indent=2), encoding="utf-8")
unreal.log("LYRA_UNARMED_EXPORT_OK clips=" + str(len(catalog)))
