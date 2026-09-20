"""Read original AnimDataModel track channels; no pose evaluation or asset writes."""
import json
import math
import os
from pathlib import Path

import unreal


def channel(keys, components, key_count, label, allow_empty=False):
    if len(keys) not in ((0, 1, key_count) if allow_empty else (1, key_count)):
        raise RuntimeError("Unexpected source channel cardinality: " + label)
    if any(len(key) != components or any(not math.isfinite(value) for value in key) for key in keys):
        raise RuntimeError("Invalid source track components: " + label)


def export():
    output = Path(os.environ["ALS_BASE_POSE_SOURCE_KEYS_OUTPUT"])
    if not output.is_absolute():
        raise ValueError("ALS_BASE_POSE_SOURCE_KEYS_OUTPUT must be absolute")
    base = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/BasePoses/"
    expected_skeleton = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton"
    assets = []
    for name in ("ALS_N_Pose", "ALS_CLF_Pose"):
        asset = unreal.load_asset(base + name)
        if not isinstance(asset, unreal.AnimSequence):
            raise RuntimeError("Missing BasePoses sequence: " + name)
        model = asset.get_editor_property("data_model_interface")
        if model is None:
            raise RuntimeError("Missing current animation data model: " + name)
        skeleton = asset.get_editor_property("skeleton")
        if skeleton is None or skeleton.get_path_name() != expected_skeleton:
            raise RuntimeError("Unexpected source skeleton: " + name)
        frame_rate = model.get_frame_rate()
        numerator = frame_rate.numerator
        denominator = frame_rate.denominator
        key_count = model.get_number_of_keys()
        play_length = model.get_play_length()
        if numerator != 30 or denominator != 1 or key_count != 2 or not math.isfinite(play_length) or play_length <= 0:
            raise RuntimeError("Unexpected BasePoses source timing: " + name)
        # Bare RawAnimSequenceTrack UPROPERTY fields are protected in Python.
        # The read-only native helper copies original arrays into double JSON numbers.
        # Native %.17g prints IEEE negative zero as -0; keep its sign bit while
        # retaining integer types for the timing metadata fields.
        row = json.loads(unreal.AlsSourcePoseKeysLibrary.read_source_pose_keys(asset),
                         parse_int=lambda token: -0.0 if token == "-0" else int(token))
        if (row["source"] != asset.get_path_name() or row["skeletonSource"] != skeleton.get_path_name()
                or row["frameRateNumerator"] != numerator or row["frameRateDenominator"] != denominator
                or row["sampledKeyCount"] != key_count or row["playLength"] != play_length):
            raise RuntimeError("Native source identity or timing mismatch: " + name)
        tracks = row["tracks"]
        for track in tracks:
            for field, components in (("positions", 3), ("rotations", 4), ("scales", 3)):
                channel(track[field], components, key_count, track["bone"] + "." + field, field == "scales")
        if len(tracks) != 68 or len({track["bone"] for track in tracks}) != 68 or any(track["bone"].startswith("VB ") for track in tracks):
            raise RuntimeError("Expected the 68 original physical bone tracks: " + name)
        assets.append(row)
    payload = {"schemaVersion": 1, "source": "AnimDataModel.BoneAnimationTracks.InternalTrackData", "assets": assets}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(payload, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    unreal.log("ALS_SOURCE_POSE_KEYS_OK assets=2 source_tracks=68 sampled_keys=2 units=cm pose_evaluations=0 assets_saved=0")


export()
