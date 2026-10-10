"""Independent airborne-loop authoring lane. References only condition generation.

Physics owns the actor trajectory; bounded periodic pelvis/limb reactions remain
in the asset. Grounded gait contacts and mantle support controls do not apply.
"""
from __future__ import annotations

import hashlib
from pathlib import Path
import numpy as np
from scipy.spatial.transform import Rotation

from .graph import read_json, topological_order
from .motion import complete, fk, local_from_global, validate_motion

POLICY = 'als-airborne-loop-v1'
INDEX = 'assets/config/refactored_animation_sources.json'
SEMANTICS = ('Fall', 'Fall_Fast', 'Flail')
LEAN_SEMANTICS = ('Air_Lean', 'Air_Lean_Forward', 'Air_Lean_Backward', 'Air_Lean_Left', 'Air_Lean_Right')
# Dedicated, new Air limits; existing gait/mantle/native-oracle gates are untouched.
LIMITS = dict(torsoTiltDegrees=35., pelvisHeightErrorCm=15., pelvisHeightRangeCm=20.,
              sourceRepairPositionCm=6., sourceRepairRotationDegrees=15.,
              planarResidualCm=6., endpointPositionCm=.01, endpointRotationDegrees=.01)


class AirError(ValueError):
    def __init__(self, message, receipt):
        self.receipt = receipt
        super().__init__(message)


def contract():
    return dict(policy=POLICY, semantics=list(SEMANTICS), actorTrajectoryOwner='character-physics',
                assetMotion='bounded-periodic-body-residual', groundFootLock=False,
                verticalDecomposition='bounded-linear-drift-before-periodic-fit', limits=LIMITS.copy())


def targets(contracts):
    selected = [f"asset:{r['edition']}:{r['asset']}" for r in contracts['assets']
                if r['semanticName'] in (*SEMANTICS, *LEAN_SEMANTICS)]
    if not selected: raise ValueError('No Air assets in the selected contract')
    return selected


def build_graph(contracts, rules):
    """Prune to the Air lane, including only its real transitive prerequisites."""
    from .graph import build_graph as build_all
    graph = build_all(contracts, rules)
    order = topological_order(graph['nodes'], targets(contracts))
    graph['nodes'] = {k: graph['nodes'][k] for k in order}
    graph['order'] = order
    graph['pipeline'] = 'air'
    graph['airContract'] = contract()
    from collections import Counter
    graph['summary'] = dict(assets=len(targets(contracts)), nodes=len(order),
                            operations=dict(Counter(n['op'] for n in graph['nodes'].values())))
    return graph


def add_branch(add, generate, nodes, key, row, rules):
    name = row['semanticName']
    if name not in SEMANTICS: raise ValueError('Unsupported Air semantic: '+name)
    native = 'reference:'+name+':native-air'
    add(native, 'native_air_reference', ['rig:soma'], indexPath=INDEX, semantic=name)
    candidate = 'candidate:'+name
    behaviour = ('gently moving bent legs and relaxed arms' if name=='Fall' else
                 'holding arms spread protectively with bent knees' if name=='Fall_Fast' else
                 'waving arms and kicking bent legs in a controlled airborne flail')
    prompt = ('A person remains suspended upright in the air, '+behaviour+'. '
              'Keep the torso upright and the hips at a stable height throughout; '
              'continue the airborne motion without landing, lying down or standing up.')
    period = row['originalTiming']['sampledKeyCount']-1
    fps = row['originalTiming']['frameRateNumerator']/row['originalTiming']['frameRateDenominator']
    if fps != rules['fps'] or period < 8: raise ValueError('Unsupported native Air timeline')
    generate(candidate, row, [native], [], prompt=prompt, duration=max(2., (3*period+1)/fps), path=None)
    nodes[candidate]['params']['airReference'] = native
    nodes[candidate]['params']['airPolicy'] = POLICY
    add(key, 'air_cycle', ['rig:soma', candidate, native], source=candidate,
        reference=native, semantic=name, policy=POLICY)


