"""Nonperiodic mantle conditioning, bounded contact IK and independent gates.

ALS references are conditioning inputs only. They never replace a generated
delivery. All contact coordinates include root travel (component space).
"""
from __future__ import annotations

import copy
import hashlib
from pathlib import Path

import numpy as np
from scipy.spatial.transform import Rotation
from scipy.optimize import minimize

from .graph import read_json
from .motion import complete, fk, local_from_global, resample
from .gait_constraints import solve_foot_ik

POLICY = 'als-mantle-contact-v1'
SOURCE_HAND_CORRECTION_CM = 8.
TARGET_HAND_CORRECTION_CM = 10.  # Independent target-proportion adaptation budget.
INDEX = 'assets/config/refactored_animation_sources.json'
# Low_Left names the free/equipment side; the opposite hand supports the body.
# These intervals were measured on the immutable ALS physical tracks at 30 Hz.
PROFILES = {
    'Mantle_High': {'hands': ('left', 'right'), 'contactSeconds': (.6,58/30),
        'handContactSeconds': {'left':(.6,58/30),'right':(.6,38/30)},
        'heightCm': 200., 'groundedSeconds': (61/30,70/30),
        'settlingSeconds': (73/30,),
        # The target mesh's thumb base extends inside the ledge with the
        # legacy wrist marker. One fixed centimeter of top clearance keeps
        # the palm contact and stays inside the independent 2 cm position gate.
        'targetMeshHandOffsetsCm': {'left': (0.,0.,1.)}},
    'Mantle_Low_Left': {'hands': ('right',), 'contactSeconds': (11/30,22/30),
        'heightCm': 100., 'groundedSeconds': (29/30,38/30)},
    'Mantle_Low_Right': {'hands': ('left',), 'contactSeconds': (11/30,22/30),
        'heightCm': 100., 'groundedSeconds': (29/30,38/30)},
}


class MantleError(ValueError):
    def __init__(self, message, receipt):
        self.receipt = receipt
        super().__init__(message)


def profile(name):
    if name not in PROFILES:
        raise ValueError('Unconfigured mantle contact profile: ' + name)
    return copy.deepcopy(PROFILES[name])


def reference_input(name, index_path=None):
    """Load and hash-check immutable inputs, not the selected generated pack."""
    from .als_reference import original_input
    index_path = Path(index_path or Path(__file__).resolve().parents[2] / INDEX)
    index = read_json(index_path)
    suffix = 'A_Als_' + name
    rows = [r for r in index['assets'] if r['source'].endswith('.' + suffix)]
    if len(rows) != 1:
        raise ValueError('Expected one immutable refactored mantle: ' + name)
    index, row, path = original_input(index_path, rows[0]['source'])
    raw = read_json(path)['raw']
    skeleton = index['skeletons'][raw['skeletonSource']]
    # Hash the skeleton as well as the motion: either changes FK/constraints.
    from .root_ownership import semantic_hash
    return raw, skeleton, dict(source=row['source'], sourceSha256=row['sha256'],
        skeletonSha256=semantic_hash(skeleton), indexSha256=hashlib.sha256(index_path.read_bytes()).hexdigest())


def _interval(spec, count, length):
    start, end = spec['contactSeconds']
    if count < 3 or length <= end or start >= end:
        raise ValueError('Invalid mantle contact time domain')
    return np.flatnonzero((np.linspace(0, length, count) >= start-1e-7) &
                         (np.linspace(0, length, count) <= end+1e-7))


def _arm_rig(rig):
    """Use the existing strict two-link solver without changing gait behavior."""
    adapted = dict(rig)
    aliases = {side+a: side+b for side in ('Left', 'Right')
               for a, b in (('Arm', 'Leg'), ('ForeArm', 'Shin'), ('Hand', 'Foot'))}
    # Other limbs must not create duplicate aliases in the name lookup.
    adapted['jointNames'] = [aliases.get(n, 'unused:' + n) for n in rig['jointNames']]
    return adapted


def _hand_frames(spec, side, count, length):
    return _interval(dict(contactSeconds=spec.get('handContactSeconds',{}).get(side,spec['contactSeconds'])),count,length)


def _angle(a, b):
    return np.rad2deg(Rotation.from_matrix(a.swapaxes(-1, -2) @ b).magnitude())


