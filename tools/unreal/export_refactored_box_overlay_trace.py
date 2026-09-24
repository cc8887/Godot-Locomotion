"""Actual Box Overlay graph, with interrupted Mantle/Get-up/Roll transitions."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(__file__).parents[2] / "assets/config"
output = Path(os.environ["ALS_BOX_OVERLAY_TRACE_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
actions = ("", "Mantling", "Rolling", "", "GettingUp", "GettingUp", "", "Rolling",
           "", "GettingUp", "GettingUp", "", "Mantling", "", "Rolling", "")
traces = []
for hz in (30, 60, 120):
    frames = []
    for frame in range(hz * 4):
        phase = frame * 4 // hz
        tag = "Als.LocomotionAction." + actions[phase] if actions[phase] else ""
        frames.append({"delta": 0 if frame % 37 == 36 else 1 / hz,
                       "reset": frame == (hz * 9 + 3) // 4,
                       "action": tag,
                       "poseState": {"GaitWalkingAmount": frame % 11 / 10,
                                     "StandingAmount": .6 if phase % 3 else 0,
                                     "CrouchingAmount": .4 if phase % 3 else 0, "InAirAmount": 0},
                       "inAirState": {"GroundPredictionAmount": 0}})
    traces.append({"name": str(hz) + "hz", "frames": frames})
hashes = {name: hashlib.sha256((root / (name + ".json")).read_bytes()).hexdigest()
          for name in ("refactored_animation_sources", "refactored_sync_inputs")}
request = {"schemaVersion": 1, "overlay": "Box", "resourceHashes": hashes, "traces": traces}
request["requestDigest"] = hashlib.sha256(json.dumps(request, separators=(",", ":"), allow_nan=False).encode()).hexdigest()
output.parent.mkdir(parents=True, exist_ok=True)
request_path = output.with_suffix(".request.json")
request_path.write_text(json.dumps(request, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
if not unreal.AlsAnimationGraphLibrary.export_refactored_default_overlay_trace(str(request_path), str(output)):
    raise RuntimeError("Native Box Overlay trace failed")
value = json.loads(output.read_text(encoding="utf-8"))
value["resourceHashes"] = hashes
output.write_text(json.dumps(value, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log("ALS_BOX_OVERLAY_TRACE_OK traces=3 frames=840 assets_saved=0")
if os.environ.get("ALS_BOX_OVERLAY_TRACE_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)
