"""Export Kimodo-regenerated ALS V4 raw movement sources into the Godot six-domain layout.

Reads Kimodo retargeted assets (ALS_Final) per the Kimodo manifest mapping, filters
their 79 logical tracks down to each original asset's track bone set, injects the
original evaluation metadata verbatim, and writes six-domain indices that satisfy
AlsRawAnimationSourceCompiler's contract while keeping the original asset identity
(assetId = SHA1(original objectPath)) so the loader digest gate routes to this tree.
"""
import gc
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


def load_original_evaluation(config_directory, source):
    identifier = asset_id(source)
    path = config_directory / "raw_sequences" / (identifier + ".json")
    if not path.is_file():
        raise RuntimeError("Missing original raw source for evaluation injection: " + str(path))
    row = decode(path.read_text(encoding="utf-8-sig"))
    if row["source"] != source:
        raise RuntimeError("Original raw source identity mismatch: " + str(path))
    return row


def kimodo_tracks_for(original_row, kimodo_row):
    # Kimodo read_source_pose_keys returns all logical tracks; keep only the bones
    # present in the original raw asset (subset is legal per the compiler contract).
    wanted = {}
    for track in original_row["tracks"]:
        wanted[track["bone"].casefold()] = True
    filtered = []
    for track in kimodo_row["tracks"]:
        if track["bone"].casefold() in wanted:
            filtered.append(track)
    if len(filtered) != len(original_row["tracks"]):
        raise RuntimeError("Kimodo track bone set does not cover the original asset: " +
                           original_row["source"] + " expected=" + str(len(original_row["tracks"])) +
                           " matched=" + str(len(filtered)))
    return filtered


def kimodo_covers_original(original_row, kimodo_row):
    # Objective coverage gate: a Kimodo clip only counts as a replacement when its
    # logical bone set actually covers the original asset's bones. Prop-skeleton
    # clips (e.g. Bow_Draw on Bow_Skeleton, 24 bow spines) are outside the Kimodo
    # humanoid synthesis rig, so they must stay kept-original instead of failing.
    wanted = {track["bone"].casefold() for track in original_row["tracks"]}
    covered = {track["bone"].casefold() for track in kimodo_row["tracks"]}
    return wanted <= covered


def assert_timing(original_row, kimodo_row):
    # Kimodo re-synthesized clips carry their own length (e.g. 31 keys against an
    # original 2-key base pose), and the exported row already describes the actual
    # Kimodo tracks, so strict timing parity with the original asset is NOT a
    # valid invariant. Record the divergence instead; additive base-frame bounds
    # are enforced separately after the closure completes.
    if (kimodo_row["sampledKeyCount"] != original_row["sampledKeyCount"]
            or kimodo_row["playLength"] != original_row["playLength"]
            or kimodo_row["frameRateNumerator"] != original_row["frameRateNumerator"]
            or kimodo_row["frameRateDenominator"] != original_row["frameRateDenominator"]):
        unreal.log("KIMODO_MOVEMENT_SOURCE_TIMING_DIFF source=" + original_row["source"] +
                   " kimodo_keys=" + str(kimodo_row["sampledKeyCount"]) +
                   " original_keys=" + str(original_row["sampledKeyCount"]) +
                   " kimodo_length=" + repr(kimodo_row["playLength"]) +
                   " original_length=" + repr(original_row["playLength"]))


def full_pose_sanity(original_row, tracks):
    # Original additive assets store FULL POSE tracks (delta is computed at runtime
    # against the base asset), so the pelvis must carry whole-body scale translation.
    evaluation = original_row["evaluation"]
    if not evaluation["baseAsset"]:
        return
    pelvis = next((track for track in tracks if track["bone"].casefold() == "pelvis"), None)
    if pelvis is None or not pelvis["positions"]:
        raise RuntimeError("Additive asset lost its pelvis track: " + original_row["source"])
    if all(abs(key[2]) < 50.0 for key in pelvis["positions"]):
        raise RuntimeError("Additive asset pelvis translation looks baked-delta, expected full pose: " +
                           original_row["source"])


