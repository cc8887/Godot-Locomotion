"""Bake planar root motion and Manny IK helper tracks into a copied animation."""

import hashlib
import json
import math
import os
from pathlib import Path

import unreal


source_path = os.environ["KIMODO_INPUT_ASSET_PATH"]
target_path = os.environ["KIMODO_TARGET_MESH"]
asset_path = os.environ["KIMODO_ASSET_PATH"]
output = Path(os.environ["KIMODO_FBX_OUT"])
if not source_path.startswith("/Game/") or not asset_path.startswith("/Game/"):
    raise ValueError("Animation assets must use /Game package paths")
if not output.is_absolute() or output.exists() or output.with_suffix(".source.json").exists():
    raise ValueError("KIMODO_FBX_OUT and its source record must be new absolute paths")
if unreal.EditorAssetLibrary.does_asset_exist(asset_path):
    raise ValueError("KIMODO_ASSET_PATH must be a new asset")

source = unreal.load_asset(source_path)
target = unreal.load_asset(target_path)
if not isinstance(source, unreal.AnimSequence) or not isinstance(target, unreal.SkeletalMesh):
    raise RuntimeError("Missing source animation or Manny mesh")
skeleton = target.get_editor_property("skeleton")
if source.get_editor_property("skeleton") != skeleton:
    raise RuntimeError("Source animation is not bound to the target Skeleton")
if unreal.AnimationLibrary.is_root_motion_enabled(source):
    raise RuntimeError("Source animation already has Root Motion enabled")

bindings = {
    "pelvis": ["pelvis", "root"],
    "ik_foot_root": ["ik_foot_root", "root"],
    "ik_foot_l": ["ik_foot_l", "ik_foot_root", "root"],
    "ik_foot_r": ["ik_foot_r", "ik_foot_root", "root"],
    "ik_hand_root": ["ik_hand_root", "root"],
    "ik_hand_gun": ["ik_hand_gun", "ik_hand_root", "root"],
    "ik_hand_l": ["ik_hand_l", "ik_hand_gun", "ik_hand_root", "root"],
    "ik_hand_r": ["ik_hand_r", "ik_hand_gun", "ik_hand_root", "root"],
}
tracks = {str(name) for name in unreal.AnimationLibrary.get_animation_track_names(source)}
for bone, expected_path in bindings.items():
    if bone not in tracks:
        raise RuntimeError("Source animation lacks Manny track " + bone)
    path = [str(name) for name in unreal.AnimationLibrary.find_bone_path_to_root(source, bone)]
    if path != expected_path:
        raise RuntimeError("Unexpected Manny hierarchy for " + bone + ": " + str(path))
for bone in ("root", "foot_l", "foot_r", "hand_l", "hand_r"):
    if bone not in tracks:
        raise RuntimeError("Source animation lacks body track " + bone)

frames = unreal.AnimationLibrary.get_num_frames(source)
keys = unreal.AnimationLibrary.get_num_keys(source)
duration = unreal.AnimationLibrary.get_sequence_length(source)
if frames < 1 or keys != frames + 1 or duration <= 0:
    raise RuntimeError("Source animation has an invalid frame layout")


def is_identity(transform):
    p, q, s = transform.translation, transform.rotation, transform.scale3d
    return (math.dist((p.x, p.y, p.z), (0, 0, 0)) < 1e-4 and
            math.dist((q.x, q.y, q.z, abs(q.w)), (0, 0, 0, 1)) < 1e-4 and
            math.dist((s.x, s.y, s.z), (1, 1, 1)) < 1e-4)


root_first = unreal.AnimationLibrary.get_bone_pose_for_frame(source, "root", 0, False)
pelvis_first = unreal.AnimationLibrary.get_bone_pose_for_frame(source, "pelvis", 0, False)
if not is_identity(root_first):
    raise RuntimeError("Planar bake requires an identity root at frame zero")

output_tracks = {name: [] for name in (
    "root", "pelvis", "ik_foot_l", "ik_foot_r", "ik_hand_gun", "ik_hand_l", "ik_hand_r")}
options = unreal.AnimPoseEvaluationOptions()
options.evaluation_type = unreal.AnimDataEvalType.RAW
options.should_retarget = False
options.extract_root_motion = False
options.incorporate_root_motion_into_pose = True

