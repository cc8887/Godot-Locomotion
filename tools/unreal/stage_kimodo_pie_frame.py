"""Hold a selected frame on the temporary Manny actor in PIE."""

import json

import unreal


def stage(frame):
    world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_game_world()
    if not world:
        raise RuntimeError("Start PIE before staging a Kimodo frame")
    actors = [actor for actor in unreal.GameplayStatics.get_all_actors_of_class(
        world, unreal.SkeletalMeshActor)
        if actor.get_actor_label() == "Kimodo_UE_Visual_Probe_FBX"]
    if len(actors) != 1:
        raise RuntimeError("Expected exactly one Manny actor in PIE")
    component = actors[0].get_editor_property("skeletal_mesh_component")
    asset = component.get_anim_instance().get_animation_asset()
    if not asset or not asset.get_name().endswith("_FBX_Visual"):
        raise RuntimeError("PIE Manny is not playing the FBX readback")
    if frame not in (0, 30, 60, 90, 120):
        raise ValueError("Unsupported comparison frame")
    component.play(False)
    component.get_anim_instance().set_play_rate(0.0)
    component.set_position(frame / 30.0, False)
    unreal.log("KIMODO_PIE_FRAME_STAGED " + json.dumps({
        "frame": frame,
        "position": component.get_position(),
        "asset": asset.get_path_name(),
    }))
