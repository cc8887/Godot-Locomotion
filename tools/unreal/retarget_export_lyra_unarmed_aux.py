"""Retarget the CDO-bound Unarmed crouch/air resources onto the ALS mannequin."""

import hashlib
import json
import os
from pathlib import Path

import unreal


root = Path(os.environ["LYRA_OUTPUT_ROOT"])
manifest_path = Path(os.environ["LYRA_CLIP_MANIFEST"])
if not root.is_absolute() or not root.is_dir() or not manifest_path.is_absolute():
    raise ValueError("Lyra auxiliary paths must be absolute and existing")
manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
inventory = json.loads((root / "linked_layer_inventory.json").read_text(encoding="utf-8"))
groups = {
    "aux": ("/Game/GodotLyraRetarget/UnarmedAux", "unarmed_aux_catalog.json", "LYRA_UNARMED_AUX", 12),
    "crouch_transitions": ("/Game/GodotLyraRetarget/UnarmedCrouchTransitions",
                           "unarmed_crouch_transitions_catalog.json", "LYRA_UNARMED_CROUCH_TRANSITIONS", 12),
    "remaining": ("/Game/GodotLyraRetarget/UnarmedRemaining",
                  "unarmed_remaining_catalog.json", "LYRA_UNARMED_REMAINING", 17),
}
group = manifest.get("group")
if manifest["schemaVersion"] != 1 or inventory["schemaVersion"] != 1 or group not in groups:
    raise RuntimeError("Invalid Lyra auxiliary clip manifest or linked-layer inventory")
directory, catalog_name, marker, expected_count = groups[group]
if len(manifest["clips"]) != expected_count:
    raise RuntimeError("Unexpected Lyra auxiliary clip count")

source_mesh = unreal.load_asset("/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny")
target_mesh = unreal.load_asset("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/Mannequin")
retargeter = unreal.load_asset("/Game/Characters/Heroes/Mannequin_UE4/Meshes/RTG_UE5Manny_UE4Manny")
if not isinstance(source_mesh, unreal.SkeletalMesh) or not isinstance(target_mesh, unreal.SkeletalMesh) or not isinstance(retargeter, unreal.IKRetargeter):
    raise RuntimeError("Missing Lyra/ALS mesh or IK retargeter")
target_skeleton = target_mesh.get_editor_property("skeleton").get_path_name()
expected_skeleton = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton"
if target_skeleton != expected_skeleton:
    raise RuntimeError("ALS target skeleton changed")

source_directory = manifest["sourceDirectory"]
expected_directory = "/Game/Characters/Heroes/Mannequin/Animations/Locomotion"
if group != "remaining":
    expected_directory += "/Unarmed"
if source_directory != expected_directory:
    raise RuntimeError("Unexpected Lyra auxiliary source directory")
project_content = Path(unreal.Paths.project_content_dir())
options = unreal.FbxExportOption()
options.set_editor_property("fbx_export_compatibility", unreal.FbxExportCompatibility.FBX_2020)
options.set_editor_property("ascii", True)
options.set_editor_property("force_front_x_axis", True)
options.set_editor_property("map_skeletal_motion_to_root", False)

