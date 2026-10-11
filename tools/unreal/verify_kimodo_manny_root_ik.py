"""Verify pelvis-to-root and Manny IK baking, then import the exported FBX."""

import hashlib
import json
import math
import os
import re
import tempfile
from pathlib import Path

import unreal


source = unreal.load_asset(os.environ["KIMODO_INPUT_ASSET_PATH"])
fixed = unreal.load_asset(os.environ["KIMODO_ASSET_PATH"])
mesh = unreal.load_asset(os.environ["KIMODO_TARGET_MESH"])
fbx = Path(os.environ["KIMODO_FBX_OUT"])
if not isinstance(source, unreal.AnimSequence) or not isinstance(fixed, unreal.AnimSequence):
    raise RuntimeError("Missing source or root-IK animation")
if not isinstance(mesh, unreal.SkeletalMesh) or not fbx.is_absolute() or not fbx.is_file():
    raise RuntimeError("Missing Manny mesh or exported FBX")
skeleton = mesh.get_editor_property("skeleton")
if any(asset.get_editor_property("skeleton") != skeleton for asset in (source, fixed)):
    raise RuntimeError("Animations do not share the Manny Skeleton")
if not unreal.AnimationLibrary.is_root_motion_enabled(fixed):
    raise RuntimeError("Baked animation does not have Root Motion enabled")
record = json.loads(fbx.with_suffix(".source.json").read_text(encoding="utf-8"))
fbx_hash = hashlib.sha256(fbx.read_bytes()).hexdigest()
if (record["fbxSha256"] != fbx_hash or record["sourceAnimation"] != source.get_path_name()
        or record["animation"] != fixed.get_path_name()
        or record["targetSkeleton"] != skeleton.get_path_name()):
    raise RuntimeError("FBX provenance record does not match the current assets")

frames = unreal.AnimationLibrary.get_num_frames(source)
keys = unreal.AnimationLibrary.get_num_keys(source)
duration = unreal.AnimationLibrary.get_sequence_length(source)
if (frames != unreal.AnimationLibrary.get_num_frames(fixed)
        or keys != unreal.AnimationLibrary.get_num_keys(fixed)
        or abs(duration - unreal.AnimationLibrary.get_sequence_length(fixed)) > 1e-5):
    raise RuntimeError("Root-IK bake changed animation timing")


def difference(a, b):
    pa, pb = a.translation, b.translation
    qa, qb = a.rotation, b.rotation
    position = math.dist((pa.x, pa.y, pa.z), (pb.x, pb.y, pb.z))
    dot = abs(qa.x * qb.x + qa.y * qb.y + qa.z * qb.z + qa.w * qb.w)
    rotation = math.degrees(2 * math.acos(min(1.0, max(-1.0, dot))))
    return position, rotation


def peak(rows):
    return {
        "positionCm": max(rows, key=lambda row: row[0]),
        "rotationDeg": max(rows, key=lambda row: row[1]),
        "count": len(rows),
    }


