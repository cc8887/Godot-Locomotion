"""Event-driven SOMA gait authoring. No endpoint pose blending is performed.

The SOMA landmark gate is an authoring diagnostic, not target-skin or runtime
acceptance. An estimated heel is explicitly identified when a rig has no sole
landmarks. Final target geometry must be measured independently by the publisher.
"""
from __future__ import annotations

import hashlib
import json
import math

import numpy as np
from scipy.spatial.transform import Rotation, Slerp

from .motion import complete, fk, validate_motion


DEFAULT_POLICY = {
    'version': 'soma-gait-authoring-v3-unique-period',
    'acceptanceScope': 'authoring_landmarks_only',
    'groundHeightMeters': 0.,
    'contactEnterHeightMeters': .035,
    'contactLeaveHeightMeters': .055,
    'contactEnterSpeedMps': .25,
    'contactLeaveSpeedMps': .35,
    'contactVerticalSpeedMps': .35,
    'minimumContactSeconds': .08,
    'minimumCycleSeconds': .35,
    'maximumCycleSeconds': 2.,
    'minimumCompleteCycles': 2,
    'oppositePhaseRange': [.35, .65],
    'maximumPeriodRelativeDeviation': .25,
    'maximumSpeedRelativeError': .25,
    'maximumRetimingRatio': 1.35,
    'maximumSeamRotationDegrees': 20.,
    'maximumSeamBodyMeters': .06,
    'maximumSeamRootHeightMeters': .035,
    'maximumSeamVelocityMps': .75,
    'maximumSeamAngularVelocityRps': 6.,
    'maximumNearGroundMedianSpeedMps': .25,
    'maximumPenetrationMeters': .03,
    'minimumNearGroundSamplesPerFoot': 3,
}

STAGE_VALUES = {'left_stable': -1., 'right_stable': 1.,
                'plant_left': -.25, 'plant_right': .25}


class GaitValidationError(ValueError):
    def __init__(self, receipt):
        self.receipt = receipt
        failures = [c['name'] for c in receipt.get('checks', []) if not c['pass']]
        super().__init__('Gait authoring rejected: ' + ', '.join(failures))


def _policy(override):
    result = dict(DEFAULT_POLICY)
    if override:
        unknown = set(override) - set(result)
        if unknown:
            raise ValueError('Unknown gait policy keys: ' + ', '.join(sorted(unknown)))
        result.update(override)
    for key, value in result.items():
        if isinstance(value, (int, float)) and (not math.isfinite(value) or
                                               (key != 'groundHeightMeters' and value <= 0)):
            raise ValueError('Invalid gait policy value: ' + key)
    if result['contactEnterHeightMeters'] > result['contactLeaveHeightMeters']:
        raise ValueError('Contact height hysteresis is inverted')
    if result['contactEnterSpeedMps'] > result['contactLeaveSpeedMps']:
        raise ValueError('Contact speed hysteresis is inverted')
    for key in ('minimumCompleteCycles', 'minimumNearGroundSamplesPerFoot'):
        if not isinstance(result[key], int):
            raise ValueError('Gait count must be an integer: ' + key)
    phase = result['oppositePhaseRange']
    if len(phase) != 2 or not 0 < phase[0] < phase[1] < 1:
        raise ValueError('Invalid opposite-foot phase interval')
    return result


def _array_hash(*arrays):
    h = hashlib.sha256()
    for array in arrays:
        array = np.ascontiguousarray(array, dtype='<f8')
        h.update(str(array.shape).encode('ascii'))
        h.update(array.tobytes())
    return h.hexdigest()


def _check(name, passed, observed, limit=None):
    return {'name': name, 'pass': bool(passed), 'observed': observed, 'limit': limit}


def _runs(mask):
    changes = np.diff(np.r_[False, np.asarray(mask, bool), False].astype(int))
    return list(zip(np.flatnonzero(changes == 1).tolist(),
                    np.flatnonzero(changes == -1).tolist()))


