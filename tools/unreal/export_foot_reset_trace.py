"""Execute compiled ResetIKOffsets in an isolated commandlet; restore CDO fields."""
import json
import os
from pathlib import Path

import unreal


def vector(value):
    return [value.x, value.y, value.z]


def rotator(value):
    return [value.pitch, value.yaw, value.roll]


def export():
    source = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP_C"
    output = Path(os.environ["ALS_FOOT_RESET_OUTPUT"])
    if not output.is_absolute():
        raise ValueError("ALS_FOOT_RESET_OUTPUT must be absolute")
    anim_class = unreal.load_class(None, source)
    positions = {"FootOffset_L_Location": (3, 5, -17), "FootOffset_R_Location": (-7, 11, -29),
                 "FootLock_L_Location": (31, 3, -8), "FootLock_R_Location": (-19, 9, -23)}
    rotations = {"FootOffset_L_Rotation": (10, 20, -30), "FootOffset_R_Rotation": (-15, 25, 35),
                 "FootLock_L_Rotation": (4, 8, 12), "FootLock_R_Rotation": (-6, 2, 18)}
    # These properties are EditDefaultsOnly. Python rejects writes on transient
    # instances. The function only touches the listed properties, so use the CDO
    # in this isolated process and restore every field even on failure. No save.
    instance = unreal.get_default_object(anim_class)
    fields = list(positions) + list(rotations) + ["FootLock_L_Alpha", "FootLock_R_Alpha", "DeltaTimeX"]
    def copy_value(name):
        value = instance.get_editor_property(name)
        if name in positions:
            return unreal.Vector(*vector(value))
        if name in rotations:
            return unreal.Rotator(pitch=value.pitch, yaw=value.yaw, roll=value.roll)
        return value

    original = {name: copy_value(name) for name in fields}
    rows = []
    try:
        for hz in (30, 60, 120):
            for name, value in positions.items():
                instance.set_editor_property(name, unreal.Vector(*value))
            for name, value in rotations.items():
                instance.set_editor_property(name, unreal.Rotator(pitch=value[0], yaw=value[1], roll=value[2]))
            instance.set_editor_property("FootLock_L_Alpha", 0.8)
            instance.set_editor_property("FootLock_R_Alpha", 0.6)
            instance.set_editor_property("DeltaTimeX", 1.0 / hz)

            def snapshot():
                result = {name: vector(instance.get_editor_property(name)) for name in positions}
                result.update({name: rotator(instance.get_editor_property(name)) for name in rotations})
                result.update({name: instance.get_editor_property(name) for name in ("FootLock_L_Alpha", "FootLock_R_Alpha")})
                return result

            for frame in range(1, 5):
                before = snapshot()
                instance.call_method("ResetIKOffsets")
                rows.append({"hz": hz, "frame": frame, "delta": 1.0 / hz, "before": before, "after": snapshot()})
    finally:
        for name, value in original.items():
            instance.set_editor_property(name, value)
    for name, value in original.items():
        restored = instance.get_editor_property(name)
        if name in positions:
            same = vector(restored) == vector(value)
        elif name in rotations:
            same = rotator(restored) == rotator(value)
        else:
            same = restored == value
        if not same:
            raise RuntimeError("CDO property was not restored: " + name)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps({"schemaVersion": 1, "source": source, "defaultsRestored": True, "rows": rows}, indent=2) + "\n", encoding="utf-8")
    unreal.log("ALS_FOOT_RESET_TRACE_OK rows=" + str(len(rows)) + " defaults_restored=1 assets_saved=0")


export()
