"""Record item AimOffset samples and recovery additive without saving UE assets."""

import hashlib
import json
import os
from pathlib import Path

import unreal


root = Path(os.environ["LYRA_OUTPUT_ROOT"])
if not root.is_absolute() or not root.is_dir():
    raise ValueError("LYRA_OUTPUT_ROOT must be an existing absolute directory")
inventory = json.loads((root / "linked_layer_inventory.json").read_text(encoding="utf-8"))
profile_name = os.environ.get("LYRA_SPECIAL_PROFILE", "pistol")
if profile_name not in ("pistol", "rifle"):
    raise ValueError("Unsupported item special profile: " + profile_name)
profile = inventory["classes"][profile_name]["assets"]
unarmed = inventory["classes"]["unarmed"]["assets"]
aim_path = profile["IdleAimOffset"]
jump_path = profile["Jump_RecoveryAdditive"]
if (inventory["schemaVersion"] != 1 or not aim_path or not jump_path or
        profile["RelaxedAimOffset"] != unarmed["RelaxedAimOffset"] or
        aim_path == profile["RelaxedAimOffset"]):
    raise RuntimeError(profile_name + " special CDO bindings changed")

space = unreal.load_asset(aim_path)
jump = unreal.load_asset(jump_path)
if (not isinstance(space, unreal.AimOffsetBlendSpace) or
        not isinstance(jump, unreal.AnimSequence) or
        space.get_path_name() != aim_path or jump.get_path_name() != jump_path):
    raise RuntimeError(profile_name + " AimOffset or recovery additive is missing")
source_skeleton = "/Game/Characters/Heroes/Mannequin/Meshes/SK_Mannequin.SK_Mannequin"
if (space.get_editor_property("skeleton").get_path_name() != source_skeleton or
        jump.get_editor_property("skeleton").get_path_name() != source_skeleton):
    raise RuntimeError(profile_name + " special assets use a different skeleton")
content = Path(unreal.Paths.project_content_dir())


def digest(path):
    filename = content / (path.removeprefix("/Game/").split(".")[0] + ".uasset")
    if not path.startswith("/Game/") or not filename.is_file():
        raise RuntimeError("Missing " + profile_name + " package: " + path)
    return hashlib.sha256(filename.read_bytes()).hexdigest()


axes = []
for parameter in space.get_editor_property("blend_parameters")[:2]:
    axes.append({"name": parameter.get_editor_property("display_name"),
                 "min": parameter.get_editor_property("min"),
                 "max": parameter.get_editor_property("max"),
                 "gridDivisions": parameter.get_editor_property("grid_num"),
                 "snapToGrid": parameter.get_editor_property("snap_to_grid"),
                 "wrapInput": parameter.get_editor_property("wrap_input")})
if len(axes) != 2 or any(axis["max"] <= axis["min"] for axis in axes):
    raise RuntimeError("Invalid " + profile_name + " AimOffset axes")

samples = []
for index, sample in enumerate(space.get_editor_property("sample_data")):
    sequence = sample.get_editor_property("animation")
    value = sample.get_editor_property("sample_value")
    if not isinstance(sequence, unreal.AnimSequence) or sequence.get_editor_property(
            "skeleton").get_path_name() != source_skeleton:
        raise RuntimeError("Invalid " + profile_name + " AimOffset sample: " + str(index))
    samples.append({"index": index, "source": sequence.get_path_name(),
                    "sourceUassetSha256": digest(sequence.get_path_name()),
                    "point": [value.x, value.y],
                    "metadata": json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(
                        sequence))})
if len(samples) < 3 or len({row["source"] for row in samples}) != len(samples):
    raise RuntimeError("Incomplete " + profile_name + " AimOffset samples")
jump_metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(jump))
if jump_metadata["additiveType"] != "AAT_LocalSpaceBase":
    raise RuntimeError(profile_name + " recovery additive type changed")
payload = {"schemaVersion": 1, "sourceSkeleton": source_skeleton,
           "aimOffset": {"source": aim_path, "sourceUassetSha256": digest(aim_path),
                         "axes": axes, "samples": samples},
           "jumpRecovery": {"source": jump_path, "sourceUassetSha256": digest(jump_path),
                            "metadata": jump_metadata}}
output = root / (profile_name + "_special_inventory.json")
if output.exists():
    if json.loads(output.read_text(encoding="utf-8")) != payload:
        raise RuntimeError("Existing " + profile_name + " special inventory differs: " + str(output))
else:
    output.write_text(json.dumps(payload, indent=2), encoding="utf-8")
unreal.log("LYRA_ITEM_SPECIAL_OK profile=" + profile_name + " samples=" + str(len(samples)) +
           " baseFrame=" + str(jump_metadata["baseFrame"]) +
           " assets_saved=0")
