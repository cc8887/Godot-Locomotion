"""Read-only GetAnimationPose oracle for the actual movement additive source closure."""
import hashlib
import json
import os
from pathlib import Path

import unreal


def decode(text):
    return json.loads(text, parse_int=lambda token: -0.0 if token == "-0" else int(token))


def absolute(name):
    path = Path(os.environ[name])
    if not path.is_absolute():
        raise ValueError(name + " must be absolute")
    return path


def export():
    index_path = absolute("ALS_ADDITIVE_SOURCE_INDEX")
    output_path = absolute("ALS_ADDITIVE_SOURCE_ORACLE")
    if index_path == output_path:
        raise ValueError("Source index and test oracle must differ")
    index_bytes = index_path.read_bytes()
    index = decode(index_bytes.decode("utf-8-sig"))
    # Match the source export's loading order, retaining original FName display spelling.
    loaded = {entry["source"]: unreal.load_asset(entry["source"])
              for entry in sorted(index["assets"], key=lambda entry: entry["source"])}
    rows = []
    contexts = (("unretargeted", False, False, True), ("before_root_lock", True, False, True),
                ("asset_root_lock", True, False, False), ("extract_root_lock", True, True, False))
    for entry in index["assets"]:
        payload = (index_path.parent / entry["file"]).read_bytes()
        if hashlib.sha256(payload).hexdigest().lower() != entry["sha256"].lower():
            raise RuntimeError("Source file digest changed: " + entry["source"])
        source = decode(payload.decode("utf-8"))
        if source["evaluation"]["additiveType"] == "AAT_None":
            continue
        asset = loaded[entry["source"]]
        if not isinstance(asset, unreal.AnimSequence) or asset.get_path_name() != entry["source"]:
            raise RuntimeError("Missing exact additive sequence")
        metadata = decode(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(asset))
        if metadata != source["evaluation"]:
            raise RuntimeError("Native additive policy changed since source export: " + entry["source"])
        current_keys = decode(unreal.AlsSourcePoseKeysLibrary.read_source_pose_keys(asset))
        if current_keys != {key: value for key, value in source.items() if key != "evaluation"}:
            raise RuntimeError("Native source keys changed since source export: " + entry["source"])
        length = source["playLength"]
        frame_seconds = source["frameRateDenominator"] / source["frameRateNumerator"]
        times = [0.0, min(length, frame_seconds * .25), length * .5,
                 max(0.0, length - frame_seconds * .25), length]
        times += [length * (index + .375) / 8 for index in range(8)]
        samples = []
        for seconds in times:
            for label, retarget, extract, ignore in contexts:
                sample = decode(unreal.AlsSourceAnimationLibrary.read_raw_animation_pose(asset, seconds, retarget, extract, ignore))
                if not sample["evaluatedAdditive"]:
                    raise RuntimeError("Authored additive did not take the actual additive extraction path")
                sample["context"] = label
                samples.append(sample)
        rows.append({"assetId": entry["assetId"], "source": entry["source"], "evaluation": metadata, "samples": samples})
    if not rows:
        raise RuntimeError("The movement source closure contains no additive sequences")
    result = {"schemaVersion": 1, "source": "UAnimSequence.GetAnimationPose(RAW bone container); actual additive delta",
              "sourceIndexSha256": hashlib.sha256(index_bytes).hexdigest(), "request": index["request"], "assets": rows}
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(result, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    unreal.log("ALS_ADDITIVE_SOURCE_NATIVE_OK assets=" + str(len(rows)) + " poses=" +
               str(sum(len(row["samples"]) for row in rows)) + " assets_saved=0")


export()