def native_reference(name, rig, index_path=None):
    """Invert the outgoing angular retarget, then solve the support hands."""
    from .publish import world_pose
    from .offline_retarget import aligned_neutral, mapping, BASIS
    raw, skeleton, identity = reference_input(name, index_path)
    spec = profile(name); world = world_pose(raw, skeleton)
    names = {n: i for i, n in enumerate(rig['jointNames'])}
    count = raw['sampledKeyCount']; fps = (count-1)/raw['playLength']
    aligned = aligned_neutral(skeleton, rig, mapping())
    aligned_by_name = {n.lower(): aligned[i] for i, n in enumerate(skeleton['rawBoneNames'])}
    inverse_map = {v: k for k, v in mapping().items()}
    source_leg = sum(np.linalg.norm(rig['offsets'][names['Left'+n]]) for n in ('Shin', 'Foot'))
    target_leg = sum(np.linalg.norm(skeleton['referencePose'][skeleton['rawBoneNames'].index(n)]['position'])
                     for n in ('calf_l', 'foot_l'))
    scale = (target_leg/100)/source_leg
    global_rot = np.tile(np.eye(3), (count, len(names), 1, 1))
    for n, i in names.items():
        bone = inverse_map.get(n)
        if bone:
            global_rot[:, i] = BASIS @ (world[bone][0] @ aligned_by_name[bone].T) @ BASIS.T
        elif rig['parents'][i] >= 0:
            global_rot[:, i] = global_rot[:, rig['parents'][i]]
    local = local_from_global(global_rot, rig['parents'])
    root = world['pelvis'][1] @ BASIS.T / (100*scale)
    # SOMA and ALS have different shoulder/arm proportions. A native wrist
    # coordinate divided by leg scale is not a valid SOMA contact target.
    # Pin a reachable window-average wrist in source space;
    # the independent target-space pass restores the immutable ALS ledge.
    _, guide_positions = fk(local, root, rig)
    contact_adaptations = []
    for side in spec['hands']:
        contact = _hand_frames(spec,side,count,raw['playLength'])
        hand = names[side.title()+'Hand']
        initial = guide_positions[contact, hand].mean(axis=0)
        shoulder = guide_positions[contact, names[side.title()+'Arm']]
        upper = np.linalg.norm(rig['offsets'][names[side.title()+'ForeArm']])
        lower = np.linalg.norm(rig['offsets'][hand])
        # Find ONE reachable anchor for the complete window, rather than
        # clipping it separately each frame (which would create hand slide).
        reach = upper+lower-2e-6; inner = abs(upper-lower)+2e-6
        fitted = minimize(lambda p:np.sum((p-initial)**2),initial,method='SLSQP',
            constraints=[{'type':'ineq','fun':lambda p: reach**2-np.sum((shoulder-p)**2,axis=1)},
                         {'type':'ineq','fun':lambda p: np.sum((shoulder-p)**2,axis=1)-inner**2}],
            options={'ftol':1e-13,'maxiter':200})
        anchor = fitted.x; distances = np.linalg.norm(shoulder-anchor,axis=1)
        adaptation = float(np.linalg.norm(anchor-initial))
        if not fitted.success or adaptation > .05 or np.any(distances >= upper+lower-1e-7) or np.any(distances <= abs(upper-lower)+1e-7):
            raise ValueError('No bounded fixed source-hand anchor for '+name+': '+side)
        for frame in contact:
            local[frame]=solve_foot_ik(local[frame],root[frame],_arm_rig(rig),side,anchor,global_rot[frame,hand],{})
        contact_adaptations.append(dict(side=side,maximumAnchorAdaptationCm=adaptation*100,anchorMetersYUp=anchor.tolist()))
    motion = complete(local, root, rig, fps)
    receipt = dict(schemaVersion=1, policy=POLICY, semanticName=name,
        scope='constraint_reference_only', deliverable=False, productionReady=False,
        reference=identity, profile=spec, uniformTargetScale=float(scale),
        contactAdaptations=contact_adaptations,
        adaptation='inverse outgoing aligned angular retarget; fixed-length support-arm IK')
    return motion, receipt