def reference_input(name, index_path=None):
    if name not in SEMANTICS: raise ValueError('Unsupported Air semantic: '+name)
    from .als_reference import original_input
    from .root_ownership import semantic_hash
    path=Path(index_path or Path(__file__).resolve().parents[2]/INDEX)
    index=read_json(path)
    rows=[r for r in index['assets'] if r['source'].endswith('.A_Als_'+name)]
    if len(rows)!=1: raise ValueError('Expected one immutable Air reference: '+name)
    index,row,payload=original_input(path,rows[0]['source'])
    raw=read_json(payload)['raw'];skel=index['skeletons'][raw['skeletonSource']]
    return raw,skel,dict(source=row['source'],sourceSha256=row['sha256'],
                         skeletonSha256=semantic_hash(skel),indexSha256=hashlib.sha256(path.read_bytes()).hexdigest())


def native_reference(name, rig, index_path=None):
    from .publish import world_pose
    from .offline_retarget import aligned_neutral, mapping, BASIS
    raw,skel,identity=reference_input(name,index_path)
    w=world_pose(raw,skel);names={n:i for i,n in enumerate(rig['jointNames'])}
    bone_map={b:n for b,n in mapping().items() if n in names}
    neutral=aligned_neutral(skel,rig,bone_map)
    inverse={n:b for b,n in bone_map.items()}
    aligned={n.lower():neutral[i] for i,n in enumerate(skel['rawBoneNames'])}
    count=raw['sampledKeyCount'];rot=np.tile(np.eye(3),(count,len(names),1,1))
    for n,i in names.items():
        bone=inverse.get(n)
        if bone:rot[:,i]=BASIS@(w[bone][0]@aligned[bone].T)@BASIS.T
        elif rig['parents'][i]>=0:rot[:,i]=rot[:,rig['parents'][i]]
    source_leg=sum(np.linalg.norm(rig['offsets'][names['Left'+n]]) for n in ('Shin','Foot'))
    target_leg=sum(np.linalg.norm(skel['referencePose'][next(i for i,b in enumerate(skel['rawBoneNames']) if b.lower()==n)]['position'])
                   for n in ('calf_l','foot_l'))
    scale=(target_leg/100)/source_leg
    root=w['pelvis'][1]@BASIS.T/(100*scale)
    motion=complete(local_from_global(rot,rig['parents']),root,rig,
                    raw['frameRateNumerator']/raw['frameRateDenominator'])
    return motion,dict(policy=POLICY,semanticName=name,scope='constraint_reference_only',
                       deliverable=False,productionReady=False,reference=identity,
                       uniformTargetScale=float(scale))


def materialize_constraints(params, reference, rig):
    if params.get('airPolicy')!=POLICY: raise ValueError('Unknown Air conditioning policy')
    count=int(params['seconds']*params['fps']);period=len(reference['root_positions'])-1
    if count<period+1 or float(reference['fps'])!=params['fps']:raise ValueError('Air conditioning timeline differs')
    frames=np.unique(np.linspace(0,count-1,min(19,count)).round().astype(int))
    indices=frames%period
    local=reference['local_rot_mats'][indices]
    poses=reference['root_positions'][indices]
    axis=Rotation.from_matrix(local.reshape(-1,3,3)).as_rotvec().reshape(len(frames),len(rig['jointNames']),3)
    # Hips sparse conditions explicitly constrain Y. root2d alone cannot do so.
    conditions=[dict(type='fullbody',frame_indices=frames.tolist(),local_joints_rot=axis.tolist(),root_positions=poses.tolist()),
                dict(type='end-effector',joint_names=['Hips'],frame_indices=frames.tolist(),
                     local_joints_rot=axis.tolist(),root_positions=poses.tolist())]
    return conditions,dict(policy=POLICY,semanticName=params['semantic'],generationFrames=count,
        referencePeriodFrames=period,fullBodyFrames=frames.tolist(),verticalHipFrames=frames.tolist(),
        groundFootLock=False,referenceOnly=True)


def _check(checks, name, value, limit):
    checks.append(dict(check=name,value=float(value),limit=float(limit),passed=bool(np.isfinite(value) and value<=limit)))


