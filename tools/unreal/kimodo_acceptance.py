"""Live UE acceptance fixture for Kimodo root motion and virtual bones.

Import in the Editor Python console. Call prepare(), setup(), start PIE,
run(60), stop PIE, and cleanup(). All test assets use a separate package.
"""

import json
import math
import re
import tempfile
from pathlib import Path

import unreal


OUT = Path(__file__).resolve().parents[2] / "assets/generated/kimodo_ue/acceptance"
PACKAGE = "/Game/KimodoMocara/AcceptanceV2"
MESH = "/Game/Characters/UE5_Mannequins/Meshes/SKM_Manny"
NATIVE = "/Game/KimodoMocara/AS_KimodoWalkStop_Manny_RootIK_Final"
READBACK = "/Game/KimodoMocara/VisualReadback/AS_KimodoWalkStop_Manny_RootIK_FBX_Visual"
VB_MESH = PACKAGE + "/SKM_Manny_VB_Imported"
PREFIX = "Kimodo_Acceptance_"
VB_PAIRS = (("root", "foot_l"), ("root", "foot_r"),
            ("pelvis", "hand_l"), ("hand_r", "hand_l"))
BONES = ("root", "pelvis", "foot_l", "ball_l", "foot_r", "ball_r",
         "hand_l", "hand_r", "ik_foot_l", "ik_foot_r")
_handle = None
_state = None


def vector(v):
    return [v.x, v.y, v.z]


def transform(t):
    return {"p": vector(t.translation),
            "q": [t.rotation.x, t.rotation.y, t.rotation.z, t.rotation.w]}


def write(name, value):
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / name).write_text(json.dumps(value, indent=2), encoding="utf-8")


def duplicate(source, name):
    path = PACKAGE + "/" + name
    if unreal.EditorAssetLibrary.does_asset_exist(path):
        return unreal.load_asset(path)
    result = unreal.EditorAssetLibrary.duplicate_asset(source, path)
    if not result:
        raise RuntimeError("Could not duplicate " + source)
    return result


def virtual_names(clip):
    with tempfile.TemporaryDirectory(prefix="kimodo-vb-") as directory:
        task = unreal.AssetExportTask()
        task.object = clip.get_editor_property("skeleton")
        task.exporter = unreal.ObjectExporterT3D()
        task.filename = str(Path(directory) / "skeleton.t3d")
        task.automated = True
        task.prompt = False
        if not unreal.Exporter.run_asset_export_task(task):
            raise RuntimeError("Skeleton inspection failed")
        data = Path(task.filename).read_bytes()
        text = data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")
    return re.findall(r'VirtualBoneName="([^"]+)"', text)


