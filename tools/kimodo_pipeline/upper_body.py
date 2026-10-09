"""Measure relaxed Walk arm posture against its actual generated Idle dependency."""
import numpy as np

from .gait import _array_hash
from .motion import fk, validate_motion

POLICY = dict(maximumMedianArmLiftFromIdleDegrees=20.,
              maximumArmLiftFromIdleDegrees=35.,
              maximumMedianHandLateralFromIdleMeters=.12)


def measure_arms(motion, rig):
    validate_motion(motion, rig)
    rotations, positions = fk(motion['local_rot_mats'], motion['root_positions'], rig)
    names = {name: i for i, name in enumerate(rig['jointNames'])}
    # Hip heading removes travel direction and world origin, without removing
    # arm elevation. Chest-relative positions avoid confusing a body lean with
    # a lateral hand reach.
    forward = rotations[:, 0, :, 2]
    heading = np.arctan2(forward[:, 0], forward[:, 2])
    result = []
    for side in ('Left', 'Right'):
        arm, elbow, hand = (names[side + suffix] for suffix in ('Arm', 'ForeArm', 'Hand'))
        segment = positions[:, elbow] - positions[:, arm]
        elevation = np.degrees(np.arccos(np.clip(-segment[:, 1] / np.linalg.norm(segment, axis=1), -1, 1)))
        reach = positions[:, hand] - positions[:, names['Chest']]
        lateral = np.abs(np.cos(heading) * reach[:, 0] - np.sin(heading) * reach[:, 2])
        result.append(dict(side=side, medianArmElevationDegrees=float(np.median(elevation)),
                           maximumArmElevationDegrees=float(elevation.max()),
                           medianHandLateralMeters=float(np.median(lateral))))
    return result


def audit_idle_relative(motion, idle, rig, policy=None):
    limits = dict(POLICY)
    if policy:
        if set(policy) - set(limits):
            raise ValueError('Unknown Idle-relative upper-body policy')
        limits.update(policy)
    if any(not np.isfinite(value) or value <= 0 for value in limits.values()):
        raise ValueError('Upper-body limits must be finite and positive')
    reference, observed = measure_arms(idle, rig), measure_arms(motion, rig)
    checks = []
    for a, b in zip(reference, observed):
        for metric, key in [('medianArmElevationDegrees', 'maximumMedianArmLiftFromIdleDegrees'),
                            ('maximumArmElevationDegrees', 'maximumArmLiftFromIdleDegrees'),
                            ('medianHandLateralMeters', 'maximumMedianHandLateralFromIdleMeters')]:
            limit = a[metric] + limits[key]
            checks.append(dict(side=b['side'], metric=metric, idle=a[metric], observed=b[metric],
                               limit=limit, passed=b[metric] <= limit))
    return dict(schemaVersion=1, policy='walk-idle-relative-upper-body-v1', passed=all(c['passed'] for c in checks),
                limits=limits, checks=checks, idle=reference, observed=observed,
                motionSha256=_array_hash(motion['local_rot_mats'], motion['root_positions'], motion['fps']),
                idleMotionSha256=_array_hash(idle['local_rot_mats'], idle['root_positions'], idle['fps']),
                scope='Relaxed unarmed Walk posture; does not certify hand contact or artistic acceptance')
