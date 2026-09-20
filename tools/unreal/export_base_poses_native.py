"""Native BasePoses samples and FTransform oracle; no asset saves or project mutation."""
import json
import os
import re
import struct
import tempfile
from pathlib import Path

import unreal


def atom(transform):
    p, q, s = transform.translation, transform.rotation, transform.scale3d
    return {"position": [p.x, p.y, p.z], "rotation": [q.x, q.y, q.z, q.w], "scale": [s.x, s.y, s.z]}


def transform(position, rotation, scale):
    # Construct a native quaternion through Rotator rather than duplicating UE Euler conventions.
    value = unreal.Transform()
    value.translation = unreal.Vector(*position)
    value.rotation = unreal.Rotator(*rotation).quaternion()
    value.scale3d = unreal.Vector(*scale)
    return value


def native_text(asset):
    with tempfile.TemporaryDirectory(prefix="als-base-poses-") as directory:
        task = unreal.AssetExportTask()
        task.object = asset
        task.exporter = unreal.ObjectExporterT3D()
        task.filename = str(Path(directory) / "asset.t3d")
        task.automated = True
        task.prompt = False
        if not unreal.Exporter.run_asset_export_task(task):
            raise RuntimeError("Native asset text export failed: " + asset.get_path_name())
        data = Path(task.filename).read_bytes()
        text = data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")
    return text


def virtual_bone_definitions(text):
    # VirtualBones is serialized but has no Python property accessor in this engine build.
    rows = re.findall(r'^\s*VirtualBones\(\d+\)=\(SourceBoneName="([^"]+)",TargetBoneName="([^"]+)",VirtualBoneName="([^"]+)"\)\s*$', text, re.MULTILINE)
    if len(rows) != 11:
        raise RuntimeError("Expected the eleven authored native virtual bones")
    return rows


def inputs_payload(payload):
    """Production input metadata is separate from sampled pose/oracle values."""
    return {"schemaVersion": payload["schemaVersion"], "skeletonSource": payload["skeletonSource"],
            "skeletonText": payload["skeletonText"],
            "assets": [{key: asset[key] for key in ("source", "nativeText", "tracks", "curveNames")}
                       for asset in payload["assets"]]}


