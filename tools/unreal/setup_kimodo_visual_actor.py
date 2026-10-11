"""Set up a removable Manny actor that plays the FBX readback in the Editor world."""

import json

import unreal


mesh_path = "/Game/Characters/UE5_Mannequins/Meshes/SKM_Manny"
animation_path = "/Game/KimodoMocara/VisualReadback/AS_KimodoWalkStop_Manny_RootIK_FBX_Visual"
label = "Kimodo_UE_Visual_Probe_FBX"
world = unreal.EditorLevelLibrary.get_editor_world()
if any(actor.get_actor_label() == label for actor in unreal.EditorLevelLibrary.get_all_level_actors()):
    raise RuntimeError("Kimodo visual probe already exists")
mesh = unreal.load_asset(mesh_path)
animation = unreal.load_asset(animation_path)
if not isinstance(mesh, unreal.SkeletalMesh) or not isinstance(animation, unreal.AnimSequence):
    raise RuntimeError("Manny mesh or FBX readback is missing")
if animation.get_editor_property("skeleton") != mesh.get_editor_property("skeleton"):
    raise RuntimeError("FBX readback cannot play on Manny's Skeleton")
actor = unreal.EditorLevelLibrary.spawn_actor_from_class(
    unreal.SkeletalMeshActor, unreal.Vector(0, 0, 0))
if not actor:
    raise RuntimeError("Could not spawn Manny visual probe")
actor.set_actor_label(label)
component = actor.get_editor_property("skeletal_mesh_component")
component.set_skeletal_mesh_asset(mesh)
component.set_animation_mode(unreal.AnimationMode.ANIMATION_SINGLE_NODE)
component.set_update_animation_in_editor(True)
component.override_animation_data(animation, True, True, 0.0, 1.0)
unreal.log("KIMODO_VISUAL_ACTOR_READY " + json.dumps({
    "actor": actor.get_path_name(),
    "component": component.get_path_name(),
    "mesh": mesh.get_path_name(),
    "animation": animation.get_path_name(),
    "mode": str(component.get_animation_mode()),
    "world": world.get_path_name(),
}))
