"""Read the CDO-bound Unarmed AimOffset and Jump Recovery additive assets."""

import hashlib
import json
import os
from pathlib import Path

import unreal


root = Path(os.environ["LYRA_OUTPUT_ROOT"])
if not root.is_absolute() or not root.is_dir():
    raise ValueError("The Lyra output directory must exist and be absolute")
inventory = json.loads((root / "linked_layer_inventory.json").read_text(encoding="utf-8"))
profile = inventory["classes"]["unarmed"]["assets"]
aim_path = profile["IdleAimOffset"]
jump_path = profile["Jump_RecoveryAdditive"]
if (inventory["schemaVersion"] != 1 or not aim_path or not jump_path or
        profile["RelaxedAimOffset"] != aim_path):
    raise RuntimeError("Unarmed special asset CDO bindings changed")

space = unreal.load_asset(aim_path)
jump = unreal.load_asset(jump_path)
if (not isinstance(space, unreal.AimOffsetBlendSpace) or
        not isinstance(jump, unreal.AnimSequence) or
        space.get_path_name() != aim_path or jump.get_path_name() != jump_path):
    raise RuntimeError("Missing Unarmed AimOffset or recovery additive")
source_skeleton = "/Game/Characters/Heroes/Mannequin/Meshes/SK_Mannequin.SK_Mannequin"
if (space.get_editor_property("skeleton").get_path_name() != source_skeleton or
        jump.get_editor_property("skeleton").get_path_name() != source_skeleton):
    raise RuntimeError("Unarmed special assets use a different skeleton")
content = Path(unreal.Paths.project_content_dir())


def digest(path):
    filename = content / (path.removeprefix("/Game/").split(".")[0] + ".uasset")
    if not path.startswith("/Game/") or not filename.is_file():
        raise RuntimeError("Missing Lyra package: " + path)
    return hashlib.sha256(filename.read_bytes()).hexdigest()


axes = []
for parameter in space.get_editor_property("blend_parameters")[:2]:
    axes.append({"name": parameter.get_editor_property("display_name"),
                 "min": parameter.get_editor_property("min"),
                 "max": parameter.get_editor_property("max"),
                 "gridDivisions": parameter.get_editor_property("grid_num"),
                 "snapToGrid": parameter.get_editor_property("snap_to_grid"),
                 "wrapInput": parameter.get_editor_property("wrap_input")})
if any(axis["max"] <= axis["min"] or axis["gridDivisions"] < 1 for axis in axes):
    raise RuntimeError("Invalid AimOffset axes")

samples = []
for index, sample in enumerate(space.get_editor_property("sample_data")):
    sequence = sample.get_editor_property("animation")
    value = sample.get_editor_property("sample_value")
    if not isinstance(sequence, unreal.AnimSequence) or sequence.get_editor_property(
            "skeleton").get_path_name() != source_skeleton:
        raise RuntimeError("Invalid AimOffset sample: " + str(index))
    samples.append({"index": index, "source": sequence.get_path_name(),
                    "sourceUassetSha256": digest(sequence.get_path_name()),
                    "point": [value.x, value.y],
                    "metadata": json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(
                        sequence))})
if len(samples) < 3 or len({row["source"] for row in samples}) < 3:
    raise RuntimeError("Incomplete Unarmed AimOffset sample grid")

jump_metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(jump))
if (jump_metadata["additiveType"] != "AAT_LocalSpaceBase" or
        jump_metadata["basePoseType"] != "ABPT_LocalAnimFrame" or
        jump_metadata["baseAsset"] is not None):
    raise RuntimeError("Jump Recovery additive base policy changed")
jump_poses = []
for fraction in (0, 0.5, 1):
    time = jump_metadata["sequencePlayLength"] * fraction
    pose = json.loads(unreal.AlsSourceAnimationLibrary.read_raw_animation_pose(
        jump, time, True, False, False))
    if not pose.get("evaluatedAdditive") or len(pose["names"]) != len(pose["pose"]):
        raise RuntimeError("Jump Recovery did not evaluate as additive")
    jump_poses.append(pose)

payload = {"schemaVersion": 1, "sourceSkeleton": source_skeleton,
           "aimOffset": {"source": aim_path, "sourceUassetSha256": digest(aim_path),
                         "axes": axes, "samples": samples,
                         "nativeTriangulation": None},
           "jumpRecovery": {"source": jump_path,
                            "sourceUassetSha256": digest(jump_path),
                            "metadata": jump_metadata, "evaluatedPoses": jump_poses}}
output = root / "unarmed_special_inventory.json"
if output.exists():
    if json.loads(output.read_text(encoding="utf-8")) != payload:
        raise RuntimeError("Existing Unarmed special inventory differs: " + str(output))
else:
    output.write_text(json.dumps(payload, indent=2), encoding="utf-8")
unreal.log("LYRA_UNARMED_SPECIAL_OK samples=" + str(len(samples)) +
           " unique=" + str(len({row["source"] for row in samples})) +
           " additiveBaseFrame=" + str(jump_metadata["baseFrame"]) +
           " assets_saved=0")
