"""Original complete Standing graph, controlled Parent inputs, no asset saves."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(__file__).parents[2]
output = Path(os.environ["ALS_STANDING_HOST_TRACE_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
directory = root / "artifacts/standing-host-native"
directory.mkdir(parents=True, exist_ok=True)
names = ("refactored_animation_sources", "refactored_stance_machines", "refactored_sync_inputs",
         "refactored_triangulation_inputs", "refactored_movement_settings", "refactored_rest_settings",
         "refactored_skeleton_curves", "refactored_slot_inventory", "refactored_quick_stop_settings")
hashes = {name: hashlib.sha256((root / "assets/config" / (name + ".json")).read_bytes()).hexdigest() for name in names}
traces = []
for hz in (30, 60, 120):
    frames = []
    for i in range(hz * 11):
        phase = i // hz
        local = (i % hz) / hz
        moving = 2 <= phase <= 5 and (local < .08 or .3 <= local < .82)
        coast = phase == 5 and .82 <= local < .95
        frames.append({"delta": 1 / hz, "moving": moving, "movingSmooth": moving or coast,
                       "speed": 200 if moving else 170 if coast else 0,
                       "side": 1 if phase % 2 == 0 else -1, "aiming": phase in (7, 8),
                       "yaw": {1: -110, 6: 110, 7: -85, 8: 85}.get(phase, 0),
                       "foot": (-.75, -.25, .25, .75)[phase % 4], "dynamic": phase == 9,
                       "pivot": phase == 4 and i % hz == 0, "pending": i == 0,
                       "evaluate": i % 13 >= 3})
    traces.append({"name": str(hz), "frames": frames})
request = {"schemaVersion": 1, "resourceHashes": hashes, "traces": traces}
path = directory / "request.json"
path.write_text(json.dumps(request, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
if not unreal.AlsAnimationGraphLibrary.export_standing_host_trace(str(path), str(output)):
    raise RuntimeError("Standing host native trace failed")
value = json.loads(output.read_text(encoding="utf-8"))
if value["resourceHashes"] != hashes or sum(len(t["frames"]) for t in value["traces"]) != 2310 or len(value["names"]) != 79:
    raise RuntimeError("Standing host provenance/layout differs")
output.write_text(json.dumps(value, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log("ALS_STANDING_HOST_TRACE_OK traces=3 frames=2310 assets_saved=0")
if os.environ.get("ALS_STANDING_HOST_TRACE_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)
