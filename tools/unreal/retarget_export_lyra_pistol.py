"""Retarget CDO-bound Pistol or Rifle sequences onto the ALS mannequin."""

import hashlib
import json
import math
import os
from pathlib import Path

import unreal


root = Path(os.environ["LYRA_OUTPUT_ROOT"])
if not root.is_absolute() or not root.is_dir():
    raise ValueError("LYRA_OUTPUT_ROOT must be an existing absolute directory")
inventory = json.loads((root / "linked_layer_inventory.json").read_text(encoding="utf-8"))
if inventory["schemaVersion"] != 1:
    raise RuntimeError("Unsupported linked-layer inventory")
profile_name = os.environ.get("LYRA_ITEM_PROFILE", "pistol")
expected_counts = {"pistol": (66, 63), "rifle": (67, 64)}
if profile_name not in expected_counts:
    raise ValueError("Unsupported Lyra ordinary item profile: " + profile_name)
profile = inventory["classes"][profile_name]
profile_title = profile_name.title()
source_directory = "/Game/Characters/Heroes/Mannequin/Animations/Locomotion/" + profile_title
target_directory = "/Game/GodotLyraRetarget/" + profile_title
target_skeleton = (
    "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/"
    "ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton"
)
source_mesh = unreal.load_asset("/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny")
target_mesh = unreal.load_asset("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/Mannequin")
retargeter = unreal.load_asset("/Game/Characters/Heroes/Mannequin_UE4/Meshes/RTG_UE5Manny_UE4Manny")
if (not isinstance(source_mesh, unreal.SkeletalMesh) or
        not isinstance(target_mesh, unreal.SkeletalMesh) or
        not isinstance(retargeter, unreal.IKRetargeter) or
        target_mesh.get_editor_property("skeleton").get_path_name() != target_skeleton):
    raise RuntimeError(profile_title + " source or ALS retarget rig differs")

cycle_groups = {"Walk_Cardinals", "Jog_Cardinals", "Crouch_Walk_Cardinals"}
loop_assets = {"Idle_ADS", "Idle_Hipfire", "Crouch_Idle", "Crouch_Idle_Entry",
               "Crouch_Idle_Exit", "Jump_StartLoop", "Jump_FallLoop"}
bindings = {}
for property_name, path in profile["assets"].items():
    if path:
        bindings.setdefault(path, []).append(property_name)
for group_name, directions in profile["cardinals"].items():
    for direction, path in directions.items():
        if path:
            bindings.setdefault(path, []).append(group_name + "/" + direction)
if len(bindings) != expected_counts[profile_name][0]:
    raise RuntimeError(profile_title + " CDO bindings changed")

prepared = []
special = {}
for path, properties in sorted(bindings.items()):
    asset = unreal.load_asset(path)
    if isinstance(asset, unreal.AimOffsetBlendSpace):
        special[path] = "AimOffsetBlendSpace"
        continue
    if not isinstance(asset, unreal.AnimSequence):
        raise RuntimeError(profile_title + " CDO asset is missing or unsupported: " + path)
    metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(asset))
    if metadata["additiveType"] != "AAT_None":
        special[path] = metadata["additiveType"]
        continue
    name = asset.get_name()
    if (not path.startswith(source_directory + "/") or
            path != source_directory + "/" + name + "." + name or
            not name.startswith("MM_" + profile_title + "_") or
            asset.get_editor_property("skeleton") != source_mesh.get_editor_property("skeleton")):
        raise RuntimeError("Invalid " + profile_title + " ordinary sequence: " + path)
    loop = bool(loop_assets.intersection(properties) or
                any(value.split("/", 1)[0] in cycle_groups for value in properties))
    slot = profile_name + "_" + name.removeprefix("MM_" + profile_title + "_").lower()
    if any(value.split("/", 1)[0] in cycle_groups for value in properties):
        slot += "_cycle"
    target_name = "LY_" + name
    target_path = target_directory + "/" + target_name
    output = root / "animations" / profile_name / (target_name + ".fbx")
    sidecar = output.with_suffix(".source.json")
    if output.exists() != sidecar.exists():
        raise RuntimeError("Partial " + profile_title + " FBX export: " + str(output))
    if output.exists():
        recorded = json.loads(sidecar.read_text(encoding="utf-8"))
        if (recorded["source"] != target_path + "." + target_name or
                recorded["skeleton"] != target_skeleton or
                recorded["fbxSha256"].lower() != hashlib.sha256(output.read_bytes()).hexdigest()):
            raise RuntimeError("Existing " + profile_title + " FBX differs: " + str(output))
    prepared.append((slot, path, properties, loop, asset, metadata,
                     target_path, output, sidecar))