def prepare():
    mesh = unreal.load_asset(MESH)
    original_skeleton = mesh.get_editor_property("skeleton")
    root_clip = duplicate(READBACK, "AS_FBX_RootMotion")
    unreal.AnimationLibrary.set_root_motion_enabled(root_clip, True)
    skeleton = duplicate(original_skeleton.get_path_name(), "SK_Manny_VB")
    unreal.EditorAssetLibrary.save_loaded_asset(skeleton, only_if_is_dirty=False)
    if not unreal.EditorAssetLibrary.does_asset_exist(VB_MESH):
        OUT.mkdir(parents=True, exist_ok=True)
        mesh_fbx = OUT / "manny_reference.fbx"
        export = unreal.AssetExportTask()
        export.object = mesh
        export.exporter = unreal.SkeletalMeshExporterFBX()
        export.filename = str(mesh_fbx)
        export.options = unreal.FbxExportOption()
        export.automated = True
        export.prompt = False
        export.replace_identical = True
        if not unreal.Exporter.run_asset_export_task(export):
            raise RuntimeError("Manny mesh export failed")
        ui = unreal.FbxImportUI()
        ui.automated_import_should_detect_type = False
        ui.mesh_type_to_import = unreal.FBXImportType.FBXIT_SKELETAL_MESH
        ui.import_as_skeletal = True
        ui.import_mesh = True
        ui.import_animations = False
        ui.import_materials = False
        ui.import_textures = False
        ui.create_physics_asset = False
        ui.skeleton = skeleton
        task = unreal.AssetImportTask()
        task.filename = str(mesh_fbx)
        task.destination_path = PACKAGE
        task.destination_name = VB_MESH.rsplit("/", 1)[1]
        task.options = ui
        task.automated = True
        task.save = True
        unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([task])
    vb_mesh = unreal.load_asset(VB_MESH)
    if not vb_mesh or vb_mesh.get_editor_property("skeleton") != skeleton:
        raise RuntimeError("Manny mesh did not bind to isolated Skeleton")
    vb_mesh.set_editor_property("materials", mesh.get_editor_property("materials"))
    unreal.AnimationLibrary.set_skeleton_preview_mesh(skeleton, vb_mesh)
    unreal.EditorAssetLibrary.save_loaded_asset(skeleton, only_if_is_dirty=False)
    # Define virtual bones before importing animation data. Updating the hierarchy
    # of an already populated Interchange data model asserted in UE 5.8.1.
    seed_path = PACKAGE + "/AS_VB_Definitions"
    seed = unreal.load_asset(seed_path) if unreal.EditorAssetLibrary.does_asset_exist(seed_path) else None
    if seed is None:
        factory = unreal.AnimSequenceFactory()
        factory.target_skeleton = skeleton
        seed = unreal.AssetToolsHelpers.get_asset_tools().create_asset(
            "AS_VB_Definitions", PACKAGE, unreal.AnimSequence, factory)
    existing = set(virtual_names(seed))
    for source, target in VB_PAIRS:
        if "VB " + source + "_" + target not in existing:
            unreal.AnimationLibrary.add_virtual_bone(seed, source, target)
    unreal.EditorAssetLibrary.save_loaded_asset(skeleton, only_if_is_dirty=False)
    unreal.EditorAssetLibrary.save_loaded_asset(seed, only_if_is_dirty=False)
    vb_path = PACKAGE + "/AS_FBX_RootMotion_VB"
    if not unreal.EditorAssetLibrary.does_asset_exist(vb_path):
        options = unreal.FbxImportUI()
        options.mesh_type_to_import = unreal.FBXImportType.FBXIT_ANIMATION
        options.import_as_skeletal = True
        options.import_mesh = False
        options.import_animations = True
        options.skeleton = skeleton
        task = unreal.AssetImportTask()
        task.filename = str(OUT.parent / "kimodo_walk_stop_manny_root_ik_final.fbx")
        task.destination_path = PACKAGE
        task.destination_name = "AS_FBX_RootMotion_VB"
        task.options = options
        task.automated = True
        task.save = True
        unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([task])
    vb_clip = unreal.load_asset(vb_path)
    if not vb_clip or vb_clip.get_editor_property("skeleton") != skeleton:
        raise RuntimeError("VB clip did not import on isolated Skeleton")
    unreal.AnimationLibrary.set_root_motion_enabled(vb_clip, True)
    vb_clip.set_preview_skeletal_mesh(vb_mesh)
    for asset in (root_clip, skeleton, vb_mesh, vb_clip):
        if not unreal.EditorAssetLibrary.save_loaded_asset(asset, only_if_is_dirty=False):
            raise RuntimeError("Could not save " + asset.get_path_name())
    names = virtual_names(vb_clip)
    if len(names) != len(VB_PAIRS):
        raise RuntimeError("Unexpected virtual bone definitions " + str(names))
    result = {"rootClip": root_clip.get_path_name(), "vbClip": vb_clip.get_path_name(),
              "vbMesh": vb_mesh.get_path_name(), "vbSkeleton": skeleton.get_path_name(),
              "virtualBones": names,
              "originalVirtualBones": virtual_names(unreal.load_asset(NATIVE))}
    write("assets.json", result)
    unreal.log("KIMODO_ACCEPTANCE_PREPARED " + json.dumps(result))
    sample_assets()


