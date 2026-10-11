"""Persist the final Manny FBX readback for a rendered Editor inspection."""

import hashlib
import json
import os
from pathlib import Path

import unreal


fbx = Path(os.environ["KIMODO_FBX_OUT"])
asset_path = os.environ["KIMODO_ASSET_PATH"]
mesh_path = os.environ["KIMODO_TARGET_MESH"]
destination = "/Game/KimodoMocara/VisualReadback"
name = "AS_KimodoWalkStop_Manny_RootIK_FBX_Visual"
readback_path = destination + "/" + name

if not fbx.is_file() or not fbx.is_absolute():
    raise RuntimeError("Final FBX is missing")
record = json.loads(fbx.with_suffix(".source.json").read_text(encoding="utf-8"))
if record["fbxSha256"] != hashlib.sha256(fbx.read_bytes()).hexdigest():
    raise RuntimeError("Final FBX hash differs from its source record")
if record["animation"].rsplit(".", 1)[0] != asset_path or record["targetMesh"] != mesh_path:
    raise RuntimeError("FBX provenance does not identify the requested animation and mesh")
if unreal.EditorAssetLibrary.does_asset_exist(readback_path):
    raise RuntimeError("Visual readback asset already exists: " + readback_path)

original = unreal.load_asset(asset_path)
mesh = unreal.load_asset(mesh_path)
if not isinstance(original, unreal.AnimSequence) or not isinstance(mesh, unreal.SkeletalMesh):
    raise RuntimeError("Original Manny animation or mesh is missing")
skeleton = mesh.get_editor_property("skeleton")
if original.get_editor_property("skeleton") != skeleton:
    raise RuntimeError("Original animation does not use Manny's Skeleton")

ui = unreal.FbxImportUI()
ui.set_editor_property("mesh_type_to_import", unreal.FBXImportType.FBXIT_ANIMATION)
ui.set_editor_property("import_as_skeletal", True)
ui.set_editor_property("import_mesh", False)
ui.set_editor_property("import_animations", True)
ui.set_editor_property("skeleton", skeleton)
task = unreal.AssetImportTask()
task.set_editor_property("filename", str(fbx))
task.set_editor_property("destination_path", destination)
task.set_editor_property("destination_name", name)
task.set_editor_property("options", ui)
task.set_editor_property("automated", True)
task.set_editor_property("save", True)
unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([task])
paths = list(task.get_editor_property("imported_object_paths"))
if len(paths) != 1 or paths[0].rsplit(".", 1)[0] != readback_path:
    raise RuntimeError("FBX import did not create the expected asset: " + str(paths))
readback = unreal.load_asset(paths[0])
if not isinstance(readback, unreal.AnimSequence) or readback.get_editor_property("skeleton") != skeleton:
    raise RuntimeError("FBX import produced an incompatible animation")
if (unreal.AnimationLibrary.get_num_frames(readback) != unreal.AnimationLibrary.get_num_frames(original)
        or unreal.AnimationLibrary.get_num_keys(readback) != unreal.AnimationLibrary.get_num_keys(original)
        or abs(unreal.AnimationLibrary.get_sequence_length(readback)
               - unreal.AnimationLibrary.get_sequence_length(original)) > 1e-5):
    raise RuntimeError("FBX import changed animation timing")
if not unreal.EditorAssetLibrary.save_loaded_asset(readback):
    raise RuntimeError("Could not save FBX visual readback asset")
unreal.log("KIMODO_VISUAL_READBACK_OK " + json.dumps({
    "fbx": str(fbx),
    "fbxSha256": record["fbxSha256"],
    "readback": readback.get_path_name(),
    "targetMesh": mesh.get_path_name(),
    "targetSkeleton": skeleton.get_path_name(),
    "frames": unreal.AnimationLibrary.get_num_frames(readback),
    "keys": unreal.AnimationLibrary.get_num_keys(readback),
    "duration": unreal.AnimationLibrary.get_sequence_length(readback),
    "tracks": len(unreal.AnimationLibrary.get_animation_track_names(readback)),
}))
