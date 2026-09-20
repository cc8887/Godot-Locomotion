"""Run the actual compiled Overlay machines against controlled frame inputs."""
import hashlib
import json
import os
from pathlib import Path

import unreal


def frame(hz, **changes):
    value = dict(delta=1 / hz, overlay=0, mode=1, gait=1, movement=1,
                 moving=False, enable=1, rotation=0, relevant=True, inactive=False, weight=1)
    value.update(changes)
    return value


def traces():
    result = []
    for hz in (30, 60, 120):
        pairs = []
        for source in range(13):
            for target in range(13):
                pairs.extend((frame(hz, overlay=source), frame(hz, overlay=target)))
        pairs.extend(frame(hz, overlay=i % 13, relevant=i not in (7, 8, 18), weight=0 if i % 5 == 0 else .4,
                           inactive=i % 7 == 0) for i in range(30))
        result.append(dict(name=f"overlay_pairs_{hz}", machine=0, hz=hz, frames=pairs))
        for machine in range(1, 5):
            frames = []
            phases = [(.3, {}), (1, dict(mode=2)), (3.2, dict(enable=.5, rotation=.7)),
                      (.8, dict(enable=1, rotation=0)), (.6, dict(mode=2)), (.2, {}), (.2, dict(mode=2)),
                      (3.2, dict(enable=.5, rotation=.7, moving=True)), (.6, dict(mode=2)),
                      (.4, dict(gait=2)), (.3, dict(mode=2, gait=2)), (.3, dict(movement=2)),
                      (.2, dict(relevant=False)), (.3, dict(mode=2)), (.2, dict(weight=0)),
                      (.2, dict(mode=2, inactive=True)), (.2, {})]
            for seconds, changes in phases:
                frames.extend(frame(hz, **changes) for _ in range(round(seconds * hz)))
            # Exact threshold and zero-delta cases, with a fresh relevance epoch.
            frames.extend([frame(hz, relevant=False), frame(hz, mode=2, delta=0), frame(hz, delta=3),
                           frame(hz, delta=0), frame(hz, delta=.001), frame(hz, delta=0), frame(hz, mode=2)])
            result.append(dict(name=f"weapon_{machine}_{hz}", machine=machine, hz=hz, frames=frames))
    return result


def export():
    root = Path(os.environ["ALS_OVERLAY_REPOSITORY"])
    output = Path(os.environ["ALS_OVERLAY_STATE_OUTPUT"])
    if not root.is_absolute() or not output.is_absolute():
        raise ValueError("Absolute repository and output required")
    source_request = json.loads((root / "artifacts/overlay-source-request.json").read_text(encoding="utf-8-sig"))
    layer = (root / "assets/config/v4_layering_inputs.json").read_bytes().decode("utf-8")
    overlay = (root / "assets/config/v4_overlay_inputs.json").read_bytes().decode("utf-8")
    digest = hashlib.sha256((layer + "\n" + overlay + "\n" + source_request["definitionDigest"]).encode()).hexdigest()
    if digest != source_request["bindingDigest"]:
        raise RuntimeError("Overlay source request is stale")
    request = dict(schemaVersion=1, bindingDigest=digest, traces=traces())
    request_path = root / "artifacts/overlay-state-request.json"
    request_path.write_text(json.dumps(request, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    if not unreal.AlsAnimationGraphLibrary.export_overlay_state_trace(str(request_path), str(output)):
        raise RuntimeError("Native Overlay machine trace failed")
    actual = json.loads(output.read_text(encoding="utf-8-sig"))
    count = sum(len(t["frames"]) for t in request["traces"])
    if (actual["bindingDigest"] != digest or len(actual["traces"]) != 15 or
            sum(len(t["frames"]) for t in actual["traces"]) != count or len(actual["notifies"]) != 8):
        raise RuntimeError("Incomplete native Overlay trace")
    unreal.log(f"ALS_OVERLAY_STATE_EXPORT_OK traces=15 frames={count} assets_saved=0")
    if os.environ.get("ALS_OVERLAY_QUIT") == "1":
        ticks = 0
        handle = None

        def quit_later(_delta):
            nonlocal ticks
            ticks += 1
            if ticks >= 3:
                unreal.unregister_slate_post_tick_callback(handle)
                unreal.SystemLibrary.quit_editor()

        handle = unreal.register_slate_post_tick_callback(quit_later)


export()
