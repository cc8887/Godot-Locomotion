"""Whole V4 AnimGraph fixture. Controlled properties, not a character-input oracle."""
import hashlib
import json
import math
import os
import time
from pathlib import Path

import unreal


VELOCITY_FIELDS = (
    "F_3_2154ABAD4BD15DAC904154B63D704219",
    "B_5_0A0855774CB13BB3E4B0A6847E7154F6",
    "L_8_DFEBB8584D28F158D2562CA60EB07B6D",
    "R_9_79E6E09B4A52B442B9FE6DB7192CFBEE",
)


def make_traces():
    traces = []
    for hz in (30, 60, 120):
        for first in (2, 3):
            frames = []
            for step in range(3 * hz):
                time = step / hz
                moving = .5 <= time < 2.5
                direction = first if time < 1.5 else 5 - first
                velocity = [0., 0., 0., 0.]
                if moving:
                    velocity[direction] = 1.
                properties = dict(
                    MovementState=1, Stance=0, RotationMode=1, Gait=1, OverlayState=0,
                    ShouldMove=moving, IsMoving=moving, HasMovementInput=moving,
                    # VelocityBlend is F/B/L/R; the native direction enum is
                    # Forward/Right/Left/Backward. These are different orders.
                    Speed=350. if moving else 0., MovementDirection=2 if direction == 2 else 1,
                    WalkRunBlend=1., StrideBlend=1., StandingPlayRate=1.,
                    BasePose_N=1., BasePose_CLF=0.,
                    VelocityBlend=dict(zip(VELOCITY_FIELDS, velocity)),
                )
                frames.append(dict(delta=1 / hz, properties=properties))
            traces.append(dict(name=f"controlled_strafe_{first}_{hz}", frames=frames))
    return traces


def validate(request, result):
    if (result["schemaVersion"] != 1 or len(result["names"]) != 79 or
            result["requestDigest"] != request["requestDigest"] or
            len(result["traces"]) != len(request["traces"]) or
            not result["source"].startswith("/Game/AdvancedLocomotionV4/")):
        raise RuntimeError("Wrong whole-graph provenance or shape")
    if result.get('dispatchStopNotifies', False) != request.get('dispatchStopNotifies', False):
        raise RuntimeError('Stop montage lifecycle mode differs')
    if result.get('runIdleControls', False) != request.get('runIdleControls', False):
        raise RuntimeError('Idle control execution mode differs')
    if result.get('captureUpperStages', False) != request.get('captureUpperStages', False):
        raise RuntimeError('Upper stage capture mode differs')
    count = 0
    for expected, trace in zip(request["traces"], result["traces"]):
        if expected["name"] != trace["name"] or len(expected["frames"]) != len(trace["frames"]):
            raise RuntimeError("Incomplete whole-graph trace")
        previous = {}
        changed = False
        initial = trace["frames"][0]["pose"]
        for serial, (input_frame, row) in enumerate(zip(expected["frames"], trace["frames"]), 1):
            if (row["serial"] != serial or row["input"] != input_frame or
                    len(row["pose"]) != 79 or row["previousCurves"] != previous or
                    not row["players"] or not row["machines"]):
                raise RuntimeError(f"Incomplete pose, sources or feedback: {trace['name']}:{serial}")
            for bone in row["pose"]:
                if any(not math.isfinite(v) for key in ("position", "rotation", "scale") for v in bone[key]):
                    raise RuntimeError("Nonfinite final pose")
                if abs(sum(v*v for v in bone["rotation"]) - 1) > .002:
                    raise RuntimeError("Non-unit final rotation")
            if any(not math.isfinite(v) for v in row["curves"].values()):
                raise RuntimeError("Nonfinite final curve")
            if request.get('captureStages', False):
                expected_stages = {'MainMovement', 'BaseLayer'}
                if request.get('captureUpperStages', False):
                    expected_stages |= {'PostLayering', 'PostAim'}
                if set(row.get('stages', {})) != expected_stages:
                    raise RuntimeError('Stage capture set differs')
                for name, stage in row['stages'].items():
                    if stage['evaluations'] != 1 or len(stage['pose']) != 79:
                        raise RuntimeError(f'Invalid native stage evaluation: {name}:{serial}')
            changed |= row["pose"] != initial
            previous = row["curves"]
            count += 1
        if not changed:
            raise RuntimeError("Whole graph never changed pose")
    return count


def export():
    output = Path(os.environ["ALS_FULL_GRAPH_OUTPUT"])
    if not output.is_absolute():
        raise ValueError("ALS_FULL_GRAPH_OUTPUT must be absolute")
    output.parent.mkdir(parents=True, exist_ok=True)
    replay_path = os.environ.get("ALS_FULL_GRAPH_REQUEST")
    dispatch_stop = os.environ.get('ALS_FULL_GRAPH_STOP') == '1'
    if replay_path:
        replay = Path(replay_path)
        if not replay.is_absolute() or replay.resolve() in (output.resolve(), output.with_suffix(".request.json").resolve()):
            raise ValueError("Replay request must be absolute and separate from generated outputs")
        replay_data = json.loads(replay.read_text(encoding="utf-8-sig"))
        traces = replay_data["traces"]
        dispatch_stop |= replay_data.get('dispatchStopNotifies', False)
    else:
        traces = make_traces()
    digest = hashlib.sha256(json.dumps(traces, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    request = dict(schemaVersion=1, requestDigest=digest, traces=traces)
    if replay_path and replay_data.get('runIdleControls', False):
        request['runIdleControls'] = True
    if dispatch_stop:
        request['dispatchStopNotifies'] = True
    if os.environ.get("ALS_FULL_GRAPH_STAGES") == "1":
        request["captureStages"] = True
    if os.environ.get("ALS_FULL_GRAPH_UPPER") == "1":
        request["captureStages"] = True
        request["captureUpperStages"] = True
    request_path = output.with_suffix(".request.json")
    request_path.write_text(json.dumps(request, separators=(",", ":")), encoding="utf-8")
    if not unreal.AlsAnimationGraphLibrary.export_full_graph_trace(str(request_path), str(output)):
        raise RuntimeError("Native whole-graph export failed")
    result = json.loads(output.read_text(encoding="utf-8-sig"))
    count = validate(request, result)
    content = Path(unreal.Paths.project_content_dir())
    provenance = {
        "requestDigest": digest,
        "assets": {str(path.relative_to(content)): hashlib.sha256(path.read_bytes()).hexdigest()
                   for path in (content / "AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.uasset",
                                content / "AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/Mannequin.uasset")},
        "engine": result["engine"], "frames": count, "scope": result["scope"],
        "exportScriptSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        "outputSha256": hashlib.sha256(output.read_bytes()).hexdigest(),
    }
    output.with_suffix(".provenance.json").write_text(json.dumps(provenance, indent=2), encoding="utf-8")
    unreal.log(f"ALS_FULL_GRAPH_EXPORT_OK traces={len(traces)} frames={count} bones=79 assets_saved=0 scope=controlled_properties")


try:
    export()
finally:
    if os.environ.get("ALS_FULL_GRAPH_QUIT") == "1":
        # Normal Editor needs deferred shutdown after Python wrappers unwind.
        _deadline = time.monotonic() + 10
        _handle = None

        def _quit_later(_delta):
            if time.monotonic() >= _deadline:
                unreal.unregister_slate_post_tick_callback(_handle)
                unreal.SystemLibrary.quit_editor()

        _handle = unreal.register_slate_post_tick_callback(_quit_later)