def foot_landmarks(motion, rig):
    """Return world-space [frame, side(left/right), sample(heel/toe), XYZ].

    Rigs can supply footLandmarks[side][heel/toe] = {joint, offset:[x,y,z]}.
    Native SOMA has no heel joint; the fallback is a neutral-pose sole proxy,
    never described as a measured skin vertex or an exact heel bone.
    """
    validate_motion(motion, rig)
    global_rot, joints = fk(motion['local_rot_mats'], motion['root_positions'], rig)
    lookup = {name: i for i, name in enumerate(rig['jointNames'])}
    definitions = rig.get('footLandmarks')
    geometry = 'explicit_rig_landmarks'
    if definitions is None:
        # Offsets were built from SOMA neutral_joints, whose native rest local
        # rotations are identity. BVH standard-T-pose offsets are a coordinate
        # conversion, not a physical pose to apply during sole calibration.
        neutral_local = np.tile(np.eye(3), (1, len(rig['jointNames']), 1, 1))
        neutral_global, _ = fk(neutral_local, np.zeros((1, 3)), rig)
        _, neutral_pos = fk(neutral_local, np.zeros((1, 3)), rig)
        definitions = {}
        for side in ('left', 'right'):
            prefix = side.title()
            foot, toe = lookup[prefix + 'Foot'], lookup[prefix + 'ToeEnd']
            ankle = neutral_pos[0, foot]
            toe_pos = neutral_pos[0, toe]
            # ToeEnd fixes the neutral plantar plane. A short rearward extension
            # supplies a heel proxy; exact target skin remains a separate gate.
            heel = ankle.copy()
            heel[1] = toe_pos[1]
            heel[[0, 2]] -= .3 * (toe_pos - ankle)[[0, 2]]
            heel_local = neutral_global[0, foot].T @ (heel - ankle)
            definitions[side] = {'heel': {'joint': prefix + 'Foot', 'offset': heel_local.tolist()},
                                 'toe': {'joint': prefix + 'ToeEnd', 'offset': [0., 0., 0.]}}
        geometry = 'soma_native_rest_toe_plane_heel_proxy'
    points = np.empty((len(joints), 2, 2, 3))
    for side_index, side in enumerate(('left', 'right')):
        for sample_index, sample in enumerate(('heel', 'toe')):
            definition = definitions[side][sample]
            bone = lookup[definition['joint']]
            offset = np.asarray(definition['offset'], dtype=float)
            if offset.shape != (3,) or not np.isfinite(offset).all():
                raise ValueError('Invalid sole landmark offset')
            points[:, side_index, sample_index] = joints[:, bone] + np.einsum(
                'tij,j->ti', global_rot[:, bone], offset)
    return points, {'kind': geometry, 'definitions': definitions,
                    'targetSkinValidationRequired': True}


