"""Offline target-space planar trajectory ownership; playback does not author it.

Native target data uses centimeters, XY ground and Z up. Pose tables are
discrete poses, not paths. The official Kimodo smoother is reused for moving
clips; no inference model or GPU is loaded.
"""
from __future__ import annotations

import copy
import hashlib
import json
import numpy as np
from scipy.spatial.transform import Rotation

POLICY = 'target-planar-ownership-v1'
MARGIN_CM = 6.0  # Kimodo's get_smooth_root_pos uses 0.06 m.
POSE_FAMILIES = frozenset(('pose_table', 'pose_table_copy', 'gait_pose', 'base_pose',
                          'lean_additive', 'acceleration_additive', 'look_grid', 'reference'))


def semantic_hash(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':'),
                                    allow_nan=False).encode()).hexdigest()


def planar_trajectory(positions, *, in_place=False):
    p = np.asarray(positions, dtype=float)
    if p.ndim != 2 or p.shape[1] != 2 or not len(p) or not np.isfinite(p).all():
        raise ValueError('Invalid target planar positions')
    # A bounded in-place cycle must retain its complete weight shift. Its
    # constant path origin is still removed, even if the animated range is zero.
    center = p.mean(axis=0)
    if in_place and np.max(np.linalg.norm(p-center, axis=1)) <= MARGIN_CM:
        return np.broadcast_to(center, p.shape).copy(), 'bounded-in-place-center'
    if len(p) < 3 or np.max(np.abs(np.diff(p, n=2, axis=0))) < 1e-10:
        return p.copy(), 'exact-linear-path'
    from kimodo.motion_rep.smooth_root import smooth_signal, TrajectorySmoother
    margins = np.full(len(p), MARGIN_CM / 100.0)
    seed = smooth_signal(p / 100.0, margins)
    # The multigrid helper also interpolates at its final (step=1) level.
    # Refine on the actual exported keys, so half-frame interpolation cannot
    # violate the margin or silently shift the path's time domain.
    solver = TrajectorySmoother(margins, admm_iters=1000, alpha_overrelax=1.8)
    trajectory = solver.smooth(p / 100.0, seed) * 100.0
    if not np.isfinite(trajectory).all() or np.max(np.linalg.norm(p-trajectory, axis=1)) > MARGIN_CM + .05:
        raise ValueError('Kimodo planar smoother exceeded its residual envelope')
    return trajectory, 'kimodo-margin-constrained-smoother-final-key-refinement'


def lock_planar_root(raw, skeleton):
    """Make the ALS in-place view from the SAME factored body poses."""
    result = copy.deepcopy(raw)
    root = next(t for t in result['tracks'] if t['bone'].lower() == 'root')
    ref = skeleton['referencePose'][0]['position']
    p = np.broadcast_to(np.asarray(root['positions']), (raw['sampledKeyCount'], 3)).copy()
    p[:, :2] = ref[:2]
    root['positions'] = p.tolist()
    return result


