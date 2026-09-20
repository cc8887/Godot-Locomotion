"""Exercise the real ALS UpdateAimingValues Blueprint on transient instances; never save assets."""
import json
import math
import os
import tempfile
from pathlib import Path

import unreal

SOURCE = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP"
CHARACTER = "/Game/AdvancedLocomotionV4/Blueprints/CharacterLogic/ALS_Base_CharacterBP.ALS_Base_CharacterBP"
ROTATIONS = ("SmoothedAimingRotation", "SpineRotation")
ANGLES = ("AimingAngle", "SmoothedAimingAngle")
TIMES = ("AimSweepTime", "InputYawOffsetTime", "LeftYawTime", "RightYawTime", "ForwardYawTime")


def native_text(obj):
    with tempfile.TemporaryDirectory(prefix="als-aiming-") as directory:
        task = unreal.AssetExportTask()
        task.object = obj
        task.exporter = unreal.ObjectExporterT3D()
        task.filename = str(Path(directory) / "object.t3d")
        task.automated = True
        task.prompt = False
        if not unreal.Exporter.run_asset_export_task(task):
            raise RuntimeError("Native aiming graph export failed")
        data = Path(task.filename).read_bytes()
        return data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")


def rot(value):
    return [value.pitch, value.yaw, value.roll]


def state(instance):
    result = {name: rot(instance.get_editor_property(name)) for name in ROTATIONS}
    for name in ANGLES:
        value = instance.get_editor_property(name)
        result[name] = [value.x, value.y]
    result.update({name: instance.get_editor_property(name) for name in TIMES})
    return result


def export():
    output = Path(os.environ["ALS_AIMING_OUTPUT"])
    if not output.is_absolute():
        raise ValueError("ALS_AIMING_OUTPUT must be absolute")
    cls = unreal.load_class(None, SOURCE + "_C")
    defaults = unreal.get_default_object(cls)
    initial = state(defaults)
    settings = {name: defaults.get_editor_property(name) for name in
                ("SmoothedAimingRotationInterpSpeed", "InputYawOffsetInterpSpeed")}
    subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    actor = subsystem.spawn_actor_from_class(unreal.load_class(None, CHARACTER + "_C"), unreal.Vector(0, 0, 10000), transient=True)
    if actor is None:
        raise RuntimeError("Cannot create the transient native character")
    trajectories = []
    system = unreal.get_default_object(unreal.SystemLibrary)
    def assign(instance, kind, name, value):
        # Blueprint defaults-only fields cannot be edited through the editor UI
        # setter on instances. Use the engine's typed runtime property setters on
        # this transient object, and verify the reflected value after each write.
        system.call_method("Set" + kind + "PropertyByName", args=(instance, name, value))
        actual = instance.get_editor_property(name)
        expected = value
        if kind == "Byte":
            actual = actual.value
        elif kind == "Rotator":
            actual, expected = rot(actual), rot(value)
        elif kind == "Vector":
            actual, expected = [actual.x, actual.y, actual.z], [value.x, value.y, value.z]
        if actual != expected:
            raise RuntimeError("Native transient property assignment failed: " + name)
    try:
        mesh = actor.get_editor_property("Mesh")
        for hz in (30, 60, 120):
            instance = unreal.new_object(cls, outer=mesh)
            assign(instance, "Object", "Character", actor)
            rows = []
            for frame in range(hz * 5):
                seconds = frame / hz
                mode = (frame // (hz // 2)) % 3
                has_input = frame % hz >= hz // 4
                actor.set_actor_rotation(unreal.Rotator(pitch=7 * math.sin(seconds), yaw=179 * math.cos(seconds * 1.3), roll=3 * math.sin(seconds * 2)), False)
                aiming = unreal.Rotator(pitch=115 * math.sin(seconds * 1.7), yaw=(-179 if frame % 2 else 179) if seconds < .5 else 540 * math.sin(seconds * 1.1), roll=9 * math.cos(seconds))
                movement = unreal.Vector(math.cos(seconds * 3), math.sin(seconds * 3), .1)
                assign(instance, "Double", "DeltaTimeX", 1 / hz)
                assign(instance, "Byte", "RotationMode", mode)
                assign(instance, "Bool", "HasMovementInput", has_input)
                assign(instance, "Rotator", "AimingRotation", aiming)
                assign(instance, "Vector", "MovementInput", movement)
                before = state(instance)
                instance.call_method("UpdateAimingValues")
                rows.append({"delta": instance.get_editor_property("DeltaTimeX"), "mode": mode,
                             "hasInput": has_input, "actor": rot(actor.get_actor_rotation()),
                             "aim": rot(aiming), "movement": [movement.x, movement.y, movement.z],
                             "before": before, "after": state(instance)})
            trajectories.append({"hz": hz, "frames": rows})
            del instance
        # Isolated boundary cases complement the continuous histories, including
        # native full-rotator early exits and non-normalized targets.
        boundaries = []
        for current, target in (([0, 179, 0], [0, -179, 0]), ([10, 40, 2], [10.00001, 40.00001, 2.00001]),
                                ([10, 40, 2], [10.00001, 80, 2.00001]), ([0, 0, 0], [0, -180, 0]),
                                ([0, 0, 0], [0, 540, 0]), ([0, 0, 0], [0, 0, 0])):
            for delta in (0, 1 / 60, .5):
                for speed in (0, 10):
                    a = unreal.Rotator(pitch=current[0], yaw=current[1], roll=current[2])
                    b = unreal.Rotator(pitch=target[0], yaw=target[1], roll=target[2])
                    # Python construction invokes native MakeRotator float inputs.
                    # Record the actual constructed values, not the Python intent.
                    boundaries.append({"current": rot(a), "target": rot(b), "delta": delta, "speed": speed,
                                       "output": rot(unreal.MathLibrary.r_interp_to(a, b, delta, speed))})
        graphs = [{"name": name, "path": SOURCE + ":" + name,
                   "nativeText": native_text(unreal.load_object(None, SOURCE + ":" + name))}
                  for name in ("UpdateAimingValues", "UpdateGraph")]
        payload = {"schemaVersion": 1, "source": SOURCE, "evaluation": "actual_blueprint_UpdateAimingValues",
                   "initial": initial, "settings": settings, "graphs": graphs,
                   "trajectories": trajectories, "rotatorBoundaries": boundaries}
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
        unreal.log("ALS_AIMING_INPUT_NATIVE_OK frames=" + str(sum(len(t["frames"]) for t in trajectories)) +
                   " boundaries=" + str(len(boundaries)) + " invocation=actual_blueprint assets_saved=0")
    finally:
        subsystem.destroy_actor(actor)


export()