def materialize_constraints(params, reference, rig):
    name = params['semantic']; spec = profile(name)
    count = int(params['seconds']*params['fps'])
    guide = resample(reference, count, rig)
    native, _, identity = reference_input(name)
    # Fractional mapping is explicit: generation can exceed the 1.5 s low
    # mantle, but delivery returns to the native time domain before auditing.
    indices = _interval(spec, count, native['playLength'])
    side_frames={side:_hand_frames(spec,side,count,native['playLength']) for side in spec['hands']}
    full_frames = sorted(set([0,count-1]+[int(f[i]) for f in side_frames.values() for i in (0,-1)]+
        [round(t/native['playLength']*(count-1)) for t in (*spec['groundedSeconds'], *spec.get('settlingSeconds',()))]+
        # The native cleanup enforces hip height only at full-body keyframes;
        # generic Hips constraints remain soft inference conditions. Bound
        # gaps to 0.25 s instead of leaving lift/late landing unconstrained.
        list(range(0,count,max(1,int(params['fps']*.25))))))
    rot = Rotation.from_matrix(guide['local_rot_mats'].reshape(-1, 3, 3)).as_rotvec().reshape(count, -1, 3)
    def condition(kind, frames):
        frames = np.asarray(frames, dtype=int)
        return dict(type=kind, frame_indices=frames.tolist(),
                    local_joints_rot=rot[frames].tolist(), root_positions=guide['root_positions'][frames].tolist())
    # Effector contacts only constrain the hip trajectory at their sampled
    # frames. Pin Hips throughout approach/lift/release as a separate native
    # end-effector condition; unpinned intervals otherwise admit large body
    # excursions even when both palms and the full-body endpoints match.
    # This constrains inference, rather than replacing the generated hip track
    # after inference or pinning the entire pose on every frame.
    # Hips is a 3D effector, not the dense 2D-root exception. Keep every
    # constraint type within Kimodo's sparse-key conditioning budget.
    trajectory_frames = np.unique(np.linspace(0,count-1,min(19,count)).round().astype(int))
    trajectory = condition('end-effector', trajectory_frames)
    trajectory['joint_names'] = ['Hips']
    constraints = [condition('fullbody', full_frames), trajectory]
    hand_frames={}
    for side in spec['hands']:
        indices=side_frames[side]
        selected=np.unique(np.linspace(indices[0],indices[-1],min(19,len(indices))).round().astype(int))
        hand_frames[side]=selected.tolist()
        # Kimodo's hand-effector conditions include global positions AND
        # rotations computed from these local poses, not position-only pins.
        constraints.append(condition(side+'-hand', selected))
    finish_frames = np.flatnonzero(np.linspace(0,native['playLength'],count)>=spec['groundedSeconds'][1]-1e-7)
    finish_frames = np.unique(np.linspace(finish_frames[0],count-1,min(19,len(finish_frames))).round().astype(int))
    for side in ('left','right'):
        constraints.append(condition(side+'-foot',finish_frames))
    if any(len(c['frame_indices'])>19 for c in constraints):
        raise ValueError('Mantle conditioning exceeds the sparse-key budget; configure a shorter profile')
    return constraints, dict(schemaVersion=1, policy=POLICY, reference=identity,
        semanticName=name, supportHands=list(spec['hands']),
        nativeContactSeconds=list(spec['contactSeconds']),
        generationContactFrames=hand_frames, fullBodyFrames=full_frames,
        generationHipTrajectoryFrames=trajectory_frames.tolist(),
        generationFinishSupportFrames=finish_frames.tolist(),
        handPositionAndOrientation=True, generationFrames=count, deliveryFrames=native['sampledKeyCount'])


