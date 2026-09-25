"""Original six-direction machine used as the proxy root, controlled Parent inputs."""
import hashlib
import json
import os
from pathlib import Path
import unreal

directory = Path(os.environ["ALS_DIRECTION_TRACE_DIRECTORY"])
if not directory.is_absolute():
    raise ValueError("Absolute output directory required")
directory.mkdir(parents=True, exist_ok=True)
root = Path(__file__).parents[2] / "assets/config"
hashes = {name: hashlib.sha256((root / (name + ".json")).read_bytes()).hexdigest()
          for name in ("refactored_animation_sources", "refactored_stance_machines")}

def frame(delta, direction=0, lock=0, crossing=1, reset=False):
    return {"delta": delta, "reset": reset, "poseState": {}, "inAirState": {},
            "groundedState": {"MovementDirection": dict(zip(
                ("bForward", "bBackward", "bLeft", "bRight"), [direction == i for i in range(4)])),
                "HipsDirectionLockAmount": lock}, "feetState": {"FeetCrossingAmount": crossing},
            "standingState": {"PlayRate": 1, "StrideBlendAmount": 1, "WalkRunBlendAmount": 1},
            "crouchingState": {"PlayRate": 1}}

traces = []
for hz in (30, 60, 120):
    delta = 1 / hz
    frames = [frame(0), frame(.125, 1), frame(0), frame(delta, 1, reset=True)]
    # A half-lock selects LeftBackward; unlocking must wait for its full weight.
    frames.extend([frame(0, reset=True), frame(.01, 2, .5, 0), frame(.01, 2, 0, 0),
                   frame(1, 2, 0, 0), frame(0, 2, 0, 0)])
    for i in range(hz * 4):
        frames.append(frame(0 if i % 31 == 30 else delta, (i // (hz // 4)) % 4,
                            -.5 if i % 17 < 5 else .5 if i % 17 < 10 else 0,
                            1 if i % 11 < 5 else 0, i == hz * 2))
    # Start from every fully weighted direction, then cover independent
    # direction requests and unlocked/left/right hip gates (including no request).
    for source_direction, source_lock in ((0, 0), (1, 0), (3, 0), (3, -.5), (2, 0), (2, .5)):
        for target_direction in range(5):
            for target_lock in (-.5, 0, .5):
                frames.append(frame(0, source_direction, source_lock, 0, reset=True))
                frames.append(frame(delta, target_direction, target_lock, 0))
    traces.append({"name": str(hz) + "hz", "frames": frames})
for kind in ("Standing", "Crouching"):
    request = {"schemaVersion": 1, "overlay": kind, "resourceHashes": hashes, "traces": traces}
    request["requestDigest"] = hashlib.sha256(json.dumps(request, separators=(",", ":"), allow_nan=False).encode()).hexdigest()
    path = directory / (kind + ".request.json")
    output = directory / (kind + ".json")
    path.write_text(json.dumps(request, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
    if not unreal.AlsAnimationGraphLibrary.export_refactored_default_overlay_trace(str(path), str(output)):
        raise RuntimeError("Native direction trace failed: " + kind)
    value = json.loads(output.read_text(encoding="utf-8"))
    value["resourceHashes"] = hashes
    output.write_text(json.dumps(value, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log(f"ALS_DIRECTION_TRACE_OK graphs=2 frames={2 * sum(len(t['frames']) for t in traces)} assets_saved=0")
if os.environ.get("ALS_DIRECTION_TRACE_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)
