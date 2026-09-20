"""Native V4 gait functions and continuous CMC -> Character parameter updates. No asset saves."""
import json
import math
import os
import tempfile
from pathlib import Path
import unreal

SOURCE = "/Game/AdvancedLocomotionV4/Blueprints/CharacterLogic/ALS_Base_CharacterBP.ALS_Base_CharacterBP"
output = Path(os.environ["ALS_MOVEMENT_RUNTIME_OUTPUT"])
if not output.is_absolute():
    raise ValueError("ALS_MOVEMENT_RUNTIME_OUTPUT must be absolute")


def native_text(obj):
    with tempfile.TemporaryDirectory(prefix="als-movement-runtime-") as directory:
        task = unreal.AssetExportTask()
        task.object = obj
        task.exporter = unreal.ObjectExporterT3D()
        task.filename = str(Path(directory) / "object.t3d")
        task.automated = True
        task.prompt = False
        if not unreal.Exporter.run_asset_export_task(task):
            raise RuntimeError("Native export failed: " + obj.get_path_name())
        data = Path(task.filename).read_bytes()
        return data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")


def vec(v):
    return [v.x, v.y, v.z]


system = unreal.get_default_object(unreal.SystemLibrary)


def assign(obj, kind, name, value):
    system.call_method("Set" + kind + "PropertyByName", args=(obj, name, value))
    actual = obj.get_editor_property(name)
    if kind == "Byte":
        actual = actual.value
    if actual != value:
        raise RuntimeError("Native input assignment failed: " + name)


def values(component):
    return {key: component.get_editor_property(key) for key in (
        "max_walk_speed", "max_walk_speed_crouched", "max_acceleration", "braking_deceleration_walking", "ground_friction")}


cls = unreal.load_class(None, SOURCE + "_C")
defaults = unreal.get_default_object(cls)
default_component = defaults.get_editor_property("CharacterMovement")
settings = {key: default_component.get_editor_property(key) for key in (
    "tick_before_owner", "air_control", "air_control_boost_multiplier", "air_control_boost_velocity_threshold",
    "falling_lateral_friction", "braking_deceleration_falling")}
graphs = [{"name": name, "path": SOURCE + ":" + name, "nativeText": native_text(unreal.load_object(None, SOURCE + ":" + name))}
          for name in ("EventGraph", "GetAllowedGait", "CanSprint")]
subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
actor = subsystem.spawn_actor_from_class(cls, unreal.Vector(0, 0, -100000), transient=True)
if actor is None:
    raise RuntimeError("Cannot spawn transient native ALS character")
