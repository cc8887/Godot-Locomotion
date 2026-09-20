"""Read native sequence/skeleton/foot contracts without saving Unreal assets.

ALS_FOOT_SOURCE_AUDIT supplies {output, assets: [object paths]}. Samples include
raw bone poses and the actual additive-aware animation entry; neither is claimed
to represent a complete AnimBP. Component transforms use UE composition.
"""
import json
import os
import time
from pathlib import Path

import unreal


def transform(atom):
    result = unreal.Transform()
    result.translation = unreal.Vector(*atom["position"])
    result.rotation = unreal.Quat(*atom["rotation"])
    result.scale3d = unreal.Vector(*atom["scale"])
    return result


def atom(value):
    p, q, s = value.translation, value.rotation, value.scale3d
    return {"position": [p.x, p.y, p.z], "rotation": [q.x, q.y, q.z, q.w],
            "scale": [s.x, s.y, s.z]}


def components(pose, skeleton):
    names = skeleton["logicalBoneNames"]
    assert pose["names"] == names
    # Unreal FName equality ignores case; V4 uses Foot_L and Refactored foot_l.
    # Keep original spellings in the full pose, normalize only observation keys.
    indices = {name.casefold(): index for index, name in enumerate(names)}
    assert len(indices) == len(names)
    result = []
    for index, local in enumerate(pose["pose"]):
        parent = skeleton["logicalParents"][index]
        assert -1 <= parent < index
        value = transform(local)
        if parent >= 0:
            value = unreal.MathLibrary.compose_transforms(value, result[parent])
        result.append(value)
    observed = ("root", "pelvis", "thigh_l", "thigh_r", "calf_l", "calf_r",
                "foot_l", "foot_r", "ball_l", "ball_r", "ik_foot_root",
                "ik_foot_l", "ik_foot_r", "VB foot_target_l", "VB foot_target_r")
    return {name: atom(result[indices[name.casefold()]]) for name in observed if name.casefold() in indices}


request = json.loads(os.environ["ALS_FOOT_SOURCE_AUDIT"])
output = Path(request["output"])
if not output.is_absolute() or output.exists() or not request["assets"]:
    raise ValueError("A fresh absolute output and explicit source assets are required")
rows = []
for source in request["assets"]:
    asset = unreal.load_asset(source)
    if not isinstance(asset, unreal.AnimSequence):
        raise ValueError("Missing native sequence: " + source)
    metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(asset))
    skeleton = json.loads(unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(
        asset.get_editor_property("skeleton")))
    samples = []
    for seconds in sorted({0.0, metadata["sequencePlayLength"] * .5, metadata["sequencePlayLength"]}):
        raw = json.loads(unreal.AlsSourceAnimationLibrary.read_raw_bone_pose(asset, seconds, True, False, False))
        animation = json.loads(unreal.AlsSourceAnimationLibrary.read_raw_animation_pose(asset, seconds, True, False, False))
        samples.append({"time": seconds, "raw": raw, "animation": animation,
                        "rawFootComponents": components(raw, skeleton)})
    rows.append({"source": asset.get_path_name(), "metadata": metadata,
                 "skeleton": skeleton, "samples": samples})
output.write_text(json.dumps({"schemaVersion": 1, "assetsSaved": 0,
    "scope": "Native source metadata and RAW pose evaluation; not a complete AnimBP or contact acceptance",
    "assets": rows}, indent=2, allow_nan=False), encoding="utf-8")
unreal.log(f"ALS_FOOT_SOURCE_AUDIT_OK assets={len(rows)} assets_saved=0 output={output}")

if os.environ.get("ALS_FOOT_SOURCE_AUDIT_QUIT") == "1":
    _deadline = time.monotonic() + 10
    def _finish(_delta):
        if time.monotonic() >= _deadline:
            unreal.unregister_slate_post_tick_callback(_handle)
            unreal.SystemLibrary.quit_editor()
    _handle = unreal.register_slate_post_tick_callback(_finish)
