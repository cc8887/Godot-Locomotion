"""Bounded target-rig stance cleanup after native retargeting, before ALS gates."""
import copy
import numpy as np
from scipy.spatial.transform import Rotation, Slerp

from .gait_constraints import solve_foot_ik
from .motion import fk


class TargetContactRepairError(ValueError):
    """Keep successful phase evidence when bounded IK is the failing stage."""
    def __init__(self, error, phase):
        super().__init__(str(error))
        self.phase = phase


def cleanup_walk(raw, skeleton, source_gate, *, phase_shift=1, max_ankle_cm=8., max_hip_cm=5.):
    if not source_gate.get('pass') or source_gate['outputFrameCount'] != raw['sampledKeyCount']:
        raise ValueError('Target cleanup needs a passing source gate in the same time domain')
    observed = np.asarray(source_gate['transformedDetection']['sampleContacts'], bool).all(axis=-1)
    result, report = _cleanup_contacts(raw, skeleton, observed, 150., (0., 1.),
        phase_shift=phase_shift, max_ankle_cm=max_ankle_cm, max_hip_cm=max_hip_cm)
    report['sourceGatePolicy'] = source_gate['policy']['version']
    return result, report


def repair_periodic_gait(raw, skeleton, source):
    from .target_phase import infer_phases
    from .target_gait import PILOTS, DIRECTIONS, asset_name, original_protocol
    name = asset_name(source)
    phase = infer_phases(raw, skeleton, source)
    speed, kind = PILOTS[name]
    if original_protocol()['assets'][kind]['contactMode'] != 'flat_sole':
        raise ValueError('Periodic IK currently requires a flat-sole contact contract')
    try:
        result, report = _cleanup_contacts(raw, skeleton, np.asarray(phase['candidateContacts'], bool),
            speed, DIRECTIONS[name], phase_shift=0)
    except ValueError as error:
        raise TargetContactRepairError(error, phase) from error
    report.update(phasePolicy=phase['policy'], passed=True, applied=True)
    return result, phase, report