def detect_foot_contacts(points, fps, policy=None, *, cycle_displacement=None):
    """Measure contacts from world landmarks, never motion.contacts or curves.

    Near-ground speed uses *all* geometric near-ground samples, including fast
    ones rejected by the contact detector. Thus skating cannot hide itself by
    making the detector report zero contact.
    """
    p = _policy(policy)
    points = np.asarray(points, dtype=float)
    if points.ndim != 4 or points.shape[1:] != (2, 2, 3) or len(points) < 3:
        raise ValueError('Expected at least three frames of left/right heel/toe XYZ')
    if not np.isfinite(points).all() or not math.isfinite(float(fps)) or fps <= 0:
        raise ValueError('Non-finite landmark data or invalid FPS')
    cycle_frames = None
    if cycle_displacement is not None:
        displacement = np.asarray(cycle_displacement, dtype=float)
        if displacement.shape != (3,) or not np.isfinite(displacement).all():
            raise ValueError('Invalid cycle displacement')
        cycle_frames = len(points) - 1
        unique = points[:-1]
        # Evaluate the middle repetition with physical neighbouring samples.
        # Otherwise the inclusive endpoint's one-frame contact would be erased
        # by the minimum-duration filter despite continuing into the next loop.
        points = np.concatenate([unique - displacement, points,
                                 unique[1:] + displacement, unique[:1] + 2 * displacement])
    velocity = np.gradient(points, 1 / float(fps), axis=0)
    horizontal = np.linalg.norm(velocity[..., [0, 2]], axis=-1)
    vertical = np.abs(velocity[..., 1])
    height = points[..., 1] - p['groundHeightMeters']
    minimum_frames = max(2, math.ceil(p['minimumContactSeconds'] * fps))
    sample_contact = np.zeros(points.shape[:3], bool)
    for side in range(2):
        for sample in range(2):
            locked = False
            for frame in range(len(points)):
                hlimit = p['contactLeaveHeightMeters'] if locked else p['contactEnterHeightMeters']
                vlimit = p['contactLeaveSpeedMps'] if locked else p['contactEnterSpeedMps']
                locked = bool(height[frame, side, sample] <= hlimit and
                              horizontal[frame, side, sample] <= vlimit and
                              vertical[frame, side, sample] <= p['contactVerticalSpeedMps'])
                sample_contact[frame, side, sample] = locked
            for start, stop in _runs(sample_contact[:, side, sample]):
                if stop - start < minimum_frames:
                    sample_contact[start:stop, side, sample] = False
    if cycle_frames is not None:
        crop = slice(cycle_frames, 2 * cycle_frames + 1)
        points, sample_contact = points[crop], sample_contact[crop]
        height, horizontal = height[crop], horizontal[crop]
    contact = sample_contact.any(axis=-1)
    events, support = [], {}
    measurements = {}
    for index, side in enumerate(('left', 'right')):
        ranges = _runs(contact[:, index])
        support[side] = [{'start': start, 'stopExclusive': stop} for start, stop in ranges]
        for start, stop in ranges:
            # A contact already present at frame zero is not an observed landing.
            if start > 0:
                events.append({'frame': start, 'time': start / fps, 'side': side, 'kind': 'touchdown'})
            if stop < len(points):
                events.append({'frame': stop, 'time': stop / fps, 'side': side, 'kind': 'liftoff'})
        measured_height = height[:-1, index] if cycle_frames is not None else height[:, index]
        measured_speed = horizontal[:-1, index] if cycle_frames is not None else horizontal[:, index]
        measured_contact = contact[:-1, index] if cycle_frames is not None else contact[:, index]
        near = measured_height <= p['contactLeaveHeightMeters']
        speeds = measured_speed[near]
        measurements[side] = {
            'nearGroundSamples': int(near.sum()),
            'nearGroundMedianSpeedMps': float(np.median(speeds)) if len(speeds) else None,
            'nearGroundP95SpeedMps': float(np.percentile(speeds, 95)) if len(speeds) else None,
            'contactFrames': int(measured_contact.sum()),
            'minimumHeightMeters': float(height[:, index].min()),
            'maximumHeightMeters': float(height[:, index].max()),
        }
    return {'events': sorted(events, key=lambda e: (e['frame'], e['side'], e['kind'])),
            'support': support, 'contacts': contact.astype(int).tolist(),
            'sampleContacts': sample_contact.astype(int).tolist(),
            'heightsMeters': height.tolist(), 'horizontalSpeedMps': horizontal.tolist(),
            'measurements': measurements,
            'measurementFrameCount': len(height) - int(cycle_frames is not None),
            'measurementDomain': 'unique_cycle_frames' if cycle_frames is not None else 'all_frames'}


def detect_gait_events(motion, rig, policy=None, *, cyclic=False):
    points, geometry = foot_landmarks(motion, rig)
    displacement = None
    if cyclic:
        displacement = (motion['root_positions'][-1] - motion['root_positions'][0]) * [1, 0, 1]
    result = detect_foot_contacts(points, float(motion['fps']), policy, cycle_displacement=displacement)
    result['landmarks'] = geometry
    return result


def compile_foot_planted(stages):
    """ALS: full left <= -.5, full right > .5; partial values stay between.

    Unknown, missing or ambiguous support is a data error, not a zero curve.
    """
    try:
        return np.asarray([STAGE_VALUES[stage] for stage in stages], dtype=float)
    except KeyError as exc:
        raise ValueError('Unresolved support stage: ' + str(exc.args[0])) from exc


def support_stages(contact, events):
    contact = np.asarray(contact, bool)
    landings = [event for event in events if event['kind'] == 'touchdown']
    if contact.ndim != 2 or contact.shape[1] != 2 or not landings:
        raise ValueError('Support stages require contact frames and observed landings')
    stages = []
    for frame, (left, right) in enumerate(contact):
        if left != right:
            stages.append('left_stable' if left else 'right_stable')
        elif left:
            previous = [event for event in landings if event['frame'] <= frame]
            # During double support the newly landing foot is preparing to take
            # support; no full-lock claim is made without exclusive support.
            side = previous[-1]['side'] if previous else landings[-1]['side']
            stages.append('plant_' + side)
        else:
            following = [event for event in landings if event['frame'] > frame]
            stages.append('plant_' + (following[0] if following else landings[0])['side'])
    return stages