if (len(prepared) != expected_counts[profile_name][1] or len(special) != 3 or
        len({row[0] for row in prepared}) != len(prepared)):
    raise RuntimeError(profile_title + " ordinary/additive inventory changed: " +
                       str((len(prepared), len(special))))
batch_new = int(os.environ.get("LYRA_ITEM_BATCH_NEW",
                               os.environ.get("LYRA_PISTOL_BATCH_NEW", "0")))
if batch_new < 0:
    raise ValueError("LYRA_ITEM_BATCH_NEW must be nonnegative")
if batch_new:
    pending = [row for row in prepared if not row[7].exists()]
    allowed = {row[0] for row in pending[:batch_new]}
    prepared = [row for row in prepared if row[7].exists() or row[0] in allowed]
partial = len(prepared) != expected_counts[profile_name][1]

project_content = Path(unreal.Paths.project_content_dir())
options = unreal.FbxExportOption()
options.set_editor_property("fbx_export_compatibility", unreal.FbxExportCompatibility.FBX_2020)
options.set_editor_property("ascii", True)
options.set_editor_property("force_front_x_axis", True)
options.set_editor_property("map_skeletal_motion_to_root", False)
catalog, timing, notifies, playback, roots = [], [], [], [], []
for slot, source_path, properties, loop, source, source_metadata, target_path, output, sidecar in prepared:
    unreal.log("LYRA_ITEM_START profile=" + profile_name + " slot=" + slot)
    new_target = not unreal.EditorAssetLibrary.does_asset_exist(target_path)
    if new_target:
        inputs = unreal.IKRetargetBatchOperationInputs()
        inputs.set_editor_property("assets_to_retarget", [
            unreal.EditorAssetLibrary.find_asset_data(source_path.split(".", 1)[0])])
        inputs.set_editor_property("source_mesh", source_mesh)
        inputs.set_editor_property("target_mesh", target_mesh)
        inputs.set_editor_property("ik_retarget_asset", retargeter)
        inputs.set_editor_property("target_path", target_directory)
        inputs.set_editor_property("prefix", "LY_")
        inputs.set_editor_property("include_referenced_assets", False)
        inputs.set_editor_property("overwrite_existing_files", False)
        results = unreal.IKRetargetBatchOperation.run_batch_retarget(inputs)
        if len(results) != 1:
            raise RuntimeError(profile_title + " retarget did not produce one asset: " + slot)
    target = unreal.load_asset(target_path)
    target_metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(target))
    if (not isinstance(target, unreal.AnimSequence) or
            target.get_editor_property("skeleton").get_path_name() != target_skeleton or
            abs(source_metadata["sequencePlayLength"] - target_metadata["sequencePlayLength"]) > 1e-5 or
            source_metadata["additiveType"] != target_metadata["additiveType"] or
            source_metadata["enableRootMotion"] != target_metadata["enableRootMotion"]):
        raise RuntimeError(profile_title + " retarget changed source policy: " + slot)
    if new_target and not unreal.EditorAssetLibrary.save_loaded_asset(target, only_if_is_dirty=False):
        raise RuntimeError("Could not save " + profile_title + " target: " + slot)
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
            raise RuntimeError(profile_title + " FBX export failed: " + slot)
        sidecar.write_text(json.dumps({
            "schemaVersion": 1, "source": target.get_path_name(),
            "skeleton": target_skeleton, "fbx": output.name,
            "fbxSha256": hashlib.sha256(output.read_bytes()).hexdigest(),
            "metadata": target_metadata,
        }, indent=2), encoding="utf-8")
    source_file = project_content / (source_path.split(".", 1)[0].removeprefix("/Game/") + ".uasset")
    target_file = project_content / (target_path.removeprefix("/Game/") + ".uasset")
    if not source_file.is_file() or not target_file.is_file():
        raise RuntimeError(profile_title + " source or target package is missing: " + slot)
    source_curves = json.loads(unreal.AlsSourceAnimationLibrary.read_source_float_curves(source))["curves"]
    target_curves = json.loads(unreal.AlsSourceAnimationLibrary.read_source_float_curves(target))["curves"]
    source_markers = json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(source))["markers"]
    target_markers = json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(target))["markers"]
    source_notify = json.loads(unreal.AlsSourceAnimationLibrary.read_source_notify_metadata(source))
    target_notify = json.loads(unreal.AlsSourceAnimationLibrary.read_source_notify_metadata(target))
    length = target_metadata["sequencePlayLength"]
    source_root = json.loads(unreal.AlsSourceAnimationLibrary.read_source_root_motion_range(source, 0.0, length))
    target_root = json.loads(unreal.AlsSourceAnimationLibrary.read_source_root_motion_range(target, 0.0, length))
    source_rate = float(source.get_editor_property("rate_scale"))
    target_rate = float(target.get_editor_property("rate_scale"))
    mismatches = []
    if source_curves != target_curves:
        mismatches.append("curves=" + str([value["name"] for value in source_curves]) +
                          "/" + str([value["name"] for value in target_curves]))
    if source_markers != target_markers:
        mismatches.append("markers=" + str(source_markers) + "/" + str(target_markers))
    if source_notify["events"] != target_notify["events"]:
        mismatches.append("notifies=" + str(source_notify["events"]) +
                          "/" + str(target_notify["events"]))
    if any(abs(value["length"] - length) > 1e-4 for value in (source_notify, target_notify)):
        mismatches.append("notifyLength")
    if any(not math.isfinite(rate) or rate <= 0 for rate in (source_rate, target_rate)):
        mismatches.append("rate")
    if any(value["source"] != path or abs(value["playLength"] - length) > 1e-4
           for value, path in ((source_root, source_path), (target_root, target.get_path_name()))):
        mismatches.append("rootIdentity")
    if mismatches:
        raise RuntimeError(profile_title + " source/target metadata differs: " + slot + " " +
                           "; ".join(mismatches))
    catalog.append({
        "slot": slot, "source": source_path,
        "sourceUassetSha256": hashlib.sha256(source_file.read_bytes()).hexdigest(),
        "target": target.get_path_name(),
        "targetUassetSha256": hashlib.sha256(target_file.read_bytes()).hexdigest(),
        "fbx": "animations/" + profile_name + "/" + output.name,
        "fbxSha256": hashlib.sha256(output.read_bytes()).hexdigest(),
        "playLength": length, "enableRootMotion": target_metadata["enableRootMotion"],
        "forceRootLock": target_metadata["forceRootLock"],
        "rootMotionRootLock": target_metadata["rootMotionRootLock"],
        "additiveType": target_metadata["additiveType"],
        "floatCurveNames": target_metadata["floatCurveNames"],
        "loop": loop, "cdoProperties": properties,
    })
    timing.append({
        "slot": slot, "source": source_path, "target": target.get_path_name(),
        "sourceCurves": source_curves, "targetCurves": target_curves,
        "sourceSyncMarkers": source_markers, "targetSyncMarkers": target_markers,
        "curvesUnchanged": True, "syncMarkersUnchanged": True,
    })
    notifies.append({
        "slot": slot, "source": source_path, "target": target.get_path_name(),
        "sourceEvents": source_notify["events"], "targetEvents": target_notify["events"],
        "eventsUnchanged": True,
    })
    playback.append({
        "slot": slot, "source": source_path, "target": target.get_path_name(),
        "sourceRateScale": source_rate, "targetRateScale": target_rate,
    })
    roots.append({"slot": slot, "source": source_root, "target": target_root})
    unreal.log("LYRA_ITEM_CLIP_OK profile=" + profile_name + " slot=" + slot)

