"""Controlled inputs through the real Refactored Camera AnimGraph, no asset saves."""
import hashlib
import json
import math
import os
import itertools
from pathlib import Path
import unreal


def traces():
    result = []
    for hz in (30, 60, 120):
        for rapid in (False, True):
            frames = []
            for i in range(hz * (4 if rapid else 9)):
                t = i / hz
                look = ("ViewDirection", "Aiming", "VelocityDirection")[i % 3] if rapid else (
                    "ViewDirection" if t < 1 else "Aiming" if t < 1.2 else "VelocityDirection" if t < 1.4 else "Aiming")
                frames.append(dict(delta=1 / hz, RotationMode="Als.RotationMode." + look,
                    Stance="Als.Stance." + ("Crouching" if 2 < t < 4 else "Standing"),
                    Gait="Als.Gait." + ("Walking" if i % 37 < 17 else "Sprinting"),
                    ViewMode="Als.ViewMode." + ("FirstPerson" if (i % 23 < 10 if rapid else 2.1 < t < 3) else "ThirdPerson"),
                    LocomotionAction="Als.LocomotionAction.Ragdolling" if not rapid and 4 < t < 6.5 else
                        "Als.LocomotionAction.Rolling" if not rapid and 1.5 < t < 1.8 else "",
                    RightShoulder=i % 17 < 8 if rapid else t < 1.1 or t > 3))
            result.append(dict(name=("rapid" if rapid else "reentry") + str(hz), frames=frames))
    # Settled branch coverage includes Running/Mantling and invalid rotation
    # fallback that the continuously interrupted traces do not exercise.
    branches = []
    for look, stance, gait, view, action, shoulder in itertools.product(
            ("", "ViewDirection", "Aiming", "VelocityDirection"), ("Standing", "Crouching"),
            ("Walking", "Running", "Sprinting"), ("ThirdPerson", "FirstPerson"),
            ("", "Mantling", "Rolling", "Ragdolling"), (True, False)):
        branches.append(dict(delta=3., RotationMode="Als.RotationMode." + look if look else "",
            Stance="Als.Stance." + stance, Gait="Als.Gait." + gait, ViewMode="Als.ViewMode." + view,
            LocomotionAction="Als.LocomotionAction." + action if action else "", RightShoulder=shoulder))
    result.append(dict(name="settled_branches", frames=branches))
    return result


output = Path(os.environ["ALS_CAMERA_GRAPH_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
output.parent.mkdir(parents=True, exist_ok=True)
inputs = traces()
digest = hashlib.sha256(json.dumps(inputs, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
request = dict(schemaVersion=1, requestDigest=digest, traces=inputs)
request_path = output.with_suffix(".request.json")
request_path.write_text(json.dumps(request, separators=(",", ":")), encoding="utf-8")
if not unreal.AlsAnimationGraphLibrary.export_camera_graph_trace(str(request_path), str(output)):
    raise RuntimeError("Native camera graph export failed")
data = json.loads(output.read_text(encoding="utf-8-sig"))
if (data["requestDigest"] != digest or data["source"] != "/ALS/ALSCamera/AB_Als_Camera.AB_Als_Camera" or
        len(data["traces"]) != len(inputs)):
    raise RuntimeError("Camera source/trace mismatch")
count = 0
for expected, actual in zip(inputs, data["traces"]):
    if actual["name"] != expected["name"] or len(actual["frames"]) != len(expected["frames"]):
        raise RuntimeError("Incomplete camera trace")
    for serial, (frame, row) in enumerate(zip(expected["frames"], actual["frames"]), 1):
        if row["serial"] != serial or row["input"] != frame or not row["curves"]:
            raise RuntimeError("Camera frame identity/input mismatch")
        if any(not math.isfinite(v) for v in row["curves"].values()):
            raise RuntimeError("Nonfinite camera curve")
        count += 1
unreal.log(f"ALS_CAMERA_GRAPH_EXPORT_OK traces={len(inputs)} frames={count} assets_saved=0")
if os.environ.get("ALS_CAMERA_GRAPH_QUIT") == "1":
    _ticks = 0
    def _quit_after_tick(delta):
        global _ticks
        _ticks += 1
        if _ticks >= 3:
            unreal.unregister_slate_post_tick_callback(_quit_handle)
            unreal.SystemLibrary.quit_editor()
    _quit_handle = unreal.register_slate_post_tick_callback(_quit_after_tick)
