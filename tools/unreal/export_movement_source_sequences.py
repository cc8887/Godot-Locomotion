"""Export the bound movement source closure and separate native raw-pose oracles."""
import hashlib
import json
import math
import os
from pathlib import Path

import unreal

SOURCE = "AnimDataModel.BoneAnimationTracks.InternalTrackData"


def decode(text):
    # UE's %.17g prints binary negative zero as -0.
    return json.loads(text, parse_int=lambda token: -0.0 if token == "-0" else int(token))


def write_json(path, value):
    payload = (json.dumps(value, indent=2, allow_nan=False) + "\n").encode("utf-8")
    if os.environ.get("ALS_RAW_SOURCE_PRESERVE_EXISTING") == "1" and path.parent.name == "raw_sequences" and path.exists():
        if path.read_bytes() != payload:
            raise RuntimeError("Existing shared raw source differs; preserve it for investigation: " + str(path))
        return hashlib.sha256(payload).hexdigest()
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(payload)
    return hashlib.sha256(payload).hexdigest()


def absolute_env(name):
    path = Path(os.environ[name])
    if not path.is_absolute():
        raise ValueError(name + " must be absolute")
    return path


def asset_id(source):
    return hashlib.sha1(source.encode("utf-8")).hexdigest()


def validate_tracks(row, skeleton):
    # UE FName identity is case-insensitive; preserve actual exported spelling.
    names = {name.casefold() for name in skeleton["logicalBoneNames"]}
    key_count = row["sampledKeyCount"]
    if key_count < 1 or row["frameRateNumerator"] <= 0 or row["frameRateDenominator"] <= 0:
        raise RuntimeError("Invalid source timing: " + row["source"])
    seen = set()
    for track in row["tracks"]:
        bone = track["bone"]
        bone_identity = bone.casefold()
        if bone_identity not in names or bone_identity in seen:
            raise RuntimeError("Unknown or duplicate source bone: " + bone + " source=" + row["source"] +
                               " logical_count=" + str(len(names)) + " first_names=" +
                               repr(skeleton["logicalBoneNames"][:8]) + " duplicate=" + str(bone_identity in seen))
        seen.add(bone_identity)
        for field, dimensions in (("positions", 3), ("rotations", 4), ("scales", 3)):
            allowed = (0, 1, key_count) if field == "scales" else (1, key_count)
            if len(track[field]) not in allowed:
                raise RuntimeError("Invalid original channel count: " + bone + "." + field)
            if any(len(key) != dimensions or any(not math.isfinite(x) for x in key) for key in track[field]):
                raise RuntimeError("Invalid original key components: " + bone + "." + field)


def sample_times(row):
    frame_rate = row["frameRateNumerator"] / row["frameRateDenominator"]
    end_frame = max(0, row["sampledKeyCount"] - 1)
    frames = (("first", 0.0), ("quarter_frame", min(end_frame, .25)),
              ("middle_fraction", max(0.0, end_frame * .5 - .125)),
              ("near_tail", max(0.0, end_frame - .25)), ("tail", float(end_frame)))
    return [(label, frame / frame_rate) for label, frame in frames]


