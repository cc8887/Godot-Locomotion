"""Original direction notify -> Parent -> Movement Details, without asset saves."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(__file__).parents[2]
output = Path(os.environ["ALS_PIVOT_TRACE_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
directory = root / "artifacts/pivot-native"
directory.mkdir(parents=True, exist_ok=True)
names = ("refactored_animation_sources", "refactored_stance_machines", "refactored_sync_inputs",
         "refactored_triangulation_inputs", "refactored_movement_settings", "refactored_rest_settings",
         "refactored_skeleton_curves", "refactored_slot_inventory", "refactored_quick_stop_settings")
hashes = {n: hashlib.sha256((root / "assets/config" / (n + ".json")).read_bytes()).hexdigest() for n in names}
traces = []
for hz in (30, 60, 120):
    frames = []
    for i in range(hz * 8):
        t = i / hz
        phase = i // hz
        backward = 1 <= t < 1.1 or 2 <= t < 3 or 4 <= t < 5 or 6 <= t < 7
        speed = {4: 200, 5: 199, 6: 201}.get(phase, 150)
        frames.append({"delta": 1 / hz, "moving": True, "movingSmooth": True,
                       "speed": speed, "side": 1, "heading": 180 if backward else 0,
                       "velocityX": -speed if backward else speed, "velocityY": 0,
                       "aiming": False, "yaw": 0, "foot": (-.75, .75)[phase % 2],
                       "dynamic": False, "pivot": False, "pending": i == 0, "evaluate": i % 13 == 0})
    traces.append({"name": str(hz), "frames": frames})
request = {"schemaVersion": 1, "resourceHashes": hashes, "dispatchGeneratedPivot": True, "traces": traces}
path = directory / "request.json"
path.write_text(json.dumps(request, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
if not unreal.AlsAnimationGraphLibrary.export_standing_host_trace(str(path), str(output)):
    raise RuntimeError("Pivot native trace failed")
value = json.loads(output.read_text(encoding="utf-8"))
if value["resourceHashes"] != hashes or len(value["names"]) != 79:
    raise RuntimeError("Pivot provenance/layout differs")
for trace in value["traces"]:
    rows = trace["frames"]
    states = {r["detailsState"] for r in rows}
    if len(rows) != int(trace["name"]) * 8 or not {3, 4}.issubset(states):
        raise RuntimeError("Native First/Second Pivot not covered: " + str(states))
    if sum(r["pivotNotifies"] for r in rows) < 5:
        raise RuntimeError("Native direction notifications not covered")
output.write_text(json.dumps(value, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log("ALS_PIVOT_TRACE_OK traces=3 frames=1680 assets_saved=0")
if os.environ.get("ALS_PIVOT_TRACE_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)