def _cycle_metrics(motion, start, end, rig, detection, policy, anchor_side='left'):
    fps = float(motion['fps'])
    local = motion['local_rot_mats'][start:end + 1]
    root = motion['root_positions'][start:end + 1]
    duration = (end - start) / fps
    trajectory = np.zeros_like(root)
    trajectory[:, [0, 2]] = (root[0, [0, 2]] + np.linspace(0, 1, len(root))[:, None] *
                             (root[-1, [0, 2]] - root[0, [0, 2]]))
    _, body = fk(local, root - trajectory, rig)
    seam_angles = np.linalg.norm(Rotation.from_matrix(local[-1] @ local[0].swapaxes(-1, -2)).as_rotvec(), axis=-1)
    velocity = np.gradient(body, 1 / fps, axis=0)
    initial_angular = Rotation.from_matrix(local[1] @ local[0].swapaxes(-1, -2)).as_rotvec() * fps
    final_angular = Rotation.from_matrix(local[-1] @ local[-2].swapaxes(-1, -2)).as_rotvec() * fps
    opposite_side = 'right' if anchor_side == 'left' else 'left'
    opposite = [event['frame'] for event in detection['events']
                if event['kind'] == 'touchdown' and event['side'] == opposite_side and start < event['frame'] < end]
    phase = (opposite[0] - start) / (end - start) if len(opposite) == 1 else None
    metrics = {'start': start, 'endInclusive': end, 'durationSeconds': duration,
               'oppositeTouchdowns': opposite, 'oppositePhase': phase,
               'speedMps': float(np.linalg.norm(root[-1, [0, 2]] - root[0, [0, 2]]) / duration),
               'seamRotationDegrees': float(np.rad2deg(seam_angles.max())),
               'seamBodyMeters': float(np.linalg.norm(body[-1] - body[0], axis=-1).max()),
               'seamRootHeightMeters': float(abs(root[-1, 1] - root[0, 1])),
               'seamVelocityMps': float(np.linalg.norm(velocity[-1] - velocity[0], axis=-1).max()),
               'seamAngularVelocityRps': float(np.linalg.norm(final_angular - initial_angular, axis=-1).max()),
               'seamContactsAgree': detection['contacts'][start] == detection['contacts'][end]}
    limits = [('seamRotationDegrees', 'maximumSeamRotationDegrees'),
              ('seamBodyMeters', 'maximumSeamBodyMeters'),
              ('seamRootHeightMeters', 'maximumSeamRootHeightMeters'),
              ('seamVelocityMps', 'maximumSeamVelocityMps'),
              ('seamAngularVelocityRps', 'maximumSeamAngularVelocityRps')]
    checks = [_check(key, metrics[key] <= policy[limit], metrics[key], policy[limit]) for key, limit in limits]
    checks += [_check('cycle_duration', policy['minimumCycleSeconds'] <= duration <= policy['maximumCycleSeconds'],
                      duration, [policy['minimumCycleSeconds'], policy['maximumCycleSeconds']]),
               _check('opposite_support_phase', phase is not None and
                      policy['oppositePhaseRange'][0] <= phase <= policy['oppositePhaseRange'][1],
                      phase, policy['oppositePhaseRange']),
               _check('cycle_contact_continuity', metrics['seamContactsAgree'], metrics['seamContactsAgree'])]
    metrics['checks'] = checks
    metrics['pass'] = all(check['pass'] for check in checks)
    return metrics