def _source_checks(motion, reference, rig):
    _,p=fk(motion['local_rot_mats'],motion['root_positions'],rig)
    names={n:i for i,n in enumerate(rig['jointNames'])}
    torso=p[:,names['Neck2']]-p[:,names['Hips']]
    tilt=np.rad2deg(np.arccos(np.clip(torso[:,1]/np.linalg.norm(torso,axis=1),-1,1)))
    root=motion['root_positions'];ref=reference['root_positions'];checks=[]
    _check(checks,'torsoTiltDegrees',tilt.max(),LIMITS['torsoTiltDegrees'])
    orientation=np.rad2deg(Rotation.from_matrix((motion['local_rot_mats'][:,0]@reference['local_rot_mats'][:,0].swapaxes(-1,-2))).magnitude())
    _check(checks,'pelvisOrientationFromReferenceDegrees',orientation.max(),35.)
    _check(checks,'pelvisHeightErrorCm',np.max(np.abs(root[:,1]-np.median(ref[:,1])))*100,LIMITS['pelvisHeightErrorCm'])
    _check(checks,'pelvisHeightRangeCm',np.ptp(root[:,1])*100,LIMITS['pelvisHeightRangeCm'])
    _check(checks,'planarResidualCm',np.max(np.linalg.norm(root[:,[0,2]]-root[:,[0,2]].mean(axis=0),axis=1))*100,LIMITS['planarResidualCm'])
    return checks