def sample_assets():
    results = {}
    for key, clip_path, mesh_path in (
        ("native", NATIVE, MESH),
        ("fbx", PACKAGE + "/AS_FBX_RootMotion", MESH),
        ("virtual", PACKAGE + "/AS_FBX_RootMotion_VB", VB_MESH),
    ):
        clip = unreal.load_asset(clip_path)
        mesh = unreal.load_asset(mesh_path)
        vb_names = virtual_names(clip)
        options = unreal.AnimPoseEvaluationOptions()
        options.optional_skeletal_mesh = mesh
        options.evaluation_type = unreal.AnimDataEvalType.COMPRESSED
        options.should_retarget = True
        options.extract_root_motion = False
        options.incorporate_root_motion_into_pose = True
        samples = []
        for frame in range(121):
            pose = unreal.AnimPoseExtensions.get_anim_pose_at_time(clip, frame / 30.0, options)
            if not unreal.AnimPoseExtensions.is_valid(pose):
                raise RuntimeError("Invalid sample")
            samples.append({bone: transform(unreal.AnimPoseExtensions.get_bone_pose(
                pose, bone, unreal.AnimPoseSpaces.WORLD)) for bone in BONES + tuple(vb_names)})
        results[key] = {"clip": clip.get_path_name(), "mesh": mesh.get_path_name(),
                        "tracks": [str(n) for n in unreal.AnimationLibrary.get_animation_track_names(clip)],
                        "virtualBones": vb_names, "samples": samples}
    write("asset_poses.json", results)
    unreal.log("KIMODO_ACCEPTANCE_SAMPLED 3 clips x 121 frames")


def setup():
    actors = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    if any(a.get_actor_label().startswith(PREFIX) for a in actors.get_all_level_actors()):
        raise RuntimeError("Acceptance actors already exist; clean up first")
    floor = actors.spawn_actor_from_class(unreal.StaticMeshActor, unreal.Vector(0, 0, 9995))
    floor.set_actor_label(PREFIX + "Floor")
    floor.static_mesh_component.set_static_mesh(unreal.load_asset("/Engine/BasicShapes/Cube"))
    floor.set_actor_scale3d(unreal.Vector(12, 12, 0.1))
    floor.static_mesh_component.set_collision_profile_name("BlockAll")
    for key, y, mesh_path, clip_path in (
        ("native", -220, MESH, NATIVE),
        ("fbx", 0, MESH, PACKAGE + "/AS_FBX_RootMotion"),
        ("virtual", 220, VB_MESH, PACKAGE + "/AS_FBX_RootMotion_VB"),
    ):
        actor = actors.spawn_actor_from_class(unreal.Character, unreal.Vector(-200, y, 10098))
        if not actor:
            raise RuntimeError("Could not spawn Character")
        actor.set_actor_label(PREFIX + key)
        actor.capsule_component.set_capsule_size(34, 96)
        component = actor.mesh
        component.set_skeletal_mesh_asset(unreal.load_asset(mesh_path))
        component.set_relative_location(unreal.Vector(0, 0, -96), False, True)
        component.set_relative_rotation(unreal.Rotator(pitch=0, yaw=-90, roll=0), False, True)
        component.set_collision_profile_name("NoCollision")
        component.set_editor_property("visibility_based_anim_tick_option",
            unreal.VisibilityBasedAnimTickOption.ALWAYS_TICK_POSE_AND_REFRESH_BONES)
        component.override_animation_data(unreal.load_asset(clip_path), False, False, 0, 1)
        actor.character_movement.set_editor_property("run_physics_with_no_controller", True)
    unreal.log("KIMODO_ACCEPTANCE_SETUP 3 Characters and a floor")


