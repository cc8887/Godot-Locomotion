"""Probe Kimodo ALS_Final sequences: non-empty source pose keys and ALS bone compatibility."""
import gc
import json
import math
from pathlib import Path

import unreal

PACKAGE_ROOT = "/Game/KimodoMocara/ALS_Final/"
OUTPUT = Path("F:/Github/Godot-Locomotion/artifacts/gasp58/kimodo_pose_keys_probe.json")
PREFERRED = {
    "AS_ALS_N_JumpWalk_RF",  # baked root-ik variant
    "AS_ALS_N_LandRoll_F",   # baked root-ik variant, original had root motion enabled
    "AS_ALS_N_Mantle_2m",    # baked root-ik mantle
}


def decode(text):
    return json.loads(text, parse_int=lambda token: -0.0 if token == "-0" else int(token))


def short_name(package_path):
    return package_path.rsplit("/", 1)[-1]


def validate(row, logical_names):
    key_count = row["sampledKeyCount"]
    if key_count < 1 or row["frameRateNumerator"] <= 0 or row["frameRateDenominator"] <= 0:
        raise RuntimeError("Invalid timing: " + row["source"])
    names = {name.casefold() for name in logical_names}
    seen = set()
    total_keys = 0
    for track in row["tracks"]:
        bone = track["bone"]
        identity = bone.casefold()
        if identity not in names or identity in seen:
            raise RuntimeError("Unknown or duplicate bone: " + bone + " source=" + row["source"])
        seen.add(identity)
        for field, dimensions in (("positions", 3), ("rotations", 4), ("scales", 3)):
            allowed = (0, 1, key_count) if field == "scales" else (1, key_count)
            if len(track[field]) not in allowed:
                raise RuntimeError("Invalid channel count: " + bone + "." + field)
            if any(len(key) != dimensions or any(not math.isfinite(x) for x in key) for key in track[field]):
                raise RuntimeError("Invalid key components: " + bone + "." + field)
            total_keys += len(track[field])
    return total_keys


def probe():
    listings = unreal.EditorAssetLibrary.list_assets(PACKAGE_ROOT, recursive=False)
    package_paths = sorted(entry.split(".")[0] for entry in listings)
    if not package_paths:
        raise RuntimeError("No assets under " + PACKAGE_ROOT)
    if len({short_name(path) for path in package_paths}) != len(package_paths):
        raise RuntimeError("Duplicate short names under " + PACKAGE_ROOT)
    selected = [path for path in package_paths if short_name(path) in PREFERRED]
    remaining = [path for path in package_paths if path not in selected]
    while len(selected) < 3 and remaining:
        selected.append(remaining.pop(len(remaining) // 2))
    if not selected:
        raise RuntimeError("No sequences selected")
    unreal.log("KIMODO_POSE_KEYS_PROBE_LISTING packages=" + str(len(package_paths)) +
               " selected=" + ",".join(short_name(path) for path in selected))

    skeletons = {}
    results = []
    for package_path in selected:
        name = short_name(package_path)
        target = package_path + "." + name
        animation = unreal.load_asset(target)
        if not isinstance(animation, unreal.AnimSequence) or animation.get_path_name() != target:
            raise RuntimeError("Missing exact source sequence: " + target)
        row = decode(unreal.AlsSourcePoseKeysLibrary.read_source_pose_keys(animation))
        if row["source"] != target:
            raise RuntimeError("Identity mismatch: " + target)
        evaluation = decode(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(animation))
        skeleton = animation.get_editor_property("skeleton")
        skeleton_source = skeleton.get_path_name()
        if skeleton_source not in skeletons:
            skeletons[skeleton_source] = decode(
                unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(skeleton))
        if row["skeletonSource"] != skeleton_source:
            raise RuntimeError("Skeleton mismatch: " + target)
        logical = skeletons[skeleton_source]["logicalBoneNames"]
        total_keys = validate(row, logical)
        results.append({
            "source": target,
            "skeletonSource": skeleton_source,
            "logicalBoneCount": len(logical),
            "trackCount": len(row["tracks"]),
            "sampledKeyCount": row["sampledKeyCount"],
            "frameRate": str(row["frameRateNumerator"]) + "/" + str(row["frameRateDenominator"]),
            "totalKeys": total_keys,
            "playLength": animation.get_play_length(),
            "enableRootMotion": evaluation["enableRootMotion"],
            "forceRootLock": evaluation["forceRootLock"],
            "baseAsset": evaluation["baseAsset"],
        })
        unreal.log("KIMODO_POSE_KEYS_PROBE_ROW source=" + name +
                   " tracks=" + str(len(row["tracks"])) + " keys=" + str(row["sampledKeyCount"]))

    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(json.dumps({"schemaVersion": 1, "packageCount": len(package_paths),
                                  "targets": results}, indent=2) + "\n", encoding="utf-8")
    unreal.log("KIMODO_POSE_KEYS_PROBE_OK packages=" + str(len(package_paths)) +
               " targets=" + str(len(results)))


probe()
gc.collect()