def _periodic_fit(values, period):
    """One Fourier cycle, evaluated with the SAME phase at both endpoints."""
    phase=np.arange(period)/period;harmonics=min(5,(period-1)//2)
    def design(t):
        return np.column_stack([np.ones(len(t)),*[f(2*np.pi*k*t) for k in range(1,harmonics+1) for f in (np.cos,np.sin)]])
    flat=np.asarray(values)[:period].reshape(period,-1)
    coefficients=np.linalg.lstsq(design(phase),flat,rcond=None)[0]
    result=(design(np.arange(period+1)/period)@coefficients).reshape((period+1,*values.shape[1:]))
    result[-1]=result[0]
    return result


def author(candidate, reference, rig, name):
    if name not in SEMANTICS:raise ValueError('Unknown Air authoring semantic')
    validate_motion(candidate,rig);validate_motion(reference,rig)
    count=len(reference['root_positions']);period=count-1;fps=float(reference['fps'])
    if float(candidate['fps'])!=fps or len(candidate['root_positions'])<count:raise ValueError('Air candidate timeline differs')
    _,_,identity=reference_input(name)
    attempts=[];accepted=[]
    for start in range(0,len(candidate['root_positions'])-period,period):
        local=candidate['local_rot_mats'][start:start+count].copy()
        root=candidate['root_positions'][start:start+count].copy()
        source=complete(local,root,rig,fps)
        checks=_source_checks(source,reference,rig)
        attempt=dict(startFrame=start,checks=checks,passed=False)
        attempts.append(attempt)
        if not all(c['passed'] for c in checks):continue
        # Bound the drift removal and regression together against the untouched
        # candidate. A meter-scale fall is rejected before any repair runs.
        drift=np.linspace(-.5,.5,count)[:,None]*(root[-1]-root[0])[None,:]
        residual=root-drift
        result_root=_periodic_fit(residual,period)
        result_local=np.empty_like(local)
        for j in range(local.shape[1]):
            rotations=Rotation.from_matrix(local[:-1,j]);centre=rotations.mean()
            vector=(centre.inv()*Rotation.from_matrix(local[:,j])).as_rotvec()
            result_local[:,j]=(centre*Rotation.from_rotvec(_periodic_fit(vector,period))).as_matrix()
        angular=np.rad2deg(Rotation.from_matrix((result_local@local.swapaxes(-1,-2)).reshape(-1,3,3)).magnitude()).max()
        movement=np.linalg.norm(result_root-root,axis=1).max()*100
        _check(checks,'boundedPeriodicRotationRepairDegrees',angular,LIMITS['sourceRepairRotationDegrees'])
        _check(checks,'boundedTrajectoryAndPeriodicRepairCm',movement,LIMITS['sourceRepairPositionCm'])
        output=complete(result_local,result_root,rig,fps)
        checks.extend(_source_checks(output,reference,rig))
        attempt['passed']=all(c['passed'] for c in checks)
        if attempt['passed']:
            score=float(angular+movement)
            accepted.append((score,output,dict(startFrame=start,removedLinearDriftMeters=drift.tolist(),
                             inputRootPositionsMeters=root.tolist(),outputRootPositionsMeters=result_root.tolist())))
    receipt=dict(policy=POLICY,semanticName=name,reference=identity,passed=bool(accepted),
                 sampledKeyCount=count,playLengthSeconds=period/fps,fps=fps,attempts=attempts,
                 groundFootLock=False,productionReady=False)
    if not accepted:raise AirError('No Air window passed source semantics and bounded periodic repair',receipt)
    _,motion,trajectory=min(accepted,key=lambda x:x[0])
    from .gait import _array_hash
    receipt['outputMotionSha256']=_array_hash(motion['local_rot_mats'],motion['root_positions'],motion['fps'])
    receipt['selectedStartFrame']=trajectory['startFrame']
    trajectory.update(policy=POLICY,semanticName=name,actorTrajectoryOwner='character-physics',units='meters Y-up',
                      outputMotionSha256=receipt['outputMotionSha256'])
    return motion,receipt,trajectory


def analyze_target(raw, skeleton, name):
    """Independent final runtime FK, stable height and periodic boundary gates."""
    from .publish import world_pose
    original,original_skeleton,identity=reference_input(name)
    world=world_pose(raw,skeleton);native=world_pose(original,original_skeleton)
    count=raw['sampledKeyCount'];fps=raw['frameRateNumerator']/raw['frameRateDenominator'];checks=[]
    if count<3:raise ValueError('Empty Air target cycle')
    if (count!=original['sampledKeyCount'] or abs(raw['playLength']-original['playLength'])>1e-6 or
        abs(raw['playLength']-(count-1)/fps)>1e-6):raise ValueError('Air target timeline differs from its immutable reference')
    torso=world['neck_01'][1]-world['pelvis'][1]
    tilt=np.rad2deg(np.arccos(np.clip(torso[:,2]/np.linalg.norm(torso,axis=1),-1,1)))
    p=world['pelvis'][1]-world['root'][1];baseline=np.median(native['pelvis'][1][:,2]-native['root'][1][:,2])
    _check(checks,'torsoTiltDegrees',tilt.max(),LIMITS['torsoTiltDegrees'])
    orientation=np.rad2deg(Rotation.from_matrix(world['pelvis'][0]@native['pelvis'][0].swapaxes(-1,-2)).magnitude())
    _check(checks,'pelvisOrientationFromReferenceDegrees',orientation.max(),35.)
    _check(checks,'pelvisHeightErrorCm',np.max(np.abs(p[:,2]-baseline)),LIMITS['pelvisHeightErrorCm'])
    _check(checks,'pelvisHeightRangeCm',np.ptp(p[:,2]),LIMITS['pelvisHeightRangeCm'])
    _check(checks,'planarResidualCm',np.max(np.linalg.norm(p[:,:2]-p[:,:2].mean(axis=0),axis=1)),LIMITS['planarResidualCm'])
    bones=tuple(n.lower() for n in skeleton['rawBoneNames'] if n.lower()!='root' and not n.lower().startswith('ik_'))
    endpoint=max(np.linalg.norm(world[n][1][-1]-world[n][1][0]) for n in bones)
    qerror=max(np.rad2deg(Rotation.from_matrix(world[n][0][-1]@world[n][0][0].T).magnitude()) for n in bones)
    velocity=max(np.linalg.norm((world[n][1][1]-world[n][1][0])-(world[n][1][-1]-world[n][1][-2]))*fps for n in bones)
    native_velocity=max(np.linalg.norm((native[n][1][1]-native[n][1][0])-(native[n][1][-1]-native[n][1][-2]))*fps for n in bones)
    _check(checks,'endpointPositionCm',endpoint,LIMITS['endpointPositionCm'])
    _check(checks,'endpointRotationDegrees',qerror,LIMITS['endpointRotationDegrees'])
    # Finite differences include acceleration even for a smooth periodic curve.
    # Measure against the immutable reference; do not confuse C0 with C1.
    _check(checks,'endpointVelocityMismatchCmPerSecond',velocity,max(30.,native_velocity*1.5))
    _check(checks,'actorRootTranslationCm',np.max(np.linalg.norm(world['root'][1]-np.asarray(skeleton['referencePose'][0]['position']),axis=1)),.001)
    root_rotation=Rotation.from_quat(skeleton['referencePose'][0]['rotation']).as_matrix()
    _check(checks,'actorRootRotationDegrees',np.rad2deg(Rotation.from_matrix(world['root'][0]@root_rotation.T).magnitude()).max(),.001)
    from .root_ownership import semantic_hash
    return dict(policy=POLICY,semanticName=name,reference=identity,checks=checks,passed=all(c['passed'] for c in checks),
                finalRawSemanticSha256=semantic_hash(raw),sampledKeyCount=count,
                playLengthSeconds=raw['playLength'],actorTrajectoryOwner='character-physics',groundFootLock=False)


def validate_source_receipt(job):
    from .runner import file_hash
    from .gait import _array_hash
    name=job['contract']['semanticName'];receipt=job.get('airValidationReceipt')
    if job.get('targetAirPolicy',job['contract'].get('targetAirPolicy'))!=POLICY:raise ValueError('Air asset requires its dedicated policy')
    if not receipt or file_hash(receipt['file'])!=receipt.get('sha256'):raise ValueError('Missing or changed Air authoring receipt')
    report=read_json(receipt['file']);_,_,identity=reference_input(name)
    timing=job['contract']['originalTiming']
    if (report.get('passed') is not True or report.get('policy')!=POLICY or report.get('semanticName')!=name or
        report.get('reference')!=identity or report.get('sampledKeyCount')!=timing['sampledKeyCount'] or
        abs(report.get('playLengthSeconds',-1)-timing['playLength'])>1e-6 or
        receipt.get('deliveredMotionSha256')!=job['motionSha256'] or file_hash(job['motionPath'])!=job['motionSha256']):
        raise ValueError('Air authoring receipt does not certify these inputs')
    with np.load(job['motionPath'],allow_pickle=False) as motion:
        if report.get('outputMotionSha256')!=_array_hash(motion['local_rot_mats'],motion['root_positions'],motion['fps']):
            raise ValueError('Air receipt motion identity differs')
        delivered_root=np.asarray(motion['root_positions']).copy()
    trajectory=job.get('airTrajectory')
    if not trajectory or file_hash(trajectory['file'])!=trajectory.get('sha256'):raise ValueError('Missing or changed Air trajectory decomposition')
    path=read_json(trajectory['file'])
    positions=np.asarray(path.get('outputRootPositionsMeters'),dtype=float)
    if (path.get('outputMotionSha256')!=report['outputMotionSha256'] or path.get('policy')!=POLICY or
        path.get('semanticName')!=name or positions.shape!=delivered_root.shape or not np.array_equal(positions,delivered_root)):
        raise ValueError('Air trajectory does not certify the delivered cycle')
    return report


def validate_pack(pack_path, pack):
    """Recheck actual staged inputs; stale or forged pass booleans are insufficient."""
    from .runner import file_hash
    from .publish import ROOT, CONFIG
    folder=Path(pack_path).parent
    bundle=read_json(pack['nativeRetargetBundle'])
    jobs=[j for j in bundle['jobs'] if j['contract']['semanticName'] in SEMANTICS]
    checks={c['source']:c for c in pack.get('targetAirChecks',[])}
    if not jobs and not checks:return dict(policy=POLICY,assets=0,passed=True)
    if pack.get('airPipelineContract')!=contract() or set(checks)!={j['source'] for j in jobs}:
        raise ValueError('Missing Air pipeline contract or per-asset target gates')
    files={f['path']:f['sha256'] for f in pack['files']}
    index=read_json(CONFIG/'refactored_animation_sources.json')
    v4=read_json(CONFIG/'v4_recovery_movement_source_inputs.json')['skeletons'][0]
    for job in jobs:
        validate_source_receipt(job)
        check=checks[job['source']]
        expected_file='target-air/'+hashlib.sha256(job['source'].encode()).hexdigest()+'/quality.json'
        if check.get('policy')!=POLICY or check.get('file')!=expected_file:
            raise ValueError('Air target report identity differs')
        report_path=folder/check['file']
        raw_path=folder/'root-ownership'/hashlib.sha256(job['source'].encode()).hexdigest()/'runtime.raw.json'
        for path in (report_path,raw_path):
            uri='res://'+path.resolve().relative_to(ROOT).as_posix()
            if not path.is_file() or files.get(uri)!=file_hash(path):raise ValueError('Missing or changed Air target payload')
        skel=index['skeletons']['/ALS/ALS/Character/SK_Als.SK_Als'] if job['edition']=='refactored' else v4
        actual=analyze_target(read_json(raw_path),skel,job['contract']['semanticName'])
        recorded=read_json(report_path)
        if (not actual['passed'] or not recorded['passed'] or recorded.get('policy')!=POLICY or
            recorded.get('reference')!=actual['reference'] or recorded.get('semanticName')!=actual['semanticName'] or
            recorded['finalRawSemanticSha256']!=actual['finalRawSemanticSha256']):
            raise ValueError('Air target gate failed; keep candidate inactive')
    return dict(policy=POLICY,assets=len(jobs),passed=True)
