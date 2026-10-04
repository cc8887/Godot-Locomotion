"""Inspect the actual UE types and additive policies bound by an item layer CDO."""

import json
import math
import os
from pathlib import Path

import unreal


root = Path(os.environ["LYRA_OUTPUT_ROOT"])
profile_name = os.environ.get("LYRA_ITEM_PROFILE", "pistol")
inventory = json.loads((root / "linked_layer_inventory.json").read_text(encoding="utf-8"))
expected_counts = {"pistol": 66, "rifle": 67, "shotgun": 68}
if profile_name not in expected_counts:
    raise ValueError("Unsupported Lyra item layer profile: " + profile_name)
profile = inventory["classes"][profile_name]
paths = set(value for value in profile["assets"].values() if value)
for cardinals in profile["cardinals"].values():
    paths.update(value for value in cardinals.values() if value)
if inventory["schemaVersion"] != 1 or len(paths) != expected_counts[profile_name]:
    raise RuntimeError(profile_name + " CDO inventory changed")

types = {}
for path in sorted(paths):
    asset = unreal.load_asset(path)
    if isinstance(asset, unreal.AnimSequence):
        metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(asset))
        kind = metadata["additiveType"]
    else:
        kind = asset.get_class().get_name() if asset else "Missing"
    types[kind] = types.get(kind, 0) + 1
    unreal.log("LYRA_ITEM_ASSET profile=" + profile_name + " path=" + path + " kind=" + kind)
unreal.log("LYRA_ITEM_INSPECT_OK profile=" + profile_name + " total=" + str(len(paths)) +
           " types=" + json.dumps(types, sort_keys=True))

probe = os.environ.get("LYRA_ITEM_POSE_PROBE", os.environ.get("LYRA_PISTOL_POSE_PROBE"))
if probe:
    path = next((path for path in paths if path.rsplit("/", 1)[-1].split(".", 1)[0] == probe), None)
    sequence = unreal.load_asset(path) if path else None
    if not isinstance(sequence, unreal.AnimSequence):
        raise RuntimeError("Unknown item pose probe: " + probe)
    metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(sequence))
    if os.environ.get("LYRA_ITEM_COMPRESSION_PROBE") == "1":
        compression = {}
        for name in (probe, "LY_" + probe):
            item = sequence if name == probe else unreal.load_asset(
                "/Game/GodotLyraRetarget/" + profile_name.title() + "/" + name)
            if not isinstance(item, unreal.AnimSequence):
                continue
            compression[name] = {
                key: item.get_editor_property(key).get_path_name()
                for key in ("bone_compression_settings", "curve_compression_settings")
            }
        retargeter = unreal.load_asset(
            "/Game/Characters/Heroes/Mannequin_UE4/Meshes/RTG_UE5Manny_UE4Manny")
        controller = unreal.IKRetargeterController.get_controller(retargeter)
        operations = []
        for index in range(controller.get_num_retarget_ops()):
            operation = controller.get_op_controller(index)
            operations.append({"index": index, "type": operation.get_class().get_name(),
                               "enabled": controller.get_retarget_op_enabled(index)})
        unreal.log("LYRA_ITEM_COMPRESSION_PROBE " + json.dumps({
            "assets": compression, "retargetOps": operations}, sort_keys=True))
    peak = (0.0, "", 0.0, "")
    frames = max(30, math.ceil(metadata["sequencePlayLength"] * 60))
    worst_rotation = (0.0, "", 0.0)
    for frame in range(frames + 1):
        time = metadata["sequencePlayLength"] * frame / frames
        pose = json.loads(unreal.AlsSourceAnimationLibrary.read_raw_bone_pose(
            sequence, time, False, False, False))
        for name, bone in zip(pose["names"], pose["pose"]):
            for channel in ("position", "rotation", "scale"):
                components = [float(component) for component in bone[channel]]
                if not all(math.isfinite(component) for component in components):
                    raise RuntimeError(f"Non-finite source pose: {probe}/{name}/{time}/{channel}")
                value = max(abs(component) for component in components)
                if value > peak[0]:
                    peak = (value, name, time, channel)
                if channel == "rotation":
                    error = abs(sum(component * component for component in components) - 1)
                    if error > worst_rotation[0]:
                        worst_rotation = (error, name, time)
    if worst_rotation[0] > 1e-3 or peak[0] > 1e5:
        raise RuntimeError(f"Invalid source pose: {probe} peak={peak} rotation={worst_rotation}")
    unreal.log("LYRA_ITEM_POSE_OK profile=" + profile_name + " name=" + probe +
               " frames=" + str(frames + 1) + " peak=" + json.dumps(peak) +
               " maxRotationNormError=" + str(worst_rotation[0]))

    if os.environ.get("LYRA_ITEM_BATCH_SOURCE_PROBE") == "1":
        options = unreal.AnimPoseEvaluationOptions()
        options.evaluation_type = unreal.AnimDataEvalType.RAW
        options.should_retarget = True
        options.extract_root_motion = False
        options.incorporate_root_motion_into_pose = True
        options.optional_skeletal_mesh = unreal.load_asset(
            "/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny")
        count = unreal.AnimationLibrary.get_num_keys(sequence)
        worst_rotation = (0.0, "", -1)
        peak = (0.0, "", -1, "")
        smallest_scale = (float("inf"), "", -1)
        for frame in range(count):
            pose = unreal.AnimPoseExtensions.get_anim_pose_at_frame(sequence, frame, options)
            if not unreal.AnimPoseExtensions.is_valid(pose):
                raise RuntimeError(f"Invalid batch source pose: {probe}/{frame}")
            for name in unreal.AnimPoseExtensions.get_bone_names(pose):
                transform = unreal.AnimPoseExtensions.get_bone_pose(
                    pose, name, unreal.AnimPoseSpaces.WORLD)
                values = {
                    "position": transform.translation,
                    "rotation": transform.rotation,
                    "scale": transform.scale3d,
                }
                for channel, value in values.items():
                    axes = ("x", "y", "z", "w") if channel == "rotation" else ("x", "y", "z")
                    components = [float(getattr(value, axis)) for axis in axes]
                    if not all(math.isfinite(component) for component in components):
                        raise RuntimeError(
                            f"Non-finite batch source pose: {probe}/{name}/{frame}/{channel}")
                    magnitude = max(abs(component) for component in components)
                    if magnitude > peak[0]:
                        peak = (magnitude, str(name), frame, channel)
                    if channel == "rotation":
                        error = abs(sum(component * component for component in components) - 1)
                        if error > worst_rotation[0]:
                            worst_rotation = (error, str(name), frame)
                    elif channel == "scale":
                        minimum = min(abs(component) for component in components)
                        if minimum < smallest_scale[0]:
                            smallest_scale = (minimum, str(name), frame)
        if worst_rotation[0] > 1e-3 or peak[0] > 1e5:
            raise RuntimeError(
                f"Invalid batch source pose: {probe} peak={peak} rotation={worst_rotation}")
        unreal.log("LYRA_ITEM_BATCH_SOURCE_OK profile=" + profile_name +
                   " name=" + probe + " frames=" + str(count) +
                   " peak=" + json.dumps(peak) +
                   " maxRotationNormError=" + str(worst_rotation[0]) +
                   " minAbsScale=" + json.dumps(smallest_scale))
