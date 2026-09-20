"""Native Kismet math oracle for SetFootLockOffsets, without changing assets."""
import json
import os
import struct
from pathlib import Path
import unreal


def export():
    output = Path(os.environ["ALS_FOOT_LOCK_MATH_OUTPUT"])
    if not output.is_absolute():
        raise ValueError("ALS_FOOT_LOCK_MATH_OUTPUT must be absolute")
    library = unreal.get_default_object(unreal.MathLibrary)
    def call(name, *args):
        return library.call_method(name, args=args)
    def rotation(value):
        return unreal.Rotator(pitch=value[0], yaw=value[1], roll=value[2])
    def rv(value):
        return [value.pitch, value.yaw, value.roll]
    def vv(value):
        return [value.x, value.y, value.z]
    rows = []
    for hz in (30, 60, 120):
        delta = struct.unpack("f", struct.pack("f", 1.0 / hz))[0]
        for component, actor, last in (((0, -90, 0), (0, 95, 0), (0, 90, 0)),
                                       ((10, 45, -8), (8, -175, 3), (3, 175, -2)),
                                       ((-25, 163.123456, 11), (1, 77.12345678, 2), (2, 1, 4))):
            for grounded in (True, False):
                velocity = unreal.Vector(300, -200, 50)
                location = unreal.Vector(30, 25, -85)
                local_rotation = rotation((14, 179, -7))
                component_rotation = rotation(component)
                actor_rotation = rotation(actor)
                last_rotation = rotation(last)
                difference = call("NormalizedDeltaRotator", actor_rotation, last_rotation) if grounded else rotation((0, 0, 0))
                translation = call("LessLess_VectorRotator", call("Multiply_VectorFloat", velocity, delta), component_rotation)
                adjusted = call("Subtract_VectorVector", location, translation)
                adjusted = call("RotateAngleAxis", adjusted, difference.yaw, unreal.Vector(0, 0, -1))
                adjusted_rotation = call("NormalizedDeltaRotator", local_rotation, difference)
                # Record the values that crossed the reflected constructor,
                # whose MakeRotator parameters are float, not Python doubles.
                rows.append({"delta": delta, "grounded": grounded, "component": rv(component_rotation), "actor": rv(actor_rotation), "last": rv(last_rotation),
                             "velocity": vv(velocity), "location": vv(location), "rotation": rv(local_rotation),
                             "resultLocation": vv(adjusted), "resultRotation": rv(adjusted_rotation)})
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps({"schemaVersion": 1, "source": "UE5.9 Kismet SetFootLockOffsets math", "rows": rows}, indent=2) + "\n", encoding="utf-8")
    unreal.log("ALS_FOOT_LOCK_MATH_OK rows=" + str(len(rows)) + " assets_saved=0")


export()
