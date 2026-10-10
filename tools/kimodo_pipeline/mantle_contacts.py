"""Bake mantle gameplay controls against immutable final pose/root inputs.

No Unreal, motion generation, or animation-file rewriting. Original references
provide the already-validated contact contract; generated pose remains intact.
"""
from __future__ import annotations
import argparse
import hashlib
from pathlib import Path
import numpy as np
from scipy.spatial.transform import Rotation
from .graph import read_json
from .runner import file_hash, write_json
from .mantle import PROFILES, reference_input
from .mantle_curve_contract import contract, root_progress, analysis, validate

POLICY = 'mantle-fixed-ledge-contact-v1'
ROOT = Path(__file__).resolve().parents[2]


def skin_advance(raw, skeleton, spec, planar, skin):
    """Keep the body outside the front until its actual skin clears the top.

    Hands are independently anchored by the runtime arm control. This changes
    gameplay advance only; the generated pose, Root and Pelvis stay intact.
    """
    from .publish import world_pose
    world=world_pose(raw,skeleton);count=len(planar)
    body=np.setdiff1d(np.arange(skin.count),np.union1d(skin.sides['l'],skin.sides['r']))
    limits=np.empty(count);wrist_distances=[]
    for side in spec['hands']:
        p=world['hand_'+side[0]][1]
        wrist_distances.append(np.maximum(p[:,2]-spec['heightCm'],50-p[:,1]))
    near=np.flatnonzero(np.min(wrist_distances,axis=0)<20.)
    ready=float(near[0]*raw['frameRateDenominator']/raw['frameRateNumerator']) if len(near) else 0.
    for frame in range(count):
        pose={bone:(v[0][frame],v[1][frame]) for bone,v in world.items()}
        points=skin.evaluate(pose,body)
        below=points[:,2]<spec['heightCm']-.5
        excess=max(0.,float(points[below,1].max())-50.+.5) if np.any(below) else 0.
        limits[frame]=max(0.,planar[frame]-excess)
    allowed=np.minimum(planar,limits)
    allowed=np.minimum.accumulate(allowed[::-1])[::-1]
    dt=raw['frameRateDenominator']/raw['frameRateNumerator']
    result=allowed.copy()
    for frame in range(1,count):result[frame]=min(result[frame],result[frame-1]+200.*dt)
    if abs(result[-1]-planar[-1])>.001:
        raise ValueError('Skin-safe mantle advance cannot reach the landing endpoint')
    return result,ready,dict(policy='target-skin-front-clearance-v1',meshSha256=skin.mesh_sha256,
        depthMarginCm=.5,maximumAdvanceSpeedCmPerSecond=200.,
        maximumAdvanceDelayCm=float(np.max(planar-result)),bodyVertices=len(body),
        bodyBlendReadyTimeSeconds=ready)