def export():
    request_path = absolute_env("ALS_MOVEMENT_SOURCE_REQUEST")
    index_path = absolute_env("ALS_MOVEMENT_SOURCE_INDEX")
    config_directory = absolute_env("KIMODO_ORIGINAL_RAW_DIR")
    manifest_path = absolute_env("KIMODO_MANIFEST_PATH")
    if request_path == index_path:
        raise ValueError("Request and production index must be distinct")

    manifest = decode(manifest_path.read_text(encoding="utf-8-sig"))
    marker = manifest.get("kimodoSourceMarker")
    if not marker or marker.get("variant") != "kimodo-als-regen":
        raise RuntimeError("Kimodo manifest marker missing or wrong variant: " + str(manifest_path))
    retarget_map = {}
    for animation in manifest["animations"]:
        retarget_map[animation["objectPath"]] = animation.get("kimodoSource", {}).get("retargetedAssetPath", "")

    request = decode(request_path.read_text(encoding="utf-8-sig"))
    roots = request["rootAssets"]
    if not roots or len({row["assetId"] for row in roots}) != len(roots):
        raise RuntimeError("Empty or duplicate root animation closure")
    pending = {}
    for row in roots:
        if row["assetId"] != asset_id(row["source"]):
            raise RuntimeError("Root stable asset ID does not match its source")
        pending[row["source"]] = row["assetId"]
    assets, skeletons, dependencies = {}, {}, {}
    def keep_original_row(source, original_row):
        # Shared kept-original path: reuse the original raw row verbatim (tracks and
        # evaluation already satisfy the compiler contract); register the skeleton row.
        row = original_row
        skeleton_source = row["skeletonSource"]
        if skeleton_source not in skeletons:
            kept = unreal.load_asset(source)
            if not isinstance(kept, unreal.AnimSequence) or kept.get_path_name() != source:
                raise RuntimeError("Missing kept-original source sequence: " + source)
            kept_skeleton = kept.get_editor_property("skeleton")
            if kept_skeleton.get_path_name() != skeleton_source:
                raise RuntimeError("Kept-original skeleton identity mismatch: " + source +
                                   " raw=" + skeleton_source + " asset=" + kept_skeleton.get_path_name())
            skeletons[skeleton_source] = decode(
                unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(kept_skeleton))
        return row, skeleton_source

    while pending:
        source = sorted(pending)[0]
        identifier = pending.pop(source)
        if source in assets:
            continue
        original_row = load_original_evaluation(config_directory, source)
        load_path = retarget_map.get(source, "")
        if not load_path:
            # Kept-original asset (no kimodoSource block in the manifest): reuse the
            # original raw row verbatim; only register its skeleton row for the index.
            row, skeleton_source = keep_original_row(source, original_row)
            validate_tracks(row, skeletons[skeleton_source])
            assets[source] = row
            base_source = row["evaluation"]["baseAsset"]
            if base_source:
                base_id = asset_id(base_source)
                dependencies.setdefault(base_source, set()).add(identifier)
                if base_source not in assets:
                    pending[base_source] = base_id
            unreal.log("KIMODO_MOVEMENT_SOURCE_KEPT_ORIGINAL source=" + source +
                       " reason=no-kimodo-source")
            continue
        animation = unreal.load_asset(load_path)
        if not isinstance(animation, unreal.AnimSequence) or animation.get_path_name() != load_path:
            raise RuntimeError("Missing exact Kimodo source sequence: " + load_path + " original=" + source)
        kimodo_row = decode(unreal.AlsSourcePoseKeysLibrary.read_source_pose_keys(animation))
        if kimodo_row["source"] != load_path:
            raise RuntimeError("Kimodo source track identity mismatch: " + load_path)
        skeleton = animation.get_editor_property("skeleton")
        skeleton_source = skeleton.get_path_name()
        if kimodo_row["skeletonSource"] != skeleton_source:
            raise RuntimeError("Kimodo skeleton mismatch: " + load_path)
        if not kimodo_covers_original(original_row, kimodo_row):
            # Kimodo clip exists but cannot cover this asset's bones (prop skeleton
            # outside the humanoid rig); keep the original tracks instead. Do NOT
            # register the Kimodo skeleton on this path: the index may only declare
            # skeletons used by emitted rows or the Godot compiler's used-vs-
            # declared SetEquals assertion fails (overlay_prop Bow_Draw case).
            row, skeleton_source = keep_original_row(source, original_row)
            validate_tracks(row, skeletons[skeleton_source])
            assets[source] = row
            base_source = row["evaluation"]["baseAsset"]
            if base_source:
                base_id = asset_id(base_source)
                dependencies.setdefault(base_source, set()).add(identifier)
                if base_source not in assets:
                    pending[base_source] = base_id
            unreal.log("KIMODO_MOVEMENT_SOURCE_KEPT_ORIGINAL source=" + source +
                       " reason=bone-set-not-covered kimodo=" + load_path)
            continue
        # Replacement path (Kimodo clip covers the bone set): the emitted row uses
        # the Kimodo skeleton, so register it here — after the covers gate — so a
        # kept-original fallback never leaks an unused skeleton into the index.
        if skeleton_source not in skeletons:
            skeletons[skeleton_source] = decode(unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(skeleton))
        assert_timing(original_row, kimodo_row)
        tracks = kimodo_tracks_for(original_row, kimodo_row)
        full_pose_sanity(original_row, tracks)
        row = {
            "source": source,
            "skeletonSource": kimodo_row["skeletonSource"],
            "frameRateNumerator": kimodo_row["frameRateNumerator"],
            "frameRateDenominator": kimodo_row["frameRateDenominator"],
            "sampledKeyCount": kimodo_row["sampledKeyCount"],
            "playLength": kimodo_row["playLength"],
            "tracks": tracks,
            "evaluation": original_row["evaluation"],
        }
        validate_tracks(row, skeletons[skeleton_source])
        assets[source] = row
        base_source = row["evaluation"]["baseAsset"]
        if base_source:
            base_id = asset_id(base_source)
            dependencies.setdefault(base_source, set()).add(identifier)
            if base_source not in assets:
                pending[base_source] = base_id
        if len(assets) % 10 == 0:
            unreal.log("KIMODO_MOVEMENT_SOURCE_PROGRESS assets=" + str(len(assets)))

    # Cross-asset invariant: additive evaluation.baseFrame comes from the ORIGINAL
    # asset, but base-asset tracks are the re-synthesized Kimodo clip whose key
    # count may differ. Any base frame beyond the Kimodo base clip would sample
    # the reference pose silently, so fail loudly instead.
    for source, row in assets.items():
        evaluation = row["evaluation"]
        base_source = evaluation["baseAsset"]
        if not base_source or base_source not in assets:
            continue
        base_frame = evaluation["baseFrame"]
        base_keys = assets[base_source]["sampledKeyCount"]
        if base_frame >= base_keys and not (base_frame == 0 and base_keys == 0):
            raise RuntimeError("Additive base frame beyond Kimodo base clip: " + source +
                               " baseAsset=" + base_source + " baseFrame=" + str(base_frame) +
                               " kimodo_base_keys=" + str(base_keys))

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
    unreal.log("KIMODO_MOVEMENT_SOURCE_INPUTS_OK roots=" + str(len(roots)) +
               " assets=" + str(len(assets)) + " skeletons=" + str(len(skeletons)) + " assets_saved=0")


export()

globals().pop("export", None)
gc.collect()
