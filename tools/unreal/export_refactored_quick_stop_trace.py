"""Actual Standing StopQuick notify -> Parent -> physical Montage -> Transition Slot."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(__file__).parents[2]
output = Path(os.environ["ALS_QUICK_STOP_TRACE_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
directory = root / "artifacts/quick-stop-native"
directory.mkdir(parents=True, exist_ok=True)
hashes = {name: hashlib.sha256((root / "assets/config" / (name + ".json")).read_bytes()).hexdigest()
          for name in ("refactored_animation_sources", "refactored_quick_stop_settings", "refactored_stance_machines", "refactored_slot_inventory")}
cases = {0: (0, False), 3: (175, False), 6: (176, False), 9: (-180, False), 12: (180, True),
         15: (90, True), 18: (-90, True), 19: (90, False), 24: (540, False),
         28: (1000000090, False), 32: (-540, True), 40: (-90, False), 50: (176, True)}
traces = []
for hz in (30, 60, 120):
    frames = []
    for i in range(hz * 5):
        angle, crouch = cases.get(i, (0, False))
        frames.append({"delta": 0 if i == 19 else 1 / hz,
                       "stance": "" if i == 24 else "Als.Stance.Crouching" if crouch else "Als.Stance.Standing",
                       "moving": i in (18, 40), "notifies": [], "stop": i in (24, 40), "stopDuration": 0 if i == 24 else .05,
                       "quick": {"count": 2 if i == 19 else 1 if i in cases else 0,
                                 "rotation": "Als.RotationMode.Aiming" if i == 18 else "Als.RotationMode.ViewDirection" if i == 40 else "Als.RotationMode.VelocityDirection",
                                 "hasInput": i != 32, "inputYaw": angle, "targetYaw": angle if i == 32 else 30,
                                 "actorYaw": 1000000000 if i == 28 else 0}})
    traces.append({"kind": "Standing", "hz": hz, "frames": frames})
request = {"schemaVersion": 1, "resourceHashes": hashes, "traces": traces}
request["requestDigest"] = hashlib.sha256(json.dumps(request, separators=(",", ":"), allow_nan=False).encode()).hexdigest()
path = directory / "request.json"
path.write_text(json.dumps(request, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
if not unreal.AlsAnimationGraphLibrary.export_refactored_transition_trace(str(path), str(output)):
    raise RuntimeError("QuickStop native trace failed")
value = json.loads(output.read_text(encoding="utf-8"))
if value["requestDigest"] != request["requestDigest"] or len(value["traces"]) != 3:
    raise RuntimeError("QuickStop trace provenance differs")
value["resourceHashes"] = hashes
output.write_text(json.dumps(value, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log("ALS_QUICK_STOP_TRACE_OK traces=3 frames=1050 assets_saved=0")
if os.environ.get("ALS_QUICK_STOP_TRACE_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)
