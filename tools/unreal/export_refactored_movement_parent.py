"""Exercise original Parent refresh functions, without running replacement algorithms."""
import hashlib
import json
import math
import os
from pathlib import Path
import unreal

directory = Path(os.environ["ALS_MOVEMENT_PARENT_DIRECTORY"])
if not directory.is_absolute():
    raise ValueError("Absolute directory required")
directory.mkdir(parents=True, exist_ok=True)
root = Path(__file__).parents[2] / "assets/config"
hashes = {name: hashlib.sha256((root / (name + ".json")).read_bytes()).hexdigest()
          for name in ("refactored_animation_sources", "refactored_movement_settings")}
traces = []
angles = [-720, -181, -180, -115.001, -115, -75.001, -75, 0, 75, 75.001, 115, 115.001, 180, 181, 720]
for hz in (30, 60, 120):
    frames = []
    for n in range(hz * 5):
        phase = n / hz
        yaw = math.sin(n * .073) * 2
        # Includes all 3 axes; scalar speed/yaw are independent captured Parent fields.
        axis = (1 / math.sqrt(14), 2 / math.sqrt(14), 3 / math.sqrt(14))
        rotation = [x * math.sin(yaw / 2) for x in axis] + [math.cos(yaw / 2)]
        velocity = [math.cos(phase * 3) * 250, math.sin(phase * 3) * 180, math.sin(n * .17) * 40]
        if n % 29 == 0:
            velocity = [0, 0, 0]
        acceleration = [(-1 if n % 7 < 3 else 1) * x * 7 for x in velocity]
        gait = "Als.Gait.Walking" if phase < .5 else "Als.Gait.Running" if phase < 1 else "Als.Gait.Sprinting" if phase < 3 else ""
        value = dict(velocity=velocity, acceleration=acceleration, rotation=rotation,
                     speed=0 if n % 31 == 0 else (n % 19) * 70, scale=[.5, 1, 2][n % 3],
                     velocityYaw=angles[n % len(angles)], viewYaw=0 if n % 2 else .00001,
                     maxAcceleration=.0001 if n % 37 == 0 else 1500, maxBraking=800,
                     gait=gait, velocityMode=n % 23 == 0, pending=n == 0 or n == hz * 2,
                     delta=0 if n % 41 == 0 else 1 / hz,
                     running=[0, .3, 1][n % 3], sprinting=.7 if gait == "Als.Gait.Sprinting" else 0,
                     hipsLock=[-2, -.5, 0, .5, 2][n % 5], sprintBlock=[-.1, .25, 1.1][n % 3])
        operations = []
        if n % 43 == 0:
            operations += ["InitializeGrounded"]
        if n % 47 == 0:
            operations += ["InitializeLean"]
        if n % 11 != 0:
            operations += ["RefreshGrounded"]
        operations += ["RefreshGroundedMovement"]
        if n % 59 == 0:
            operations += ["InitializeStandingMovement"]
        if n % 13 != 0:
            operations += ["RefreshStandingMovement"]
        if n % 17 != 0:
            operations += ["RefreshCrouchingMovement"]
        if n % 19 == 0:
            operations += ["ActivatePivot"]
        if n % 29 == 0:
            operations += ["ResetPivot"]
        frames.append(dict(input=value, operations=operations))
    traces.append(dict(name=f"movement-{hz}", frames=frames))
request = directory / "request.json"
request.write_text(json.dumps(dict(schemaVersion=1, resourceHashes=hashes, traces=traces),
                              separators=(",", ":"), allow_nan=False), encoding="utf-8")
if not unreal.AlsAnimationGraphLibrary.export_movement_parent_trace(str(request), str(directory / "native.json")):
    raise RuntimeError("Native movement Parent trace failed")
if os.environ.get("ALS_MOVEMENT_PARENT_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)
