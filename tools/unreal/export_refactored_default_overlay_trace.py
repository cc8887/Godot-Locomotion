"""Tick an unmodified Default/Feminine/Masculine AnimGraph with controlled parent state."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(__file__).parents[2] / "assets/config"
output = Path(os.environ["ALS_DEFAULT_OVERLAY_TRACE_OUTPUT"])
kind = os.environ.get("ALS_BASIC_OVERLAY_KIND", "Default")
if kind not in ("Default", "Feminine", "Masculine"):
    raise ValueError("Unknown basic Overlay")
if not output.is_absolute():
    raise ValueError("Absolute output required")
traces = []
for hz in (30, 60, 120):
    frames = []
    for frame in range(hz * 3):
        phase = frame * 4 // hz
        walking, standing, crouching, air = (
            (0, 1, 0, 0), (.25, .6, .4, 0), (1, 1, 0, 0), (0, 0, 1, 0),
            (0, 0, 0, 0), (.6, .7, .3, 1), (.4, 1, 0, .4), (0, 1, 0, 0),
            (1, .5, .5, 1), (.3, .2, .8, 1), (.5, 1, 0, 1e-6), (.8, 1, 0, 1)
        )[phase]
        prediction = (frame % 17) / 16
        frames.append({"delta": 0 if frame % 37 == 36 else 1 / hz,
                       "reset": frame == hz * 2 + hz // 4,
                       "poseState": {"GaitWalkingAmount": walking, "StandingAmount": standing,
                                     "CrouchingAmount": crouching, "InAirAmount": air},
                       "inAirState": {"GroundPredictionAmount": prediction}})
    traces.append({"name": str(hz) + "hz", "frames": frames})
hashes = {name: hashlib.sha256((root / (name + ".json")).read_bytes()).hexdigest()
          for name in ("refactored_animation_sources", "refactored_sync_inputs")}
request = {"schemaVersion": 1, "resourceHashes": hashes, "traces": traces}
if kind != "Default":
    request["overlay"] = kind
request["requestDigest"] = hashlib.sha256(json.dumps(request, separators=(",", ":"), allow_nan=False).encode()).hexdigest()
output.parent.mkdir(parents=True, exist_ok=True)
request_path = output.with_suffix(".request.json")
request_path.write_text(json.dumps(request, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
if not unreal.AlsAnimationGraphLibrary.export_refactored_default_overlay_trace(str(request_path), str(output)):
    raise RuntimeError("Native Default Overlay trace failed")
value = json.loads(output.read_text(encoding="utf-8"))
value["resourceHashes"] = hashes
output.write_text(json.dumps(value, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log("ALS_DEFAULT_OVERLAY_TRACE_OK overlay=" + kind + " traces=3 frames=630 assets_saved=0")
if os.environ.get("ALS_DEFAULT_OVERLAY_TRACE_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)
