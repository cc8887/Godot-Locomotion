"""Independent target-rig checks against immutable native locomotion sources.

UE centimeter/Z-up data is reconstructed at the original authored speed. Sole
proxies are fixed in the immutable skeleton bind pose; generated curves never
choose the samples used to measure skating. Runtime skin/ground checks remain
mandatory: these FK proxies are not a substitute for them.
"""
from __future__ import annotations

import hashlib
import json
from functools import lru_cache
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
CONFIG = ROOT / "assets/config"
POLICY = "als-forward-v1"
PILOTS = {
    "A_Als_Walk_Forward": (150.0, "walk"),
    "A_Als_Run_Forward": (350.0, "run"),
    "A_Als_Crouch_Walk_Forward": (150.0, "crouch"),
    "ALS_N_Walk_F": (150.0, "walk"),
    "ALS_N_Run_F": (350.0, "run"),
    "ALS_CLF_Walk_F": (150.0, "crouch"),
}
DIRECTIONS = {name: (0.,1.) for name in PILOTS}
FORWARD_KINDS = {'walk','run','crouch'}
for prefix,short,speed in (('Walk','Walk',150.),('Run','Run',350.),('Crouch_Walk','',150.)):
    for side,symbol in (('Left','L'),('Right','R')):
        for variant,suffix in (('Forward','F'),('Backward','B')):
            semantic=prefix+'_'+side+'_'+variant
            kind=semantic.lower()
            native='A_Als_'+semantic
            PILOTS[native]=(speed,kind)
            DIRECTIONS[native]=(1. if side=='Left' else -1.,0.)
            if short:
                legacy='ALS_N_'+short+'_'+symbol+suffix
                PILOTS[legacy]=(speed,kind);DIRECTIONS[legacy]=DIRECTIONS[native]
PILOTS.update(A_Als_Sprint_Acceleration=(500.,'sprint_acceleration'),
              ALS_N_Sprint_F_Impulse=(500.,'sprint_acceleration'))
DIRECTIONS.update(A_Als_Sprint_Acceleration=(0.,1.),ALS_N_Sprint_F_Impulse=(0.,1.))
ORIGINALS = {kind: name for name, (_, kind) in PILOTS.items() if name.startswith("A_Als_")}


def asset_name(source):
    return source.rsplit(".", 1)[-1]


def selected_stop_rule(value):
    """Priority order from AlsRefactoredStopRules, including asymmetric .5."""
    return ("LockRight" if value > .5 else "LockLeft" if value <= -.5
            else "PlantRight" if value > 0 else "PlantLeft")


def _read(path, hashes):
    data = path.read_bytes()
    hashes[path.relative_to(ROOT).as_posix()] = hashlib.sha256(data).hexdigest()
    return json.loads(data.decode("utf-8-sig"))


def _curve_samples(payload, name):
    curve = next((c for c in payload["curves"]["curves"] if c["name"] == name), None)
    if curve is None:
        return None
    keys = curve["keys"]
    time = np.linspace(0, payload["raw"]["playLength"], payload["raw"]["sampledKeyCount"])
    return np.interp(time, [k["time"] for k in keys], [k["value"] for k in keys])


def _sole_geometry(raw, skeleton, speed_cm_s, direction=(0.,1.)):
    # Imported lazily to let publish consume this module without an import cycle.
    from .publish import world_pose
    world = world_pose(raw, skeleton)
    bind = world_pose(dict(sampledKeyCount=1, tracks=[]), skeleton)
    points = []
    for side in ("l", "r"):
        foot = []
        for bone in ("foot_" + side, "ball_" + side):
            rotation, position, _ = world[bone]
            bind_r, bind_p, _ = bind[bone]
            offset = bind_r[0].T @ np.array([0., 0., -bind_p[0, 2]])
            foot.append(position + np.einsum("tij,j->ti", rotation, offset))
        points.append(foot)
    points = np.transpose(np.asarray(points), (2, 0, 1, 3))
    step = raw["playLength"] / (raw["sampledKeyCount"] - 1)
    velocity = np.gradient(points, step, axis=0)
    velocity[..., :2] += speed_cm_s*np.asarray(direction)
    return points, velocity, world


