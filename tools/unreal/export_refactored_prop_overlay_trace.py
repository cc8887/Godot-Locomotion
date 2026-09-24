"""Original Binoculars/Torch graphs: nested action/aim updates and full poses."""
import hashlib
import json
import os
from pathlib import Path
import unreal

directory = Path(os.environ["ALS_PROP_OVERLAY_TRACE_DIRECTORY"])
if not directory.is_absolute():
    raise ValueError("Absolute directory required")
directory.mkdir(parents=True, exist_ok=True)
root = Path(__file__).parents[2] / "assets/config"
hashes = {name: hashlib.sha256((root / (name + ".json")).read_bytes()).hexdigest()
          for name in ("refactored_animation_sources", "refactored_sync_inputs")}
actions = ("", "Mantling", "Rolling", "", "GettingUp", "GettingUp", "", "Rolling",
           "", "GettingUp", "GettingUp", "", "Mantling", "", "Rolling", "")
traces = []
for hz in (30, 60, 120):
    frames = []
    for frame in range(hz * 4):
        phase = frame * 4 // hz
        tag = actions[phase]
        frames.append({"delta": 0 if frame % 37 == 36 else 1 / hz,
                       "reset": frame == (hz * 9 + 3) // 4,
                       "action": "Als.LocomotionAction." + tag if tag else "",
                       "rotationMode": "Als.RotationMode.Aiming" if frame * 7 // hz % 3 else "Als.RotationMode.LookingDirection",
                       "poseState": {"GaitWalkingAmount": frame % 11 / 10, "GaitSprintingAmount": frame % 7 / 6,
                                     "StandingAmount": .6 if phase % 3 else 0, "CrouchingAmount": .4 if phase % 3 else 0, "InAirAmount": 0},
                       "inAirState": {"GroundPredictionAmount": 0}, "viewState": {"PitchAmount": frame % 17 / 16}})
    traces.append({"name": str(hz) + "hz", "frames": frames})
for kind in ("Binoculars", "Torch"):
    request = {"schemaVersion": 1, "overlay": kind, "resourceHashes": hashes, "traces": traces}
    request["requestDigest"] = hashlib.sha256(json.dumps(request, separators=(",", ":"), allow_nan=False).encode()).hexdigest()
    request_path = directory / (kind + ".request.json")
    output = directory / (kind + ".json")
    request_path.write_text(json.dumps(request, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
    if not unreal.AlsAnimationGraphLibrary.export_refactored_default_overlay_trace(str(request_path), str(output)):
        raise RuntimeError("Native prop Overlay failed: " + kind)
    value = json.loads(output.read_text(encoding="utf-8"))
    value["resourceHashes"] = hashes
    output.write_text(json.dumps(value, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log("ALS_PROP_OVERLAY_TRACE_OK graphs=2 traces=6 frames=1680 assets_saved=0")
if os.environ.get("ALS_PROP_OVERLAY_TRACE_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)