def audit_candidate(motion, rig, expected_speed_mps=None, policy=None, *, anchor_side='left'):
    if anchor_side not in ('left', 'right'):
        raise ValueError('Cycle anchor side must be left or right')
    p = _policy(policy)
    detection = detect_gait_events(motion, rig, p)
    starts = [event['frame'] for event in detection['events']
              if event['kind'] == 'touchdown' and event['side'] == anchor_side]
    cycles = [_cycle_metrics(motion, a, b, rig, detection, p, anchor_side) for a, b in zip(starts, starts[1:])]
    checks = [_check('complete_cycles', len(cycles) >= p['minimumCompleteCycles'],
                     len(cycles), p['minimumCompleteCycles'])]
    if expected_speed_mps is not None and (not math.isfinite(expected_speed_mps) or expected_speed_mps <= 0):
        raise ValueError('Authored gait speed must be finite and positive')
    # The stable section is selected by observed events and evidence. A bad
    # startup must not invalidate two later clean cycles, and one clean cycle
    # must not hide a bad neighbour. Each cycle is measured before selection.
    heights = np.asarray(detection['heightsMeters'])
    speeds = np.asarray(detection['horizontalSpeedMps'])
    contacts = np.asarray(detection['contacts'])
    for cycle in cycles:
        span = slice(cycle['start'], cycle['endInclusive'])
        measurements = {}
        for index, side in enumerate(('left', 'right')):
            h, v = heights[span, index], speeds[span, index]
            near = h <= p['contactLeaveHeightMeters']
            count = int(near.sum())
            median = float(np.median(v[near])) if count else None
            support_count = int(contacts[span, index].sum())
            minimum_height = float(heights[cycle['start']:cycle['endInclusive'] + 1, index].min())
            measurements[side] = {'nearGroundSamples': count, 'nearGroundMedianSpeedMps': median,
                                  'contactFrames': support_count, 'minimumHeightMeters': minimum_height}
            cycle['checks'] += [
                _check(side + '_near_ground_samples', count >= p['minimumNearGroundSamplesPerFoot'],
                       count, p['minimumNearGroundSamplesPerFoot']),
                _check(side + '_geometric_slip', median is not None and
                       median <= p['maximumNearGroundMedianSpeedMps'], median, p['maximumNearGroundMedianSpeedMps']),
                _check(side + '_observed_support', support_count > 0, support_count),
                _check(side + '_penetration', minimum_height >= -p['maximumPenetrationMeters'],
                       minimum_height, -p['maximumPenetrationMeters'])]
        cycle['geometricMeasurements'] = measurements
        if expected_speed_mps is not None:
            error = abs(cycle['speedMps'] / expected_speed_mps - 1)
            cycle['checks'].append(_check('authored_speed', error <= p['maximumSpeedRelativeError'],
                                           error, p['maximumSpeedRelativeError']))
        cycle['pass'] = all(check['pass'] for check in cycle['checks'])
    valid = [cycle for cycle in cycles if cycle['pass']]
    checks.append(_check('valid_complete_cycle', bool(valid), len(valid), 'at least one unmodified valid full cycle'))
    windows = []
    window_size = int(p['minimumCompleteCycles'])
    for index in range(len(cycles) - window_size + 1):
        window = cycles[index:index + window_size]
        periods = np.asarray([cycle['durationSeconds'] for cycle in window])
        deviation = float(np.max(np.abs(periods / np.median(periods) - 1)))
        if all(cycle['pass'] for cycle in window) and deviation <= p['maximumPeriodRelativeDeviation']:
            windows.append({'cycleIndices': list(range(index, index + window_size)),
                            'periodRelativeDeviation': deviation})
    checks.append(_check('stable_consecutive_cycles', bool(windows), windows,
                         {'minimumCycles': window_size, 'periodRelativeDeviation': p['maximumPeriodRelativeDeviation']}))
    eligible_indices = sorted({index for window in windows for index in window['cycleIndices']})
    eligible = [cycles[index] for index in eligible_indices]
    # Selection is deterministic and driven by actual seam error, never a fixed
    # percentage crop or a tail blend that forces unequal poses to look equal.
    chosen = min(eligible, key=lambda cycle: (cycle['seamBodyMeters'], cycle['seamRotationDegrees'], cycle['start'])) if eligible else None
    receipt = {'schemaVersion': 1, 'policy': p, 'policySha256': hashlib.sha256(
        json.dumps(p, sort_keys=True, separators=(',', ':')).encode()).hexdigest(),
        'sourceMotionSha256': _array_hash(motion['local_rot_mats'], motion['root_positions'], motion['fps']),
        'rigSha256': hashlib.sha256(json.dumps(rig, sort_keys=True, separators=(',', ':')).encode()).hexdigest(),
        'sourceFrameCount': len(motion['root_positions']), 'fps': float(motion['fps']),
        'expectedSpeedMps': expected_speed_mps, 'detection': detection, 'cycles': cycles,
        'selectedCycle': chosen, 'stableWindows': windows, 'checks': checks,
        'cycleAnchorSide': anchor_side, 'measurementDomain': 'unique_cycle_frames',
        'pass': all(check['pass'] for check in checks),
        'targetSkinAndRuntimeAcceptanceRequired': True}
    return receipt