def decompose(raw, skeleton, *, family, declared_root_motion=False, root_rotation=None,
              canonical_origin=None, origin_reference=None, mantle_root_positions=None):
    """Return an authored-root clip, its ALS view and a reconstruction receipt.

    The only intentional body-space change is ONE constant XY translation.
    Pelvis residuals, height and local body rotations are preserved. Existing
    root travel is included in the measured world trajectory, never added twice.
    """
    from .publish import world_pose
    names = skeleton['rawBoneNames']; parents = skeleton['rawParents']
    if names[0].lower() != 'root' or parents[0] != -1 or 'pelvis' not in [n.lower() for n in names]:
        raise ValueError('Planar ownership requires a target root/pelvis hierarchy')
    if 'rootOwnership' in raw:
        raise ValueError('Clip is already decomposed; use the original input')
    result = copy.deepcopy(raw); count = raw['sampledKeyCount']
    world = world_pose(raw, skeleton)
    tracks = {t['bone'].lower(): t for t in result['tracks']}
    for i, name in enumerate(names):
        track = tracks.get(name.lower())
        if track is not None and not track.get('scales'):
            # Empty scale channels mean reference scale to the raw sampler.
            # Materialize that same value for the full-track FBX writer.
            track['scales'] = [list(skeleton['referencePose'][i]['scale'])]
    if any(names[i].lower() not in tracks for i, parent in enumerate(parents) if parent <= 0):
        raise ValueError('Missing root or direct-child track')
    ref_position = np.asarray(skeleton['referencePose'][0]['position'], dtype=float)
    rp = world['root'][1].copy()
    rr = world['root'][0].copy() if root_rotation is None else np.asarray(root_rotation)
    origin = np.zeros(3)
    if family == 'mantle':
        if mantle_root_positions is None:
            raise ValueError('Mantle requires an explicit nominal root profile; pelvis XYZ is not a root path')
        rp = np.asarray(mantle_root_positions, dtype=float).copy()
        if rp.shape != (count,3) or not np.isfinite(rp).all() or canonical_origin is not None:
            raise ValueError('Invalid mantle root profile/time domain')
        method = 'mantle-nominal-reference-XYZ-preserve-pelvis-residual'
    elif family in POSE_FAMILIES:
        if canonical_origin is not None:
            origin = np.asarray(canonical_origin, dtype=float)
            if origin.shape != (3,) or not np.isfinite(origin).all() or origin[2] != 0:
                raise ValueError('Discrete pose origin must be a finite planar translation')
            method = 'discrete-pose-inherited-origin'
        else:
            method = 'discrete-pose-no-trajectory'
    else:
        if canonical_origin is not None:
            raise ValueError('Continuous motion owns its own trajectory origin')
        trajectory, method = planar_trajectory(world['pelvis'][1][:, :2],
            in_place=family in ('gait_cycle', 'sprint_start') and not declared_root_motion)
        origin[:2] = trajectory[0] - ref_position[:2]
        rp[:, :2] = trajectory - origin[:2]
    def assign(name, r, p, s):
        track = tracks[name]
        track['positions'] = p.tolist(); track['rotations'] = Rotation.from_matrix(r).as_quat().tolist()
        track['scales'] = s.tolist()
    rs = world['root'][2]
    assign('root', rr, rp, rs)
    inverse = rr.swapaxes(-1, -2)
    for i, name in enumerate(names):
        if parents[i] != 0:
            continue
        r, p, s = world[name.lower()]
        assign(name.lower(), inverse @ r,
               np.einsum('tij,tj->ti', inverse, p-origin-rp) / rs, s / rs)
    # Helper containers belong at the character root. Translating their old
    # world position along with the body would leave them meters behind the
    # now canonical pelvis, even though their endpoint FK matches the feet.
    for i, name in enumerate(names):
        if name.lower() not in ('ik_foot_root', 'ik_hand_root') or name.lower() not in tracks:
            continue
        if parents[i] != 0:raise ValueError('IK container must be a direct root child')
        ref=skeleton['referencePose'][i]
        assign(name.lower(), np.broadcast_to(Rotation.from_quat(ref['rotation']).as_matrix(),(count,3,3)),
               np.broadcast_to(ref['position'],(count,3)),np.broadcast_to(ref['scale'],(count,3)))
    # Rebuild helper locals from the original body FK after canonicalization.
    updated = world_pose(result, skeleton)
    helpers = {'ik_foot_l': 'foot_l', 'ik_foot_r': 'foot_r', 'ik_hand_gun': 'hand_r',
               'ik_hand_l': 'hand_l', 'ik_hand_r': 'hand_r'}
    for helper, body in helpers.items():
        if helper not in tracks or body not in world:
            continue
        index = [n.lower() for n in names].index(helper)
        parent = names[parents[index]].lower()
        pr, pp, ps = updated[parent]; br, bp, bs = world[body]
        bp = bp - origin
        assign(helper, pr.swapaxes(-1, -2) @ br,
               np.einsum('tij,tj->ti', pr.swapaxes(-1, -2), bp-pp) / ps, bs / ps)
        updated[helper] = br, bp, bs
    updated = world_pose(result, skeleton)
    deformation = [n.lower() for n in names if n.lower() != 'root' and not n.lower().startswith('ik_')]
    position_error = max(float(np.max(np.abs(updated[n][1]-(world[n][1]-origin)))) for n in deformation)
    rotation_error = max(float(np.max(np.abs(updated[n][0]-world[n][0]))) for n in deformation)
    if position_error > 1e-5 or rotation_error > 1e-6:
        raise ValueError('Planar ownership changed the canonical body pose')
    # ALS non-root-motion sequences remain in place for BOTH runtime settings.
    # Their authored path is exported independently before this view is made.
    runtime = result if declared_root_motion or family == 'mantle' else lock_planar_root(result, skeleton)
    runtime_world = world_pose(runtime, skeleton)
    report = dict(schemaVersion=1, policy=POLICY, family=family, method=method,
        ikContainerPolicy='root-reference-local',
        units='cm', coordinateSystem='native target XY ground, Z up',
        sourceRawSemanticSha256=semantic_hash(raw), canonicalOriginTranslationCm=origin.tolist(),
        sampledKeyCount=count, playLengthSeconds=raw.get('playLength'),
        maximumCanonicalBodyPositionErrorCm=position_error, maximumBodyRotationMatrixError=rotation_error,
        maximumInputPelvisRootHorizontalCm=float(np.max(np.linalg.norm(world['pelvis'][1][:,:2]-world['root'][1][:,:2],axis=1))),
        maximumAuthoredPelvisRootHorizontalCm=float(np.max(np.linalg.norm(updated['pelvis'][1][:,:2]-updated['root'][1][:,:2],axis=1))),
        maximumRuntimePelvisRootHorizontalCm=float(np.max(np.linalg.norm(runtime_world['pelvis'][1][:,:2]-runtime_world['root'][1][:,:2],axis=1))),
        runtimePlanarPolicy='authored-root' if declared_root_motion or family == 'mantle' else 'in-place-from-same-residual',
        authoredRootRawSemanticSha256=semantic_hash(result), runtimeRawSemanticSha256=semantic_hash(runtime))
    if origin_reference is not None:
        report['canonicalOriginReference'] = copy.deepcopy(origin_reference)
    return result, runtime, report