with tempfile.TemporaryDirectory(prefix="kimodo-manny-vb-") as directory:
    task = unreal.AssetExportTask()
    task.object = skeleton
    task.exporter = unreal.ObjectExporterT3D()
    task.filename = str(Path(directory) / "skeleton.t3d")
    task.automated = True
    task.prompt = False
    if not unreal.Exporter.run_asset_export_task(task):
        raise RuntimeError("Could not inspect the Manny Skeleton")
    raw = Path(task.filename).read_bytes()
    text = raw.decode("utf-16" if raw.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")
virtual_bones = [
    {"source": parent, "target": target, "name": name}
    for parent, target, name in re.findall(
        r'^\s*VirtualBones\(\d+\)=\(SourceBoneName="([^"]+)",TargetBoneName="([^"]+)",VirtualBoneName="([^"]+)"\)\s*$',
        text, re.MULTILINE)
]

track_names = [str(name) for name in unreal.AnimationLibrary.get_animation_track_names(source)]
fixed_tracks = {str(name) for name in unreal.AnimationLibrary.get_animation_track_names(fixed)}
if set(track_names) != fixed_tracks:
    raise RuntimeError("Root-IK bake changed the set of physical bone tracks")
body_bones = [name for name in track_names
              if "pelvis" in [str(parent) for parent in unreal.AnimationLibrary.find_bone_path_to_root(source, name)]]
if len(body_bones) < 140:
    raise RuntimeError("Too few Manny body bones to compare")
options = unreal.AnimPoseEvaluationOptions()
options.evaluation_type = unreal.AnimDataEvalType.RAW
options.should_retarget = False
options.extract_root_motion = False
options.incorporate_root_motion_into_pose = True
pelvis0 = unreal.AnimationLibrary.get_bone_pose_for_frame(source, "pelvis", 0, False).translation
root_errors = []
body_errors = []
ik_errors = []
virtual_errors = []
ik_bindings = (("ik_foot_l", "foot_l"), ("ik_foot_r", "foot_r"),
               ("ik_hand_gun", "hand_r"), ("ik_hand_l", "hand_l"),
               ("ik_hand_r", "hand_r"))
for frame in range(keys):
    root = unreal.AnimationLibrary.get_bone_pose_for_frame(fixed, "root", frame, False).translation
    pelvis = unreal.AnimationLibrary.get_bone_pose_for_frame(source, "pelvis", frame, False).translation
    expected = (pelvis.x - pelvis0.x, pelvis.y - pelvis0.y, 0.0)
    root_errors.append((math.dist((root.x, root.y, root.z), expected), "root", frame))
    local_pelvis = unreal.AnimationLibrary.get_bone_pose_for_frame(fixed, "pelvis", frame, False).translation
    root_errors.append((math.dist((local_pelvis.x, local_pelvis.y), (pelvis0.x, pelvis0.y)),
                        "pelvisPlanarResidual", frame))

    time = frame * duration / frames
    original_pose = unreal.AnimPoseExtensions.get_anim_pose_at_time(source, time, options)
    fixed_pose = unreal.AnimPoseExtensions.get_anim_pose_at_time(fixed, time, options)
    if not unreal.AnimPoseExtensions.is_valid(original_pose) or not unreal.AnimPoseExtensions.is_valid(fixed_pose):
        raise RuntimeError("Could not evaluate a Manny pose at frame " + str(frame))
    original_names = {str(name) for name in unreal.AnimPoseExtensions.get_bone_names(original_pose)}
    fixed_names = {str(name) for name in unreal.AnimPoseExtensions.get_bone_names(fixed_pose)}
    if original_names != fixed_names:
        raise RuntimeError("Root-IK bake changed the logical Skeleton")
    for bone in body_bones:
        a = unreal.AnimPoseExtensions.get_bone_pose(original_pose, bone, unreal.AnimPoseSpaces.WORLD)
        b = unreal.AnimPoseExtensions.get_bone_pose(fixed_pose, bone, unreal.AnimPoseSpaces.WORLD)
        body_errors.append((*difference(a, b), bone, frame))
    for helper, body in ik_bindings:
        a = unreal.AnimPoseExtensions.get_bone_pose(fixed_pose, helper, unreal.AnimPoseSpaces.WORLD)
        b = unreal.AnimPoseExtensions.get_bone_pose(fixed_pose, body, unreal.AnimPoseSpaces.WORLD)
        ik_errors.append((*difference(a, b), helper, frame))
    for bone in virtual_bones:
        if bone["name"] not in fixed_names or bone["name"] in fixed_tracks:
            raise RuntimeError("Virtual Bone is missing or incorrectly stored as an explicit track")
        a = unreal.AnimPoseExtensions.get_bone_pose(fixed_pose, bone["name"], unreal.AnimPoseSpaces.WORLD)
        b = unreal.AnimPoseExtensions.get_bone_pose(fixed_pose, bone["target"], unreal.AnimPoseSpaces.WORLD)
        virtual_errors.append((*difference(a, b), bone["name"], frame))

root_peak = max(root_errors, key=lambda row: row[0])
body_peak = peak(body_errors)
ik_peak = peak(ik_errors)
virtual_peak = peak(virtual_errors) if virtual_errors else None
if root_peak[0] > 0.001 or body_peak["positionCm"][0] > 0.001 or body_peak["rotationDeg"][1] > 0.01:
    raise RuntimeError("Planar root transfer changed body motion: " + str((root_peak, body_peak)))
if ik_peak["positionCm"][0] > 0.001 or ik_peak["rotationDeg"][1] > 0.01:
    raise RuntimeError("Manny IK helper did not follow its body target: " + str(ik_peak))
if virtual_peak and (virtual_peak["positionCm"][0] > 0.001 or virtual_peak["rotationDeg"][1] > 0.01):
    raise RuntimeError("Target Skeleton Virtual Bone did not follow its target: " + str(virtual_peak))

ui = unreal.FbxImportUI()
ui.set_editor_property("mesh_type_to_import", unreal.FBXImportType.FBXIT_ANIMATION)
ui.set_editor_property("import_as_skeletal", True)
ui.set_editor_property("import_mesh", False)
ui.set_editor_property("import_animations", True)
ui.set_editor_property("skeleton", skeleton)
task = unreal.AssetImportTask()
task.set_editor_property("filename", str(fbx))
task.set_editor_property("destination_path", "/Game/KimodoMocara/Roundtrip")
task.set_editor_property("destination_name", "AS_KimodoRootIK_FbxReadback")
task.set_editor_property("options", ui)
task.set_editor_property("automated", True)
task.set_editor_property("save", False)
unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([task])
imported_paths = list(task.get_editor_property("imported_object_paths"))
if len(imported_paths) != 1:
    raise RuntimeError("FBX import did not produce one animation: " + str(imported_paths))
readback = unreal.load_asset(imported_paths[0])
if not isinstance(readback, unreal.AnimSequence) or readback.get_editor_property("skeleton") != skeleton:
    raise RuntimeError("FBX readback is not a Manny Animation Sequence")
if (unreal.AnimationLibrary.get_num_frames(readback) != frames
        or abs(unreal.AnimationLibrary.get_sequence_length(readback) - duration) > 1e-5):
    raise RuntimeError("FBX readback changed timing")
fbx_errors = []
for frame in range(keys):
    for bone in track_names:
        a = unreal.AnimationLibrary.get_bone_pose_for_frame(fixed, bone, frame, False)
        b = unreal.AnimationLibrary.get_bone_pose_for_frame(readback, bone, frame, False)
        fbx_errors.append((*difference(a, b), bone, frame))
fbx_peak = peak(fbx_errors)
if fbx_peak["positionCm"][0] > 0.01 or fbx_peak["rotationDeg"][1] > 0.1:
    raise RuntimeError("FBX readback changed a Manny bone pose: " + str(fbx_peak))

root_last = unreal.AnimationLibrary.get_bone_pose_for_frame(fixed, "root", frames, False).translation
result = {
    "schemaVersion": 1,
    "fbxSha256": fbx_hash,
    "animation": fixed.get_path_name(),
    "targetSkeleton": skeleton.get_path_name(),
    "rootMotionEnabled": True,
    "rootEndCm": [root_last.x, root_last.y, root_last.z],
    "rootTravelCm": math.dist((root_last.x, root_last.y, root_last.z), (0, 0, 0)),
    "sampledKeys": keys,
    "bodyPose": body_peak,
    "ikHelpers": ik_peak,
    "virtualBoneDefinitions": virtual_bones,
    "virtualBonePose": virtual_peak,
    "fbxReadbackTracks": len(unreal.AnimationLibrary.get_animation_track_names(readback)),
    "fbxReadback": fbx_peak,
    "rootTransferMaxErrorCm": root_peak,
}
fbx.with_suffix(".verification.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
unreal.log("KIMODO_MANNY_ROOT_IK_VERIFY_OK " + json.dumps(result))