def _cleanup_contacts(raw, skeleton, observed, speed, direction, *, phase_shift=0,
                      max_ankle_cm=8., max_hip_cm=5.):
    from .publish import world_pose
    result = copy.deepcopy(raw); count = raw['sampledKeyCount']; period = count-1
    if observed.shape != (count, 2):
        raise ValueError('Candidate phase contact dimensions differ')
    indices = (np.arange(count)-phase_shift) % period if phase_shift else np.arange(count)
    if phase_shift:
        for track in result['tracks']:
            for key in ('positions', 'rotations', 'scales'):
                if len(track[key]) == count:
                    track[key] = np.asarray(track[key])[indices].tolist()
    names = skeleton['rawBoneNames']; lookup = {n.lower(): i for i, n in enumerate(names)}
    tracks = {t['bone'].lower(): t for t in result['tracks']}
    if any(t['scales'] and not np.allclose(t['scales'], 1, atol=1e-6) for t in result['tracks']):
        raise ValueError('Target stance cleanup requires unit bone scales')
    world = world_pose(result, skeleton)
    bind = world_pose(dict(sampledKeyCount=1, tracks=[]), skeleton)
    local = np.stack([Rotation.from_quat(np.broadcast_to(
        np.asarray(tracks[n.lower()]['rotations']), (count, 4))).as_matrix() for n in names], axis=1)
    positions = np.stack([np.broadcast_to(np.asarray(tracks[n.lower()]['positions']), (count, 3)) for n in names], axis=1).copy()
    observed = observed[indices]
    target = np.stack([world['foot_'+s][1] for s in ('l', 'r')], axis=1).copy()
    target_rot = np.stack([world['foot_'+s][0] for s in ('l', 'r')], axis=1).copy()
    toe_rot = np.stack([local[:, lookup['ball_'+s]] for s in ('l', 'r')], axis=1).copy()
    for side, suffix in enumerate(('l', 'r')):
        foot, ball = 'foot_'+suffix, 'ball_'+suffix
        f_idx, b_idx = lookup[foot], lookup[ball]
        heel_offset = bind[foot][0][0].T @ np.array([0., 0., -bind[foot][1][0, 2]])
        toe_offset = bind[ball][0][0].T @ np.array([0., 0., -bind[ball][1][0, 2]])
        flat = observed[:, side]
        edges = np.diff(np.r_[False, flat[:-1], False].astype(int))
        intervals = list(zip(np.flatnonzero(edges == 1), np.flatnonzero(edges == -1)))
        if len(intervals) > 1 and intervals[0][0] == 0 and intervals[-1][1] == period:
            intervals = [(intervals[-1][0], intervals[0][1]+period), *intervals[1:-1]]
        for first, stop in intervals:
            if stop-first < 3:
                continue
            middle = ((first+stop-1)//2) % period
            forward = world[ball][1][middle]-world[foot][1][middle]
            rest_forward = bind[ball][1][0]-bind[foot][1][0]
            yaw = np.arctan2(forward[1], forward[0])-np.arctan2(rest_forward[1], rest_forward[0])
            rotation = Rotation.from_euler('z', yaw).as_matrix() @ bind[foot][0][0]
            span = np.arange(first, stop); wrapped = span % period
            advancing = np.zeros((len(span), 3))
            advancing[:, :2] = span[:, None]*(raw['playLength']/period)*speed*np.asarray(direction)
            heels = world[foot][1][wrapped]+np.einsum('tij,j->ti', world[foot][0][wrapped], heel_offset)+advancing
            anchor = np.median(heels, axis=0); anchor[2] = 0
            ankle = anchor-rotation@heel_offset
            for unwrapped in range(first-2, stop+2):
                frame = unwrapped % period
                alpha = min(1., (unwrapped-first+2)/2, (stop+1-unwrapped)/2)
                alpha = np.clip(alpha, 0, 1); alpha = alpha*alpha*(3-2*alpha)
                advancing = np.zeros(3); advancing[:2] = unwrapped*(raw['playLength']/period)*speed*np.asarray(direction)
                target[frame, side] = world[foot][1][frame]*(1-alpha)+(ankle-advancing)*alpha
                target_rot[frame, side] = Slerp([0, 1], Rotation.from_matrix(np.stack([world[foot][0][frame], rotation])))([alpha]).as_matrix()[0]
                reference = Rotation.from_quat(skeleton['referencePose'][b_idx]['rotation']).as_matrix()
                toe_rot[frame, side] = Slerp([0, 1], Rotation.from_matrix(np.stack([local[frame, b_idx], reference])))([alpha]).as_matrix()[0]
        # Preserve the repeated pose endpoint; the reconstructed world travel
        # still advances one complete stride across the seam.
        target[-1, side] = target[0, side]
        target_rot[-1, side] = target_rot[0, side]
        toe_rot[-1, side] = toe_rot[0, side]
        for frame in range(count):
            r = target_rot[frame, side]
            heel_z = (r @ heel_offset)[2]
            toe_z = (r @ positions[frame, b_idx]+r @ toe_rot[frame, side] @ toe_offset)[2]
            target[frame, side, 2] = max(target[frame, side, 2], -min(heel_z, toe_z))
    original_ankles = np.stack([world['foot_'+s][1] for s in ('l', 'r')], axis=1)
    correction = float(np.max(np.linalg.norm(target-original_ankles, axis=-1)))
    if correction > max_ankle_cm:
        raise ValueError(f'Target foot correction exceeds bound: {correction:.4f} cm')
    aliases = {'pelvis': 'Hips', 'thigh_l': 'LeftLeg', 'calf_l': 'LeftShin', 'foot_l': 'LeftFoot', 'ball_l': 'LeftToeEnd',
               'thigh_r': 'RightLeg', 'calf_r': 'RightShin', 'foot_r': 'RightFoot', 'ball_r': 'RightToeEnd'}
    rig = dict(jointNames=[aliases.get(n.lower(), n) for n in names], parents=skeleton['rawParents'])
    changes = []
    pelvis = lookup['pelvis']; parent = rig['parents'][pelvis]
    for frame in range(count):
        lower = 0.
        for side, suffix in enumerate(('l', 'r')):
            hip, knee, ankle = [world[n+'_'+suffix][1][frame] for n in ('thigh', 'calf', 'foot')]
            reach = np.linalg.norm(knee-hip)+np.linalg.norm(ankle-knee)-1e-3
            horizontal2 = np.sum((hip[:2]-target[frame, side, :2])**2)
            if horizontal2 >= reach*reach:
                raise ValueError('Target stance has unreachable horizontal foot placement')
            lower = max(lower, hip[2]-target[frame, side, 2]-np.sqrt(reach*reach-horizontal2))
        if lower > max_hip_cm:
            raise ValueError(f'Target hip correction exceeds bound: {lower:.4f} cm')
        positions[frame, pelvis] += world[names[parent].lower()][0][frame].T @ np.array([0., 0., -lower])
        rig['offsets'] = positions[frame].tolist()
        for side, label in enumerate(('left', 'right')):
            local[frame] = solve_foot_ik(local[frame], positions[frame, 0], rig, label,
                target[frame, side], target_rot[frame, side], {lookup['ball_'+('l','r')[side]]: toe_rot[frame, side]})
        changes.append(float(lower))
    for index, name in enumerate(names):
        if name.lower() in ('thigh_l','calf_l','foot_l','ball_l','thigh_r','calf_r','foot_r','ball_r'):
            tracks[name.lower()]['rotations'] = Rotation.from_matrix(local[:, index]).as_quat().tolist()
        if index == pelvis:
            tracks[name.lower()]['positions'] = positions[:, index].tolist()
    report = dict(method='target_world_stance_ik_v1', phaseShiftFrames=phase_shift,
                  maximumAnkleCorrectionCm=correction, maximumHipLoweringCm=max(changes),
                  expectedSpeedCmS=speed, travelDirectionXY=list(direction), qualityGateRequired=True)
    return result, report