outputs = {
    profile_name + "_catalog.json": {
        "schemaVersion": 1, "sourceDirectory": source_directory,
        "targetDirectory": target_directory, "targetSkeleton": target_skeleton,
        "clips": catalog,
    },
    profile_name + "_timing.json": {"schemaVersion": 1, "clips": timing},
    profile_name + "_notifies.json": {"schemaVersion": 1, "clips": notifies},
    profile_name + "_playback.json": {"schemaVersion": 1, "clips": playback},
    profile_name + "_root_motion.json": {"schemaVersion": 1, "clips": roots},
}
if partial:
    unreal.log("LYRA_ITEM_BATCH_OK profile=" + profile_name + " clips=" + str(len(catalog)) +
               " remaining=" + str(expected_counts[profile_name][1] - len(catalog)))
else:
    for name, payload in outputs.items():
        path = root / name
        if path.exists():
            if json.loads(path.read_text(encoding="utf-8")) != payload:
                raise RuntimeError("Existing " + profile_title + " metadata differs: " + str(path))
        else:
            path.write_text(json.dumps(payload, indent=2), encoding="utf-8")
    unreal.log("LYRA_ITEM_EXPORT_OK profile=" + profile_name + " clips=" + str(len(catalog)) +
               " notifies=" + str(sum(len(row["sourceEvents"]) for row in notifies)) +
               " markers=" + str(sum(len(row["sourceSyncMarkers"]) for row in timing)))