def author_cycle(motion, rig, target_count=None, expected_speed_mps=None, policy=None, *, anchor_side='left'):
    """Select an observed same-foot cycle; preserve trajectory and derivatives.

    The returned motion has an inclusive cycle endpoint. Source poses are only
    interpolated in time, never closed by modifying the final pose. A linear
    planar travel transform, including the crop origin, is removed; pelvis sway
    and vertical bob remain. Its exact inverse is included in the receipt.
    """
    receipt = audit_candidate(motion, rig, expected_speed_mps, policy, anchor_side=anchor_side)
    if not receipt['pass']:
        raise GaitValidationError(receipt)
    p, cycle = receipt['policy'], receipt['selectedCycle']
    start, end = cycle['start'], cycle['endInclusive']
    count = target_count if target_count is not None else end - start + 1
    if not isinstance(count, (int, np.integer)) or count < 3:
        raise ValueError('A cycle needs at least three target frames')
    frames = np.linspace(start, end, count)
    ratio = (end - start) / (count - 1)
    retiming = max(ratio, 1 / ratio)
    receipt['checks'].append(_check('retiming_ratio', retiming <= p['maximumRetimingRatio'],
                                     retiming, p['maximumRetimingRatio']))
    local = np.stack([Slerp(np.arange(start, end + 1), Rotation.from_matrix(
        motion['local_rot_mats'][start:end + 1, bone]))(frames).as_matrix()
        for bone in range(len(rig['jointNames']))], axis=1)
    source_root = motion['root_positions']
    root = np.stack([np.interp(frames, np.arange(len(source_root)), source_root[:, i]) for i in range(3)], axis=1)
    removed = np.zeros_like(root)
    removed[:, [0, 2]] = (root[0, [0, 2]] + np.linspace(0, 1, count)[:, None] *
                           (root[-1, [0, 2]] - root[0, [0, 2]]))
    result = complete(local, root - removed, rig, float(motion['fps']))
    reconstructed = complete(local, result['root_positions'] + removed, rig, float(motion['fps']))
    reconstructed_detection = detect_gait_events(reconstructed, rig, p, cyclic=True)
    # Re-evaluate transformed trajectories: resampling changes speed and contact
    # windows, so copying the old receipt or event times would be invalid.
    for side in ('left', 'right'):
        measured = reconstructed_detection['measurements'][side]
        median = measured['nearGroundMedianSpeedMps']
        receipt['checks'].append(_check('retimed_' + side + '_geometric_slip', median is not None and
                                         median <= p['maximumNearGroundMedianSpeedMps'], median,
                                         p['maximumNearGroundMedianSpeedMps']))
    duration = (count - 1) / float(motion['fps'])
    displacement = removed[-1] - removed[0]
    speed = float(np.linalg.norm(displacement[[0, 2]]) / duration)
    if expected_speed_mps is not None:
        error = abs(speed / expected_speed_mps - 1)
        receipt['checks'].append(_check('retimed_authored_speed', error <= p['maximumSpeedRelativeError'],
                                         error, p['maximumSpeedRelativeError']))
    # Re-detect interior events in the final time domain. The boundary landing
    # was observed in the source before cropping, so it can be reinstated only
    # when that same foot remains in contact at both resampled endpoints.
    anchor_index = 0 if anchor_side == 'left' else 1
    boundary_valid = bool(reconstructed_detection['contacts'][0][anchor_index] and
                          reconstructed_detection['contacts'][-1][anchor_index])
    receipt['checks'].append(_check('canonical_' + anchor_side + '_contact_boundary', boundary_valid, boundary_valid))
    events = list(reconstructed_detection['events'])
    if boundary_valid:
        events += [{'frame': 0, 'time': 0., 'side': anchor_side, 'kind': 'touchdown'},
                   {'frame': count - 1, 'time': duration, 'side': anchor_side, 'kind': 'touchdown'}]
    events.sort(key=lambda event: (event['frame'], event['side'], event['kind']))
    reconstructed_detection['events'] = events
    final_metrics = _cycle_metrics(reconstructed, 0, count - 1, rig, reconstructed_detection, p, anchor_side)
    for check in final_metrics['checks']:
        receipt['checks'].append(dict(check, name='retimed_' + check['name']))
    if not events:
        receipt['pass'] = False
        raise GaitValidationError(receipt)
    stages = support_stages(reconstructed_detection['contacts'], events)
    curve = compile_foot_planted(stages)
    for side in ('left', 'right'):
        samples = stages.count(side + '_stable')
        receipt['checks'].append(_check('compiled_' + side + '_full_support', samples > 0, samples))
    reconstruction_error = float(np.max(np.abs(reconstructed['root_positions'] - root)))
    receipt['checks'].append(_check('trajectory_reconstruction', reconstruction_error <= 1e-10,
                                     reconstruction_error, 1e-10))
    distance = np.r_[0., np.cumsum(np.linalg.norm(np.diff(removed[:, [0, 2]], axis=0), axis=1))]
    receipt.update(sourceSpan={'start': start, 'endInclusive': end}, sourceFrames=frames.tolist(),
                   outputFrameCount=count, actualAuthoredSpeedMps=speed, events=events, stages=stages,
                   footPlanted=curve.tolist(), transformedDetection=reconstructed_detection,
                   transformedCycleMetrics=final_metrics,
                   removedPlanarTrajectory={
                       'kind': 'authoring_travel_removed_from_hips', 'units': 'meters', 'up': 'Y',
                       'fps': float(motion['fps']), 'positions': removed.tolist(),
                       'times': (np.arange(count) / float(motion['fps'])).tolist(),
                       'sourceFrames': frames.tolist(), 'distance': distance.tolist(),
                       'cycleDisplacement': displacement.tolist(), 'cycleDistance': float(distance[-1]),
                       'sourceMotionSha256': receipt['sourceMotionSha256'],
                       'trajectorySha256': _array_hash(removed, frames)},
                   outputMotionSha256=_array_hash(result['local_rot_mats'], result['root_positions'], result['fps']))
    receipt['pass'] = all(check['pass'] for check in receipt['checks'])
    if not receipt['pass']:
        raise GaitValidationError(receipt)
    return result, receipt