def bake(config):
    from .publish import world_pose
    animation = config/'refactored_mantle_animation_inputs.json'
    roots = config/'refactored_mantle_root_tracks.json'
    inputs = read_json(animation); root_inputs = read_json(roots)
    trajectories = {s['source']: s for s in root_inputs['sequences']}
    sequences = []
    skin=None
    for row in inputs['sequences']:
        raw = row['raw']; source = raw['source']; name = source.split('.')[-1].removeprefix('A_Als_')
        spec = PROFILES[name]; guide, skeleton, provenance = reference_input(name)
        world = world_pose(guide, skeleton); trajectory = trajectories[source]
        count = raw['sampledKeyCount']; fps = raw['frameRateNumerator']/raw['frameRateDenominator']
        times = np.arange(count)/fps
        if abs(times[-1]-raw['playLength']) > 1e-5:
            raise ValueError('Contact bake requires the final authored key timeline')
        times[-1] = raw['playLength']
        planar, lift = root_progress(trajectory, count)
        if np.min(np.diff(planar)) < -.001 or np.min(np.diff(lift)) < -.001 or lift[-1] <= 0:
            raise ValueError('Invalid mantle progress trajectory')
        planar = np.maximum.accumulate(planar); lift = np.maximum.accumulate(lift)
        original_planar = planar.copy()
        body_control=None
        if name=='Mantle_High':
            if skin is None:
                from .mantle_hand_repair import immutable_target_skin
                skin=immutable_target_skin(skeleton)
            planar,ready,body_control=skin_advance(raw,skeleton,spec,planar,skin)
        anchors = {}; weights = {}
        for side in ('left', 'right'):
            weights[side] = np.zeros(count)
            anchors[side] = dict(positionCm=[0., 0., 0.], quaternionXYZW=[0., 0., 0., 1.])
            if side not in spec['hands']: continue
            start, end = spec.get('handContactSeconds', {}).get(side, spec['contactSeconds'])
            native_times = np.arange(guide['sampledKeyCount'])/(guide['frameRateNumerator']/guide['frameRateDenominator'])
            contact = (native_times >= start-1e-7) & (native_times <= end+1e-7)
            bone = 'hand_'+side[0]
            position = world[bone][1][contact].mean(axis=0)
            # Native conditioning fixture: edge at forward Y=50cm and nominal
            # platform height. Preserve wrist-to-palm clearance above the top.
            position -= np.array([0., 50., spec['heightCm']])
            position += np.asarray(spec.get('targetMeshHandOffsetsCm',{}).get(side,(0.,0.,0.)))
            orientation = Rotation.from_matrix(world[bone][0][contact]).mean().as_quat()
            anchors[side] = dict(positionCm=position.tolist(), quaternionXYZW=orientation.tolist())
            weights[side] = np.minimum(np.clip((times-start+1/fps)*fps, 0, 1),
                                       np.clip((end+1/fps-times)*fps, 0, 1))
        sequence=dict(source=source, semanticName=name, anchors=anchors, reference=provenance,transitionLiftCm=3.,
            keys=[dict(timeSeconds=float(t), leftHandSupport=float(weights['left'][i]),
                rightHandSupport=float(weights['right'][i]), rootAdvanceCm=float(original_planar[i]), advanceCm=float(planar[i]),
                liftCm=float(lift[i]), remainingLiftCm=float(lift[i]-lift[-1])) for i,t in enumerate(times)])
        if body_control:
            sequence.update(bodyAdvanceControl=body_control,bodyBlendReadyTimeSeconds=ready)
        sequence['curveAnalysis'] = analysis(sequence['keys'])
        sequences.append(sequence)
    value = dict(schemaVersion=1, policy=POLICY, animationInputsSha256=file_hash(animation), rootInputsSha256=file_hash(roots),
        coordinateSystem='ledge local; native mesh X/right Y/forward Z/up; centimeters',
        anchorPolicy='constant wrist position and palm orientation; platform-local transport; no arm stretching',
        maximumCorrectionCm=10., curveContract=contract(), sequences=sequences)
    validate(value, inputs, root_inputs)
    return value


def export(config, output=None):
    value = bake(Path(config)); key = value['animationInputsSha256']+'-'+value['rootInputsSha256']
    path = Path(output) if output else ROOT/'assets/generated/mantle_contacts'/f'{key}.json'
    if path.exists():
        if read_json(path) != value: raise ValueError('Changed mantle contact bake; use a new version')
    else:
        path.parent.mkdir(parents=True, exist_ok=True); write_json(path, value)
    return dict(file=str(path.resolve()), sha256=file_hash(path), policy=POLICY, sequences=len(value['sequences']),
        curveContract=value['curveContract'])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--config', type=Path, required=True); parser.add_argument('--output', type=Path)
    parser.add_argument('--deps',type=Path,default=ROOT/'artifacts/kimodo_als/offline-python-deps')
    args = parser.parse_args()
    import sys
    sys.path.insert(0,str(args.deps.resolve()))
    print(export(args.config, args.output))


if __name__ == '__main__': main()