def _audit(points, rotations, guide_points, guide_rotations, pelvis, guide_pelvis,
           spec, frames, hand_names, units_to_cm):
    checks = []
    def check(label, value, limit):
        checks.append(dict(check=label, value=float(value), limit=float(limit), passed=bool(value <= limit)))
    for side in spec['hands']:
        contact = frames[side] if isinstance(frames,dict) else frames
        key = hand_names[side]
        actual, expected = points[key][contact], guide_points[key][contact]
        check(side+'HandPositionCm', np.max(np.linalg.norm(actual-expected, axis=1))*units_to_cm, 2.)
        check(side+'HandOrientationDegrees', np.max(_angle(rotations[key][contact], guide_rotations[key][contact])), 12.)
        drift = np.max(np.linalg.norm(actual-actual[0], axis=1))*units_to_cm
        native_drift = np.max(np.linalg.norm(expected-expected[0], axis=1))*units_to_cm
        check(side+'HandDriftCm', drift, native_drift+1.)
    errors = np.linalg.norm(pelvis-guide_pelvis, axis=1)*units_to_cm
    check('pelvisTrajectoryMaximumCm', np.max(errors), 15.)
    check('pelvisStartCm', errors[0], 5.)
    check('pelvisFinishCm', errors[-1], 5.)
    return checks


def author(motion, reference, rig, name):
    """No gait looping/foot planting: bounded hand IK on final delivery keys."""
    native, _, identity = reference_input(name)
    spec = profile(name); count = native['sampledKeyCount']
    fps = native['frameRateNumerator']/native['frameRateDenominator']
    result = resample(motion, count, rig); guide = resample(reference, count, rig)
    local = result['local_rot_mats'].copy(); root = result['root_positions'].copy()
    gr, gp = fk(guide['local_rot_mats'], guide['root_positions'], rig)
    names = {n: i for i, n in enumerate(rig['jointNames'])}
    frames = {side:_hand_frames(spec,side,count,native['playLength']) for side in spec['hands']}
    repairs = []
    before_rot, before_pos = fk(local, root, rig)
    for side in spec['hands']:
        contact=frames[side]
        hand = names[side.title()+'Hand']
        shift = float(np.max(np.linalg.norm(before_pos[contact, hand]-gp[contact, hand], axis=1)))
        angle = float(np.max(_angle(before_rot[contact, hand], gr[contact, hand])))
        applied = shift*100 <= SOURCE_HAND_CORRECTION_CM and angle <= 30.
        error = None
        if applied:
            proposed = local.copy()
            try:
                for frame in contact:
                    proposed[frame] = solve_foot_ik(proposed[frame], root[frame], _arm_rig(rig), side,
                                                  gp[frame, hand], gr[frame, hand], {})
                local = proposed
            except ValueError as exc:
                applied = False; error = str(exc)
        repairs.append(dict(side=side, applied=applied, maximumCorrectionCm=shift*100,
                            correctionLimitCm=SOURCE_HAND_CORRECTION_CM,maximumOrientationDegrees=angle, error=error))
    result = complete(local, root, rig, fps)
    ar, ap = fk(local, root, rig)
    checks = _audit({n: ap[:, i] for n, i in names.items()}, {n: ar[:, i] for n, i in names.items()},
                    {n: gp[:, i] for n, i in names.items()}, {n: gr[:, i] for n, i in names.items()},
                    root, guide['root_positions'], spec, frames,
                    {s: s.title()+'Hand' for s in spec['hands']}, 100.)
    finish = np.flatnonzero(np.linspace(0,native['playLength'],count)>=spec['groundedSeconds'][1]-1e-7)
    for side in ('Left','Right'):
        foot = names[side+'Foot']
        error = float(np.max(np.linalg.norm(ap[finish,foot]-gp[finish,foot],axis=1))*100)
        angle = float(np.max(_angle(ar[finish,foot],gr[finish,foot])))
        checks.extend([dict(check=side+'FinishFootCm',value=error,limit=3.,passed=error <= 3.),
                       dict(check=side+'FinishFootOrientationDegrees',value=angle,limit=12.,passed=angle <= 12.)])
    receipt = dict(schemaVersion=1, policy=POLICY, semanticName=name, reference=identity,
        profile=spec, passed=all(c['passed'] for c in checks), checks=checks, repairs=repairs,
        sampledKeyCount=count, fps=fps, playLengthSeconds=native['playLength'],
        productionReady=False, deliveryScope='generated_motion_only')
    if not receipt['passed']:
        raise MantleError('Mantle authoring gate failed: '+name, receipt)
    return result, receipt