def export():
    request_path = absolute_env("ALS_MOVEMENT_SOURCE_REQUEST")
    index_path = absolute_env("ALS_MOVEMENT_SOURCE_INDEX")
    oracle_path = absolute_env("ALS_MOVEMENT_SOURCE_ORACLE")
    if len({request_path, index_path, oracle_path}) != 3:
        raise ValueError("Request, production index, and pose oracle must be distinct")
    request = decode(request_path.read_text(encoding="utf-8-sig"))
    roots = request["rootAssets"]
    if not roots or len({row["assetId"] for row in roots}) != len(roots):
        raise RuntimeError("Empty or duplicate root animation closure")
    pending = {}
    for row in roots:
        if row["assetId"] != asset_id(row["source"]):
            raise RuntimeError("Root stable asset ID does not match its source")
        pending[row["source"]] = row["assetId"]
    assets, skeletons, dependencies, native_assets = {}, {}, {}, {}
    while pending:
        source = sorted(pending)[0]
        identifier = pending.pop(source)
        if source in assets:
            continue
        animation = unreal.load_asset(source)
        if not isinstance(animation, unreal.AnimSequence) or animation.get_path_name() != source:
            raise RuntimeError("Missing exact source sequence: " + source)
        row = decode(unreal.AlsSourcePoseKeysLibrary.read_source_pose_keys(animation))
        if row["source"] != source:
            raise RuntimeError("Source track identity mismatch")
        row["evaluation"] = decode(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(animation))
        skeleton = animation.get_editor_property("skeleton")
        skeleton_source = skeleton.get_path_name()
        if skeleton_source not in skeletons:
            skeletons[skeleton_source] = decode(unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(skeleton))
        if row["skeletonSource"] != skeleton_source:
            raise RuntimeError("Source skeleton mismatch")
        validate_tracks(row, skeletons[skeleton_source])
        assets[source] = row
        native_assets[source] = animation
        base_source = row["evaluation"]["baseAsset"]
        if base_source:
            base_id = asset_id(base_source)
            dependencies.setdefault(base_source, set()).add(identifier)
            if base_source not in assets:
                pending[base_source] = base_id
        if len(assets) % 10 == 0:
            unreal.log("ALS_MOVEMENT_SOURCE_PROGRESS assets=" + str(len(assets)))

    index_rows = []
    for source in sorted(assets, key=asset_id):
        identifier = asset_id(source)
        relative_file = "raw_sequences/" + identifier + ".json"
        digest = write_json(index_path.parent / relative_file, assets[source])
        entry = {"assetId": identifier, "source": source, "file": relative_file, "sha256": digest}
        if source in dependencies:
            entry["dependencyOf"] = sorted(dependencies[source])
        index_rows.append(entry)
    skeleton_rows = [skeletons[source] for source in sorted(skeletons)]
    index = {"schemaVersion": 1, "source": SOURCE, "request": request,
             "skeletons": skeleton_rows, "assets": index_rows}
    write_json(index_path, index)
    unreal.log("ALS_MOVEMENT_SOURCE_INPUTS_OK roots=" + str(len(roots)) +
               " assets=" + str(len(assets)) + " skeletons=" + str(len(skeletons)) + " assets_saved=0")

    contexts = (("raw_unretargeted", False, False, True),
                ("after_retarget_before_root_lock", True, False, True),
                ("asset_root_lock", True, False, False),
                ("extract_root_lock", True, True, False))
    oracle_assets = []
    for entry in index_rows:
        source = entry["source"]
        samples = []
        for label, seconds in sample_times(assets[source]):
            for context_name, retarget, extract_root, ignore_lock in contexts:
                sample = decode(unreal.AlsSourceAnimationLibrary.read_raw_bone_pose(
                    native_assets[source], seconds, retarget, extract_root, ignore_lock))
                if sample["names"] != skeletons[assets[source]["skeletonSource"]]["logicalBoneNames"]:
                    raise RuntimeError("Oracle lost logical bones: " + source)
                sample["label"] = label
                sample["context"] = context_name
                samples.append(sample)
        oracle_assets.append({"assetId": entry["assetId"], "source": source, "samples": samples})
    # Re-sample representative source policies and the largest observed virtual-bone
    # motion densely, without introducing synthetic poses or asset edits.
    dense_sources = set()
    for predicate in (lambda row: row["evaluation"]["forceRootLock"],
                      lambda row: row["evaluation"]["enableRootMotion"],
                      lambda row: any(track["bone"].startswith("VB ") for track in row["tracks"]),
                      lambda row: any(abs(value - 1.0) > 1e-4 for track in row["tracks"]
                                      for key in track["scales"] for value in key)):
        candidate = next((source for source in sorted(assets) if predicate(assets[source])), None)
        if candidate:
            dense_sources.add(candidate)
    motion_scores = []
    for row in oracle_assets:
        raw = [sample for sample in row["samples"] if sample["context"] == "raw_unretargeted"]
        vb_indices = [index for index, name in enumerate(raw[0]["names"]) if name.startswith("VB ")]
        score = sum(abs(sample["pose"][index][field][component] - raw[0]["pose"][index][field][component])
                    for sample in raw[1:] for index in vb_indices for field in ("position", "rotation", "scale")
                    for component in range(len(sample["pose"][index][field])))
        motion_scores.append((score, row["source"]))
    if motion_scores:
        dense_sources.add(max(motion_scores)[1])
    for row in oracle_assets:
        if row["source"] not in dense_sources:
            continue
        row["denseCoverage"] = True
        source = row["source"]
        for index in range(16):
            seconds = assets[source]["playLength"] * (index + .375) / 16
            for context_name, retarget, extract_root, ignore_lock in contexts:
                sample = decode(unreal.AlsSourceAnimationLibrary.read_raw_bone_pose(
                    native_assets[source], seconds, retarget, extract_root, ignore_lock))
                sample["label"] = "dense_" + str(index)
                sample["context"] = context_name
                row["samples"].append(sample)
    write_json(oracle_path, {"schemaVersion": 1,
                            "source": "UAnimSequence.GetBonePose(forceRaw=true); non-additive source evaluation",
                            "request": request, "skeletons": skeleton_rows, "assets": oracle_assets})
    sampling_output = os.environ.get("ALS_RAW_SEQUENCE_SAMPLING_OUTPUT")
    if sampling_output:
        sampling_path = absolute_env("ALS_RAW_SEQUENCE_SAMPLING_OUTPUT")
        write_json(sampling_path, decode(unreal.AlsSourceAnimationLibrary.read_raw_sampling_cases()))
    unreal.log("ALS_MOVEMENT_SOURCE_NATIVE_OK assets=" + str(len(assets)) +
               " poses=" + str(sum(len(row["samples"]) for row in oracle_assets)) +
               " assets_saved=0")


export()