def revalidate_authored_cycle(source, rig, receipt):
    """Recheck an unchanged bound cycle with current sampling and thresholds."""
    validate_motion(source, rig)
    if not receipt.get('pass') or receipt.get('outputMotionSha256') != _array_hash(
            source['local_rot_mats'], source['root_positions'], source['fps']):
        raise ValueError('Revalidation requires the unchanged accepted gait master')
    removed = np.asarray(receipt['removedPlanarTrajectory']['positions'], dtype=float)
    if removed.shape != source['root_positions'].shape or not np.isfinite(removed).all():
        raise ValueError('Gait revalidation requires its complete authoring trajectory')
    policy = _policy(receipt['policy'])
    policy['version'] = DEFAULT_POLICY['version']
    anchor_side = receipt.get('cycleAnchorSide', 'left')
    if anchor_side not in ('left', 'right'):
        raise ValueError('Invalid bound cycle anchor side')
    travel = complete(source['local_rot_mats'], source['root_positions'] + removed, rig, float(source['fps']))
    detection = detect_gait_events(travel, rig, policy, cyclic=True)
    cycle = _cycle_metrics(travel, 0, len(removed) - 1, rig, detection, policy, anchor_side)
    checks = list(cycle['checks'])
    anchor_index = 0 if anchor_side == 'left' else 1
    checks.append(_check('bound_contact_boundary', bool(detection['contacts'][0][anchor_index] and
                         detection['contacts'][-1][anchor_index]), anchor_side))
    for side in ('left', 'right'):
        measured = detection['measurements'][side]
        median = measured['nearGroundMedianSpeedMps']
        checks += [
            _check(side + '_geometric_slip', median is not None and
                   median <= policy['maximumNearGroundMedianSpeedMps'], median, policy['maximumNearGroundMedianSpeedMps']),
            _check(side + '_near_ground_samples', measured['nearGroundSamples'] >= policy['minimumNearGroundSamplesPerFoot'],
                   measured['nearGroundSamples'], policy['minimumNearGroundSamplesPerFoot']),
            _check(side + '_observed_support', measured['contactFrames'] > 0, measured['contactFrames']),
            _check(side + '_penetration', measured['minimumHeightMeters'] >= -policy['maximumPenetrationMeters'],
                   measured['minimumHeightMeters'], -policy['maximumPenetrationMeters'])]
    speed = receipt['actualAuthoredSpeedMps']
    error = abs(cycle['speedMps'] / speed - 1)
    checks.append(_check('bound_authored_speed', error <= policy['maximumSpeedRelativeError'],
                         error, policy['maximumSpeedRelativeError']))
    report = dict(policy=policy, cycleAnchorSide=anchor_side, checks=checks,
                  policySha256=hashlib.sha256(json.dumps(policy, sort_keys=True, separators=(',', ':')).encode()).hexdigest(),
                  transformedDetection=detection, transformedCycleMetrics=cycle,
                  outputMotionSha256=receipt['outputMotionSha256'], pass_=all(c['pass'] for c in checks))
    report['pass'] = report.pop('pass_')
    if not report['pass']:
        raise GaitValidationError(report)
    return report


