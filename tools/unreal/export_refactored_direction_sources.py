"""Moving original direction graphs: shared caches, sync clocks and final pose."""
import hashlib
import json
import math
import os
from pathlib import Path
import unreal

directory = Path(os.environ["ALS_DIRECTION_SOURCES_DIRECTORY"])
if not directory.is_absolute():
    raise ValueError("Absolute output directory required")
directory.mkdir(parents=True, exist_ok=True)
root = Path(__file__).parents[2] / "assets/config"
hashes = {name: hashlib.sha256((root / (name + ".json")).read_bytes()).hexdigest()
          for name in ("refactored_animation_sources", "refactored_stance_machines", "refactored_sync_inputs", "refactored_triangulation_inputs")}
traces = []
for hz in (30, 60, 120):
    frames = []
    for i in range(hz * 4):
        direction = (i // (hz // 4)) % 4
        velocity = ([1, 0, 0, 0] if i < hz // 2 else
                    [0, 0, 0, 0] if i % 37 == 0 else
                    [.1 + .1 * math.sin(i / 13), .3, .2, .4 - .1 * math.sin(i / 13)])
        frames.append({"delta": 0 if i % 31 == 0 else 1 / hz, "reset": i == hz * 2,
                       "poseState": {}, "inAirState": {},
                       "gait": "Als.Gait.Sprinting" if i % hz < hz // 2 else "Als.Gait.Running",
                       "groundedState": {
                           "MovementDirection": dict(zip(("bForward", "bBackward", "bLeft", "bRight"), [direction == n for n in range(4)])),
                           "HipsDirectionLockAmount": -.5 if i % 17 < 5 else .5 if i % 17 < 10 else 0,
                           "VelocityBlend": dict(zip(("ForwardAmount", "BackwardAmount", "LeftAmount", "RightAmount"), velocity)),
                           "RotationYawOffsets": {"ForwardAngle": 25 * math.sin(i / 19), "BackwardAngle": -15, "LeftAngle": 30, "RightAngle": -35}},
                       "feetState": {"FeetCrossingAmount": 1 if i % 11 < 4 else 0},
                       "standingState": {"PlayRate": 1.1 + .2 * math.sin(i / 23), "StrideBlendAmount": .55 + .4 * math.sin(i / 29),
                                         "WalkRunBlendAmount": .5 + .5 * math.sin(i / 31), "SprintBlockAmount": 1 if i % 13 == 0 else .3,
                                         "SprintAccelerationAmount": (i % 19) / 18},
                       "crouchingState": {"PlayRate": .9 + .2 * math.sin(i / 23)}})
    traces.append({"name": str(hz) + "hz", "frames": frames})
for kind in ("Standing", "Crouching"):
    request = {"schemaVersion": 1, "overlay": kind, "directionSources": True, "resourceHashes": hashes, "traces": traces}
    request["requestDigest"] = hashlib.sha256(json.dumps(request, separators=(",", ":"), allow_nan=False).encode()).hexdigest()
    path = directory / (kind + ".request.json")
    output = directory / (kind + ".json")
    path.write_text(json.dumps(request, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
    if not unreal.AlsAnimationGraphLibrary.export_refactored_default_overlay_trace(str(path), str(output)):
        raise RuntimeError("Native moving direction trace failed: " + kind)
    value = json.loads(output.read_text(encoding="utf-8"))
    value["resourceHashes"] = hashes
    output.write_text(json.dumps(value, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log(f"ALS_DIRECTION_SOURCES_OK graphs=2 frames={2 * sum(len(t['frames']) for t in traces)} assets_saved=0")
if os.environ.get("ALS_DIRECTION_SOURCES_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)
