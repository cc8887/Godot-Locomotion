"""Original V4 CDO and real CMC based movement; transient Editor actors, no saves."""
import json
import math
import os
from pathlib import Path
import unreal

SOURCE = "/Game/AdvancedLocomotionV4/Blueprints/CharacterLogic/ALS_Base_CharacterBP.ALS_Base_CharacterBP"
output = Path(os.environ["ALS_BASED_MOVEMENT_OUTPUT"])
if not output.is_absolute():
    raise ValueError("ALS_BASED_MOVEMENT_OUTPUT must be absolute")
subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
cls = unreal.load_class(None, SOURCE + "_C")
defaults = unreal.get_default_object(cls)
component_defaults = defaults.get_editor_property("CharacterMovement")


def settings(actor, component):
    return {
        "character": {key: actor.get_editor_property(key) for key in (
            "use_controller_rotation_pitch", "use_controller_rotation_yaw", "use_controller_rotation_roll")},
        "movement": {key: component.get_editor_property(key) for key in (
            "ignore_base_rotation", "orient_rotation_to_movement", "use_controller_desired_rotation",
            "impart_base_velocity_x", "impart_base_velocity_y", "impart_base_velocity_z", "impart_base_angular_velocity")},
    }


def vec(v):
    return [v.x, v.y, v.z]


traces = []
cube = unreal.load_asset("/Engine/BasicShapes/Cube.Cube")
for hz, controller_enabled, control in ((30, True, False), (60, True, False), (120, True, False),
                                       (60, False, False), (60, True, True)):
    actors = []
    try:
        base_actor = unreal.AlsAnimationGraphLibrary.spawn_movement_probe_actor(world, unreal.StaticMeshActor, unreal.Vector(0, 0, -100000))
        actors.append(base_actor)
        base = base_actor.static_mesh_component
        base.set_mobility(unreal.ComponentMobility.MOVABLE)
        base.set_static_mesh(cube)
        base_actor.set_actor_scale3d(unreal.Vector(100, 100, 1))
        actor = unreal.AlsAnimationGraphLibrary.spawn_movement_probe_actor(world, cls, unreal.Vector(0, 200, -99850))
        actors.append(actor)
        component = actor.get_editor_property("CharacterMovement")
        capsule = actor.get_editor_property("CapsuleComponent")
        component.set_updated_component(capsule)
        component.set_movement_mode(unreal.MovementMode.MOVE_WALKING)
        component.set_editor_property("velocity", unreal.Vector())
        actor.set_actor_location(unreal.Vector(0, 200, -99950 + capsule.get_scaled_capsule_half_height()), False, True)
        controller = None
        if controller_enabled:
            controller = unreal.AlsAnimationGraphLibrary.spawn_movement_probe_actor(world, unreal.PlayerController, unreal.Vector(0, 0, -100000))
            actors.append(controller)
            controller.possess(actor)
            if actor.get_controller() != controller:
                raise RuntimeError("Native controller did not possess the probe")
            controller.set_control_rotation(unreal.Rotator())
        if control:
            actor.set_editor_property("use_controller_rotation_yaw", True)
            component.set_editor_property("ignore_base_rotation", False)
        initial = settings(actor, component)
        if not unreal.AlsAnimationGraphLibrary.advance_movement_probe_base(component, base, 1 / hz, True):
            raise RuntimeError("Native base initialization failed")
        rows = []
        for frame in range(1, hz * 6 + 1):
            # Rotate a real movable body without injecting any character rotation.
            base_actor.set_actor_rotation(unreal.Rotator(yaw=-.35 * (180 / math.pi) * frame / hz), False)
            if not unreal.AlsAnimationGraphLibrary.advance_movement_probe_base(component, base, 1 / hz, False):
                raise RuntimeError("Native based movement lost the base")
            rows.append({"frame": frame, "location": vec(actor.get_actor_location()),
                         "actorYaw": actor.get_actor_rotation().yaw, "controlYaw": actor.get_control_rotation().yaw,
                         "baseYaw": base_actor.get_actor_rotation().yaw, "velocity": vec(actor.get_velocity())})
        traces.append({"hz": hz, "controller": controller_enabled, "positiveControl": control,
                       "settings": initial, "frames": rows})
    finally:
        for actor in reversed(actors):
            if actor is not None:
                actor.destroy_actor()

payload = {"schemaVersion": 1, "source": SOURCE, "engine": unreal.SystemLibrary.get_engine_version(),
           "scope": "Original Character CDO and actual CMC UpdateBasedMovement/SaveBaseLocation, controlled rotating base; no Character Tick, animation Tick or full world simulation",
           "defaults": settings(defaults, component_defaults), "traces": traces}
for trace in traces:
    last = trace["frames"][-1]
    if abs(last["baseYaw"]) < 100 or math.hypot(last["location"][0], last["location"][1] - 200) < 100:
        raise RuntimeError("Probe did not exercise actual rotating-base transport")
    if trace["positiveControl"] and (abs(last["actorYaw"] - last["baseYaw"]) > .001 or
                                     abs(last["controlYaw"] - last["baseYaw"]) > .001):
        raise RuntimeError("Positive rotation control did not follow the native base")
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
unreal.log(f"ALS_BASED_MOVEMENT_OK traces={len(traces)} frames={sum(len(t['frames']) for t in traces)} assets_saved=0")
if os.environ.get("ALS_BASED_MOVEMENT_QUIT") == "1":
    _ticks = 0
    _handle = None

    def _quit_later(_delta):
        global _ticks
        _ticks += 1
        if _ticks >= 3:
            unreal.unregister_slate_post_tick_callback(_handle)
            unreal.SystemLibrary.quit_editor()

    _handle = unreal.register_slate_post_tick_callback(_quit_later)
