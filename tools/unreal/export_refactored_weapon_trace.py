"""Actual linked weapon graphs: state transitions, local notifies and full pose evidence."""
import hashlib
import json
import os
from pathlib import Path
import unreal

directory = Path(os.environ["ALS_WEAPON_TRACE_DIRECTORY"])
if not directory.is_absolute():
    raise ValueError("Absolute output directory required")
directory.mkdir(parents=True, exist_ok=True)
root = Path(__file__).parents[2] / "assets/config"
hashes = {name: hashlib.sha256((root / (name + ".json")).read_bytes()).hexdigest()
          for name in ("refactored_animation_sources", "refactored_weapon_machines")}


def frame(delta, aim=False, allowed=False, moving=False, sprint=False, air=False, reset=False):
    return {"delta": delta, "reset": reset, "action": "",
            "rotationMode": "Als.RotationMode.Aiming" if aim else "Als.RotationMode.ViewDirection",
            "gait": "Als.Gait.Sprinting" if sprint else "Als.Gait.Running",
            "locomotionMode": "Als.LocomotionMode.InAir" if air else "Als.LocomotionMode.Grounded",
            "moving": moving, "allowed": allowed,
            "poseState": {"GaitWalkingAmount": .4, "GaitSprintingAmount": .6 if sprint else 0,
                          "StandingAmount": .7, "CrouchingAmount": .3, "InAirAmount": 0},
            "inAirState": {"GroundPredictionAmount": 0}, "viewState": {"PitchAmount": .65}}


traces = []
for hz in (30, 60, 120):
    d = 1 / hz
    frames = [frame(d, aim=True), frame(3), frame(d, aim=True, allowed=True, moving=True),
              frame(3), frame(d, moving=True), frame(d, aim=True), frame(d, air=True), frame(d, reset=True)]
    for i in range(120):
        frames.append(frame(0 if i % 37 == 36 else d, aim=i % 31 < 15,
                            allowed=i % 17 < 5, moving=i % 23 < 5,
                            sprint=i % 41 < 7, air=i % 61 < 5, reset=i == 81))
    frames.extend([frame(d, aim=True), frame(d)] + [frame(d) for _ in range(hz + 2)])
    traces.append({"name": str(hz) + "hz", "frames": frames})
for kind in ("Bow", "PistolOneHanded", "PistolTwoHanded", "Rifle"):
    request = {"schemaVersion": 1, "overlay": kind, "resourceHashes": hashes, "traces": traces}
    request["requestDigest"] = hashlib.sha256(json.dumps(request, separators=(",", ":"), allow_nan=False).encode()).hexdigest()
    path = directory / (kind + ".request.json")
    output = directory / (kind + ".json")
    path.write_text(json.dumps(request, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
    if not unreal.AlsAnimationGraphLibrary.export_refactored_default_overlay_trace(str(path), str(output)):
        raise RuntimeError("Native weapon graph failed: " + kind)
    value = json.loads(output.read_text(encoding="utf-8"))
    value["resourceHashes"] = hashes
    output.write_text(json.dumps(value, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log(f"ALS_WEAPON_TRACE_OK graphs=4 frames={4 * sum(len(t['frames']) for t in traces)} assets_saved=0")
if os.environ.get("ALS_WEAPON_TRACE_QUIT") == "1":
    ticks = 0

    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()

    handle = unreal.register_slate_post_tick_callback(finish)
