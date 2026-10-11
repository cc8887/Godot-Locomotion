"""Compare raw and compressed UE poses for the native and FBX Manny clips."""

import json
import math

import unreal


names = (
    "/Game/KimodoMocara/AS_KimodoWalkStop_Manny_RootIK_Final",
    "/Game/KimodoMocara/VisualReadback/AS_KimodoWalkStop_Manny_RootIK_FBX_Visual",
)
bone_names = ("root", "pelvis", "foot_l", "foot_r", "hand_l", "hand_r")
result = {"evaluationTypes": [name for name in dir(unreal.AnimDataEvalType) if name.isupper()],
          "animations": {}}
for path in names:
    asset = unreal.load_asset(path)
    if not isinstance(asset, unreal.AnimSequence):
        raise RuntimeError("Missing animation: " + path)
    samples = {}
    for eval_type in (unreal.AnimDataEvalType.RAW, unreal.AnimDataEvalType.COMPRESSED):
        options = unreal.AnimPoseEvaluationOptions()
        options.evaluation_type = eval_type
        options.should_retarget = False
        options.extract_root_motion = False
        options.incorporate_root_motion_into_pose = True
        frames = {}
        for frame in (0, 30, 60, 90, 120):
            pose = unreal.AnimPoseExtensions.get_anim_pose_at_time(asset, frame / 30.0, options)
            if not unreal.AnimPoseExtensions.is_valid(pose):
                raise RuntimeError("Could not evaluate " + path + " frame " + str(frame))
            frames[str(frame)] = {
                bone: [round(getattr(
                    unreal.AnimPoseExtensions.get_bone_pose(pose, bone, unreal.AnimPoseSpaces.WORLD)
                    .translation, axis), 4) for axis in ("x", "y", "z")]
                for bone in bone_names
            }
        samples[str(eval_type)] = frames
    a = samples[str(unreal.AnimDataEvalType.COMPRESSED)]["0"]["foot_l"]
    b = samples[str(unreal.AnimDataEvalType.COMPRESSED)]["60"]["foot_l"]
    result["animations"][path] = {"samples": samples, "footTravelCm0to60": math.dist(a, b)}
unreal.log("KIMODO_RUNTIME_POSE " + json.dumps(result))