gaits = []
traces = []
try:
    component = actor.get_editor_property("CharacterMovement")
    component.set_updated_component(actor.get_editor_property("CapsuleComponent"))
    component.set_movement_mode(unreal.MovementMode.MOVE_WALKING)
    actor.call_method("SetMovementModel")
    for mode in (0, 1, 2):
        assign(actor, "Byte", "RotationMode", mode)
        for stance in (0, 1):
            assign(actor, "Byte", "Stance", stance)
            for desired in (0, 1, 2):
                assign(actor, "Byte", "DesiredGait", desired)
                for amount in (0, .5, .9, .900001, 1):
                    assign(actor, "Double", "MovementInputAmount", amount)
                    for has_input in (False, True):
                        assign(actor, "Bool", "HasMovementInput", has_input)
                        for angle in (-180, -50, -49.999, 0, 49.999, 50, 180):
                            direction = unreal.Vector(math.cos(math.radians(angle)), math.sin(math.radians(angle)), 0)
                            if not unreal.AlsAnimationGraphLibrary.set_movement_probe_input(component, direction * 1500, 1):
                                raise RuntimeError("Invalid native movement probe")
                            allowed = actor.call_method("GetAllowedGait")
                            input_rotation = unreal.get_default_object(unreal.MathLibrary).call_method("Conv_VectorToRotator", args=(direction * 1500,))
                            can_sprint = actor.call_method("CanSprint")
                            gaits.append({"mode": mode, "stance": stance, "desired": desired, "amount": amount,
                                          "hasInput": has_input, "angle": angle, "inputYaw": input_rotation.yaw, "controlYaw": actor.get_control_rotation().yaw,
                                          "allowed": allowed.value, "canSprint": can_sprint})
    for hz in (30, 60, 120):
        if not subsystem.destroy_actor(actor):
            raise RuntimeError("Failed to retire previous native scenario")
        actor = subsystem.spawn_actor_from_class(cls, unreal.Vector(0, 0, -100000), transient=True)
        component = actor.get_editor_property("CharacterMovement")
        component.set_updated_component(actor.get_editor_property("CapsuleComponent"))
        component.set_movement_mode(unreal.MovementMode.MOVE_WALKING)
        actor.call_method("SetMovementModel")
        for key, value in values(default_component).items():
            component.set_editor_property(key, value)
        component.set_editor_property("velocity", unreal.Vector())
        initial = values(component)
        rows = []
        for frame in range(hz * 4):
            seconds = frame / hz
            mode = 2 if seconds >= 3 else 1
            stance = 1 if seconds >= 3.5 else 0
            desired = 0 if 2.5 <= seconds < 3 else 2
            amount = 0 if 2 <= seconds < 2.5 else 1
            angle = 0 if seconds < 1 else 90 if seconds < 1.5 else -90
            assign(actor, "Byte", "RotationMode", mode)
            assign(actor, "Byte", "Stance", stance)
            assign(actor, "Byte", "DesiredGait", desired)
            assign(actor, "Bool", "bIsCrouched", bool(stance))
            before = values(component)
            old_velocity = vec(component.get_editor_property("velocity"))
            acceleration = unreal.Vector(math.cos(math.radians(angle)), math.sin(math.radians(angle)), 0) * (amount * before["max_acceleration"])
            if not unreal.AlsAnimationGraphLibrary.set_movement_probe_input(component, acceleration, amount):
                raise RuntimeError("Invalid native movement probe")
            component.calc_velocity(1 / hz, before["ground_friction"], False, before["braking_deceleration_walking"])
            velocity = component.get_editor_property("velocity")
            # Character's SetEssentialValues reads the CMC result before
            # UpdateCharacterMovement replaces the dynamic parameters.
            assign(actor, "Double", "Speed", math.sqrt(velocity.x * velocity.x + velocity.y * velocity.y))
            assign(actor, "Double", "MovementInputAmount", amount)
            assign(actor, "Bool", "HasMovementInput", amount > 0)
            allowed = actor.call_method("GetAllowedGait")
            actor.call_method("UpdateCharacterMovement")
            if component.get_editor_property("max_acceleration") <= 0:
                raise RuntimeError(f"Original movement graph produced invalid settings at {hz} Hz frame {frame}")
            rows.append({"frame": frame, "mode": mode, "stance": stance, "desired": desired, "amount": amount,
                         "angle": angle, "before": before, "oldVelocity": old_velocity, "acceleration": vec(acceleration),
                         "velocity": vec(velocity), "after": values(component), "allowed": allowed.value})
        traces.append({"hz": hz, "initial": initial, "frames": rows})
finally:
    if not subsystem.destroy_actor(actor):
        raise RuntimeError("Failed to destroy native movement probe")

payload = {"schemaVersion": 1, "source": SOURCE, "engine": unreal.SystemLibrary.get_engine_version(),
           "scope": "Actual CMC CalcVelocity followed by original Character Blueprint movement functions; controlled grounded inputs, no collision or automatic world tick",
           "settings": settings, "initial": values(default_component), "graphs": graphs, "gaitVerification": gaits, "traces": traces}
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
unreal.log(f"ALS_MOVEMENT_RUNTIME_OK gait_cases={len(gaits)} frames={sum(len(t['frames']) for t in traces)} assets_saved=0")
if os.environ.get("ALS_MOVEMENT_RUNTIME_QUIT") == "1":
    _ticks = 0
    _handle = None

    def _quit_later(_delta):
        global _ticks
        _ticks += 1
        if _ticks >= 3:
            unreal.unregister_slate_post_tick_callback(_handle)
            unreal.SystemLibrary.quit_editor()

    _handle = unreal.register_slate_post_tick_callback(_quit_later)
