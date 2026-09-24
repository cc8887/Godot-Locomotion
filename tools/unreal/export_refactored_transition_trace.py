"""Real linked notify functions, parent montage lifecycle and continuous Transition Slot."""
import hashlib
import json
import os
from pathlib import Path
import unreal

directory = Path(os.environ["ALS_TRANSITION_TRACE_DIRECTORY"])
if not directory.is_absolute():
    raise ValueError("Absolute output directory required")
directory.mkdir(parents=True, exist_ok=True)
root = Path(__file__).parents[2] / "assets/config"
hashes = {name: hashlib.sha256((root / (name + ".json")).read_bytes()).hexdigest()
          for name in ("refactored_animation_sources", "refactored_weapon_machines", "refactored_slot_inventory")}
traces = []
worker_mode = os.environ.get("ALS_TRANSITION_WORKER_TRACE") == "1"
for kind in ("Bow", "PistolOneHanded", "PistolTwoHanded", "Rifle"):
    for hz in (30, 60, 120):
        frames = []
        for i in range(hz * 4):
            frames.append({"delta": 0 if i == 19 else 1 / hz,
                           "stance": "" if i == 7 else "Als.Stance.Crouching" if i == 6 else "Als.Stance.Standing",
                           "moving": i == 5,
                           "notifies": {0: [0], 5: [0], 6: [1], 7: [0], 12: [1], 18: [1, 0],
                                        19: [0], 28: [1], 40: [0], 50: [1]}.get(i, []),
                           "stop": i in (24, 32, 44),
                           "stopDuration": .05 if i == 32 else 0 if i == 44 else -1})
            if worker_mode:
                def play(index):
                    return {"op": "play", "notify": index}

                def stop(duration):
                    return {"op": "stop", "duration": duration}

                frames[-1].update({
                    "worker": {0: [play(0), play(1)], 1: [play(0), stop(.4), stop(.05)],
                               3: [play(0)], 4: [play(1)], 5: [play(0)], 6: [play(0)], 7: [play(0)],
                               9: [play(0)], 10: [play(-1)], 18: [stop(.3), play(1)],
                               28: [play(0)], 32: [stop(.1)], 40: [play(0), play(-1)]}.get(i, []),
                    "postUpdate": i not in (3, 4, 5, 6, 7, 9, 10, 28, 32),
                    "notifies": {12: [1, 0], 28: [1], 32: [0]}.get(i, []),
                    "stop": i in (24, 32), "stopDuration": 0 if i == 32 else .05})
        traces.append({"kind": kind, "hz": hz, "frames": frames})
request = {"schemaVersion": 1, "resourceHashes": hashes, "traces": traces}
request["requestDigest"] = hashlib.sha256(json.dumps(request, separators=(",", ":"), allow_nan=False).encode()).hexdigest()
path = directory / "request.json"
output = directory / "transition.json"
path.write_text(json.dumps(request, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
if not unreal.AlsAnimationGraphLibrary.export_refactored_transition_trace(str(path), str(output)):
    raise RuntimeError("Native Transition trace failed")
value = json.loads(output.read_text(encoding="utf-8"))
if value["requestDigest"] != request["requestDigest"] or len(value["traces"]) != 12:
    raise RuntimeError("Native Transition trace provenance differs")
value["resourceHashes"] = hashes
output.write_text(json.dumps(value, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log(f"ALS_TRANSITION_TRACE_OK traces=12 frames={sum(len(t['frames']) for t in traces)} assets_saved=0")
if os.environ.get("ALS_TRANSITION_TRACE_QUIT") == "1":
    ticks = 0

    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()

    handle = unreal.register_slate_post_tick_callback(finish)
