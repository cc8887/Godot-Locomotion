"""Periodic motion fitting with world-space stance targets and fixed-length IK.

This is deterministic authoring cleanup, not another generated performance.
The result must still pass the independent gait, target-rig and runtime gates.
"""
from __future__ import annotations

import math
import numpy as np
from scipy.ndimage import gaussian_filter1d, maximum_filter1d
from scipy.spatial.transform import Rotation, Slerp

from . import gait, motion
from .gait_constraints import solve_foot_ik


def _fit(values, output_phase, harmonics, regularization=.002):
    """Fit the entire inclusive observed cycle, including both seam samples."""
    phase = np.linspace(0, 1, len(values))
    def basis(x):
        return np.stack([np.ones_like(x), *[f(2*np.pi*k*x)
                         for k in range(1, harmonics+1) for f in (np.sin, np.cos)]], axis=1)
    a = basis(phase)
    penalty = np.diag([0., *[regularization*k*k for k in range(1, harmonics+1) for _ in range(2)]])
    coefficients = np.linalg.lstsq(np.vstack([a, penalty]),
        np.concatenate([values.reshape(len(values), -1), np.zeros((len(penalty), np.prod(values.shape[1:], dtype=int)))]), rcond=None)[0]
    return (basis(output_phase) @ coefficients).reshape((len(output_phase), *values.shape[1:]))


def _runs(mask):
    edges = np.diff(np.r_[False, mask, False].astype(int))
    return zip(np.flatnonzero(edges == 1), np.flatnonzero(edges == -1))


def _ease(x):
    x = np.clip(x, 0, 1)
    return x*x*x*(10+x*(-15+6*x))