for frame in range(keys):
    root = unreal.AnimationLibrary.get_bone_pose_for_frame(source, "root", frame, False)
    pelvis = unreal.AnimationLibrary.get_bone_pose_for_frame(source, "pelvis", frame, False)
    if not is_identity(root):
        raise RuntimeError("Planar bake requires an identity root at frame " + str(frame))
    for helper in ("ik_foot_root", "ik_hand_root"):
        local = unreal.AnimationLibrary.get_bone_pose_for_frame(source, helper, frame, False)
        if not is_identity(local):
            raise RuntimeError(helper + " is not an identity child of root at frame " + str(frame))

    delta = unreal.Vector(pelvis.translation.x - pelvis_first.translation.x,
                          pelvis.translation.y - pelvis_first.translation.y, 0)
    new_root = unreal.Transform()
    new_root.translation = delta
    new_root.rotation = root.rotation
    new_root.scale3d = root.scale3d
    new_pelvis = unreal.Transform()
    new_pelvis.translation = unreal.Vector(pelvis.translation.x - delta.x,
                                           pelvis.translation.y - delta.y,
                                           pelvis.translation.z)
    new_pelvis.rotation = pelvis.rotation
    new_pelvis.scale3d = pelvis.scale3d

    pose = unreal.AnimPoseExtensions.get_anim_pose_at_time(source, frame * duration / frames, options)
    if not unreal.AnimPoseExtensions.is_valid(pose):
        raise RuntimeError("Could not evaluate body pose at frame " + str(frame))
    body = {name: unreal.AnimPoseExtensions.get_bone_pose(pose, name, unreal.AnimPoseSpaces.WORLD)
            for name in ("foot_l", "foot_r", "hand_l", "hand_r")}
    relative = unreal.MathLibrary.make_relative_transform
    output_tracks["root"].append(new_root)
    output_tracks["pelvis"].append(new_pelvis)
    output_tracks["ik_foot_l"].append(relative(body["foot_l"], new_root))
    output_tracks["ik_foot_r"].append(relative(body["foot_r"], new_root))
    output_tracks["ik_hand_gun"].append(relative(body["hand_r"], new_root))
    output_tracks["ik_hand_l"].append(relative(body["hand_l"], body["hand_r"]))
    output_tracks["ik_hand_r"].append(relative(body["hand_r"], body["hand_r"]))

directory = asset_path.rsplit("/", 1)[0]
unreal.EditorAssetLibrary.make_directory(directory)
animation = unreal.EditorAssetLibrary.duplicate_asset(source_path, asset_path)
if not isinstance(animation, unreal.AnimSequence):
    raise RuntimeError("Could not duplicate the Manny animation")

controller = animation.controller
controller.open_bracket("Bake Kimodo planar root and Manny IK", False)
try:
    for bone, transforms in output_tracks.items():
        positions = [item.translation for item in transforms]
        rotations = [item.rotation for item in transforms]
        scales = [item.scale3d for item in transforms]
        if not controller.set_bone_track_keys(bone, positions, rotations, scales, False):
            raise RuntimeError("Could not write Manny track " + bone)
finally:
    controller.close_bracket(False)

unreal.AnimationLibrary.set_root_motion_enabled(animation, True)
if not unreal.EditorAssetLibrary.save_loaded_asset(animation, only_if_is_dirty=False):
    raise RuntimeError("Could not save the root-motion Manny animation")

export_options = unreal.FbxExportOption()
export_options.set_editor_property("fbx_export_compatibility", unreal.FbxExportCompatibility.FBX_2020)
export_options.set_editor_property("ascii", True)
export_options.set_editor_property("force_front_x_axis", False)
export_options.set_editor_property("map_skeletal_motion_to_root", False)
task = unreal.AssetExportTask()
task.set_editor_property("object", animation)
task.set_editor_property("exporter", unreal.AnimSequenceExporterFBX())
task.set_editor_property("filename", str(output))
task.set_editor_property("options", export_options)
task.set_editor_property("automated", True)
task.set_editor_property("prompt", False)
task.set_editor_property("replace_identical", False)
task.set_editor_property("use_file_archive", True)
output.parent.mkdir(parents=True, exist_ok=True)
if not unreal.Exporter.run_asset_export_task(task) or not output.is_file():
    raise RuntimeError("FBX export failed: " + str(task.get_editor_property("errors")))

manifest = {
    "schemaVersion": 2,
    "sourceAnimation": source.get_path_name(),
    "targetMesh": target.get_path_name(),
    "targetSkeleton": skeleton.get_path_name(),
    "animation": animation.get_path_name(),
    "rootPolicy": "planar pelvis displacement from first frame; vertical pelvis motion retained; no yaw extraction",
    "ikBindings": {"ik_foot_l": "foot_l", "ik_foot_r": "foot_r",
                   "ik_hand_gun": "hand_r", "ik_hand_l": "hand_l", "ik_hand_r": "hand_r"},
    "virtualBonePolicy": "preserve target Skeleton definitions; do not author virtual tracks",
    "frames": frames,
    "sampledKeys": keys,
    "fbx": str(output),
    "fbxSha256": hashlib.sha256(output.read_bytes()).hexdigest(),
}
output.with_suffix(".source.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
unreal.log("KIMODO_MANNY_ROOT_IK_EXPORT_OK " + json.dumps(manifest))

# The commandlet destroys editor objects during shutdown; release Python-held
# wrappers for the controller and sampled pose arrays before that teardown.
for _name in (
    "source", "target", "skeleton", "animation", "controller", "task", "export_options",
    "output_tracks", "root_first", "pelvis_first", "root", "pelvis", "local",
    "new_root", "new_pelvis", "pose", "body", "positions", "rotations",
    "scales", "transforms", "relative", "delta",
):
    globals().pop(_name, None)
import gc as _gc
_gc.collect()
