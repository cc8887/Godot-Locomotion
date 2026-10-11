"""Remove only the temporary Manny actor used for visual verification."""

import unreal


actors = [actor for actor in unreal.EditorLevelLibrary.get_all_level_actors()
          if actor.get_actor_label() == "Kimodo_UE_Visual_Probe_FBX"]
if len(actors) != 1 or not isinstance(actors[0], unreal.SkeletalMeshActor):
    raise RuntimeError("Expected exactly one temporary Manny actor")
path = actors[0].get_path_name()
if not unreal.EditorLevelLibrary.destroy_actor(actors[0]):
    raise RuntimeError("Could not remove temporary Manny actor")
unreal.log("KIMODO_VISUAL_ACTOR_REMOVED " + path)
