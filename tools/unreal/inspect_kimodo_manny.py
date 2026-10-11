"""Read the Manny skeleton and a Kimodo-retargeted sequence without saving assets."""

import json
import os
import re
import tempfile
from pathlib import Path

import unreal


target = unreal.load_asset(os.environ["KIMODO_TARGET_MESH"])
sequence = unreal.load_asset(os.environ["KIMODO_ASSET_PATH"])
if not isinstance(target, unreal.SkeletalMesh) or not isinstance(sequence, unreal.AnimSequence):
    raise RuntimeError("Missing Manny mesh or Kimodo animation")
skeleton = target.get_editor_property("skeleton")
if sequence.get_editor_property("skeleton") != skeleton:
    raise RuntimeError("Kimodo animation uses another skeleton")


def transform_record(value):
    return {
        "translation": [value.translation.x, value.translation.y, value.translation.z],
        "rotation": [value.rotation.x, value.rotation.y, value.rotation.z, value.rotation.w],
        "scale": [value.scale3d.x, value.scale3d.y, value.scale3d.z],
    }


with tempfile.TemporaryDirectory(prefix="kimodo-manny-skeleton-") as directory:
    task = unreal.AssetExportTask()
    task.object = skeleton
    task.exporter = unreal.ObjectExporterT3D()
    task.filename = str(Path(directory) / "skeleton.t3d")
    task.automated = True
    task.prompt = False
    if not unreal.Exporter.run_asset_export_task(task):
        raise RuntimeError("Could not export the Manny Skeleton as text")
    raw_text = Path(task.filename).read_bytes()
    skeleton_text = raw_text.decode("utf-16" if raw_text.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")

virtual_bones = [
    {"source": source, "target": target, "name": name}
    for source, target, name in re.findall(
        r'^\s*VirtualBones\(\d+\)=\(SourceBoneName="([^"]+)",TargetBoneName="([^"]+)",VirtualBoneName="([^"]+)"\)\s*$',
        skeleton_text, re.MULTILINE)
]

frames = unreal.AnimationLibrary.get_num_frames(sequence)
samples = []
tracked_bones = (
    "root", "pelvis", "foot_l", "foot_r", "ball_l", "ball_r",
    "ik_foot_root", "ik_foot_l", "ik_foot_r", "hand_l", "hand_r",
    "ik_hand_root", "ik_hand_gun", "ik_hand_l", "ik_hand_r",
)
options = unreal.AnimPoseEvaluationOptions()
options.evaluation_type = unreal.AnimDataEvalType.RAW
options.should_retarget = True
options.extract_root_motion = False
options.incorporate_root_motion_into_pose = False
for frame in (0, frames // 4, frames // 2, 3 * frames // 4, frames):
    pose = unreal.AnimPoseExtensions.get_anim_pose_at_time(
        sequence, frame * unreal.AnimationLibrary.get_sequence_length(sequence) / frames, options)
    if not unreal.AnimPoseExtensions.is_valid(pose):
        raise RuntimeError("Could not evaluate Manny pose at frame " + str(frame))
    samples.append({
        "frame": frame,
        "root": transform_record(unreal.AnimationLibrary.get_bone_pose_for_frame(sequence, "root", frame, False)),
        "pelvis": transform_record(unreal.AnimationLibrary.get_bone_pose_for_frame(sequence, "pelvis", frame, False)),
        "virtualPose": {
            bone["name"]: transform_record(unreal.AnimPoseExtensions.get_bone_pose(
                pose, bone["name"], unreal.AnimPoseSpaces.WORLD))
            for bone in virtual_bones
        },
        "componentPose": {
            bone: transform_record(unreal.AnimPoseExtensions.get_bone_pose(
                pose, bone, unreal.AnimPoseSpaces.WORLD))
            for bone in tracked_bones
        },
    })

result = {
    "skeleton": skeleton.get_path_name(),
    "sequence": sequence.get_path_name(),
    "virtualBones": virtual_bones,
    "skeletonTextMentionsVirtualBones": "VirtualBones" in skeleton_text,
    "poseBoneNames": [str(name) for name in unreal.AnimPoseExtensions.get_bone_names(pose)],
    "bonePaths": {bone: [str(name) for name in unreal.AnimationLibrary.find_bone_path_to_root(sequence, bone)]
                  for bone in (str(name) for name in unreal.AnimationLibrary.get_animation_track_names(sequence))},
    "boneTracks": [str(name) for name in unreal.AnimationLibrary.get_animation_track_names(sequence)],
    "frames": frames,
    "samples": samples,
    "animationMethods": [name for name in dir(sequence) if "controller" in name or "data_model" in name],
    "controllerMethods": [name for name in dir(sequence.controller)
                          if "bone_track" in name or "bracket" in name],
    "libraryMethods": [name for name in dir(unreal.AnimationLibrary) if "bone_track" in name or "root_motion" in name],
}
output = Path(os.environ["KIMODO_INSPECT_OUT"])
if not output.is_absolute():
    raise ValueError("KIMODO_INSPECT_OUT must be absolute")
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(result, indent=2), encoding="utf-8")
unreal.log("KIMODO_MANNY_INSPECT_OK virtual_bones=" + str(len(virtual_bones)) +
           " bone_tracks=" + str(len(result["boneTracks"])))