def target(raw, skeleton, name):
    """Target-space arm IK; root keeps the authored nominal approach/lift path.

    Body travel must already match the profile. Arbitrarily translating the
    pelvis, stretching limbs, or flattening its height would conceal failure.
    """
    from .publish import world_pose, sample_native_track
    native, native_skeleton, identity = reference_input(name)
    spec = profile(name); count = raw['sampledKeyCount']
    guide = copy.deepcopy(native)
    guide.update({k: raw[k] for k in ('sampledKeyCount', 'playLength', 'frameRateNumerator', 'frameRateDenominator')})
    for track in guide['tracks']:
        for field in ('positions', 'rotations', 'scales'):
            if track[field]:track[field] = sample_native_track(track[field], native, raw, field=='rotations')
    gw = world_pose(guide, native_skeleton); before = world_pose(raw, skeleton)
    names = skeleton['rawBoneNames']; parents = skeleton['rawParents']
    lookup = {t['bone'].lower(): t for t in raw['tracks']}
    local = np.stack([Rotation.from_quat(np.broadcast_to(lookup[n.lower()]['rotations'], (count, 4))).as_matrix()
                      for n in names], axis=1)
    offsets = np.stack([np.broadcast_to(lookup[n.lower()]['positions'], (count, 3)) for n in names], axis=1)
    result = copy.deepcopy(raw); repaired_local = local.copy()
    frames = {side:_hand_frames(spec,side,count,raw['playLength']) for side in spec['hands']}; repairs = []
    # Existing target raw banks use constant non-root offsets and unit scales.
    # Refuse an incompatible rig rather than allowing hidden skeletal stretch.
    arm_bones = {'upperarm_'+s for s in ('l','r')} | {'lowerarm_'+s for s in ('l','r')} | {'hand_'+s for s in ('l','r')}
    compatible = all(np.allclose(offsets[:, i], offsets[0, i]) for i,n in enumerate(names) if n.lower() in arm_bones)
    compatible &= all(np.allclose(t.get('scales') or [[1, 1, 1]], 1) for t in raw['tracks'])
    rig_names = []
    for n in names:
        alias = n.lower()
        for side, suffix in (('Left', 'l'), ('Right', 'r')):
            for bone, end in (('upperarm', 'Arm'), ('lowerarm', 'ForeArm'), ('hand', 'Hand')):
                if n.lower() == bone+'_'+suffix:alias = side+end
        rig_names.append(alias)
    for side in spec['hands']:
        contact=frames[side]
        bone = 'hand_'+side[0]
        mesh_offset=np.asarray(spec.get('targetMeshHandOffsetsCm',{}).get(side,(0.,0.,0.)))
        if mesh_offset.shape!=(3,) or not np.isfinite(mesh_offset).all() or np.linalg.norm(mesh_offset)>2:
            raise ValueError('Target mesh wrist adaptation exceeds original position tolerance')
        desired_positions=gw[bone][1]+mesh_offset
        shift = float(np.max(np.linalg.norm(before[bone][1][contact]-desired_positions[contact], axis=1)))
        angle = float(np.max(_angle(before[bone][0][contact], gw[bone][0][contact])))
        applied = compatible and shift <= TARGET_HAND_CORRECTION_CM and angle <= 30.; error = None
        if applied:
            proposed = repaired_local.copy()
            try:
                for frame in contact:
                    rig = dict(jointNames=rig_names, parents=parents, offsets=(offsets[frame]/100).tolist())
                    proposed[frame] = solve_foot_ik(proposed[frame], offsets[frame, 0]/100,
                        _arm_rig(rig), side, desired_positions[frame]/100, gw[bone][0][frame], {})
                repaired_local = proposed
            except ValueError as exc:
                applied = False; error = str(exc)
        repairs.append(dict(side=side, applied=bool(applied), maximumCorrectionCm=shift,
                            targetMeshOffsetCm=mesh_offset.tolist(),
                            correctionLimitCm=TARGET_HAND_CORRECTION_CM,maximumOrientationDegrees=angle, error=error))
    tracks = {t['bone'].lower(): t for t in result['tracks']}
    for i, n in enumerate(names):
        tracks[n.lower()]['rotations'] = Rotation.from_matrix(repaired_local[:, i]).as_quat().tolist()
    actual = world_pose(result, skeleton)
    checks = _audit({n: v[1] for n, v in actual.items()}, {n: v[0] for n, v in actual.items()},
                    {n: v[1] for n, v in gw.items()}, {n: v[0] for n, v in gw.items()},
                    actual['pelvis'][1], gw['pelvis'][1], spec, frames,
                    {s: 'hand_'+s[0] for s in spec['hands']}, 1.)
    offset_error=max(float(np.max(np.abs(offsets[:,i]-skeleton['referencePose'][i]['position'])))
                     for i,n in enumerate(names) if n.lower() in arm_bones)
    scale_error=max(float(np.max(np.abs(np.asarray(t.get('scales') or [[1,1,1]])-1))) for t in raw['tracks'])
    checks.extend([dict(check='immutableArmOffsetsCm',value=offset_error,limit=.001,passed=offset_error <= .001),
                   dict(check='unitBoneScaleError',value=scale_error,limit=1e-5,passed=scale_error <= 1e-5)])
    root_path = gw['root'][1].copy(); root_rot = gw['root'][0].copy()
    # Validate vertical residual AFTER assigning the nominal root, independent
    # of total hip ascent. A clip with flattened hip bob cannot pass.
    residual = (actual['pelvis'][1]-root_path)[:, 2]
    expected = (gw['pelvis'][1]-root_path)[:, 2]
    excursion = float(np.ptp(residual)); minimum = float(np.ptp(expected)*.5)
    checks.append(dict(check='pelvisVerticalResidualRangeCm', value=excursion, minimum=minimum,
                       passed=bool(excursion >= minimum)))
    for side in ('l', 'r'):
        finish = np.flatnonzero(np.linspace(0,raw['playLength'],count)>=spec['groundedSeconds'][1]-1e-7)
        error = float(np.max(np.linalg.norm(actual['foot_'+side][1][finish]-gw['foot_'+side][1][finish],axis=1)))
        angle = float(np.max(_angle(actual['foot_'+side][0][finish],gw['foot_'+side][0][finish])))
        checks.append(dict(check='finishFoot'+side.upper()+'Cm', value=error, limit=5., passed=error <= 5.))
        checks.append(dict(check='finishFoot'+side.upper()+'OrientationDegrees',value=angle,limit=12.,passed=angle <= 12.))
    receipt = dict(schemaVersion=1, policy=POLICY, semanticName=name, reference=identity, profile=spec,
        passed=all(c['passed'] for c in checks), checks=checks, repairs=repairs,
        rootPolicy='immutable-reference-nominal-XYZ-and-rotation; retain-generated-pelvis-residual',
        supportHandTargets={side:dict(componentPositionsCm=(gw['hand_'+side[0]][1][frames[side]]+
            np.asarray(spec.get('targetMeshHandOffsetsCm',{}).get(side,(0.,0.,0.)))).tolist(),
            componentQuaternionsXYZW=Rotation.from_matrix(gw['hand_'+side[0]][0][frames[side]]).as_quat().tolist())
            for side in spec['hands']},
        contactFrames=sorted(set(int(f) for window in frames.values() for f in window)),
        handContactFrames={side:f.tolist() for side,f in frames.items()},
        sampledKeyCount=count, playLengthSeconds=raw['playLength'])
    return result, root_path, root_rot, receipt


def contact_curves(report, raw):
    """Pipeline sidecars; these names are not unverified ALS AnimGraph inputs."""
    count = raw['sampledKeyCount']; frames = {side:set(f) for side,f in report['handContactFrames'].items()}
    hands = report['profile']['hands']
    return dict(schemaVersion=1, policy=POLICY, semanticName=report['semanticName'],
        scope='mantle-contact-metadata', targetRawSemanticSha256=report['finalRawSemanticSha256'],
        coordinateSystem='target component space; centimeters; XY ground/Z up',
        consumerStatus='sidecar only; Motion Warping runtime integration pending',
        poseCurvePolicy='native PoseStanding/PoseGrounded preserved in validated native time domain',
        contactFrames=report['contactFrames'],handContactFrames=report['handContactFrames'],supportHandTargets=report['supportHandTargets'],
        keys=[dict(timeSeconds=float(t), leftHandSupport=float('left' in hands and i in frames['left']),
                   rightHandSupport=float('right' in hands and i in frames['right']),
                   finishFeetSupport=float(t>=report['profile']['groundedSeconds'][1]-1e-7))
              for i, t in enumerate(np.linspace(0, raw['playLength'], count))])