entries = []
seen_slots, seen_names = set(), set()
for clip in manifest["clips"]:
    slot, name = clip["slot"], clip["name"]
    if (slot in seen_slots or name in seen_names or
            not (name.startswith("MM_Unarmed_") or
                 group == "remaining" and name == "MM_Pistol_Crouch_Idle") or "/" in name):
        raise RuntimeError("Invalid Lyra auxiliary slot/name: " + str(clip))
    seen_slots.add(slot)
    seen_names.add(name)
    subdirectory = clip.get("subdirectory", "Unarmed")
    if group == "remaining" and subdirectory not in ("Unarmed", "Pistol"):
        raise RuntimeError("Unsupported Lyra remaining source directory: " + subdirectory)
    if group != "remaining" and "subdirectory" in clip:
        raise RuntimeError("Unexpected Lyra auxiliary subdirectory: " + slot)
    source_path = source_directory + ("/" + subdirectory if group == "remaining" else "") + "/" + name
    binding = (inventory["classes"]["unarmed"]["assets"][clip["property"]]
               if "property" in clip else
               inventory["classes"]["unarmed"]["cardinals"][clip["cardinal"]][clip["direction"]])
    if binding != source_path + "." + name:
        raise RuntimeError("Unarmed CDO does not bind the expected asset: " + slot)
    target_name = "LY_" + name
    target_path = directory + "/" + target_name
    source = unreal.load_asset(source_path)
    if not isinstance(source, unreal.AnimSequence) or source.get_editor_property("skeleton") != source_mesh.get_editor_property("skeleton"):
        raise RuntimeError("Invalid Lyra source sequence: " + slot)
    output = root / "animations" / (target_name + ".fbx")
    sidecar = output.with_suffix(".source.json")
    if output.exists() != sidecar.exists():
        raise RuntimeError("Partial auxiliary FBX export: " + str(output))
    if output.exists():
        recorded = json.loads(sidecar.read_text(encoding="utf-8"))
        if (recorded["source"] != target_path + "." + target_name or
                recorded["skeleton"] != expected_skeleton or
                recorded["fbxSha256"].lower() != hashlib.sha256(output.read_bytes()).hexdigest()):
            raise RuntimeError("Existing auxiliary FBX is not the expected asset: " + slot)
    entries.append((clip, source, source_path, target_path, output, sidecar))

catalog = []
for clip, source, source_path, target_path, output, sidecar in entries:
    slot = clip["slot"]
    unreal.log(marker + "_START slot=" + slot)
    new_target = not unreal.EditorAssetLibrary.does_asset_exist(target_path)
    if new_target:
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
        if len(results) != 1:
            raise RuntimeError("Lyra auxiliary retarget did not produce exactly one asset: " + slot)
    target = unreal.load_asset(target_path)
    if not isinstance(target, unreal.AnimSequence) or target.get_editor_property("skeleton").get_path_name() != expected_skeleton:
        raise RuntimeError("Lyra auxiliary retarget has the wrong skeleton: " + slot)
    source_metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(source))
    target_metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(target))
    if (group == "remaining" and source_metadata["additiveType"] != "AAT_None" or
            abs(source_metadata["sequencePlayLength"] - target_metadata["sequencePlayLength"]) > 1e-5 or
            source_metadata["enableRootMotion"] != target_metadata["enableRootMotion"] or
            source_metadata["additiveType"] != target_metadata["additiveType"]):
        raise RuntimeError("Lyra auxiliary sequence metadata changed during retarget: " + slot)
    if new_target and not unreal.EditorAssetLibrary.save_loaded_asset(target, only_if_is_dirty=False):
        raise RuntimeError("Could not save Lyra auxiliary sequence: " + slot)
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
            raise RuntimeError("Lyra auxiliary FBX export failed: " + slot)
        sidecar.write_text(json.dumps({
            "schemaVersion": 1, "source": target.get_path_name(),
            "skeleton": expected_skeleton, "fbx": output.name,
            "fbxSha256": hashlib.sha256(output.read_bytes()).hexdigest(),
            "metadata": target_metadata,
        }, indent=2), encoding="utf-8")
    source_file = project_content / (source_path.removeprefix("/Game/") + ".uasset")
    target_file = project_content / (target_path.removeprefix("/Game/") + ".uasset")
    if not source_file.is_file() or not target_file.is_file():
        raise RuntimeError("Missing auxiliary source/retarget package: " + slot)
    catalog.append({
        "slot": slot, "source": source.get_path_name(),
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
    unreal.log(marker + "_CLIP_OK slot=" + slot + " target=" + target_path)

catalog_path = root / catalog_name
payload = {"schemaVersion": 1, "sourceDirectory": source_directory,
           "targetDirectory": directory, "targetSkeleton": expected_skeleton,
           "clips": catalog}
if catalog_path.exists():
    if json.loads(catalog_path.read_text(encoding="utf-8")) != payload:
        raise RuntimeError("Existing Lyra auxiliary catalog differs: " + str(catalog_path))
else:
    catalog_path.write_text(json.dumps(payload, indent=2), encoding="utf-8")
unreal.log(marker + "_EXPORT_OK clips=" + str(len(catalog)))
