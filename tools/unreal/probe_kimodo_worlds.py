"""Read Kimodo Manny playback from both Editor and PIE worlds."""

import json

import unreal


label = "Kimodo_UE_Visual_Probe_FBX"
subsystem = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
results = {}
for name, world in (("editor", subsystem.get_editor_world()),
                    ("game", subsystem.get_game_world())):
    if not world:
        results[name] = None
        continue
    actors = unreal.GameplayStatics.get_all_actors_of_class(world, unreal.SkeletalMeshActor)
    matches = [actor for actor in actors if actor.get_actor_label() == label]
    if len(matches) != 1:
        results[name] = {"world": world.get_path_name(), "matchingActors": len(matches)}
        continue
    actor = matches[0]
    component = actor.get_editor_property("skeletal_mesh_component")
    instance = component.get_anim_instance()
    results[name] = {
        "world": world.get_path_name(),
        "actor": actor.get_path_name(),
        "tickEnabled": component.is_component_tick_enabled(),
        "animationMode": str(component.get_animation_mode()),
        "asset": instance.get_animation_asset().get_path_name()
        if instance and instance.get_animation_asset() else None,
        "position": component.get_position(),
        "playing": component.is_playing(),
        "bones": {
            bone: [round(getattr(component.get_socket_transform(
                bone, unreal.RelativeTransformSpace.RTS_COMPONENT).translation, axis), 4)
                for axis in ("x", "y", "z")]
            for bone in ("root", "pelvis", "foot_l", "foot_r", "hand_l", "hand_r")
        },
    }
unreal.log("KIMODO_WORLDS " + json.dumps(results))
