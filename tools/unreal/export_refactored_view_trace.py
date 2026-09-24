"""Run original View/Spine and explicit Head callbacks; no asset saves."""
import hashlib
import json
import math
import os
from pathlib import Path
import unreal

root = Path(__file__).parents[2]
output = Path(os.environ["ALS_VIEW_TRACE_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
traces = []
for hz in (30, 60, 120):
    frames, previous_update = [], False
    for frame in range(hz * 5):
        phase = frame * 10 // (hz * 5)
        delta = 0 if phase == 9 and frame % 5 == 0 else 1 / hz * (.1 if phase == 3 else 1)
        character_yaw = frame * .8
        yaw = character_yaw + (179 if phase == 5 else 170 * math.sin(frame / hz * 4))
        value = dict(ViewYaw=yaw, ViewPitch=50 * math.cos(frame / hz * 3), CharacterYaw=character_yaw,
                     CharacterPitch=5, ViewYawSpeed=200 if phase == 2 else 20, CharacterYawVelocity=-30 if phase == 4 else 0,
                     InputYaw=character_yaw + 179, TargetYaw=character_yaw - 60, HasInput=phase in (4, 5),
                     HasAction=phase == 6, RotationMode=2 if phase == 1 else 0 if phase in (4, 5) else 1,
                     FirstPerson=phase == 3, PendingUpdate=frame == 0, RelativeBaseRotation=phase in (2, 4, 8),
                     BaseDeltaYaw=.7, Delta=delta, RealDelta=1 / hz, ViewBlock=1 if phase == 7 else -.2,
                     PoseAiming=.8 if phase in (1, 2) else 0)
        update = phase != 7
        frames.append(dict(input=value, initializeHead=update and not previous_update, updateHead=update))
        previous_update = update
    traces.append(dict(name=f"{hz}hz", frames=frames))
request = dict(schemaVersion=1, inputsSha256=hashlib.sha256((root / "assets/config/refactored_head_inputs.json").read_bytes()).hexdigest(), traces=traces)
output.parent.mkdir(parents=True, exist_ok=True)
request_path = output.with_suffix(".request.json")
request_path.write_text(json.dumps(request, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
if not unreal.AlsAnimationGraphLibrary.export_refactored_view_trace(str(request_path), str(output)):
    raise RuntimeError("Native View trace failed")
if os.environ.get("ALS_VIEW_TRACE_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)
