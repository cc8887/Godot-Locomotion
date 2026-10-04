"""Read Lyra's linked-layer blend masks and map them onto the ALS skeleton."""

import json
import os
from pathlib import Path

import unreal


output_root = Path(os.environ["LYRA_OUTPUT_ROOT"])
if not output_root.is_absolute():
    raise ValueError("Lyra output root must be absolute")

source_path = "/Game/Characters/Heroes/Mannequin/Meshes/SK_Mannequin"
target_path = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_Mannequin_Skeleton"
source = unreal.load_asset(source_path)
target = unreal.load_asset(target_path)
if not isinstance(source, unreal.Skeleton) or not isinstance(target, unreal.Skeleton):
    raise RuntimeError("Lyra or ALS skeleton is missing")

source_metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(source))
source_bones = source_metadata["rawBoneNames"]
source_logical_bones = source_metadata["logicalBoneNames"]
target_bones = json.loads(unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(target))["rawBoneNames"]
if len(source_bones) != 161 or len(target_bones) != 68:
    raise RuntimeError("Unexpected Lyra or ALS skeleton bone count")
source_by_name = {name.casefold(): (index, name) for index, name in enumerate(source_bones)}
if len(source_by_name) != len(source_bones):
    raise RuntimeError("Lyra source skeleton has ambiguous bone names")
target_names = {name.casefold() for name in target_bones}
if len(target_names) != len(target_bones) or not target_names.issubset(source_by_name):
    raise RuntimeError("ALS target bones do not map uniquely to the Lyra source skeleton")

rows = {}
for name in ("UpperBodyMask", "LeftFingersMask"):
    profile = unreal.load_object(None, source.get_path_name() + ":" + name)
    if profile is None or profile.get_outer() != source:
        raise RuntimeError("Missing Lyra blend mask: " + name)
    exported = json.loads(unreal.AlsSourceAnimationLibrary.read_blend_profile_metadata(profile))
    if (exported["path"] != profile.get_path_name() or
            exported["skeleton"] != source.get_path_name() or
            exported["mode"] != "BlendMask" or
            len(exported["bones"]) != len(source_logical_bones)):
        raise RuntimeError("Invalid Lyra blend mask metadata: " + name +
                           " path=" + exported["path"] +
                           " skeleton=" + exported["skeleton"] +
                           " mode=" + exported["mode"] +
                           " bones=" + str(len(exported["bones"])))
    entries = exported["entries"]
    scales = {entry["bone"].casefold(): entry["scale"] for entry in entries}
    if len(scales) != len(entries):
        raise RuntimeError("Duplicate Lyra blend mask entry: " + name)
    for index, bone in enumerate(source_logical_bones):
        row = exported["bones"][index]
        if (row["index"] != index or row["bone"] != bone or
                row["scale"] != scales.get(bone.casefold(), 0.0) or
                not 0 <= row["scale"] <= 1):
            raise RuntimeError("Invalid Lyra blend mask bone: " + name + "/" + bone)
    mapped = []
    for index, bone in enumerate(target_bones):
        source_index, source_name = source_by_name[bone.casefold()]
        mapped.append({"index": index, "bone": bone, "sourceIndex": source_index,
                       "sourceBone": source_name, "scale": scales.get(bone.casefold(), 0.0)})
    rows[name] = {"path": profile.get_path_name(), "mode": exported["mode"],
                  "defaultScale": 0.0, "entries": entries, "sourceBones": exported["bones"],
                  "alsBones": mapped,
                  "unmappedSourceEntries": [entry for entry in entries
                                            if entry["bone"].casefold() not in target_names]}
    unreal.log("LYRA_UNARMED_MASK_CLIP_OK name=" + name + " entries=" +
               str(len(entries)) + " unmapped=" + str(len(rows[name]["unmappedSourceEntries"])))

payload = {"schemaVersion": 1, "sourceSkeleton": source.get_path_name(),
           "targetSkeleton": target.get_path_name(), "profiles": rows}
output = output_root / "unarmed_layer_masks.json"
if output.exists():
    if json.loads(output.read_text(encoding="utf-8")) != payload:
        raise RuntimeError("Existing Lyra layer masks differ: " + str(output))
else:
    output.write_text(json.dumps(payload, indent=2), encoding="utf-8")
unreal.log("LYRA_UNARMED_MASK_OK profiles=2 targetBones=" + str(len(target_bones)))