def run(hz=60, pause_frames=()):
    global _handle, _state
    if _handle is not None:
        raise RuntimeError("A run is already active")
    world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_game_world()
    if not world:
        raise RuntimeError("Start PIE first")
    settings = world.get_world_settings()
    settings.set_editor_property("min_undilated_frame_time", 1.0 / hz)
    settings.set_editor_property("max_undilated_frame_time", 1.0 / hz)
    chars = {a.get_actor_label()[len(PREFIX):]: a
             for a in unreal.GameplayStatics.get_all_actors_of_class(world, unreal.Character)
             if a.get_actor_label().startswith(PREFIX)}
    if set(chars) != {"native", "fbx", "virtual"}:
        raise RuntimeError("Missing acceptance Characters")
    for actor in chars.values():
        actor.mesh.stop()
        actor.mesh.set_position(0, False)
        actor.mesh.get_anim_instance().set_root_motion_mode(unreal.RootMotionMode.ROOT_MOTION_FROM_EVERYTHING)
    _state = {"hz": hz, "world": world, "characters": chars,
              "warmup": 0, "rows": [], "lastTime": None, "starts": {},
              "pauseFrames": tuple(pause_frames)}
    _handle = unreal.register_slate_post_tick_callback(_tick)
    unreal.log("KIMODO_ACCEPTANCE_RUNNING " + str(hz))


def _tick(delta):
    global _handle
    try:
        s = _state
        now = unreal.GameplayStatics.get_time_seconds(s["world"])
        if now == s["lastTime"]:
            return
        s["lastTime"] = now
        if s["warmup"] < 15:
            s["warmup"] += 1
            if s["warmup"] == 15:
                for key, actor in s["characters"].items():
                    s["starts"][key] = vector(actor.get_actor_location())
                    actor.mesh.play(False)
                    actor.mesh.set_position(0, False)
            return
        row = {"worldTime": now, "characters": {}}
        for key, actor in s["characters"].items():
            component = actor.mesh
            vb = ["VB " + a + "_" + b for a, b in VB_PAIRS] if key == "virtual" else []
            row["characters"][key] = {
                "time": component.get_position(), "actor": vector(actor.get_actor_location()),
                "velocity": vector(actor.get_velocity()),
                "movementMode": str(actor.character_movement.movement_mode),
                "rootMode": str(component.get_anim_instance().get_editor_property("root_motion_mode")),
                "bonesWorld": {n: transform(component.get_socket_transform(n, unreal.RelativeTransformSpace.RTS_WORLD))
                               for n in BONES + tuple(vb)},
                "rootComponent": transform(component.get_socket_transform("root", unreal.RelativeTransformSpace.RTS_COMPONENT)),
            }
        s["rows"].append(row)
        if len(s["rows"]) in s["pauseFrames"]:
            unreal.GameplayStatics.set_game_paused(s["world"], True)
            unreal.log("KIMODO_CAPTURE_READY " + str(len(s["rows"])))
        if len(s["rows"]) >= int(s["hz"] * 4.25):
            result = {"hz": s["hz"], "starts": s["starts"], "rows": s["rows"]}
            suffix = "_capture" if s["pauseFrames"] else ""
            write("runtime_" + str(s["hz"]) + suffix + ".json", result)
            unreal.unregister_slate_post_tick_callback(_handle)
            _handle = None
            unreal.log("KIMODO_ACCEPTANCE_RUN_DONE " + str(s["hz"]) + " rows=" + str(len(s["rows"])))
    except Exception as exc:
        if _handle is not None:
            unreal.unregister_slate_post_tick_callback(_handle)
            _handle = None
        unreal.log_error("KIMODO_ACCEPTANCE_RUN_FAILED " + repr(exc))


def cleanup():
    global _handle
    if _handle is not None:
        unreal.unregister_slate_post_tick_callback(_handle)
        _handle = None
    subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    actors = [a for a in subsystem.get_all_level_actors() if a.get_actor_label().startswith(PREFIX)]
    for actor in actors:
        if not subsystem.destroy_actor(actor):
            raise RuntimeError("Could not remove " + actor.get_actor_label())
    unreal.log("KIMODO_ACCEPTANCE_CLEANUP " + str(len(actors)))
