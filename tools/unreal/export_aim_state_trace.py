"""Run the original compiled Aim subgraph on transient ALS AnimBP instances."""
import hashlib
import json
import math
import os
import struct
from pathlib import Path

import unreal


def export():
    root = Path(os.environ["ALS_AIM_STATE_REPOSITORY"])
    output = Path(os.environ["ALS_AIM_STATE_OUTPUT"])
    request_path = Path(os.environ["ALS_AIM_STATE_REQUEST"])
    if not all(p.is_absolute() for p in (root, output, request_path)) or output == request_path:
        raise ValueError("Distinct absolute request/output paths required")
    configs = ("v4_layering_inputs.json", "v4_aim_pose_inputs.json", "v4_aim_sampling.json", "v4_aim_source_inputs.json")
    bindings = {name: hashlib.sha256((root / "assets/config" / name).read_bytes()).hexdigest() for name in configs}
    index = json.loads((root / "assets/config/v4_aim_source_inputs.json").read_bytes())
    for entry in sorted(index["assets"], key=lambda a: a["source"]):
        payload = (root / "assets/config" / entry["file"]).read_bytes()
        if hashlib.sha256(payload).hexdigest().lower() != entry["sha256"].lower():
            raise RuntimeError("Aim source hash differs")
        expected = json.loads(payload)
        asset = unreal.load_asset(entry["source"])
        actual_keys = json.loads(unreal.AlsSourcePoseKeysLibrary.read_source_pose_keys(asset))
        actual_policy = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(asset))
        if actual_policy != expected["evaluation"] or actual_keys != {k: v for k, v in expected.items() if k != "evaluation"}:
            raise RuntimeError("Native source changed: " + entry["source"])
    traces = []
    for hz in (30, 60, 120):
        frames = []
        delta = struct.unpack("f", struct.pack("f", 1 / hz))[0]
        for frame in range(hz * 12):
            seconds = frame / hz
            yaw = math.sin(seconds * 10) * 179
            mode = 0 if seconds < 2 else 1 if seconds < 4 else 2
            if 4 <= seconds < 7.2:
                yaw = -170
            elif 7.2 <= seconds < 8:
                yaw = 0
            elif 8 <= seconds < 9:
                yaw = -170
            elif 9 <= seconds < 10:
                yaw = 170
            elif seconds >= 10:
                mode = (frame // max(1, hz // 5)) % 3
            frames.append({"delta": delta, "mode": mode, "hasInput": frame % hz >= hz // 3,
                           "yaw": yaw, "pitch": math.cos(seconds * 7) * 115,
                           "InputYawOffsetTime": .5 + .6 * math.sin(seconds * 8),
                           "LeftYawTime": .5 - abs(yaw) / 360, "RightYawTime": .5 + abs(yaw) / 360,
                           "ForwardYawTime": .5 + yaw / 360,
                           "relevant": frame >= 3 and not (3.1 <= seconds < 3.3 or 10.4 <= seconds < 10.55),
                           "weight": 0 if frame % 79 == 0 else .35 if frame % 17 == 0 else 1,
                           "inactive": frame % 97 == 0})
        traces.append({"name": "aim_" + str(hz), "hz": hz, "frames": frames})
    request_path.parent.mkdir(parents=True, exist_ok=True)
    output.parent.mkdir(parents=True, exist_ok=True)
    request_path.write_text(json.dumps({"schemaVersion": 1, "bindings": bindings, "traces": traces}, allow_nan=False), encoding="utf-8")
    if not unreal.AlsAnimationGraphLibrary.export_aim_state_trace(str(request_path), str(output)):
        raise RuntimeError("Actual native Aim subgraph trace failed")
    data = json.loads(output.read_bytes())
    if data["bindings"] != bindings or len(data["names"]) != 79 or len(data["traces"]) != 3:
        raise RuntimeError("Incomplete native Aim trace")
    unreal.log("ALS_AIM_STATE_TRACE_EXPORT_OK frames=" + str(sum(len(t["frames"]) for t in data["traces"])) + " assets_saved=0")


export()
if os.environ.get("ALS_AIM_STATE_QUIT") == "1":
    # Leave the startup script before requesting normal Editor shutdown.
    _ticks = 0
    def _quit_after_tick(delta):
        global _ticks
        _ticks += 1
        if _ticks >= 3:
            unreal.unregister_slate_post_tick_callback(_quit_handle)
            unreal.SystemLibrary.quit_editor()
    _quit_handle = unreal.register_slate_post_tick_callback(_quit_after_tick)
