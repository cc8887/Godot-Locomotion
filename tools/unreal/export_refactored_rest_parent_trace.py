"""Original Rest Parent refresh functions on an exclusive real worker in a transient world."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(__file__).parents[2]
output = Path(os.environ["ALS_REST_PARENT_TRACE_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
directory = root / "artifacts/rest-parent-native"
directory.mkdir(parents=True, exist_ok=True)
hashes = {name: hashlib.sha256((root / "assets/config" / (name + ".json")).read_bytes()).hexdigest()
          for name in ("refactored_animation_sources", "refactored_rest_settings")}
traces = []
for hz in (30, 60, 120):
    frames = []
    for i in range(hz * 16):
        phase = i // hz
        t = (i % hz) / hz
        data = {"delta": 0 if i == hz * 9 + 7 else 1 / hz, "yaw": (-90, 90, -135, 135)[phase % 4],
                "speed": 0, "rotation": 1, "stance": 0 if phase < 4 else 1,
                "moving": False, "firstPerson": False, "pending": i == 0 or i == hz * 9,
                "allowed": True, "gameWorld": i not in (hz * 8 + 4, hz * 11 + 9), "scale": 1,
                "leftLock": 0, "rightLock": 0, "leftTarget": [0, 0, 0], "leftLocation": [0, 0, 0],
                "rightTarget": [0, 0, 0], "rightLocation": [0, 0, 0]}
        operations = ["RefreshRotateInPlace", "RefreshTurnInPlace", "RefreshDynamicTransitions"]
        if phase < 8 and i % hz == 0:
            operations.insert(0, "InitializeTurnInPlace")
        if 8 <= phase < 11:
            data.update(rotation=2 if phase != 9 else 1, firstPerson=phase == 9,
                        yaw=(-60 if t < .5 else 60) if phase != 9 else (65 if t < .5 else 65.00001),
                        speed=t * 600, moving=phase == 10 and t > .3)
        if phase >= 11:
            data.update(rotation=0, stance=(phase - 11) % 3, yaw=45, allowed=i % 11 != 0,
                        scale=2 if i % 13 == 0 else 1, leftLock=1 if i % 5 else .00001, rightLock=1,
                        leftTarget=[8 if i % 7 == 0 else 10, 0, 0], rightTarget=[11 if i % 3 == 0 else 10, 0, 0])
            if i % 17 == 0:
                operations.remove("RefreshDynamicTransitions")
        if i % 4 == 0:
            operations += operations[-3:]
        frames.append({"input": data, "operations": operations})
    traces.append({"name": str(hz), "frames": frames})
request = {"schemaVersion": 1, "resourceHashes": hashes, "traces": traces}
path = directory / "request.json"
path.write_text(json.dumps(request, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
if not unreal.AlsAnimationGraphLibrary.export_rest_parent_trace(str(path), str(output)):
    raise RuntimeError("Rest Parent native trace failed")
value = json.loads(output.read_text(encoding="utf-8"))
if value["resourceHashes"] != hashes or sum(len(t["frames"]) for t in value["traces"]) != 3360:
    raise RuntimeError("Rest Parent provenance or frame count differs")
output.write_text(json.dumps(value, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log("ALS_REST_PARENT_TRACE_OK traces=3 frames=3360 assets_saved=0")
if os.environ.get("ALS_REST_PARENT_TRACE_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)
