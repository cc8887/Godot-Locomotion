"""Export an ALS-bound Lyra retarget sequence for the Godot import pipeline."""

import hashlib
import json
import os
from pathlib import Path

__repository_root = Path(__file__).resolve().parents[2]

import unreal


SOURCE = os.environ.get("LYRA_RETARGET_ASSET",
                        "/Game/GodotLyraRetarget/Unarmed/LY_MM_Unarmed_Jog_Left")
OUTPUT = Path(os.environ.get("LYRA_RETARGET_FBX",
                          str(__repository_root / 'assets/generated/lyra_als/animations/LY_MM_Unarmed_Jog_Left.fbx')))
EXPECTED_SKELETON = (
    "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/"
    "ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton"
)

if not OUTPUT.is_absolute() or OUTPUT.exists():
    raise ValueError("Output must be a fresh absolute path: " + str(OUTPUT))
animation = unreal.load_asset(SOURCE)
if not isinstance(animation, unreal.AnimSequence):
    raise RuntimeError("Missing retargeted animation: " + SOURCE)
if animation.get_editor_property("skeleton").get_path_name() != EXPECTED_SKELETON:
    raise RuntimeError("Animation is not bound to the ALS mannequin skeleton")

OUTPUT.parent.mkdir(parents=True, exist_ok=True)
options = unreal.FbxExportOption()
options.set_editor_property("fbx_export_compatibility", unreal.FbxExportCompatibility.FBX_2020)
options.set_editor_property("ascii", True)
options.set_editor_property("force_front_x_axis", True)
options.set_editor_property("map_skeletal_motion_to_root", False)

task = unreal.AssetExportTask()
task.set_editor_property("object", animation)
task.set_editor_property("exporter", unreal.AnimSequenceExporterFBX())
task.set_editor_property("filename", str(OUTPUT))
task.set_editor_property("options", options)
task.set_editor_property("automated", True)
task.set_editor_property("prompt", False)
task.set_editor_property("replace_identical", False)
task.set_editor_property("use_file_archive", True)
if not unreal.Exporter.run_asset_export_task(task) or not OUTPUT.is_file():
    raise RuntimeError("FBX export failed: " + str(task.get_editor_property("errors")))

metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(animation))
digest = hashlib.sha256(OUTPUT.read_bytes()).hexdigest()
manifest = OUTPUT.with_suffix(".source.json")
manifest.write_text(json.dumps({
    "schemaVersion": 1,
    "source": animation.get_path_name(),
    "skeleton": EXPECTED_SKELETON,
    "fbx": OUTPUT.name,
    "fbxSha256": digest,
    "metadata": metadata,
}, indent=2), encoding="utf-8")
unreal.log("LYRA_ALS_FBX_EXPORT_OK source=" + animation.get_path_name() +
           " bytes=" + str(OUTPUT.stat().st_size) + " sha256=" + digest)