@lru_cache(maxsize=1)
def original_protocol():
    """Calibrate against immutable original exports, never a staged pack."""
    hashes = {}
    index = _read(CONFIG / "refactored_animation_sources.json", hashes)
    sync = _read(CONFIG / "refactored_sync_inputs.json", hashes)
    skeleton = index["skeletons"]["/ALS/ALS/Character/SK_Als.SK_Als"]
    assets = {}
    flat_heights = []
    for kind, name in ORIGINALS.items():
        row = next(r for r in index["assets"] if asset_name(r["source"]) == name)
        payload = _read(CONFIG / row["file"], hashes)
        if hashes[(CONFIG / row["file"]).relative_to(ROOT).as_posix()] != row["sha256"]:
            raise ValueError("Changed immutable gait protocol payload: " + name)
        speed = PILOTS[name][0]
        points, velocity, world = _sole_geometry(payload["raw"], skeleton, speed,DIRECTIONS[name])
        values = _curve_samples(payload, "FootPlanted")
        asset_flat=[]
        if values is not None:
            for side, sign in ((0, -1), (1, 1)):
                # Only original authored full support calibrates the proxy.
                mask = np.isclose(values, sign, atol=1e-5)
                if not mask.any():
                    raise ValueError("Original protocol lacks full support: " + name)
                asset_flat.extend(points[mask,side,:,2].tolist())
                if kind in FORWARD_KINDS:
                    flat_heights.extend(points[mask, side, :, 2].tolist())
        forefoot=kind=='sprint_acceleration'
        if not asset_flat and forefoot:
            # Native acceleration plants its toes with the heels raised by
            # 3.55cm or more; a flat-sole Walk contract is the wrong consumer.
            slow=np.linalg.norm(velocity[...,:2],axis=-1)<=25.
            low=np.abs(points[...,2])<=.4
            for side in range(2):
                if np.count_nonzero((slow&low)[:,side,1])<3:
                    raise ValueError('Native Sprint lacks measured forefoot support')
            asset_flat=[[.3,.3]]
        if not asset_flat and not forefoot:
            slow=np.all(np.linalg.norm(velocity[...,:2],axis=-1)<=25.,axis=2)
            low=np.all(np.abs(points[...,2])<=3.5,axis=2)
            asset_flat.extend(points[...,2][slow&low].tolist())
        if not asset_flat:
            raise ValueError('Native protocol has no geometric flat support: '+name)
        speed_xy = np.linalg.norm(velocity[..., :2], axis=-1)
        medians = []
        for side in range(2):
            near = points[:, side, :, 2] <= 3.5
            medians.append(float(np.median(speed_xy[:, side][near])))
        marker = next(r for r in sync["assets"] if r["source"] == row["source"])
        assets[kind] = dict(source=row["source"], raw=payload["raw"], payload=payload,
                            expectedSpeedCmS=speed, travelDirectionXY=DIRECTIONS[name],nearGroundMedianCmS=medians,
                            flatHeightCm=(np.max(np.abs(asset_flat),axis=0)+.1).tolist(),
                            contactMode='toe' if forefoot or kind.startswith('run_') and kind.endswith('backward') else 'flat_sole',
                            landingSample=1 if forefoot or kind.endswith('backward') else 0,
                            bodyOriginLimitCm=max(15., float(np.max(np.linalg.norm(
                                world["pelvis"][1][:, :2]-world["root"][1][:, :2], axis=1)))+10.),
                            markers=marker["markers"], curveValues=None if values is None else values.tolist())
    # A millimeter of numeric/retarget tolerance above the ORIGINAL maxima.
    flat_height = (np.max(np.abs(flat_heights), axis=0) + .1).tolist()
    for path in (ROOT / "src/Als.Core/Locomotion/AlsRefactoredStopRule.cs",
                 ROOT / "src/Als.Import/Compilation/AlsRefactoredStopEvaluators.cs"):
        data = path.read_bytes()
        hashes[path.relative_to(ROOT).as_posix()] = hashlib.sha256(data).hexdigest()
    return dict(version=POLICY, assets=assets, skeleton=skeleton, sourceHashes=hashes,
                flatHeightCm=flat_height, nearGroundHeightCm=3.5,
                stableSpeedCmS=25., minimumStableFrames=3,
                heelContactHeightCm=.4, halfCycleTolerance=.1,
                penetrationLimitCm=2.)


