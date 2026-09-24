"""Export original mantle root keys, exact segment fields and separate native checks.

Single-segment first slots are the currently supported export closure. Reject a
changed topology instead of assuming get_first_anim_reference covers all slots.
No assets are saved; sampled reference transforms are never production keys.
"""
import json
import os
from pathlib import Path
import unreal

output = Path(os.environ["ALS_MANTLE_ROOT_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
inputs = json.loads(Path(__file__).parents[2].joinpath("assets/config/refactored_mantle_inputs.json").read_text(encoding="utf-8"))
sequences, montages, references = {}, [], []

def decode(text):
    return json.loads(text, parse_int=lambda token: -0.0 if token == "-0" else int(token))

def pose(transform):
    p, q, s = transform.translation, transform.rotation, transform.scale3d
    return {"position": [p.x,p.y,p.z], "rotation": [q.x,q.y,q.z,q.w], "scale": [s.x,s.y,s.z]}

for source in inputs["montages"]:
    montage = unreal.load_asset(source["path"])
    slots = montage.get_editor_property("slot_anim_tracks")
    segments = slots[0].get_editor_property("anim_track").get_editor_property("anim_segments")
    if len(slots) != 1 or len(segments) != 1:
        raise RuntimeError("Unsupported mantle slot topology: " + source["path"])
    sequence = montage.get_first_anim_reference()
    if not isinstance(sequence, unreal.AnimSequence):
        raise RuntimeError("Mantle source is not an animation sequence")
    path = sequence.get_path_name()
    if path not in sequences:
        raw = decode(unreal.AlsSourcePoseKeysLibrary.read_source_pose_keys(sequence))
        skeleton = decode(unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(sequence.get_editor_property("skeleton")))
        metadata = decode(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(sequence))
        if metadata["transformCurveCount"] != 0:
            raise RuntimeError("Root transform-curve evaluation is not yet supported: " + path)
        root_name = skeleton["rawBoneNames"][0]
        roots = [track for track in raw["tracks"] if track["bone"].casefold() == root_name.casefold()]
        if len(roots) > 1:
            raise RuntimeError("Duplicate root track")
        # Preserve original channel counts and exact float components.
        raw["tracks"] = roots
        raw.update(rootName=root_name, rootReference=skeleton["referencePose"][0],
                   interpolation=metadata["interpolation"], rateScale=sequence.get_editor_property("rate_scale"))
        sequences[path] = raw
    segment = segments[0]
    fields = {key: segment.get_editor_property(native) for key, native in (
        ("trackStart", "start_pos"), ("animationStart", "anim_start_time"),
        ("animationEnd", "anim_end_time"), ("segmentRate", "anim_play_rate"), ("loopCount", "looping_count"))}
    fields["sequence"] = path
    montages.append({"path": source["path"], "segments": [fields]})
    times = [-.1, .0137, .3719, montage.get_play_length() * .7341, montage.get_play_length() + .1]
    references.append({"path": source["path"], "samples": [dict(time=time, **pose(
        unreal.AlsMontageUtility.extract_root_transform_from_montage(montage, time))) for time in times],
        "last": pose(unreal.AlsMontageUtility.extract_last_root_transform_from_montage(montage))})

output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps({"schemaVersion": 1, "source": "AnimDataModel.BoneAnimationTracks.InternalTrackData",
    "sequences": [sequences[key] for key in sorted(sequences)], "montages": montages,
    "references": references}, indent=2, allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log(f"ALS_MANTLE_ROOT_OK sequences={len(sequences)} montages={len(montages)} assets_saved=0")
