"""Retarget a Kimodo SOMA BVH through Mocara and export a Manny animation FBX.

Run with UnrealEditor-Cmd.exe -run=pythonscript -Script=<this file> and set
KIMODO_BVH, KIMODO_TARGET_MESH, KIMODO_ASSET_PATH, and KIMODO_FBX_OUT.
Set KIMODO_REUSE_ASSET=1 only when exporting an already saved target animation.
"""

import hashlib
import json
import os
from pathlib import Path

import unreal


def required_file(name):
    path = Path(os.environ[name])
    if not path.is_absolute() or not path.is_file():
        raise ValueError(name + " must name an existing absolute file")
    return path


bvh = required_file("KIMODO_BVH")
output = Path(os.environ["KIMODO_FBX_OUT"])
asset_path = os.environ["KIMODO_ASSET_PATH"]
target_path = os.environ["KIMODO_TARGET_MESH"]
if not output.is_absolute() or output.exists():
    raise ValueError("KIMODO_FBX_OUT must be a fresh absolute path")
if not asset_path.startswith("/Game/") or "." in asset_path:
    raise ValueError("KIMODO_ASSET_PATH must be a /Game package path")

target_mesh = unreal.load_asset(target_path)
if not isinstance(target_mesh, unreal.SkeletalMesh):
    raise RuntimeError("Missing target Skeletal Mesh: " + target_path)
target_skeleton = target_mesh.get_editor_property("skeleton")
if target_skeleton is None:
    raise RuntimeError("Target mesh has no Skeleton: " + target_path)

reuse = os.environ.get("KIMODO_REUSE_ASSET") == "1"
exists = unreal.EditorAssetLibrary.does_asset_exist(asset_path)
if reuse != exists:
    raise ValueError("KIMODO_REUSE_ASSET=1 requires an existing target animation; "
                     "otherwise KIMODO_ASSET_PATH must be new")
if reuse:
    animation = unreal.load_asset(asset_path)
else:
    verify_dir = "/Game/Mocara/Verify"
    before = set(unreal.EditorAssetLibrary.list_assets(verify_dir, recursive=False, include_folder=False))
    if any(path.startswith(verify_dir + "/AS_RtVerify_") for path in before):
        raise RuntimeError("Mocara diagnostic output already exists; use a clean project")

    # Mocara runs its importer, IK setup, batch retarget, and motion-basis correction.
    command = 'Mocara.VerifyRetarget "' + str(bvh) + '" ' + target_path
    unreal.log("KIMODO_MOCARA_START " + command)
    unreal.SystemLibrary.execute_console_command(None, command)

    after = set(unreal.EditorAssetLibrary.list_assets(verify_dir, recursive=False, include_folder=False))
    candidates = []
    for path in after - before:
        if path.startswith(verify_dir + "/AS_RtVerify_"):
            asset = unreal.load_asset(path)
            if isinstance(asset, unreal.AnimSequence):
                candidates.append((path, asset))
    if len(candidates) != 1:
        raise RuntimeError("Mocara produced " + str(len(candidates)) + " retargeted animations")
    source_path, source_animation = candidates[0]
    if source_animation.get_editor_property("skeleton") != target_skeleton:
        raise RuntimeError("Mocara output is not bound to the target Skeleton")

    directory = asset_path.rsplit("/", 1)[0]
    unreal.EditorAssetLibrary.make_directory(directory)
    animation = unreal.EditorAssetLibrary.duplicate_asset(source_path, asset_path)
    if not isinstance(animation, unreal.AnimSequence):
        raise RuntimeError("Could not duplicate the retargeted animation")
    if not unreal.EditorAssetLibrary.save_loaded_asset(animation, only_if_is_dirty=False):
        raise RuntimeError("Could not save the retargeted animation")
if not isinstance(animation, unreal.AnimSequence):
    raise RuntimeError("Saved target asset is not an AnimSequence")
if animation.get_editor_property("skeleton") != target_skeleton:
    raise RuntimeError("Saved target asset is not bound to the target Skeleton")

options = unreal.FbxExportOption()
options.set_editor_property("fbx_export_compatibility", unreal.FbxExportCompatibility.FBX_2020)
options.set_editor_property("ascii", True)
options.set_editor_property("force_front_x_axis", False)
options.set_editor_property("map_skeletal_motion_to_root", False)
task = unreal.AssetExportTask()
task.set_editor_property("object", animation)
task.set_editor_property("exporter", unreal.AnimSequenceExporterFBX())
task.set_editor_property("filename", str(output))
task.set_editor_property("options", options)
task.set_editor_property("automated", True)
task.set_editor_property("prompt", False)
task.set_editor_property("replace_identical", False)
task.set_editor_property("use_file_archive", True)
output.parent.mkdir(parents=True, exist_ok=True)
if not unreal.Exporter.run_asset_export_task(task) or not output.is_file():
    raise RuntimeError("FBX export failed: " + str(task.get_editor_property("errors")))

manifest = {
    "schemaVersion": 1,
    "reusedAnimation": reuse,
    "sourceBvh": str(bvh),
    "sourceBvhSha256": hashlib.sha256(bvh.read_bytes()).hexdigest(),
    "targetMesh": target_mesh.get_path_name(),
    "targetSkeleton": target_skeleton.get_path_name(),
    "animation": animation.get_path_name(),
    "fbx": str(output),
    "fbxSha256": hashlib.sha256(output.read_bytes()).hexdigest(),
}
output.with_suffix(".source.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
unreal.log("KIMODO_MOCARA_EXPORT_OK animation=" + manifest["animation"] +
           " fbx_sha256=" + manifest["fbxSha256"])
