"""Read-only RAW UBlendSpace.GetAnimationPose oracle for the authored ALS Aim asset."""
import hashlib
import json
import math
import os
import struct
from pathlib import Path

import unreal


def f32(value):
    return struct.unpack("f", struct.pack("f", value))[0]


def export():
    root = Path(os.environ["ALS_AIM_POSE_REPOSITORY"])
    output = Path(os.environ["ALS_AIM_BLEND_POSE_OUTPUT"])
    if not root.is_absolute() or not output.is_absolute():
        raise ValueError("Aim oracle paths must be absolute")
    sampling_bytes = (root / "assets/config/v4_aim_sampling.json").read_bytes()
    index_bytes = (root / "assets/config/v4_aim_source_inputs.json").read_bytes()
    sampling = json.loads(sampling_bytes)
    index = json.loads(index_bytes)
    # Check the native source keys/policy before using the native combined sampler.
    for entry in sorted(index["assets"], key=lambda item: item["source"]):
        raw = (root / "assets/config" / entry["file"]).read_bytes()
        if hashlib.sha256(raw).hexdigest().lower() != entry["sha256"].lower():
            raise RuntimeError("Raw source digest changed")
        expected = json.loads(raw)
        asset = unreal.load_asset(entry["source"])
        actual_keys = json.loads(unreal.AlsSourcePoseKeysLibrary.read_source_pose_keys(asset))
        actual_policy = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(asset))
        if actual_policy != expected["evaluation"] or actual_keys != {k: v for k, v in expected.items() if k != "evaluation"}:
            raise RuntimeError("Native source changed: " + entry["source"])
    blend = unreal.load_asset(sampling["objectPath"])
    if not isinstance(blend, unreal.BlendSpace1D):
        raise RuntimeError("Missing actual one-dimensional Aim asset")
    pitches = [-120, -90, -89.9999, -67.5, -45.0001, -45, -44.9999, -22.5,
               -.001, -.0001, 0, .0001, .001, 22.5, 44.9999, 45, 45.0001, 67.5, 89.9999, 90, 120]
    times = [-.1, 0, 1 / 120, .046875, .21875, .5, .78125, 1 - 1 / 120, 1, 1.1]
    rows = []

    def sample(pitch, time, hz=0, frame=0):
        row = json.loads(unreal.AlsSourceAnimationLibrary.read_raw_blend_space_pose(blend, f32(pitch), f32(time)))
        if row["source"] != sampling["objectPath"] or len(row["names"]) != 79 or len(row["pose"]) != 79:
            raise RuntimeError("Incomplete native Aim blend pose")
        row["hz"] = hz
        row["frame"] = frame
        rows.append(row)

    for pitch in pitches:
        for time in times:
            sample(pitch, time)
    for hz in (30, 60, 120):
        for frame in range(hz * 2):
            seconds = frame / hz
            sample(105 * math.sin(seconds * 9), .5 + .6 * math.sin(seconds * 7), hz, frame)
    result = {"schemaVersion": 1, "source": "UBlendSpace.GetAnimationPose(RAW, retargeted, asset root lock)",
              "samplingSha256": hashlib.sha256(sampling_bytes).hexdigest(),
              "sourceIndexSha256": hashlib.sha256(index_bytes).hexdigest(), "rows": rows}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8")
    unreal.log("ALS_AIM_BLEND_POSE_NATIVE_OK static=210 frames=420 poses=" + str(len(rows)) + " assets_saved=0")


export()
