"""Actual HandsTied/Injured/Barrel graphs and shared SaveCachedPose updates."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(__file__).parents[2] / "assets/config"
directory = Path(os.environ["ALS_CACHED_OVERLAY_TRACE_DIRECTORY"])
if not directory.is_absolute():
    raise ValueError("Absolute output directory required")
directory.mkdir(parents=True, exist_ok=True)
hashes = {name: hashlib.sha256((root / (name + ".json")).read_bytes()).hexdigest()
          for name in ("refactored_animation_sources", "refactored_sync_inputs")}
actions = ("", "Mantling", "Rolling", "", "GettingUp", "GettingUp", "", "Rolling",
           "", "GettingUp", "GettingUp", "", "Mantling", "", "Rolling", "")
traces = []
for hz in (30, 60, 120):
    frames = []
    for frame in range(hz * 4):
        # The first half changes actions every 0.25s; a short interruption burst
        # in the last quarter keeps three or four cached-pose consumers active.
        phase = frame * 4 // hz
        action = actions[phase]
        if phase == 13:
            action = ("", "Mantling", "Rolling")[frame % 3]
        tag = "Als.LocomotionAction." + action if action else ""
        frames.append({"delta": 0 if frame % 37 == 36 else 1 / hz,
                       "reset": frame == (hz * 9 + 3) // 4,
                       "action": tag,
                       "poseState": {"GaitWalkingAmount": frame % 11 / 10,
                                     "StandingAmount": .6 if phase % 3 else 0,
                                     "CrouchingAmount": .4 if phase % 3 else 0,
                                     "InAirAmount": (0, .4, 1, 1e-6)[phase % 4]},
                       "inAirState": {"GroundPredictionAmount": frame % 17 / 16}})
    traces.append({"name": str(hz) + "hz", "frames": frames})
for kind in ("HandsTied", "Injured", "Barrel"):
    request = {"schemaVersion": 1, "overlay": kind, "resourceHashes": hashes, "traces": traces}
    request["requestDigest"] = hashlib.sha256(json.dumps(request, separators=(",", ":"), allow_nan=False).encode()).hexdigest()
    output = directory / (kind + ".json")
    request_path = output.with_suffix(".request.json")
    request_path.write_text(json.dumps(request, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
    if not unreal.AlsAnimationGraphLibrary.export_refactored_default_overlay_trace(str(request_path), str(output)):
        raise RuntimeError("Native cached Overlay failed: " + kind)
    value = json.loads(output.read_text(encoding="utf-8"))
    value["resourceHashes"] = hashes
    output.write_text(json.dumps(value, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log("ALS_CACHED_OVERLAY_TRACE_OK graphs=3 traces=9 frames=2520 assets_saved=0")
if os.environ.get("ALS_CACHED_OVERLAY_TRACE_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)
