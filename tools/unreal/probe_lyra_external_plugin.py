"""Check that the externally packaged Lyra inspector survives commandlet shutdown."""

import json
import os

import unreal


if not hasattr(unreal.AlsSourceAnimationLibrary,
               "read_blend_space_triangulation_reference"):
    raise RuntimeError("The externally packaged BlendSpace inspector was not loaded")
mode = os.environ.get("LYRA_PROBE_MODE", "load")
space = unreal.load_asset(
    "/Game/Characters/Heroes/Mannequin/Animations/AimOffsets/AO_MM_Unarmed_Idle_Ready")
if mode == "triangulation":
    result = json.loads(unreal.AlsSourceAnimationLibrary.read_blend_space_triangulation_reference(
        space, json.dumps({"inputs": [[0, 0]], "filter": False})))
    unreal.log("LYRA_EXTERNAL_TRIANGULATION_OK cases=" + str(len(result["cases"])))
elif mode == "pose":
    result = json.loads(unreal.AlsSourceAnimationLibrary.read_raw_blend_space_pose2d(
        space, 0, 0, 0.5))
    unreal.log("LYRA_EXTERNAL_POSE_OK bones=" + str(len(result["pose"])))
elif mode.startswith("pose") and mode[4:].isdigit():
    count = int(mode[4:])
    for index in range(count):
        result = json.loads(unreal.AlsSourceAnimationLibrary.read_raw_blend_space_pose2d(
            space, -180 + index * 45, 0, 0.5))
        if len(result["pose"]) != 164:
            raise RuntimeError("Incomplete repeated AimOffset pose")
    unreal.log("LYRA_EXTERNAL_POSES_OK count=" + str(count))
elif mode != "load":
    raise RuntimeError("Unknown Lyra probe mode")
unreal.log("LYRA_EXTERNAL_PLUGIN_READY")
