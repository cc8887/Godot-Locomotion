"""Read back a Mocara-exported FBX against its Manny Animation Sequence."""

import hashlib
import json
import math
import os
from pathlib import Path

import unreal


fbx = Path(os.environ["KIMODO_FBX_OUT"])
asset_path = os.environ["KIMODO_ASSET_PATH"]
target_path = os.environ["KIMODO_TARGET_MESH"]
if not fbx.is_absolute() or not fbx.is_file():
    raise ValueError("KIMODO_FBX_OUT must name an existing absolute file")
source_record = json.loads(fbx.with_suffix(".source.json").read_text(encoding="utf-8"))
fbx_sha256 = hashlib.sha256(fbx.read_bytes()).hexdigest()
if source_record["fbxSha256"] != fbx_sha256:
    raise RuntimeError("FBX does not match its export record")
if source_record["animation"].rsplit(".", 1)[0] != asset_path or source_record["targetMesh"] != target_path:
    raise RuntimeError("Export record does not match the requested assets")

target_mesh = unreal.load_asset(target_path)
source = unreal.load_asset(asset_path)
if not isinstance(target_mesh, unreal.SkeletalMesh) or not isinstance(source, unreal.AnimSequence):
    raise RuntimeError("Missing Manny mesh or retargeted animation")
skeleton = target_mesh.get_editor_property("skeleton")
if source.get_editor_property("skeleton") != skeleton:
    raise RuntimeError("Saved animation is not bound to the target Skeleton")


def summarize(animation):
    tracks = {str(name) for name in unreal.AnimationLibrary.get_animation_track_names(animation)}
    required = ("root", "pelvis", "upperarm_l", "upperarm_r", "foot_l", "foot_r")
    missing = [bone for bone in required if bone not in tracks]
    if missing:
        raise RuntimeError("Missing Manny tracks " + str(missing) +
                           "; readable tracks=" + str(len(tracks)) +
                           "; first names=" + str(sorted(tracks)[:12]))

    frames = unreal.AnimationLibrary.get_num_frames(animation)
    keys = unreal.AnimationLibrary.get_num_keys(animation)
    duration = unreal.AnimationLibrary.get_sequence_length(animation)
    if frames < 119 or keys < 120 or duration <= 0:
        raise RuntimeError("Animation has too few frames or zero duration")

    def travel(bone):
        first = unreal.AnimationLibrary.get_bone_pose_for_frame(animation, bone, 0, False).translation
        last = unreal.AnimationLibrary.get_bone_pose_for_frame(animation, bone, frames, False).translation
        return math.dist((first.x, first.y, first.z), (last.x, last.y, last.z))

    return {
        "asset": animation.get_path_name(),
        "skeleton": animation.get_editor_property("skeleton").get_path_name(),
        "boneTracks": len(tracks),
        "sampledKeyCount": keys,
        "frameRate": frames / duration,
        "playLengthSeconds": duration,
        "rootTravelCm": travel("root"),
        "pelvisTravelCm": travel("pelvis"),
    }


original = summarize(source)
ui = unreal.FbxImportUI()
ui.set_editor_property("mesh_type_to_import", unreal.FBXImportType.FBXIT_ANIMATION)
ui.set_editor_property("import_as_skeletal", True)
ui.set_editor_property("import_mesh", False)
ui.set_editor_property("import_animations", True)
ui.set_editor_property("skeleton", skeleton)
task = unreal.AssetImportTask()
task.set_editor_property("filename", str(fbx))
task.set_editor_property("destination_path", "/Game/KimodoMocara/Roundtrip")
task.set_editor_property("destination_name", "AS_WalkStop42_Manny_FbxReadback")
task.set_editor_property("options", ui)
task.set_editor_property("automated", True)
task.set_editor_property("save", False)
unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([task])
imported_paths = list(task.get_editor_property("imported_object_paths"))
if len(imported_paths) != 1:
    raise RuntimeError("FBX readback produced " + str(len(imported_paths)) + " assets")
readback = unreal.load_asset(imported_paths[0])
if not isinstance(readback, unreal.AnimSequence):
    raise RuntimeError("FBX readback is not an AnimSequence")
if readback.get_editor_property("skeleton") != skeleton:
    raise RuntimeError("FBX readback is not bound to the Manny Skeleton")
imported = summarize(readback)
unreal.log("KIMODO_MOCARA_FBX_READBACK_STATS " +
           json.dumps({"original": original, "fbxReadback": imported}))
if imported["boneTracks"] < 80 or imported["sampledKeyCount"] < 120:
    raise RuntimeError("FBX readback lost tracks or frames: " + str(imported))
if abs(imported["playLengthSeconds"] - original["playLengthSeconds"]) > 1 / 30 + 0.001:
    raise RuntimeError("FBX readback duration differs by more than one frame")
if abs(imported["pelvisTravelCm"] - original["pelvisTravelCm"]) > 1.0:
    raise RuntimeError("FBX readback lost pelvis translation")

frame_count = min(unreal.AnimationLibrary.get_num_frames(source),
                  unreal.AnimationLibrary.get_num_frames(readback))
bones = [str(name) for name in unreal.AnimationLibrary.get_animation_track_names(source)]
pose_errors = []
for frame in range(frame_count + 1):
    for bone in bones:
        a = unreal.AnimationLibrary.get_bone_pose_for_frame(source, bone, frame, False)
        b = unreal.AnimationLibrary.get_bone_pose_for_frame(readback, bone, frame, False)
        pa, pb = a.translation, b.translation
        qa, qb = a.rotation, b.rotation
        position_cm = math.dist((pa.x, pa.y, pa.z), (pb.x, pb.y, pb.z))
        dot = abs(qa.x * qb.x + qa.y * qb.y + qa.z * qb.z + qa.w * qb.w)
        rotation_deg = math.degrees(2 * math.acos(min(1.0, max(-1.0, dot))))
        pose_errors.append((position_cm, rotation_deg, bone, frame))
max_position = max(pose_errors, key=lambda item: item[0])
max_rotation = max(pose_errors, key=lambda item: item[1])
unreal.log("KIMODO_MOCARA_FBX_POSE_ERRORS " +
           json.dumps({"maxPosition": max_position, "maxRotation": max_rotation,
                       "samples": len(pose_errors)}))
if max_position[0] > 0.01 or max_rotation[1] > 0.1:
    raise RuntimeError("FBX readback changed a sampled Manny bone pose")

result = {
    "schemaVersion": 1,
    "fbxSha256": fbx_sha256,
    "original": original,
    "fbxReadback": imported,
    "poseSamples": len(pose_errors),
    "maxPositionError": max_position,
    "maxRotationError": max_rotation,
}
fbx.with_suffix(".verification.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
unreal.log("KIMODO_MOCARA_FBX_READBACK_OK " + json.dumps(result))