def reflected_gait_reference(source,base,rig,receipt,*,reverse_time=False):
    """Reflect accepted parent travel; the returned condition is not a delivery."""
    from .motion import mirror
    revalidate_authored_cycle(source, rig, receipt)
    removed = np.asarray(receipt['removedPlanarTrajectory']['positions'], dtype=float)
    if removed.shape != source['root_positions'].shape or not np.isfinite(removed).all():
        raise ValueError('Mirrored gait requires the complete measured authoring trajectory')
    travel = complete(source['local_rot_mats'], source['root_positions'] + removed, rig, float(source['fps']))
    if reverse_time:
        root=travel['root_positions'][::-1].copy()
        root[:,[0,2]]-=root[0,[0,2]]
        travel=complete(travel['local_rot_mats'][::-1].copy(),root,rig,float(source['fps']))
    # Grip-preserving base-relative mirroring is appropriate for overlays.
    # An asymmetric standing/stride pose would bias the opposite gait legs;
    # locomotion mirrors in the native neutral skeleton basis instead.
    neutral = complete(np.broadcast_to(np.eye(3), (1, len(rig['jointNames']), 3, 3)).copy(),
        base['root_positions'][:1].copy(), rig, float(source['fps']))
    reflected = mirror(travel, neutral, rig)
    period = len(removed) - 1
    count = period * 5 + 1
    frames = np.arange(count)
    displacement = reflected['root_positions'][-1] - reflected['root_positions'][0]
    repeated = complete(reflected['local_rot_mats'][frames % period].copy(),
        reflected['root_positions'][frames % period] + (frames // period)[:, None] * displacement,
        rig, float(source['fps']))
    return repeated,period


def mirror_authored_cycle(source, base, rig, receipt, *, reverse_time=False,repair_derived=False):
    """Reflect complete travel, then independently re-detect swapped support."""
    repeated,period=reflected_gait_reference(source,base,rig,receipt,reverse_time=reverse_time)
    parent_side = receipt.get('cycleAnchorSide', 'left')
    anchor_side = 'left' if reverse_time else ('right' if parent_side == 'left' else 'left')
    policy = dict(receipt['policy'], version=DEFAULT_POLICY['version'])
    attempts=[]
    try:
        result, mirrored_receipt = author_cycle(repeated, rig, period+1,
            receipt['actualAuthoredSpeedMps'], policy, anchor_side=anchor_side)
    except GaitValidationError as original_error:
        if not repair_derived:raise
        from .periodic_repair import repair_periodic
        for harmonics,ramp,regularization in ((3,4,.02),(3,4,.1),(4,4,.1),(2,4,.1),(3,6,.1),(2,4,.5)):
            try:
                repaired,report=repair_periodic(repeated,rig,receipt['actualAuthoredSpeedMps'],
                    harmonics=harmonics,ramp_frames=ramp,target_period_frames=period,
                    enforce_sole_clearance=True,minimum_knee_bend_degrees=15,fit_regularization=regularization)
                attempts.append(report)
                if report['after']['pass']:
                    result,mirrored_receipt=author_cycle(repaired,rig,period+1,
                        receipt['actualAuthoredSpeedMps'],policy,anchor_side=anchor_side)
                    mirrored_receipt['derivedPeriodicRepair']=report
                    break
            except ValueError as error:
                attempts.append(dict(harmonics=harmonics,rampFrames=ramp,fitRegularization=regularization,error=str(error)))
        else:
            original_error.receipt['derivedRepairAttempts']=attempts
            raise original_error
    mirrored_receipt['mirrorProvenance'] = dict(sourceOutputMotionSha256=receipt['outputMotionSha256'],
        policy='native neutral reflection of complete travel; preserve cycle phase with swapped support anchor',
        sourceCycleAnchorSide=parent_side, mirroredCycleAnchorSide=anchor_side,
        reversedTime=bool(reverse_time),sourceEvents=receipt['events'])
    return result, mirrored_receipt
