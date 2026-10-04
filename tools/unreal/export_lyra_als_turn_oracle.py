"""Capture continuous native ALS turn-sequence poses and curve values."""

import hashlib
import json
import math
import os
from pathlib import Path

import unreal


root = Path(os.environ["LYRA_OUTPUT_ROOT"])
mode = os.environ.get("LYRA_TURN_ORACLE_SOURCE", "raw")
if not root.is_absolute() or not root.is_dir() or mode not in ("raw", "compressed"):
    raise ValueError("An existing absolute Lyra output directory is required")

catalog_path = root / "unarmed_remaining_catalog.json"
timing_path = root / "unarmed_remaining_timing.json"
catalog_bytes = catalog_path.read_bytes()
timing_bytes = timing_path.read_bytes()
catalog = json.loads(catalog_bytes)
timing = json.loads(timing_bytes)
slots = ("turn_left", "turn_right", "crouch_turn_left", "crouch_turn_right")
if catalog["schemaVersion"] != 1 or timing["schemaVersion"] != 1:
    raise RuntimeError("Unsupported Lyra turn metadata")
clips = {row["slot"]: row for row in catalog["clips"]}
timing_clips = {row["slot"]: row for row in timing["clips"]}
if any(slot not in clips or slot not in timing_clips for slot in slots):
    raise RuntimeError("The four ALS turn clips are not exported")

content = Path(unreal.Paths.project_content_dir())


def package_hash(path):
    filename = content / (path.split(".", 1)[0].removeprefix("/Game/") + ".uasset")
    if not path.startswith("/Game/") or not filename.is_file():
        raise RuntimeError("Missing UE package: " + path)
    return hashlib.sha256(filename.read_bytes()).hexdigest()


skeleton = unreal.load_asset(catalog["targetSkeleton"])
if not isinstance(skeleton, unreal.Skeleton):
    raise RuntimeError("Missing ALS target skeleton")
skeleton_layout = json.loads(unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(skeleton))
names = skeleton_layout["logicalBoneNames"]
if skeleton_layout["rawBoneNames"] != names[:68] or len(names) != 79:
    raise RuntimeError("ALS skeleton physical/logical layout changed")

captures = []
for slot in slots:
    row = clips[slot]
    metadata = timing_clips[slot]
    if (metadata["source"] != row["source"] or metadata["target"] != row["target"] or
            not metadata["curvesUnchanged"] or
            {item["name"] for item in metadata["targetCurves"]} !=
            {"RemainingTurnYaw", "TurnYawWeight"} or
            row["floatCurveNames"] != ["RemainingTurnYaw", "TurnYawWeight"] or
            row["forceRootLock"] is not True or row["rootMotionRootLock"] != "RefPose"):
        raise RuntimeError("ALS turn binding or curve policy changed: " + slot)
    for side in ("source", "target"):
        if package_hash(row[side]).lower() != row[side + "UassetSha256"].lower():
            raise RuntimeError("ALS turn " + side + " package changed: " + slot)
    animation = unreal.load_asset(row["target"])
    if (not isinstance(animation, unreal.AnimSequence) or
            animation.get_editor_property("skeleton") != skeleton):
        raise RuntimeError("Missing ALS turn sequence: " + slot)
    if mode == "compressed":
        unreal.AlsSourceAnimationLibrary.finish_source_compression(animation)
    length = row["playLength"]
    if not math.isfinite(length) or length <= 0:
        raise RuntimeError("Invalid ALS turn duration: " + slot)
    times = [frame / 60.0 for frame in range(math.floor(length * 60) + 1)]
    if length - times[-1] <= 1e-5:
        times[-1] = length
    else:
        times.append(length)
    frames = []
    for seconds in times:
        reader = (unreal.AlsSourceAnimationLibrary.read_raw_animation_pose
                  if mode == "raw" else
                  unreal.AlsSourceAnimationLibrary.read_compressed_animation_pose)
        raw = reader(animation, seconds, True, False, False)
        if not raw:
            raise RuntimeError("UE could not evaluate ALS turn pose: " + slot)
        pose = json.loads(raw)
        if (pose["source"] != row["target"] or pose["names"] != names or
                abs(pose["timeSeconds"] - seconds) > 1e-6 or
                len(pose["pose"]) != len(names) or
                not {"RemainingTurnYaw", "TurnYawWeight"}.issubset(pose["curves"])):
            raise RuntimeError("UE returned incomplete ALS turn pose: " + slot)
        if any(not all(math.isfinite(value) for value in atom[field])
               for atom in pose["pose"] for field in ("position", "rotation", "scale")):
            raise RuntimeError("UE returned nonfinite ALS turn pose: " + slot)
        curves = {name: pose["curves"][name]
                  for name in ("RemainingTurnYaw", "TurnYawWeight")}
        if not all(math.isfinite(value) for value in curves.values()):
            raise RuntimeError("UE returned nonfinite ALS turn curve: " + slot)
        frames.append({"timeSeconds": seconds, "curves": curves, "pose": pose["pose"]})
    captures.append({"slot": slot, "source": row["source"], "target": row["target"],
                     "sourceUassetSha256": row["sourceUassetSha256"],
                     "targetUassetSha256": row["targetUassetSha256"],
                     "fbxSha256": row["fbxSha256"], "frames": frames})
    unreal.log("LYRA_ALS_TURN_ORACLE_CLIP_OK slot=" + slot +
               " frames=" + str(len(frames)))

payload = {"schemaVersion": 1, "sampleHz": 60,
           "catalogSha256": hashlib.sha256(catalog_bytes).hexdigest(),
           "timingSha256": hashlib.sha256(timing_bytes).hexdigest(),
           "targetSkeleton": catalog["targetSkeleton"],
           "logicalBoneNames": names,
           "logicalParents": skeleton_layout["logicalParents"],
           "physicalBoneCount": len(skeleton_layout["rawBoneNames"]),
           "clips": captures}
if mode == "compressed":
    payload["sourceMode"] = "forced-compressed"
output = root / ("unarmed_turn_als_native_60hz" +
                 ("_forced_compressed" if mode == "compressed" else "") + ".json")
if output.exists():
    if json.loads(output.read_text(encoding="utf-8")) != payload:
        raise RuntimeError("Existing ALS turn oracle differs: " + str(output))
else:
    output.write_text(json.dumps(payload, separators=(",", ":")), encoding="utf-8")
unreal.log("LYRA_ALS_TURN_ORACLE_OK mode=" + mode + " clips=4 frames=" +
           str(sum(len(item["frames"]) for item in captures)) + " assets_saved=0")