def export():
    output = Path(os.environ["ALS_BASE_POSES_OUTPUT"])
    if not output.is_absolute():
        raise ValueError("ALS_BASE_POSES_OUTPUT must be absolute")
    pairs = [
        ([1, 1, 1], [1, 1, 1]), ([2, 3, 4], [.7, 1.2, 2.1]),
        ([-2, 3, 4], [1.2, .8, 2]), ([2, -3, 4], [-.7, 1.2, 2.1]),
        ([-2, -3, -4], [.7, 1.2, 2.1]),
    ]
    threshold = struct.unpack("f", struct.pack("f", 1e-8))[0]
    next_threshold = struct.unpack("f", struct.pack("I", struct.unpack("I", struct.pack("f", threshold))[0] + 1))[0]
    for tiny in (0, 1e-9, threshold, next_threshold, 1e-7):
        for sign in (1, -1):
            pairs.append(([2, 3, 4], [tiny, sign * 1.2, 2.1]))
    cases = []
    for index, (a_scale, b_scale) in enumerate(pairs):
        a = transform([1.7, -3.2, 5.6], [23, -47, 11], a_scale)
        b = transform([-4.3, 2.8, 6.1], [-31, 19, 63], b_scale)
        cases.append({"index": index, "a": atom(a), "b": atom(b),
                      "compose": atom(unreal.MathLibrary.compose_transforms(a, b)),
                      "relative": atom(unreal.MathLibrary.make_relative_transform(a, b))})
    base = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/BasePoses/"
    assets = []
    skeleton_source = None
    skeleton_text = None
    options = unreal.AnimPoseEvaluationOptions()
    options.evaluation_type = unreal.AnimDataEvalType.RAW
    options.should_retarget = True
    options.extract_root_motion = False
    options.incorporate_root_motion_into_pose = False
    for name in ("ALS_N_Pose", "ALS_CLF_Pose"):
        asset = unreal.load_asset(base + name)
        if not isinstance(asset, unreal.AnimSequence):
            raise RuntimeError("Missing BasePoses sequence: " + name)
        pose = unreal.AnimPoseExtensions.get_anim_pose_at_time(asset, 0.0, options)
        if not unreal.AnimPoseExtensions.is_valid(pose):
            raise RuntimeError("Invalid native BasePoses sample: " + name)
        names = list(unreal.AnimPoseExtensions.get_bone_names(pose))
        if len(names) != 79 or sum(str(bone).startswith("VB ") for bone in names) != 11:
            raise RuntimeError("Native BasePoses sample does not contain the complete logical skeleton")
        options.should_retarget = False
        try:
            raw_pose = unreal.AnimPoseExtensions.get_anim_pose_at_time(asset, 0.0, options)
        finally:
            options.should_retarget = True
        if not unreal.AnimPoseExtensions.is_valid(raw_pose) or list(unreal.AnimPoseExtensions.get_bone_names(raw_pose)) != names:
            raise RuntimeError("Raw BasePoses sample does not retain the logical skeleton: " + name)
        name_indices = {str(bone): index for index, bone in enumerate(names)}
        skeleton = asset.get_editor_property("skeleton")
        if skeleton_source is None:
            skeleton_source = skeleton.get_path_name()
            skeleton_text = native_text(skeleton)
        elif skeleton.get_path_name() != skeleton_source:
            raise RuntimeError("BasePoses assets do not share the same native skeleton")
        virtual_bones = []
        for source, target, name in virtual_bone_definitions(skeleton_text):
            virtual_bones.append({"bone": name_indices[name], "source": name_indices[source], "target": name_indices[target]})
        virtual_parents = {bone["bone"]: bone["source"] for bone in virtual_bones}
        parents = []
        mapping = []
        physical_count = 0
        for index, bone in enumerate(names):
            if index in virtual_parents:
                parents.append(virtual_parents[index])
                mapping.append(-1)
            else:
                path = unreal.AnimationLibrary.find_bone_path_to_root(asset, bone)
                parents.append(name_indices[str(path[1])] if len(path) > 1 else -1)
                mapping.append(physical_count)
                physical_count += 1
        assets.append({"source": asset.get_path_name(), "timeSeconds": 0,
                       "nativeText": native_text(asset),
                       "tracks": [str(bone) for bone in unreal.AnimationLibrary.get_animation_track_names(asset)],
                       "curveNames": [str(curve) for curve in unreal.AnimPoseExtensions.get_curve_names(pose)],
                       "rawCurveNames": [str(curve) for curve in unreal.AnimPoseExtensions.get_curve_names(raw_pose)],
                       "logicalParents": parents, "logicalToPhysical": mapping, "virtualBones": virtual_bones,
                       "names": [str(bone) for bone in names],
                       "pose": [atom(unreal.AnimPoseExtensions.get_bone_pose(pose, bone, unreal.AnimPoseSpaces.LOCAL)) for bone in names],
                       "rawPoseWithoutRetarget": [atom(unreal.AnimPoseExtensions.get_bone_pose(raw_pose, bone, unreal.AnimPoseSpaces.LOCAL)) for bone in names],
                       "reference": [atom(unreal.AnimPoseExtensions.get_ref_bone_pose(pose, bone, unreal.AnimPoseSpaces.LOCAL)) for bone in names]})
    payload = {"schemaVersion": 1, "evaluation": "Raw", "retarget": True,
               "extractRootMotion": False, "incorporateRootMotion": False,
               "skeletonSource": skeleton_source, "skeletonText": skeleton_text,
               "transformCases": cases, "assets": assets}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    inputs_output = os.environ.get("ALS_BASE_POSES_INPUTS_OUTPUT")
    if inputs_output:
        inputs_output = Path(inputs_output)
        if not inputs_output.is_absolute() or inputs_output == output:
            raise ValueError("ALS_BASE_POSES_INPUTS_OUTPUT must be absolute and distinct from the oracle output")
        inputs_output.parent.mkdir(parents=True, exist_ok=True)
        inputs_output.write_text(json.dumps(inputs_payload(payload), indent=2) + "\n", encoding="utf-8")
        unreal.log("ALS_BASE_POSES_INPUTS_OK assets=2 pose_samples=0")
    unreal.log("ALS_BASE_POSES_NATIVE_OK assets=2 logical_bones=79 virtual_bones=11 transforms=" + str(len(cases)) + " assets_saved=0")


export()