def _runs(mask):
    """The supported pilots have a Plant interval at the repeated boundary."""
    result = []
    start = None
    for frame, value in enumerate(np.r_[mask, False]):
        if value and start is None:
            start = frame
        elif not value and start is not None:
            result.append((start, frame - 1))
            start = None
    return result


def analyze_target_gait(raw, skeleton, source, protocol=None):
    name = asset_name(source)
    if name not in PILOTS:
        raise ValueError("Unsupported native target gait: " + name)
    protocol = original_protocol() if protocol is None else protocol
    speed, kind = PILOTS[name]
    original = protocol["assets"][kind]
    points, velocity, world = _sole_geometry(raw, skeleton, speed,DIRECTIONS[name])
    height = points[..., 2]
    planar_speed = np.linalg.norm(velocity[..., :2], axis=-1)
    flat_height=protocol['flatHeightCm'] if kind in FORWARD_KINDS else original['flatHeightCm']
    sample_stable=(np.abs(height)<=flat_height)&(planar_speed<=protocol['stableSpeedCmS'])
    forefoot=original['contactMode']=='toe'
    stable=sample_stable[...,1] if forefoot else np.all(sample_stable,axis=2)
    checks = []
    def check(name, passed, **observed):
        checks.append(dict(check=name, passed=bool(passed), **observed))
    count = raw["sampledKeyCount"]
    length = raw["playLength"]
    check("native_time_domain", count == original["raw"]["sampledKeyCount"] and
          abs(length-original["raw"]["playLength"]) < 1e-6,
          sampledKeyCount=count, playLength=length)
    check("finite_geometry", np.isfinite(points).all() and np.isfinite(velocity).all())
    root_positions=world["root"][1]
    check("in_place_root", np.max(np.abs(root_positions)) <= 1e-4,
          maximumPositionCm=float(np.max(np.abs(root_positions))),limitCm=1e-4)
    offset = np.linalg.norm(world["pelvis"][1][:, :2] - world["root"][1][:, :2], axis=1)
    check("body_origin", np.max(offset) <= original["bodyOriginLimitCm"],
          maximumOffsetCm=float(np.max(offset)), limitCm=original["bodyOriginLimitCm"])
    check("sole_penetration", np.min(height) >= -protocol["penetrationLimitCm"],
          minimumHeightCm=float(np.min(height)), limitCm=protocol["penetrationLimitCm"])
    events = []
    runs = []
    qualified_stable=np.zeros_like(stable)
    for side, label in enumerate(("left", "right")):
        blocks = _runs(stable[:-1, side])
        if kind not in FORWARD_KINDS and len(blocks)>1 and blocks[0][0]==0 and blocks[-1][1]==count-2:
            blocks=[(blocks[-1][0],blocks[0][1]+count-1),*blocks[1:-1]]
        valid = [block for block in blocks if block[1]-block[0]+1 >= protocol["minimumStableFrames"]]
        runs.append(valid)
        for start,end in valid:qualified_stable[np.arange(start,end+1)%(count-1),side]=True
        qualified_stable[-1,side]=qualified_stable[0,side]
        check(label+"_stable_support", len(valid) == 1, runs=blocks,
              minimumFrames=protocol["minimumStableFrames"])
        near = height[:, side] <= protocol["nearGroundHeightCm"]
        samples = planar_speed[:, side][near]
        median = float(np.median(samples)) if samples.size else None
        limit = max(25., original["nearGroundMedianCmS"][side] * 2.)
        check(label+"_independent_near_ground_sliding", len(samples) >= 3 and median <= limit,
              samples=int(samples.size), medianCmS=median, limitCmS=limit)
        if len(valid) != 1:
            continue
        start, end = valid[0]
        heel = height[:, side, original['landingSample']]
        # Include the segment beginning at the first stable sample. Its heel
        # can still be within the original flat-foot tolerance while crossing
        # the stricter contact-event plane before the next sample.
        crossings = [f for f in range(start-count//2 if kind not in FORWARD_KINDS else max(0,start-count//2),start+1)
                     if heel[f%(count-1)] > protocol["heelContactHeightCm"] >= heel[(f+1)%(count-1)]]
        check(label+"_heel_contact_event", bool(crossings), fullSupportStart=start)
        if not crossings:
            continue
        frame = crossings[-1]
        before,after=heel[frame%(count-1)],heel[(frame+1)%(count-1)]
        fraction=(before-protocol['heelContactHeightCm'])/(before-after)
        event_frame=(frame+float(fraction))%(count-1)
        events.append(dict(side=label, frame=event_frame, time=event_frame*length/(count-1),
                           stableStart=start, stableEnd=end))
    if len(events) == 2:
        phase_gap = abs(events[0]["time"]-events[1]["time"])/length
        check("alternating_half_cycle", abs(phase_gap-.5) <= protocol["halfCycleTolerance"],
              observedFraction=phase_gap, expected=.5, tolerance=protocol["halfCycleTolerance"])
    else:
        check("alternating_half_cycle", False, eventCount=len(events))
    values = np.zeros(count)
    stages = []
    starts = [(block[0], side) for side, blocks in enumerate(runs) for block in blocks]
    for frame in range(count):
        supported = np.flatnonzero(qualified_stable[frame])
        if len(supported) == 1:
            side = int(supported[0]); values[frame] = -1 if side == 0 else 1
            stages.append(("left" if side == 0 else "right")+"_stable")
        elif starts:
            _, side = min(starts, key=lambda item: (item[0]-frame) % (count-1))
            values[frame] = -.25 if side == 0 else .25
            stages.append(("left" if side == 0 else "right")+"_plant")
        else:
            stages.append("unresolved")
    fixed = []
    # These are the original standing Stop SequenceEvaluator reads; Run and
    # Crouch do not acquire invented fixed frame consumers.
    if name == "A_Als_Walk_Forward":
        for frame, side, rule in ((4, 0, "LockLeft"), (21, 1, "LockRight")):
            passed = frame < count and stable[frame, side] and selected_stop_rule(values[frame]) == rule
            row = dict(frame=frame, support=("left", "right")[side],
                       selectedRule=selected_stop_rule(values[frame]), expectedRule=rule,
                       soleHeightsCm=height[frame, side].tolist(),
                       soleSpeedsCmS=planar_speed[frame, side].tolist())
            check("original_stop_frame_"+str(frame), passed, **row)
            fixed.append(row)
    digest=lambda value:hashlib.sha256(json.dumps(value,sort_keys=True,separators=(',',':'),allow_nan=False).encode()).hexdigest()
    return dict(schemaVersion=1, policy=POLICY, source=source, passed=all(c["passed"] for c in checks),
                checkerSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                targetRawContentSha256=digest(raw),targetSkeletonContentSha256=digest(skeleton),
                measurement="bind-pose heel/ball FK proxies; target skin runtime acceptance remains required",
                expectedSpeedCmS=speed,travelDirectionXY=DIRECTIONS[name], checks=checks, events=events, stages=stages,
                footPlanted=values.tolist(), contacts=qualified_stable.T.astype(float).tolist(),
                fixedStopFrames=fixed, sourceHashes=protocol["sourceHashes"],
                geometry=dict(soleHeightsCm=height.tolist(), solePlanarSpeedsCmS=planar_speed.tolist()),
                thresholds=dict({k: v for k, v in protocol.items() if k not in ("assets", "skeleton", "sourceHashes")},
                                bodyOriginLimitCm=original['bodyOriginLimitCm']))


def validate_als_protocol(payload, report, markers):
    """Audit exported metadata using geometry and the independent native oracle."""
    checks = []
    values = _curve_samples(payload, "FootPlanted")
    kind=PILOTS[asset_name(report['source'])][1]
    expects_curve=original_protocol()['assets'][kind]['curveValues'] is not None
    checks.append(dict(check='native_support_curve_presence',passed=(values is not None)==expects_curve,
                       expected=expects_curve,actual=values is not None))
    if values is not None:
        checks.append(dict(check="four_native_stop_branches", passed=
                           {selected_stop_rule(v) for v in values} == {"LockLeft", "LockRight", "PlantLeft", "PlantRight"}))
        for side, sign in (("left", -1), ("right", 1)):
            frames = [i for i, stage in enumerate(report["stages"]) if stage == side+"_stable"]
            bad = [i for i in frames if abs(values[i]-sign) > 1e-5]
            checks.append(dict(check=side+"_full_support_encoding", passed=bool(frames) and not bad,
                               stableFrames=frames, badFrames=bad))
        for row in report["fixedStopFrames"]:
            actual = selected_stop_rule(values[row["frame"]])
            checks.append(dict(check="fixed_stop_curve_"+str(row["frame"]), passed=actual == row["expectedRule"],
                               actual=actual, expected=row["expectedRule"]))
    expected_names = {"left", "right"}
    checks.append(dict(check="marker_sides", passed=len(markers) == 2 and
                       {m["name"].lower() for m in markers} == expected_names))
    step = payload["raw"]["playLength"]/(payload["raw"]["sampledKeyCount"]-1)
    for event in report["events"]:
        rows = [m for m in markers if m["name"].lower() == event["side"]]
        error = abs(rows[0]["time"]-event["time"])/step if len(rows) == 1 else None
        checks.append(dict(check=event["side"]+"_marker_event", passed=error is not None and error <= 1.,
                           errorFrames=error, limitFrames=1.))
    return dict(passed=all(c["passed"] for c in checks), checks=checks)


def audit_pack(config):
    config = Path(config)
    index = json.loads((config/"refactored_animation_sources.json").read_text(encoding="utf-8-sig"))
    sync = json.loads((config/"refactored_sync_inputs.json").read_text(encoding="utf-8-sig"))
    skeleton = index["skeletons"]["/ALS/ALS/Character/SK_Als.SK_Als"]
    reports = []
    for name in ORIGINALS.values():
        row = next(r for r in index["assets"] if asset_name(r["source"]) == name)
        data = (config/row["file"]).read_bytes()
        if hashlib.sha256(data).hexdigest() != row["sha256"]:
            raise ValueError("Target payload digest differs: " + name)
        payload = json.loads(data.decode("utf-8-sig"))
        report = analyze_target_gait(payload["raw"], skeleton, row["source"])
        markers = next(r["markers"] for r in sync["assets"] if r["source"] == row["source"])
        report["protocol"] = validate_als_protocol(payload, report, markers)
        report["payloadSha256"] = row["sha256"]
        report["passed"] &= report["protocol"]["passed"]
        reports.append(report)
    return dict(policy=POLICY, config=str(config.resolve()), passed=all(r["passed"] for r in reports), assets=reports)


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    result = audit_pack(args.config)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2)+"\n", encoding="utf-8")
    print(json.dumps(dict(passed=result["passed"], assets=[dict(source=a["source"], passed=a["passed"],
        failed=[c["check"] for c in a["checks"]+a["protocol"]["checks"] if not c["passed"]]) for a in result["assets"]])))
    raise SystemExit(0 if result["passed"] else 1)
