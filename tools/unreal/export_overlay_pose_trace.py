"""Complete native Overlay graph poses; native source times isolate the pose/update graph."""
import hashlib
import json
import os
from pathlib import Path

import unreal


def traces():
    result = []
    for overlay in range(13):
        for override in range(4):
            frames = []
            for phase in range(18):
                aim = phase in (3, 4, 5, 8, 11, 15)
                frames.append(dict(
                    delta=(0 if phase in (0, 1, 11) else 1 if phase == 6 else 3.01 if phase == 9 else 1 / 60),
                    overlay=overlay if phase < 14 else (overlay + phase - 13) % 13,
                    mode=2 if aim else 1, gait=2 if phase == 12 else 1, movement=2 if phase == 13 else 1,
                    moving=phase % 4 == 2, enable=1 if phase % 3 else .5, rotation=0 if phase % 3 else .7,
                    relevant=phase != 10, inactive=phase == 16, weight=0 if phase == 17 else 1,
                    baseN=(1, .35, 0)[phase % 3], baseClf=(0, .65, 1)[phase % 3],
                    velocity=([1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1], [.1, .2, .3, .4], [0, 0, 0, 0])[phase % 6],
                    acceleration=[(phase % 5 - 2) * .12, .3, -.2], landPrediction=(phase % 5) * .25,
                    aimSweepTime=(phase % 7) / 6, override=override if phase < 7 else (override + phase) % 4,
                    weightGait=(0, 1, 1.5, 2, 2.5, 3)[phase % 6], weightInAir=(phase % 4) / 3))
            result.append(dict(name=f"overlay_{overlay}_override_{override}", machine=0, hz=60, frames=frames))
    # The idle end of the aiming sweep requires aiming and Weight_Gait=0 in
    # the same frame; alternating those independently never reaches this leaf.
    for overlay in (9, 10):
        idle_aim = dict(result[overlay * 4]["frames"][0], mode=2, override=0)
        result.append(dict(name=f"idle_aim_{overlay}", machine=0, hz=60, frames=[idle_aim]))
    return result


def export():
    root = Path(os.environ["ALS_OVERLAY_REPOSITORY"])
    output = Path(os.environ["ALS_OVERLAY_POSE_OUTPUT"])
    if not root.is_absolute() or not output.is_absolute():
        raise ValueError("Absolute paths required")
    source_request = json.loads((root / "artifacts/overlay-source-request.json").read_text(encoding="utf-8-sig"))
    layer_text = (root / "assets/config/v4_layering_inputs.json").read_bytes().decode("utf-8")
    overlay_text = (root / "assets/config/v4_overlay_inputs.json").read_bytes().decode("utf-8")
    digest = hashlib.sha256((layer_text + "\n" + overlay_text + "\n" + source_request["definitionDigest"]).encode()).hexdigest()
    if digest != source_request["bindingDigest"]:
        raise RuntimeError("Stale Overlay binding")
    nodes = sorted((n for n in json.loads(layer_text)["compiledNodeInventory"]
                    if ":OverlayLayer." in n["path"] and n["assetPlayer"]), key=lambda n: n["compiledNodeIndex"])
    sources = [dict(index=n["compiledNodeIndex"], evaluator=n["evaluator"], path=n["path"]) for n in nodes]
    request = dict(schemaVersion=1, bindingDigest=digest, capturePose=True, sources=sources, traces=traces())
    request_path = root / "artifacts/overlay-pose-request.json"
    request_path.write_text(json.dumps(request, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    if not unreal.AlsAnimationGraphLibrary.export_overlay_state_trace(str(request_path), str(output)):
        raise RuntimeError("Native Overlay pose export failed")
    result = json.loads(output.read_text(encoding="utf-8-sig"))
    rows = [row for trace in result["traces"] for row in trace["frames"]]
    poses = [row for row in rows if row["input"]["relevant"]]
    if (result["bindingDigest"] != digest or len(rows) != 938 or len(poses) != 886 or
            len(result["bones"]) != 79 or result["sources"] != sources or
            any(len(row["pose"]) != 79 or len(row["sourceTimes"]) != 148 for row in poses)):
        raise RuntimeError("Incomplete native Overlay poses")
    unreal.log("ALS_OVERLAY_POSE_EXPORT_OK traces=54 frames=938 poses=886 sources=148 assets_saved=0")
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
