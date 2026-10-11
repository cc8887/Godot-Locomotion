"""Compare Kimodo animation samples with Manny as the target mesh."""

import json

import unreal


mesh = unreal.load_asset("/Game/Characters/UE5_Mannequins/Meshes/SKM_Manny")
paths = (
    "/Game/KimodoMocara/AS_KimodoWalkStop_Manny_RootIK_Final",
    "/Game/KimodoMocara/VisualReadback/AS_KimodoWalkStop_Manny_RootIK_FBX_Visual",
    "/Game/Characters/UEFN_Mannequin/Animations/Walk/M_Relaxed_Walk_Start_F_Lfoot",
)
results = {}
for path in paths:
    animation = unreal.load_asset(path)
    if not isinstance(animation, unreal.AnimSequence):
        raise RuntimeError("Missing sequence: " + path)
    options = unreal.AnimPoseEvaluationOptions()
    options.optional_skeletal_mesh = mesh
    options.evaluation_type = unreal.AnimDataEvalType.COMPRESSED
    options.extract_root_motion = False
    options.incorporate_root_motion_into_pose = True
    samples = {}
    for retarget in (False, True):
        options.should_retarget = retarget
        frames = {}
        for frame in (0, 30, 60):
            time = min(frame / 30.0, unreal.AnimationLibrary.get_sequence_length(animation))
            pose = unreal.AnimPoseExtensions.get_anim_pose_at_time(animation, time, options)
            frames[str(frame)] = {
                bone: [round(getattr(unreal.AnimPoseExtensions.get_bone_pose(
                    pose, bone, unreal.AnimPoseSpaces.WORLD).translation, axis), 4)
                    for axis in ("x", "y", "z")]
                for bone in ("root", "pelvis", "foot_l", "foot_r", "hand_l", "hand_r")
            }
        samples[str(retarget)] = frames
    results[path] = {
        "skeleton": animation.get_editor_property("skeleton").get_path_name(),
        "meshSkeleton": mesh.get_editor_property("skeleton").get_path_name(),
        "samples": samples,
    }
unreal.log("KIMODO_MESH_POSE " + json.dumps(results))