def repair_periodic(source, rig, speed, *, harmonics=5, ramp_frames=3,
                    max_ankle_correction=.15, max_hip_lowering=.05, cycles=5, max_pose_fit_degrees=20.,
                    target_period_frames=None, constrained_cycle_span=None, contact_reference=None,
                    enforce_sole_clearance=False, minimum_knee_bend_degrees=0.,
                    upper_body_harmonics=None, contact_phase_shift_frames=0, fit_regularization=.002,
                    upper_body_motion_scale=1.,stabilize_knee_poles=False,flat_stance=False):
    if not math.isfinite(fit_regularization) or fit_regularization<0:
        raise ValueError('Periodic fit regularization must be finite and nonnegative')
    audit = gait.audit_candidate(source, rig, speed)
    candidates = [c for c in audit['cycles'] if len(c['oppositeTouchdowns']) == 1 and
                  .35 <= c['oppositePhase'] <= .65 and .35 <= c['durationSeconds'] <= 2]
    if not candidates and constrained_cycle_span is None:
        raise ValueError('Periodic repair requires an observed alternating cycle')
    if target_period_frames is not None and (not isinstance(target_period_frames,int) or target_period_frames<4):
        raise ValueError('Target period must contain at least four frames')
    contact_audit, contact_cycle = audit, None
    if constrained_cycle_span is not None:
        if contact_reference is None:
            raise ValueError('A constrained cycle span requires an independently measured contact reference')
        span = tuple(constrained_cycle_span)
        if len(span)!=2 or any(not isinstance(v,int) for v in span) or not 0<=span[0]<span[1]<len(source['root_positions']):
            raise ValueError('Invalid constrained cycle span')
        contact_audit = gait.audit_candidate(contact_reference, rig, speed)
        reference_cycles = [c for c in contact_audit['cycles'] if len(c['oppositeTouchdowns'])==1
                            and .35<=c['oppositePhase']<=.65]
        if not reference_cycles:
            raise ValueError('Contact reference has no measured alternating cycle')
        desired_period = (target_period_frames or span[1]-span[0])/float(source['fps'])
        contact_cycle = min(reference_cycles,key=lambda c:(abs(math.log(c['durationSeconds']/desired_period)),
            sum(not check['pass'] for check in c['checks']),abs(c['oppositePhase']-.5),c['start']))
        _, body = motion.fk(source['local_rot_mats'][list(span)],np.zeros((2,3)),rig)
        seam = float(np.max(np.linalg.norm(body[1]-body[0],axis=-1)))
        if seam>gait.DEFAULT_POLICY['maximumSeamBodyMeters']:
            raise ValueError(f'Constrained cycle endpoints do not match geometrically: {seam:.6f}m')
        if abs(source['root_positions'][span[1],1]-source['root_positions'][span[0],1])>gait.DEFAULT_POLICY['maximumSeamRootHeightMeters']:
            raise ValueError('Constrained cycle endpoint hip heights do not match')
        chosen = dict(start=span[0],endInclusive=span[1],
            selection='explicit constrained pose cycle; actual output must pass the independent gait gate',
            seamBodyMeters=seam)
        chosen['pass'] = False
    else:
        if contact_reference is not None:
            raise ValueError('A separate contact reference requires an explicit constrained cycle span')
        chosen = min(candidates, key=lambda c: (
            abs(math.log((c['endInclusive']-c['start'])/target_period_frames)) if target_period_frames else 0,
            not c['pass'], c['seamBodyMeters'], c['seamRotationDegrees']))
    start, end = chosen['start'], chosen['endInclusive']
    source_period = end-start
    period = source_period if target_period_frames is None else target_period_frames
    if not 1 <= harmonics < min(period,source_period)//2 or ramp_frames < 1 or cycles < 3:
        raise ValueError('Invalid periodic fitting configuration')
    phase = np.arange(period)/period
    local_source = source['local_rot_mats'][start:end+1]
    mean = np.stack([Rotation.from_matrix(local_source[:, j]).mean().as_matrix()
                     for j in range(len(rig['jointNames']))])
    residual = local_source @ mean.swapaxes(-1, -2)
    logs = Rotation.from_matrix(residual.reshape(-1, 3, 3)).as_rotvec().reshape(len(local_source), -1, 3)
    fitted_logs = _fit(logs, phase, harmonics, fit_regularization)
    if not math.isfinite(upper_body_motion_scale) or not 0<upper_body_motion_scale<=1:
        raise ValueError('Upper body motion scale must be positive and at most one')
    if upper_body_harmonics is not None or upper_body_motion_scale!=1:
        if upper_body_harmonics is not None and (not isinstance(upper_body_harmonics,int) or not 1<=upper_body_harmonics<=harmonics):
            raise ValueError('Upper body harmonics must be positive and not exceed the main fit')
        legs={i for i,name in enumerate(rig['jointNames']) if name in ('LeftLeg','RightLeg')}
        upper=[]
        for index,parent in enumerate(rig['parents']):
            ancestor=index
            while ancestor>=0 and ancestor not in legs:ancestor=rig['parents'][ancestor]
            if ancestor<0:upper.append(index)
        fitted_logs[:,upper]=_fit(logs[:,upper],phase,upper_body_harmonics or harmonics,fit_regularization)*upper_body_motion_scale
    fitted_local = Rotation.from_rotvec(fitted_logs.reshape(-1, 3)).as_matrix().reshape(period, -1, 3, 3) @ mean
    compared_source=np.stack([Slerp(np.arange(source_period+1),Rotation.from_matrix(local_source[:,j]))(
        phase*source_period).as_matrix() for j in range(len(rig['jointNames']))],axis=1)
    fit_angles = np.linalg.norm(Rotation.from_matrix(
        fitted_local@compared_source.swapaxes(-1, -2)).as_rotvec().reshape(-1, 3), axis=-1)
    maximum_fit_degrees = float(np.rad2deg(fit_angles.max()))
    if maximum_fit_degrees > max_pose_fit_degrees:
        raise ValueError(f'Periodic pose fitting exceeds correction bound: {maximum_fit_degrees:.4f} degrees')
    roots = source['root_positions'][start:end+1].copy()
    displacement = roots[-1, [0, 2]]-roots[0, [0, 2]]
    if np.linalg.norm(displacement)<1e-8:
        raise ValueError('Periodic source cycle has no measurable root travel')
    direction = displacement/np.linalg.norm(displacement)
    removed = np.zeros_like(roots)
    removed[:, [0, 2]] = roots[0, [0, 2]]+np.linspace(0, 1, len(roots))[:, None]*displacement
    residual_roots = _fit(roots-removed, phase, harmonics,fit_regularization)
    residual_roots[:, [0, 2]] -= residual_roots[0, [0, 2]]
    fps = float(source['fps'])
    count = (cycles+2)*period+1
    indices = np.arange(count)%period
    local = fitted_local[indices].copy()
    root = residual_roots[indices].copy()
    root[:, [0, 2]] += np.arange(count)[:, None]/fps*speed*direction
    base = motion.complete(local, root, rig, fps)
    global_rot, joints = motion.fk(local, root, rig)
    points, geometry = gait.foot_landmarks(base, rig)

    # Repair only independently observed source contact intervals. Height alone
    # mislabels low swings. The validator still measures ALL near-ground motion,
    # including unselected/sliding frames, so this plan cannot hide bad samples.
    contact_start,contact_end = (start,end) if contact_cycle is None else (contact_cycle['start'],contact_cycle['endInclusive'])
    intent = np.asarray(contact_audit['detection']['sampleContacts'][contact_start:contact_end], dtype=bool)[
        np.floor(phase*(contact_end-contact_start)).astype(int)]
    for side in range(2):
        for sample in range(2):
            expanded = np.tile(intent[:, side, sample], 3)
            for a, b in _runs(expanded):
                if b-a < 3:
                    expanded[a:b] = False
            intent[:, side, sample] = expanded[period:2*period]
    if not isinstance(contact_phase_shift_frames,int) or abs(contact_phase_shift_frames)>period//8:
        raise ValueError('Support phase adjustment exceeds one eighth of the gait cycle')
    intent=np.roll(intent,contact_phase_shift_frames,axis=0)
    masks = intent[indices]
    names = {n: i for i, n in enumerate(rig['jointNames'])}
    rest_global = np.tile(np.eye(3), (len(names), 1, 1))
    rest_local = motion.local_from_global(rest_global[None], rig['parents'])[0]
    _, rest_pos = motion.fk(rest_local[None], np.zeros((1, 3)), rig)
    targets = np.stack([joints[:, names[s+'Foot']] for s in ('Left', 'Right')], axis=1)
    target_rotations = np.stack([global_rot[:, names[s+'Foot']] for s in ('Left', 'Right')], axis=1)
    toe_rotations = [{}, {}]
    plans = []
    for side, label in enumerate(('left', 'right')):
        prefix = label.title(); foot = names[prefix+'Foot']
        heel_def = geometry['definitions'][label]['heel']
        toe_def = geometry['definitions'][label]['toe']
        if heel_def['joint'] != prefix+'Foot':
            raise ValueError('Periodic backend requires an ankle-local heel landmark')
        heel_offset = np.asarray(heel_def['offset'])
        toe = names[toe_def['joint']]
        toe_chain = []
        index = toe
        while index != foot and index >= 0:
            toe_chain.append(index); index = rig['parents'][index]
        if index != foot:
            raise ValueError('Toe landmark is outside the foot chain')
        toe_chain.reverse()
        toe_rotations[side] = {j: local[:, j].copy() for j in toe_chain}
        rest_heel = rest_pos[0, foot]+rest_global[foot]@heel_offset
        rest_toe = rest_pos[0, toe]+rest_global[toe]@np.asarray(toe_def['offset'])
        rest_forward = rest_toe-rest_heel
        for first, stop in _runs(masks[:, side].any(axis=-1)):
            middle = (first+stop-1)//2
            heel_ids = np.flatnonzero(masks[first:stop, side, 0])+first
            toe_ids = np.flatnonzero(masks[first:stop, side, 1])+first
            raw_heel = np.median(points[heel_ids, side, 0], axis=0) if len(heel_ids) else points[middle, side, 0].copy()
            raw_toe = np.median(points[toe_ids, side, 1], axis=0) if len(toe_ids) else points[middle, side, 1].copy()
            raw_heel[1] = raw_toe[1] = 0.
            forward = raw_toe-raw_heel
            yaw = math.atan2(forward[0], forward[2])-math.atan2(rest_forward[0], rest_forward[2])
            flat_rotation = Rotation.from_euler('y', yaw).as_matrix()@rest_global[foot]
            flat_toe_offset = flat_rotation @ rest_global[foot].T @ (rest_toe-rest_pos[0, foot])
            flat_ankle = ((raw_heel-flat_rotation@heel_offset)+(raw_toe-flat_toe_offset))*.5
            heel_anchor = flat_ankle+flat_rotation@heel_offset
            toe_anchor = flat_ankle+flat_toe_offset
            heel_anchor[1] = toe_anchor[1] = 0.
            begin, finish = max(0, first-ramp_frames), min(count, stop+ramp_frames)
            for frame in range(begin, finish):
                weight = min(_ease((frame-begin)/max(1, first-begin)),
                             _ease((finish-1-frame)/max(1, finish-stop)))
                if first <= frame < stop:
                    weight = 1.
                event_frame = min(stop-1, max(first, frame))
                heel, tip = masks[event_frame, side]
                flat = float(heel and tip or flat_stance)
                # A flat contact constrains orientation; heel/toe-only contacts
                # retain source rolling and pin only the active contact point.
                desired_rotation = Slerp([0, 1], Rotation.from_matrix(
                    np.stack([global_rot[frame, foot], flat_rotation])))([weight*flat]).as_matrix()[0]
                target_rotations[frame, side] = desired_rotation
                for j in toe_chain:
                    toe_rotations[side][j][frame] = Slerp([0, 1], Rotation.from_matrix(
                        np.stack([local[frame, j], rest_local[j]])))([weight*flat]).as_matrix()[0]
                toe_vector = np.zeros(3); orientation = desired_rotation.copy()
                for j in toe_chain:
                    toe_vector += orientation@np.asarray(rig['offsets'][j])
                    orientation = orientation@toe_rotations[side][j][frame]
                toe_vector += orientation@np.asarray(toe_def['offset'])
                ankle = heel_anchor-desired_rotation@heel_offset if heel else toe_anchor-toe_vector
                targets[frame, side] = joints[frame, foot]*(1-weight)+ankle*weight
            plans.append(dict(side=label, start=int(first), stopExclusive=int(stop),
                              heelAnchorMeters=heel_anchor.tolist(), toeAnchorMeters=toe_anchor.tolist()))

    if enforce_sole_clearance:
        for side,label in enumerate(('left','right')):
            foot=names[label.title()+'Foot']
            heel_def=geometry['definitions'][label]['heel'];toe_def=geometry['definitions'][label]['toe']
            chain=[];index=names[toe_def['joint']]
            while index!=foot:
                if index<0:raise ValueError('Clearance toe landmark is outside the foot chain')
                chain.append(index);index=rig['parents'][index]
            for frame in range(count):
                orientation=target_rotations[frame,side].copy();toe_vector=np.zeros(3)
                for joint in reversed(chain):
                    toe_vector+=orientation@np.asarray(rig['offsets'][joint])
                    orientation=orientation@toe_rotations[side][joint][frame]
                toe_vector+=orientation@np.asarray(toe_def['offset'])
                heel=targets[frame,side]+target_rotations[frame,side]@np.asarray(heel_def['offset'])
                toe=targets[frame,side]+toe_vector
                targets[frame,side,1]+=max(0.,-min(heel[1],toe[1]))
    correction = np.linalg.norm(targets-np.stack([joints[:, names[s+'Foot']] for s in ('Left', 'Right')], axis=1), axis=-1)
    if correction.max() > max_ankle_correction:
        frame, side = np.unravel_index(np.argmax(correction), correction.shape)
        nearby = [p for p in plans if p['side'] == ('left', 'right')[side] and p['start']-ramp_frames <= frame <= p['stopExclusive']+ramp_frames]
        source_ankle = joints[frame, names[('Left','Right')[side]+'Foot']]
        raise ValueError(f'Foot correction exceeds bound: {correction.max():.6f}m > {max_ankle_correction}; frame={frame}, side={side}, from={source_ankle}, to={targets[frame,side]}, points={points[frame,side]}, masks={masks[frame,side]}, plans={nearby}')
    if not math.isfinite(minimum_knee_bend_degrees) or not 0<=minimum_knee_bend_degrees<90:
        raise ValueError('Minimum knee bend must be between zero and ninety degrees')
    required = np.zeros(count)
    for side, prefix in enumerate(('Left', 'Right')):
        thigh, shin, foot = (names[prefix+s] for s in ('Leg', 'Shin', 'Foot'))
        hip = joints[:, thigh]
        upper_length=np.linalg.norm(joints[:,shin]-hip,axis=-1)
        lower_length=np.linalg.norm(joints[:,foot]-joints[:,shin],axis=-1)
        reach = np.sqrt(upper_length**2+lower_length**2+2*upper_length*lower_length*
            math.cos(math.radians(minimum_knee_bend_degrees)))-1e-5
        horizontal2 = np.sum((hip[:, [0, 2]]-targets[:, side, [0, 2]])**2, axis=-1)
        if np.any(horizontal2 >= reach*reach):
            raise ValueError('Stance target cannot be reached without changing bone lengths')
        lowering = hip[:, 1]-(targets[:, side, 1]+np.sqrt(reach*reach-horizontal2))
        required = np.maximum(required, lowering)
    required_phase = np.max(required[period:period*(cycles+1)].reshape(cycles, period), axis=0)
    lowered = gaussian_filter1d(maximum_filter1d(required_phase, size=5, mode='wrap'), 1., mode='wrap')
    lowered += max(0., float(np.max(required_phase-lowered)))
    if lowered.max() > max_hip_lowering:
        raise ValueError(f'Hip lowering exceeds bound: {lowered.max():.6f}m > {max_hip_lowering}')
    root[:, 1] -= lowered[indices]
    repaired_local = local.copy()
    first, last = period, period*(cycles+1)+1
    # Padding establishes contact anchors across the loop seam; it is not an
    # exported frame range. Solve and validate every exported frame, including
    # its repeated endpoint, using those interior anchors.
    for frame in range(first, last):
        for side, label in enumerate(('left', 'right')):
            repaired_local[frame] = solve_foot_ik(repaired_local[frame], root[frame], rig, label,
                targets[frame, side], target_rotations[frame, side],
                {j: rotations[frame] for j, rotations in toe_rotations[side].items()},
                pole_direction=global_rot[frame,0,:,2] if stabilize_knee_poles else None)
    output_roots = root[first:last].copy()
    output_roots[:, [0, 2]] -= output_roots[0, [0, 2]]
    repaired = motion.complete(repaired_local[first:last], output_roots, rig, fps)
    after = gait.audit_candidate(repaired, rig, speed)
    report = dict(schemaVersion=1, algorithm='periodic_fourier_with_world_contact_ik_v1',
        productionReady=False, sourceCycle=chosen, constructedCycles=cycles,
        sourceWasAcceptedMaster=audit['pass'], sourceAudit=audit, after=after,
        harmonics=harmonics, rampFrames=ramp_frames,
        upperBodyHarmonics=upper_body_harmonics,minimumKneeBendDegrees=minimum_knee_bend_degrees,
        contactPhaseShiftFrames=contact_phase_shift_frames,
        fitRegularization=fit_regularization,
        upperBodyMotionScale=upper_body_motion_scale,stabilizedKneePoles=bool(stabilize_knee_poles),
        flatStanceApplied=bool(flat_stance),
        soleClearanceApplied=bool(enforce_sole_clearance),
        sourcePeriodFrames=source_period, outputPeriodFrames=period,
        maximumAnkleCorrectionMeters=float(correction.max()), maximumHipLoweringMeters=float(lowered.max()),
        maximumPeriodicFitRotationDegrees=maximum_fit_degrees,
        contactPlan=plans, contactPlanSource=('measured source heel/toe contact intervals' if contact_cycle is None
            else 'independently measured generation-reference heel/toe intervals'),
        contactReferenceMotionSha256=None if contact_cycle is None else contact_audit['sourceMotionSha256'],
        contactReferenceCycle=contact_cycle,
        outputSpace='travelling', repairAcceptedByAuthoringGate=after['pass'])
    return repaired, report
