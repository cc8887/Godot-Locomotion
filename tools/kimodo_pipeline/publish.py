"""Stage a complete non-prop pack without modifying original export fixtures.

Authoring and native retarget receipts are mandatory. Activation is a separate
step after runtime checks; staging alone never changes the normal Demo.
"""
from __future__ import annotations
import argparse
import copy
import hashlib
import json
import re
import shutil
from pathlib import Path
import numpy as np
from scipy.spatial.transform import Rotation,Slerp
from .graph import read_json,target_origin_reference
from .runner import file_hash,write_json
from .scope import is_prop,non_prop_targets
from . import root_curves,target_gait,root_ownership

ROOT=Path(__file__).resolve().parents[2]
CONFIG=ROOT/'assets/config'

def prepare(authoring,output,version):
    bundle=read_json(authoring);contracts=read_json(ROOT/'docs/architecture/kimodo-als-asset-contracts.json')
    expected=set(non_prop_targets(contracts));actual={a['node'] for a in bundle['assets']}
    if actual!=expected:raise ValueError('Authoring bundle is not the exact non-prop closure')
    jobs=[]
    for a in bundle['assets']:
        o=a['outputs'];receipt=read_json(Path(o['asset.json']['path']).with_name('receipt.json'))
        for name,payload in o.items():
            if file_hash(payload['path'])!=payload['sha256']:raise ValueError('Changed authoring payload: '+name)
        c=receipt['inputs']['node']['params']['contract']
        metadata=read_json(o['asset.json']['path'])
        job=dict(node=a['node'],source=c['source'],edition=c['edition'],contract=c,
                         bvhPath=o['motion.bvh']['path'],bvhSha256=o['motion.bvh']['sha256'],
                         motionPath=o['motion.npz']['path'],motionSha256=o['motion.npz']['sha256'])
        job['targetPlanarOrigin']=target_origin_reference(c,contracts['assets'])
        if c['family']=='mantle':
            job['targetMantlePolicy']=c.get('targetMantlePolicy')
            job['mantleValidationReceipt']=metadata.get('mantleValidationReceipt')
        if c.get('targetGaitPolicy'):
            job['targetGaitPolicy']=c['targetGaitPolicy']
            job['gaitValidationReceipt']=metadata.get('gaitValidationReceipt')
            job['authoringTrajectory']=metadata.get('authoringTrajectory')
            job['upperBodyValidationReceipt']=metadata.get('upperBodyValidationReceipt')
            validate_gait_receipt(job)
            if c.get('targetCleanup'):
                job['targetCleanup']=c['targetCleanup']
        jobs.append(job)
    write_json(output,dict(version=version,authoringBundle=str(Path(authoring).resolve()),jobs=jobs))

def resample_track(values,count,quaternion=False):
    values=np.asarray(values,dtype=float)
    if len(values)==1:return values.tolist()
    times=np.linspace(0,1,len(values));target=np.linspace(0,1,count)
    if quaternion:return Slerp(times,Rotation.from_quat(values))(target).as_quat().tolist()
    return np.stack([np.interp(target,times,values[:,i]) for i in range(values.shape[1])],axis=1).tolist()


def sample_native_track(values, native, target, quaternion=False):
    """Sample source TIME, preserving discrete pose-table frame identities.

    BVH import can add a repeated terminal key. Normalizing N+1 native keys to
    N authored keys blends unrelated pose-table slots and changes gait speed.
    """
    values=np.asarray(values,dtype=float)
    if len(values)==1:return values.tolist()
    if len(values)!=native['sampledKeyCount']:
        raise ValueError('Native track key count differs from its timeline')
    source_times=np.arange(len(values))*native['frameRateDenominator']/native['frameRateNumerator']
    target_times=np.arange(target['sampledKeyCount'])*target['frameRateDenominator']/target['frameRateNumerator']
    if target_times[-1]>source_times[-1]+1e-6:
        raise ValueError('Native retarget output does not cover the authored timeline')
    target_times=np.minimum(target_times,source_times[-1])
    if quaternion:return Slerp(source_times,Rotation.from_quat(values))(target_times).as_quat().tolist()
    return np.stack([np.interp(target_times,source_times,values[:,i]) for i in range(values.shape[1])],axis=1).tolist()

def verify_compatible(a,b):
    names=[n.lower() for n in a['rawBoneNames']]
    if names!=[n.lower() for n in b['rawBoneNames']] or a['rawParents']!=b['rawParents']:
        raise ValueError('Refactored needs a separate native target rig')
    # Logical virtual bones are regenerated from each skeleton's own metadata.
    # Compare physical bind transforms only; virtual definitions may differ.
    for x,y in zip(a['referencePose'][:len(names)],b['referencePose'][:len(names)]):
        if np.max(np.abs(np.asarray(x['position'])-y['position']))>1e-4 or np.max(np.abs(np.asarray(x['scale'])-y['scale']))>2e-6:
            raise ValueError('Target bind transform differs')
        error=(Rotation.from_quat(x['rotation']).inv()*Rotation.from_quat(y['rotation'])).magnitude()
        if error>np.deg2rad(.001):raise ValueError('Target bind rotation differs')

def world_pose(raw,skeleton):
    count=raw['sampledKeyCount'];names=skeleton['rawBoneNames'];parents=skeleton['rawParents']
    tracks={t['bone'].lower():t for t in raw['tracks']};rot=[];pos=[];scale=[]
    for i,name in enumerate(names):
        t=tracks.get(name.lower());ref=skeleton['referencePose'][i]
        local_r=Rotation.from_quat(np.broadcast_to(np.asarray(t['rotations'] if t else [ref['rotation']]),(count,4))).as_matrix()
        local_p=np.broadcast_to(np.asarray(t['positions'] if t else [ref['position']]),(count,3))
        local_s=np.broadcast_to(np.asarray(t['scales'] if t and t['scales'] else [ref['scale']]),(count,3))
        p=parents[i]
        if p<0:rot.append(local_r);pos.append(local_p);scale.append(local_s)
        else:
            rot.append(rot[p]@local_r);pos.append(pos[p]+np.einsum('tij,tj->ti',rot[p],scale[p]*local_p));scale.append(scale[p]*local_s)
    return dict(zip((n.lower() for n in names),zip(rot,pos,scale)))

def contact_data(raw,skeleton):
    world=world_pose(raw,skeleton);count=raw['sampledKeyCount'];length=raw['playLength']
    contacts=[];times=[]
    for side in ('l','r'):
        height=world['foot_'+side][1][:,2]/100
        floor=float(np.percentile(height,10));contact=np.clip(1-(height-floor)/.035,0,1)
        # Author a conservative height-based envelope on the FINAL target rig.
        # The release record explicitly leaves visual contact acceptance open.
        contacts.append(contact);times.append(float(np.argmin(height))*length/max(count-1,1))
    return contacts,times

def target_root_and_ik(raw,skeleton,job,*,canonical_origin=None,origin_reference=None):
    """One target-space trajectory split, shared by raw/FBX and curve exports."""
    world=world_pose(raw,skeleton);count=raw['sampledKeyCount']
    root_rotation=world['root'][0].copy()
    if job.get('_mantleRootRotation') is not None:root_rotation=job['_mantleRootRotation']
    ev=job['contract']['originalEvaluation'];family=job['contract']['family']
    if family in ('turn','rotate'):
        with np.load(job['motionPath'],allow_pickle=False) as motion:
            soma_names=list(motion['joint_names']);pos=motion['posed_joints']
            hips=pos[:,soma_names.index('LeftLeg')]-pos[:,soma_names.index('RightLeg')]
            heading=np.unwrap(np.arctan2(-hips[:,2],hips[:,0]))
        heading=np.interp(np.linspace(0,1,count),np.linspace(0,1,len(heading)),heading)
        root_rotation=Rotation.from_euler('z',(-heading+heading[0])[:,None]).as_matrix()@root_rotation
    authored,runtime,report=root_ownership.decompose(raw,skeleton,family=family,
        declared_root_motion=ev['enableRootMotion'],root_rotation=root_rotation,
        canonical_origin=canonical_origin,origin_reference=origin_reference,
        mantle_root_positions=job.get('_mantleRootPositions'))
    job['_authoredRootRaw']=authored;job['_rootOwnershipReport']=report
    if family in ('fall','flail'):
        from .air import analyze_target
        job['_targetAirReport']=analyze_target(runtime,skeleton,job['contract']['semanticName'])
    return runtime

def curve_values(name,job,contacts,gait_report=None):
    count=job['contract']['originalTiming']['sampledKeyCount'];length=job['contract']['originalTiming']['playLength']
    left,right=contacts
    if name in ('FootLeftLock','FootRightLock','FootLock_L','FootLock_R') and \
            job['contract'].get('originalEvaluation',{}).get('additiveType','AAT_None')!='AAT_None':
        # Additive Slot lock curves are signed graph controls, not absolute
        # contact probabilities. Keep the authored unlock/lock windows in the
        # preserved time domain; a new transition needs its own phase contract.
        return None
    if name in ('FootLeftLock','FootLock_L'):return left
    if name in ('FootRightLock','FootLock_R'):return right
    if name in ('FootPlanted','Feet_Position'):
        if gait_report is not None:
            values=np.asarray(gait_report['footPlanted'])
            # V4 authors Plant as +/- .2; Refactored authors +/- .25.
            # Both reserve full +/-1 for the stable support phases.
            return np.where(np.isclose(np.abs(values),.25),np.sign(values)*.2,values) if name=='Feet_Position' else values
        # Compatibility envelopes for families outside the gated forward
        # pilots. Correct native side/sign and scale, but do not certify these
        # envelopes as an independently validated support event protocol.
        return right-left
    if name in ('RotationYawSpeed','RotationAmount','RotationYawSpeedRadiansPerSecond'):
        with np.load(job['motionPath'],allow_pickle=False) as motion:
            names=list(motion['joint_names']);pos=motion['posed_joints']
            hips=pos[:,names.index('LeftLeg')]-pos[:,names.index('RightLeg')]
            heading=np.unwrap(np.arctan2(-hips[:,2],hips[:,0]))
        speed=-np.gradient(heading,np.linspace(0,length,len(heading)))
        if name=='RotationAmount':return np.rad2deg(speed)/30
        if name=='RotationYawSpeed':return np.rad2deg(speed)
        return speed
    return None

def rewrite_curves(curves,job,contacts,gait_report=None):
    curves=copy.deepcopy(curves)
    for c in curves['curves']:
        values=curve_values(c['name'],job,contacts,gait_report)
        if values is None:continue
        if len(values)!=job['contract']['originalTiming']['sampledKeyCount']:raise ValueError('Curve sample count differs')
        prototype=c['keys'][0] if c['keys'] else dict(arriveTangent=0,leaveTangent=0,arriveWeight=0,leaveWeight=0,
                                                    tangentMode='RCTM_Auto',weightMode='RCTWM_WeightedNone')
        keys=[]
        for time,value in zip(np.linspace(0,job['contract']['originalTiming']['playLength'],len(values)),values):
            k=copy.deepcopy(prototype);k.update(time=float(time),value=float(value),arriveTangent=0,leaveTangent=0,interpolation='RCIM_Linear');keys.append(k)
        c['keys']=keys
    return curves


def assemble_overlay_lower_body(jobs, rewritten, skeletons):
    """Standing Overlay slots share Stand legs, including the moving slot.

    These are reference poses for an additive Layering graph, not locomotion
    support samples. Upper-body performance remains in each authored table.
    """
    by_semantic={(j['edition'],j['contract']['semanticName']):source for source,j in jobs.items()}
    tables={'Default_Poses','Normal_Poses','Feminine_Poses','Masculine_Poses','Injured_Poses','HandsTied_Poses'}
    reports=[]
    for source,j in jobs.items():
        if j['contract']['semanticName'] not in tables:continue
        raw=rewritten[source];count=raw['sampledKeyCount']
        if count!=4:raise ValueError('Non-prop Overlay pose table must retain its four authored slots')
        refs=[rewritten[by_semantic[(j['edition'],name)]] for name in ('Stand_Pose','Stand_Pose','Crouch_Pose','Crouch_Pose')]
        refs=[{t['bone'].lower():t for t in r['tracks']} for r in refs]
        for track in raw['tracks']:
            name=track['bone'].lower()
            if name!='pelvis' and not name.startswith(('thigh_','calf_','foot_','ball_')):continue
            for channel in ('positions','rotations','scales'):
                values=[r[name][channel][0] for r in refs if r[name][channel]]
                track[channel]=values if values else []
        previous=j.get('_rootOwnershipReport')
        rewritten[source]=target_root_and_ik(raw,skeletons[j['edition']],j)
        if previous:j['_rootOwnershipReport']['beforeOverlayAssembly']=previous
        j['_rootOwnershipReport']['canonicalBodyDependencies']=[dict(source=by_semantic[(j['edition'],name)],
            runtimeRawSemanticSha256=root_ownership.semantic_hash(rewritten[by_semantic[(j['edition'],name)]]))
            for name in ('Stand_Pose','Crouch_Pose')]
        reports.append(dict(source=source,slotBases=['Stand_Pose','Stand_Pose','Crouch_Pose','Crouch_Pose']))
    return reports

def prepare_target_raw(j,v4,ref,index,*,factor_root=True):
    source=j['source']
    if j['edition']=='refactored':old=read_json(CONFIG/next(r['file'] for r in index['assets'] if r['source']==source))['raw'];skel=ref
    else:
        original_raw=CONFIG/'raw_sequences'/(hashlib.sha1(source.encode()).hexdigest()+'.json');skel=v4
        if original_raw.exists():old=read_json(original_raw)
        else:
            old=read_json(j['raw']);old.update(j['contract']['originalTiming'])
            old.update(source=source,skeletonSource=v4['source'])
            # V4 reference assets without an original raw bank need only
            # physical tracks. Native retarget receipts also contain evaluated
            # virtual bones, which are reconstructed from the final body/IK
            # hierarchy and cannot be authored or exported as FBX bones.
            physical={name.lower() for name in v4['rawBoneNames']}
            old['tracks']=[t for t in old['tracks'] if t['bone'].lower() in physical]
    target=read_json(j['raw']);lookup={t['bone'].lower():t for t in target['tracks']};result=copy.deepcopy(old);result['tracks']=[]
    for track in old['tracks']:
        candidate=lookup.get(track['bone'].lower())
        if candidate is None:raise ValueError('Target lacks bone '+track['bone'])
        new={'bone':track['bone']}
        for field in ('positions','rotations','scales'):
            values=candidate[field];new[field]=sample_native_track(values,target,old,field=='rotations') if values else []
        result['tracks'].append(new)
    policy=j.get('targetGaitPolicy',j['contract'].get('targetGaitPolicy'))
    if j['contract'].get('family') in ('fall','flail'):
        from .air import validate_source_receipt
        validate_source_receipt(j)
    if j['contract'].get('family')=='mantle':
        from .mantle import target,POLICY,reference_input
        if j.get('targetMantlePolicy',j['contract'].get('targetMantlePolicy'))!=POLICY:
            raise ValueError('Mantle requires its configured target policy')
        receipt=j.get('mantleValidationReceipt')
        if not receipt or file_hash(receipt['file'])!=receipt['sha256']:
            raise ValueError('Missing or changed mantle authoring receipt')
        certified=read_json(receipt['file'])
        _,_,identity=reference_input(j['contract']['semanticName'])
        if (not certified.get('passed') or certified.get('policy')!=POLICY or
            certified.get('semanticName')!=j['contract']['semanticName'] or certified.get('reference')!=identity or
            receipt.get('deliveredMotionSha256')!=j['motionSha256'] or
            certified.get('sampledKeyCount')!=result['sampledKeyCount'] or
            abs(certified.get('playLengthSeconds',-1)-result['playLength'])>1e-6):
            raise ValueError('Mantle authoring receipt does not certify these inputs')
        j['_targetMantleInputRaw']=copy.deepcopy(result)
        result,path,rotation,report=target(result,skel,j['contract']['semanticName'])
        from .mantle_hand_repair import immutable_target_skin, repair as repair_mantle_hands
        result,hand_report=repair_mantle_hands(result,skel,j['contract']['semanticName'],immutable_target_skin(skel))
        report['handSkinRepair']=hand_report
        report['checks'].append(dict(check='boundedTargetHandSkinRepair',passed=hand_report['passed']))
        report['passed']=report['passed'] and hand_report['passed']
        j['_mantleRootPositions']=path;j['_targetMantleReport']=report
        j['_targetMantleRepairedRaw']=copy.deepcopy(result)
        j['_mantleRootRotation']=rotation
        policy=None
    name=target_gait.asset_name(source)
    periodic=(policy and name in target_gait.PILOTS and
              target_gait.PILOTS[name][1]!='sprint_acceleration' and
              target_gait.original_protocol()['assets'][target_gait.PILOTS[name][1]]['contactMode']=='flat_sole')
    if periodic:
        validate_gait_receipt(j)
        from .target_cleanup import repair_periodic_gait
        j['_targetPhaseInputRaw']=copy.deepcopy(result)
        try:
            result,phase,cleanup_report=repair_periodic_gait(result,skel,source)
            j['_targetPhaseReport']=phase;j['_targetCleanupReport']=cleanup_report
            j['_targetPhaseRepairedRaw']=copy.deepcopy(result)
        except ValueError as error:
            j['_targetPhaseReport']=getattr(error,'phase',dict(passed=False,error=str(error),source=source))
            j['_targetCleanupReport']=dict(passed=False,error=str(error),applied=False)
            # Phase inference seeds an optional repair; it is not an extra
            # asset-quality gate. Retain input and let the unchanged strict
            # geometry/protocol gates decide, also in normal publication.
    elif j.get('targetCleanup'):
        if j['contract']['semanticName']!='Walk_Forward':
            raise ValueError('Target cleanup currently supports forward Walk only')
        validate_gait_receipt(j)
        from .target_cleanup import cleanup_walk
        try:
            result,cleanup_report=cleanup_walk(result,skel,read_json(j['gaitValidationReceipt']['file']),
                phase_shift=j['targetCleanup']['phaseShiftFrames'])
            j['_targetCleanupReport']=cleanup_report
        except ValueError as error:
            if not j.get('_diagnosticConversion'):raise
            j['_targetCleanupReport']=dict(passed=False,error=str(error),applied=False)
    return (target_root_and_ik(result,skel,j) if factor_root else result),skel


def prepare_target_closure(jobs,v4,ref,index):
    """Factor origins in dependency order, independently of bundle job order."""
    inputs={source:prepare_target_raw(j,v4,ref,index,factor_root=False) for source,j in jobs.items()}
    rows=[j['contract'] for j in jobs.values()]
    rewritten={};visiting=set()
    def visit(source):
        if source in rewritten:return
        if source in visiting:raise ValueError('Target origin dependency cycle: '+source)
        if source not in jobs:raise ValueError('Missing target origin dependency: '+source)
        visiting.add(source);j=jobs[source];raw,skel=inputs[source]
        spec=target_origin_reference(j['contract'],rows)
        if 'targetPlanarOrigin' in j and j['targetPlanarOrigin']!=spec:
            raise ValueError('Target origin specification differs from the asset contract')
        origin=None;reference=None
        if spec:
            parent=spec['source'];visit(parent);parent_job=jobs[parent]
            authored=parent_job['_authoredRootRaw'];parent_report=parent_job['_rootOwnershipReport']
            with np.load(parent_job['motionPath'],allow_pickle=False) as motion:
                frame=spec.get('frame')
                if frame is None:frame=round(spec['fraction']*(len(motion['root_positions'])-1))
                time=frame/float(motion['fps'])
            times=np.arange(authored['sampledKeyCount'])*authored['frameRateDenominator']/authored['frameRateNumerator']
            if time<0 or time>times[-1]+1e-6:raise ValueError('Origin reference outside target timeline: '+source)
            root=world_pose(authored,inputs[parent][1])['root'][1]
            sampled=np.asarray([np.interp(time,times,root[:,axis]) for axis in range(2)])
            origin=np.asarray(parent_report['canonicalOriginTranslationCm']).copy()
            origin[:2]+=sampled-np.asarray(inputs[parent][1]['referencePose'][0]['position'])[:2]
            reference=dict(spec,sourceAuthoredFrame=frame,sourceTimeSeconds=time,
                sourceInputSemanticSha256=parent_report['sourceRawSemanticSha256'],
                sourceAuthoredRootSemanticSha256=parent_report['authoredRootRawSemanticSha256'],
                sourceRuntimeSemanticSha256=parent_report['runtimeRawSemanticSha256'])
        rewritten[source]=target_root_and_ik(raw,skel,j,canonical_origin=origin,origin_reference=reference)
        visiting.remove(source)
    for source in jobs:visit(source)
    return rewritten

def validate_gait_receipt(job):
    """A target pilot cannot bypass failed/stale upstream authoring checks."""
    receipt=job.get('gaitValidationReceipt')
    if not isinstance(receipt,dict):raise ValueError('Missing gait validation receipt: '+job['source'])
    for path_key,hash_key in (('file','sha256'),('sourceMotionFile','sourceMotionSha256')):
        if not receipt.get(path_key) or not receipt.get(hash_key) or file_hash(receipt[path_key])!=receipt[hash_key]:
            raise ValueError('Changed gait receipt input: '+path_key)
    if receipt.get('deliveredMotionSha256')!=job['motionSha256'] or file_hash(job['motionPath'])!=job['motionSha256']:
        raise ValueError('Gait receipt does not bind delivered motion')
    if read_json(receipt['file']).get('pass') is not True:raise ValueError('Upstream gait validation failed')
    upper=job.get('upperBodyValidationReceipt')
    if job.get('contract',{}).get('targetUpperBodyPolicy') and not upper:
        raise ValueError('Missing required upper-body validation receipt')
    if upper:
        if not upper.get('file') or file_hash(upper['file'])!=upper.get('sha256'):
            raise ValueError('Changed upper-body validation receipt')
        report=read_json(upper['file'])
        if report.get('passed') is not True:raise ValueError('Upstream upper-body validation failed')
        from .gait import _array_hash
        with np.load(receipt['sourceMotionFile'],allow_pickle=False) as motion:
            if report['motionSha256']!=_array_hash(motion['local_rot_mats'],motion['root_positions'],motion['fps']):
                raise ValueError('Upper-body receipt differs from validated source motion')
    trajectory=job.get('authoringTrajectory')
    if not isinstance(trajectory,dict) or not trajectory.get('file') or not trajectory.get('sha256'):
        raise ValueError('Missing authored locomotion trajectory')
    if file_hash(trajectory['file'])!=trajectory['sha256']:raise ValueError('Authored locomotion trajectory changed')
    return receipt

def audit_target_jobs(retarget,output):
    """Check-only diagnostics retain failed target evidence before staging."""
    bundle=read_json(retarget)
    index=read_json(CONFIG/'refactored_animation_sources.json')
    v4=read_json(CONFIG/'v4_recovery_movement_source_inputs.json')['skeletons'][0]
    ref=index['skeletons']['/ALS/ALS/Character/SK_Als.SK_Als']
    reports=[]
    for j in bundle['jobs']:
        policy=j.get('targetGaitPolicy',j['contract'].get('targetGaitPolicy'))
        if not policy:continue
        if policy!=target_gait.POLICY:raise ValueError('Unknown target gait policy: '+str(policy))
        validate_gait_receipt(j)
        for key in ('raw','motionPath'):
            hash_key='rawSha256' if key=='raw' else 'motionSha256'
            if file_hash(j[key])!=j[hash_key]:raise ValueError('Changed target gate input: '+key)
        raw,skeleton=prepare_target_raw(j,v4,ref,index)
        report=target_gait.analyze_target_gait(raw,skeleton,j['source'])
        if j.get('_targetPhaseReport'):report['phaseDetection']=j['_targetPhaseReport']
        if j.get('_targetCleanupReport'):report['targetCleanup']=j['_targetCleanupReport']
        report['inputs']={key:j[key] for key in ('raw','rawSha256','motionPath','motionSha256')}
        report['gaitValidationReceipt']=j.get('gaitValidationReceipt')
        reports.append(report)
    result=dict(schemaVersion=1,policy=target_gait.POLICY,retargetBundle=str(Path(retarget).resolve()),
                retargetBundleSha256=file_hash(retarget),passed=bool(reports) and all(r['passed'] for r in reports),assets=reports)
    write_json(output,result)
    return result

def stage(retarget,output,*,diagnostic=False):
    output=Path(output).resolve()
    if output.parent!=ROOT/'assets/generated' or not output.name.startswith('als_nonprop_'):
        raise ValueError('Stage must be a new assets/generated/als_nonprop_* directory')
    if output.exists():raise ValueError('Stage directory exists; preserve it and use a fresh version')
    bundle=read_json(retarget);jobs={j['source']:j for j in bundle['jobs']}
    contracts=read_json(ROOT/'docs/architecture/kimodo-als-asset-contracts.json')
    expected={r['source'] for r in contracts['assets'] if not is_prop(r)}
    if not set(jobs)<=expected or (not diagnostic and set(jobs)!=expected):raise ValueError('Incomplete native retarget closure')
    for j in jobs.values():
        if diagnostic:j['_diagnosticConversion']=True
        for key in ('raw','fbx','bvhPath','motionPath'):
            hash_key=key+'Sha256' if key in ('raw','fbx') else key.removesuffix('Path')+'Sha256'
            if file_hash(j[key])!=j[hash_key]:raise ValueError('Changed retarget input: '+key)
    index=read_json(CONFIG/'refactored_animation_sources.json');v4=read_json(CONFIG/'v4_recovery_movement_source_inputs.json')['skeletons'][0]
    ref=index['skeletons']['/ALS/ALS/Character/SK_Als.SK_Als'];verify_compatible(v4,ref)
    output.mkdir();config=output/'config';raw_dir=output/'raw';asset_dir=output/'v4'
    shutil.copytree(CONFIG,config,ignore=shutil.ignore_patterns('als_resource_pack.json'))
    original=ROOT/'assets/generated/als_v4'
    shutil.copytree(original,asset_dir,ignore=shutil.ignore_patterns('compiled','*.import'))
    raw_dir.mkdir()
    rewritten=prepare_target_closure(jobs,v4,ref,index)
    contacts_by_source={};time_by_source={};motion_curves=[];gait_reports={};gait_files=[];authoring_trajectories=[];unsupported_gaits=[]
    mantle_reports={};air_reports={}
    for source,j in jobs.items():
        result=rewritten[source];skel=ref if j['edition']=='refactored' else v4
        if j.get('_targetAirReport'):
            report=j['_targetAirReport'];air_reports[source]=report
            report['authoringReceipt']=j['airValidationReceipt']
            folder=output/'target-air'/hashlib.sha256(source.encode()).hexdigest()
            folder.mkdir(parents=True)
            write_json(folder/'quality.json',report)
            shutil.copy2(j['airValidationReceipt']['file'],folder/'authoring.json')
            shutil.copy2(j['airTrajectory']['file'],folder/'trajectory.json')
            gait_files.extend(folder.glob('*.json'))
            if not report['passed'] and not diagnostic:
                raise ValueError('Target Air gate failed for '+source+'; evidence: '+str(folder/'quality.json'))
        if j.get('_targetMantleReport'):
            report=j['_targetMantleReport'];mantle_reports[source]=report
            report.update(finalRawSemanticSha256=root_ownership.semantic_hash(result),
                          authoringReceipt=j['mantleValidationReceipt'])
            folder=output/'target-mantle'/hashlib.sha256(source.encode()).hexdigest()
            folder.mkdir(parents=True)
            write_json(folder/'quality.json',report)
            write_json(folder/'before.raw.json',j['_targetMantleInputRaw'])
            write_json(folder/'after.raw.json',j['_targetMantleRepairedRaw'])
            shutil.copy2(j['mantleValidationReceipt']['file'],folder/'authoring.json')
            from .mantle import contact_curves
            write_json(folder/'contact-curves.json',contact_curves(report,result))
            gait_files.extend(folder.glob('*.json'))
            if not report['passed'] and not diagnostic:
                raise ValueError('Target mantle gate failed for '+source+'; evidence: '+str(folder/'quality.json'))
        if j.get('upperBodyValidationReceipt'):
            upper_path=output/'upper-body'/(hashlib.sha256(source.encode()).hexdigest()+'.json')
            upper_path.parent.mkdir(exist_ok=True)
            shutil.copy2(j['upperBodyValidationReceipt']['file'],upper_path)
            gait_files.append(upper_path)
        if j.get('_targetPhaseReport'):
            phase_dir=output/'target-phase'/hashlib.sha256(source.encode()).hexdigest()
            phase_dir.mkdir(parents=True,exist_ok=True)
            write_json(phase_dir/'phase.json',j['_targetPhaseReport'])
            write_json(phase_dir/'repair.json',j['_targetCleanupReport'])
            write_json(phase_dir/'before.raw.json',j['_targetPhaseInputRaw'])
            if j.get('_targetPhaseRepairedRaw'):write_json(phase_dir/'after.raw.json',j['_targetPhaseRepairedRaw'])
            gait_files.extend(phase_dir.glob('*.json'))
        policy=j.get('targetGaitPolicy',j['contract'].get('targetGaitPolicy'))
        if policy:
            if policy!=target_gait.POLICY:raise ValueError('Unknown target gait policy: '+str(policy))
            validate_gait_receipt(j)
            try:
                report=target_gait.analyze_target_gait(result,skel,source)
            except ValueError as error:
                if not diagnostic:raise
                report=dict(source=source,policy=policy,passed=False,error=str(error),status='analysis_failed')
                gait_path=output/'target-gait'/(hashlib.sha256(source.encode()).hexdigest()+'.json')
                gait_path.parent.mkdir(parents=True,exist_ok=True);write_json(gait_path,report);gait_files.append(gait_path)
                unsupported_gaits.append(report)
                contacts,times=contact_data(result,skel)
                contacts_by_source[source]=contacts;time_by_source[source]=times
                trajectory_path=output/'authoring-trajectories'/(hashlib.sha256(source.encode()).hexdigest()+'.json')
                trajectory_path.parent.mkdir(exist_ok=True);shutil.copy2(j['authoringTrajectory']['file'],trajectory_path)
                authoring_trajectories.append(dict(source=source,file=trajectory_path.relative_to(output).as_posix(),
                    sha256=file_hash(trajectory_path),kind='removed_authoring_trajectory',gaitReceipt=j['gaitValidationReceipt']))
                continue
            if j.get('_targetCleanupReport'):report['targetCleanup']=j['_targetCleanupReport']
            if j.get('_targetPhaseReport'):report['phaseDetection']=j['_targetPhaseReport']
            report['inputs']={key:j[key] for key in ('raw','rawSha256','motionPath','motionSha256')}
            report['gaitValidationReceipt']=j.get('gaitValidationReceipt')
            gait_path=output/'target-gait'/(hashlib.sha256(source.encode()).hexdigest()+'.json')
            gait_path.parent.mkdir(parents=True,exist_ok=True)
            write_json(gait_path,report);gait_reports[source]=report;gait_files.append(gait_path)
            if not report['passed'] and not diagnostic:
                failed=', '.join(c['check'] for c in report['checks'] if not c['passed'])
                raise ValueError('Target gait gate failed for '+source+': '+failed+'; evidence: '+str(gait_path))
            trajectory_path=output/'authoring-trajectories'/(hashlib.sha256(source.encode()).hexdigest()+'.json')
            trajectory_path.parent.mkdir(exist_ok=True)
            shutil.copy2(j['authoringTrajectory']['file'],trajectory_path)
            authoring_trajectories.append(dict(source=source,file=trajectory_path.relative_to(output).as_posix(),
                sha256=file_hash(trajectory_path),kind='removed_authoring_trajectory',gaitReceipt=j['gaitValidationReceipt']))
            contacts=[np.asarray(c) for c in report['contacts']]
            fallback_times=contact_data(result,skel)[1]
            times=[next((e['time'] for e in report['events'] if e['side']==side),fallback_times[i]) for i,side in enumerate(('left','right'))]
        else:contacts,times=contact_data(result,skel)
        if j['contract']['semanticName'] in ('Fall','Fall_Fast','Flail','Air_Lean','Air_Lean_Forward','Air_Lean_Backward','Air_Lean_Left','Air_Lean_Right'):
            # No grounded contact estimate or FootLock may be authored into Air.
            contacts=[np.zeros(result['sampledKeyCount']),np.zeros(result['sampledKeyCount'])]
        rewritten[source]=result;contacts_by_source[source]=contacts;time_by_source[source]=times
    overlay_contracts=assemble_overlay_lower_body(jobs,rewritten,{'v4':v4,'refactored':ref})
    ownership_files=[];ownership_receipts=[];extracted_curves=[];final_fbx_jobs=[]
    for source,j in jobs.items():
        result=rewritten[source]
        curve=root_curves.export(output,result,j)
        if curve:motion_curves.append(curve)
        identifier=hashlib.sha256(source.encode()).hexdigest()
        folder=output/'root-ownership'/identifier;folder.mkdir(parents=True)
        report=j['_rootOwnershipReport'];report.update(source=source,
            runtimeRawSemanticSha256=root_ownership.semantic_hash(result))
        authored=j['_authoredRootRaw']
        write_json(folder/'authored-root.raw.json',authored)
        write_json(folder/'runtime.raw.json',result)
        write_json(folder/'ownership.json',report)
        extracted=root_curves.export(folder,authored,j)
        if extracted:
            extracted.update(file=(folder.relative_to(output)/extracted['file']).as_posix(),
                kind='extracted-root-before-ALS-in-place-view',
                displayedRuntimeRawSemanticSha256=report['runtimeRawSemanticSha256'])
            extracted_curves.append(extracted)
        ownership_files.extend(folder.rglob('*.json'))
        ownership_receipts.append(dict(source=source,file=(folder/'ownership.json').relative_to(output).as_posix(),
            sha256=file_hash(folder/'ownership.json')))
        if j['edition']=='v4':
            final_fbx_jobs.append(dict(source=source,sourceAsset=j['retargetedAssetPath'],
                raw=str(folder/'runtime.raw.json'),rawSha256=file_hash(folder/'runtime.raw.json')))
    # Refactored payloads and embedded base/head tables use the same full poses.
    curves={}
    for entry in index['assets']:
        source=entry['source']
        if source not in rewritten:continue
        path=config/entry['file'];payload=read_json(path)
        payload['raw']=rewritten[source];payload['curves']=rewrite_curves(payload['curves'],jobs[source],contacts_by_source[source],gait_reports.get(source))
        curves[source]=payload['curves'];write_json(path,payload)
    sync=read_json(config/'refactored_sync_inputs.json');notify=read_json(config/'refactored_notify_inputs.json')
    for entry in index['assets']:
        source=entry['source']
        if source not in rewritten:continue
        path=config/entry['file'];payload=read_json(path);text=payload['nativeText']
        sync_row=next((r for r in sync['assets'] if r['source']==source),None)
        if sync_row:
            for marker in sync_row.get('markers',[]):
                side=0 if marker['name'].lower()=='left' else 1
                marker['time']=time_by_source[source][side]
            sync_row['markers'].sort(key=lambda x:x['time'])
            for i,m in enumerate(sync_row['markers']):m['index']=i
            text=re.sub(r'(?m)^   AuthoredSyncMarkers\(\d+\)=[^\r\n]*\r?\n','',text)
            text+=''.join(f'   AuthoredSyncMarkers({m["index"]})=(MarkerName="{m["name"]}",Time={m["time"]:.6f},TrackIndex={m["track"]})\n' for m in sync_row['markers'])
        if source in gait_reports:
            protocol=target_gait.validate_als_protocol(payload,gait_reports[source],sync_row['markers'] if sync_row else [])
            gait_reports[source]['protocol']=protocol
            gait_reports[source]['passed']=gait_reports[source]['passed'] and protocol['passed']
            gait_path=output/'target-gait'/(hashlib.sha256(source.encode()).hexdigest()+'.json')
            write_json(gait_path,gait_reports[source])
            if not protocol['passed'] and not diagnostic:raise ValueError('ALS target support protocol failed: '+source)
        nrow=next((r for r in notify['assets'] if r['path']==source),None)
        if nrow:
            for n in nrow['notifies']:
                if n['class'].endswith('AlsAnimNotify_FootstepEffects'):
                    side=0 if n['payload']['foot_bone']=='LEFT' else 1
                    n['time']=float(np.float32(time_by_source[source][side]))
                    n['triggerTime']=float(np.float32(np.float32(n['time'])+np.float32(n['triggerOffset'])))
                    n['endTriggerTime']=n['triggerTime']
                    pattern=r'(?m)^   Notifies\('+str(n['index'])+r'\)=([^\r\n]*)'
                    def rewrite_event(match):
                        event=re.sub(r'LinkValue=[-0-9.]+',f'LinkValue={n["time"]:.6f}',match.group(0))
                        start=re.sub(r'EndLink=\([^()]*\)','',event)
                        if not re.search(r'(?:\(|,)LinkValue=',start):event=event[:-1]+f',LinkValue={n["time"]:.6f})'
                        return event
                    text=re.sub(pattern,rewrite_event,text)
        payload['nativeText']=text;write_json(path,payload);entry['sha256']=file_hash(path)
    write_json(config/'refactored_animation_sources.json',index)
    catalog_hash=file_hash(config/'refactored_animation_sources.json');source_hashes={r['source']:r['sha256'] for r in index['assets']}
    for row in notify['assets']:row['sourceSha256']=source_hashes[row['path']]
    write_json(config/'refactored_sync_inputs.json',sync);write_json(config/'refactored_notify_inputs.json',notify)
    def update(value):
        if isinstance(value,dict):
            if 'catalogSha256' in value:value['catalogSha256']=catalog_hash
            if isinstance(value.get('raw'),dict) and value['raw'].get('source') in rewritten:value['raw']=rewritten[value['raw']['source']]
            if isinstance(value.get('curves'),dict) and value['curves'].get('source') in curves:value['curves']=curves[value['curves']['source']]
            source=value.get('source','')
            if isinstance(source,str) and source in rewritten and isinstance(value.get('tracks'),list) and all(isinstance(t,dict) and 'bone' in t for t in value['tracks']):
                wanted={t['bone'].lower() for t in value['tracks']}
                value['tracks']=[copy.deepcopy(t) for t in rewritten[source]['tracks'] if t['bone'].lower() in wanted]
            if isinstance(source,str) and source in curves and isinstance(value.get('curves'),list):value['curves']=copy.deepcopy(curves[source]['curves'])
            for v in value.values():update(v)
        elif isinstance(value,list):
            for v in value:update(v)
    for path in config.glob('*.json'):
        if path.stem.endswith('_reference'):continue
        value=read_json(path);before=copy.deepcopy(value);update(value)
        if value!=before:write_json(path,value)
    manifest=read_json(asset_dir/'als_manifest.json');file_rows={r['relativePath']:r for r in manifest['files']}
    for entry in manifest['animations']:
        j=jobs.get(entry['objectPath'])
        if not j:continue
        path=asset_dir/entry['outputPath'];shutil.copy2(j['fbx'],path)
        final_job=next(r for r in final_fbx_jobs if r['source']==entry['objectPath'])
        final_job['fbx']=str(path)
        file_rows[entry['outputPath']].update(sha256=file_hash(path),size=path.stat().st_size)
        entry['kimodoSource']=dict(pipeline='kimodo-als-final',sourceAnimPath='',retargetedAssetPath=j['retargetedAssetPath'],fbxSha256=file_hash(path))
        for curve in entry['metadata'].get('curves',[]):
            name=curve['sourceName'];values=curve_values(name,j,contacts_by_source[entry['objectPath']],gait_reports.get(entry['objectPath']))
            if values is None:continue
            curve['keys']=[dict(timeSeconds=float(t),value=float(v),interpolation='Linear',arriveTangent=0,leaveTangent=0)
                           for t,v in zip(np.linspace(0,entry['metadata']['playLength'],len(values)),values)]
        for marker in entry['metadata'].get('syncMarkers',[]):
            key='timeSeconds' if 'timeSeconds' in marker else 'time'
            name=marker.get('name',marker.get('markerName',''))
            if name.lower() in ('left','right'):marker[key]=time_by_source[entry['objectPath']][0 if name.lower()=='left' else 1]
        entry['metadata'].get('syncMarkers',[]).sort(key=lambda m:m.get('timeSeconds',m.get('time',0)))
    write_json(asset_dir/'als_manifest.json',manifest)
    refresh_v4_bindings(output)
    refresh_config_digests(config)
    # V4 source indices keep native binding identities; the request digest is
    # refreshed from the compiled staged set before activation.
    for path in CONFIG.glob('v4_*source_inputs.json'):
        value=read_json(path)
        if 'assets' not in value or 'request' not in value:continue
        for entry in value['assets']:
            original_path=CONFIG/entry['file'];source=entry['source'];destination=raw_dir/entry['file'];destination.parent.mkdir(parents=True,exist_ok=True)
            if source in rewritten:write_json(destination,rewritten[source])
            else:shutil.copy2(original_path,destination)
            entry['sha256']=file_hash(destination)
        write_json(raw_dir/path.name,value)
    preserved=[]
    for row in contracts['assets']:
        if not is_prop(row):continue
        if row['edition']=='refactored':entry=next(r for r in index['assets'] if r['source']==row['source']);before=CONFIG/entry['file'];after=config/entry['file']
        else:entry=next(r for r in manifest['animations'] if r['objectPath']==row['source']);before=original/entry['outputPath'];after=asset_dir/entry['outputPath']
        if file_hash(before)!=file_hash(after):raise ValueError('Prop asset changed: '+row['source'])
        preserved.append({'source':row['source'],'sha256':file_hash(before)})
    prefix='res://'+output.relative_to(ROOT).as_posix()
    pack=dict(schemaVersion=1,status='diagnostic' if diagnostic else 'staged',scope='non-prop',replacedAnimations=len(jobs),preservedProps=preserved,
              assetRoot=prefix+'/v4',rawRoot=prefix+'/raw/',configRoot=prefix+'/config/',
              nativeRetargetBundle=str(Path(retarget).resolve()),quality='target gait pilots gated where configured; other contact envelopes and visual acceptance remain pending',rootMotionCurves=motion_curves,
              targetGaitChecks=[dict(source=source,policy=report['policy'],passed=report['passed']) for source,report in gait_reports.items()]+unsupported_gaits,
              targetMantleChecks=[dict(source=source,policy=report['policy'],passed=report['passed'],
                  file='target-mantle/'+hashlib.sha256(source.encode()).hexdigest()+'/quality.json') for source,report in mantle_reports.items()],
              targetAirChecks=[dict(source=source,policy=report['policy'],passed=report['passed'],
                  file='target-air/'+hashlib.sha256(source.encode()).hexdigest()+'/quality.json') for source,report in air_reports.items()],
              authoringTrajectories=authoring_trajectories,rootOwnershipPolicy=root_ownership.POLICY,
              rootOwnershipReceipts=ownership_receipts,extractedRootMotionCurves=extracted_curves,
              finalFbxStatus='pending-native-bake',files=[])
    if air_reports:
        from .air import contract as air_contract
        pack['airPipelineContract']=air_contract()
    if diagnostic:
        pack.update(productionReady=False,conversionBackend=bundle.get('conversionBackend'),
            missingNonPropSources=sorted(expected-set(jobs)),
            quality='diagnostic conversion; unchanged original fallback explicitly listed; quality failures retained',
            sourceClosureComplete=set(jobs)==expected)
    if (config/'refactored_mantle_animation_inputs.json').exists():
        from .mantle_contacts import export as export_mantle_contacts
        from .mantle_curve_contract import CONTROL_FILE
        controls=export_mantle_contacts(config,config/'refactored_mantle_contacts.json')
        pack['mantleCurveContract']=dict(controlFile=CONTROL_FILE,**controls['curveContract'])
    for directory in (config,raw_dir):
        pack['files'] += [dict(path='res://'+p.relative_to(ROOT).as_posix(),sha256=file_hash(p)) for p in directory.rglob('*.json')]
    pack['files'].append(dict(path=prefix+'/v4/als_manifest.json',sha256=file_hash(asset_dir/'als_manifest.json')))
    pack['files'] += [dict(path=prefix+'/v4/'+f['relativePath'],sha256=f['sha256']) for f in manifest['files']]
    pack['files'] += [dict(path=prefix+'/'+c['file'],sha256=c['sha256']) for c in motion_curves]
    pack['files'] += [dict(path=prefix+'/'+p.relative_to(output).as_posix(),sha256=file_hash(p)) for p in gait_files]
    pack['files'] += [dict(path=prefix+'/'+p['file'],sha256=p['sha256']) for p in authoring_trajectories]
    pack['files'] += [dict(path=prefix+'/'+p.relative_to(output).as_posix(),sha256=file_hash(p)) for p in ownership_files]
    write_json(output/'final-fbx-request.json',dict(schemaVersion=1,version='RootOwnership_'+hashlib.sha256(prefix.encode()).hexdigest()[:20],
        pack='res://'+(output/'resource-pack.json').relative_to(ROOT).as_posix(),jobs=final_fbx_jobs))
    pack['files'].append(dict(path=prefix+'/final-fbx-request.json',sha256=file_hash(output/'final-fbx-request.json')))
    if 'mantleCurveContract' in pack:
        validate_mantle_curve_delivery(output/'resource-pack.json',pack)
    write_json(output/'resource-pack.json',pack)
    from .viewer_pack import register_production_pack
    register_production_pack(output/'resource-pack.json')
    return pack

def bake_pack_curves(pack_path,report):
    """Augment a verified existing pack; never rewrite its animation/config bytes."""
    pack_path=pack_path.resolve();pack=read_json(pack_path);root=pack_path.parent
    for entry in pack['files']:
        if file_hash(ROOT/entry['path'].removeprefix('res://'))!=entry['sha256']:raise ValueError('Changed input pack: '+entry['path'])
    native=read_json(pack['nativeRetargetBundle']);index=read_json(root/'config/refactored_animation_sources.json')
    refs={r['source']:r for r in index['assets']}
    v4=read_json(CONFIG/'v4_recovery_movement_source_inputs.json')['skeletons'][0]
    ref=index['skeletons']['/ALS/ALS/Character/SK_Als.SK_Als']
    v4_files={}
    for path in (root/'raw').glob('v4_*source_inputs.json'):
        for row in read_json(path)['assets']:v4_files[row['source']]=root/'raw'/row['file']
    curves=[]
    for j in native['jobs']:
        if file_hash(j['raw'])!=j['rawSha256'] or file_hash(j['bvhPath'])!=j['bvhSha256']:raise ValueError('Changed native curve bake provenance')
        source=j['source']
        if j['edition']=='refactored':raw=read_json(root/'config'/refs[source]['file'])['raw']
        elif source in v4_files:raw=read_json(v4_files[source])
        else:raw,_=prepare_target_raw(j,v4,ref,index)
        curve=root_curves.export(root,raw,j)
        if curve:curves.append(curve)
    pack['rootMotionCurves']=curves
    prefix='res://'+root.relative_to(ROOT).as_posix();existing={r['path'] for r in pack['files']}
    for c in curves:
        resource=prefix+'/'+c['file']
        if resource not in existing:pack['files'].append(dict(path=resource,sha256=c['sha256']))
    temporary=pack_path.with_suffix('.json.next');write_json(temporary,pack);temporary.replace(pack_path)
    write_json(report,dict(schemaVersion=1,pack=str(pack_path),exported=len(curves),curves=curves))
    return curves

def refresh_config_digests(config):
    """Refresh production input links from immutable original link ownership.

    Native reference/trace fixtures are kept untouched; they do not become
    certificates for these new animation poses by changing a hash field.
    """
    originals={}
    for original in CONFIG.rglob('*.json'):
        relative=original.relative_to(CONFIG)
        if (config/relative).exists():originals[file_hash(original)]=relative
    inputs=[p for p in config.rglob('*.json') if not p.stem.endswith('_reference') and 'reference' not in p.parts
            and (b'Sha256' in p.read_bytes() or b'sha256' in p.read_bytes())]
    baseline={p:read_json(CONFIG/p.relative_to(config)) for p in inputs}
    def fix(value,original,mapping):
        if isinstance(value,dict) and isinstance(original,dict):
            for key,v in value.items():
                previous=original.get(key)
                if key.lower().endswith('sha256') and isinstance(previous,str) and previous in mapping:value[key]=mapping[previous]
                else:fix(v,previous,mapping)
        elif isinstance(value,list) and isinstance(original,list):
            for v,o in zip(value,original):fix(v,o,mapping)
    for _ in range(12):
        mapping={sha:file_hash(config/relative) for sha,relative in originals.items()};changed=False
        for path in inputs:
            value=read_json(path);before=copy.deepcopy(value);fix(value,baseline[path],mapping)
            if value!=before:write_json(path,value);changed=True
        if not changed:return
    raise ValueError('Production input hash links did not converge')

def refresh_v4_bindings(pack_root):
    manifest=read_json(pack_root/'v4/als_manifest.json')
    selected={a['objectPath']:a for a in manifest['animations'] if a.get('kimodoSource')}
    for asset in selected.values():
        for i,marker in enumerate(asset['metadata'].get('syncMarkers',[])):
            marker['sourceIndex']=i
            marker['stableMarkerId']=hashlib.sha1(f"{asset['id']}|marker|{i}|{marker['name']}".encode()).hexdigest()
        by_name={m['name'].lower():m['timeSeconds'] for m in asset['metadata'].get('syncMarkers',[])}
        for event in asset['metadata'].get('timeline',[]):
            if 'footstep' in event['sourceClassPath'].lower() and by_name:
                event['timeSeconds']=by_name.get('left' if event['sourceIndex']%2==0 else 'right',event['timeSeconds'])
    write_json(pack_root/'v4/als_manifest.json',manifest)
    def visit(value):
        if isinstance(value,dict):
            asset=selected.get(value.get('path',''))
            if asset and 'markers' in value and 'notifies' in value:
                markers=asset['metadata'].get('syncMarkers',[])
                value['markers']=[dict(index=i,name=m['name'],time=m['timeSeconds'],track=m['trackIndex']) for i,m in enumerate(markers)]
                events={e['sourceIndex']:e for e in asset['metadata'].get('timeline',[])}
                for notify in value['notifies']:
                    event=events[notify['index']];notify['time']=event['timeSeconds']
                    trigger=float(np.float32(np.float32(notify['time'])+np.float32(notify['triggerOffset'])))
                    notify['triggerTime']=trigger
                    notify['endTriggerTime']=float(np.float32(np.float32(trigger+notify['duration'])+np.float32(notify['endTriggerOffset']))) if notify['stateObject'] else trigger
            for v in value.values():visit(v)
        elif isinstance(value,list):
            for v in value:visit(v)
    for path in (pack_root/'config').glob('v4_*.json'):
        value=read_json(path);before=copy.deepcopy(value);visit(value)
        if value!=before:write_json(path,value)

def finalize_requests(pack_path,requests):
    pack_path=pack_path.resolve()
    pack=read_json(pack_path);root=pack_path.parent
    for path in (root/'raw').glob('v4_*source_inputs.json'):
        domain=path.name.removeprefix('v4_').removesuffix('_source_inputs.json')
        if domain not in ('recovery_movement','aim','overlay','ragdoll','stop','overlay_prop'):continue
        request=read_json(requests/(domain+'-request.json'))
        index=read_json(path)
        if {r['source'] for r in request['rootAssets']}!={r['source'] for r in index['request']['rootAssets']}:
            raise ValueError('Source closure changed: '+domain)
        index['request']=request;write_json(path,index)
    for entry in pack['files']:entry['sha256']=file_hash(ROOT/entry['path'].removeprefix('res://'))
    compiled=root/'v4/compiled/als_animation_set.tres'
    if compiled.exists():
        resource='res://'+compiled.relative_to(ROOT).as_posix()
        if not any(e['path']==resource for e in pack['files']):pack['files'].append(dict(path=resource,sha256=file_hash(compiled)))
    write_json(pack_path,pack)

def validate_mantle_curve_delivery(pack_path,pack):
    """Semantic preflight also applies to diagnostic, partially generated packs."""
    from .mantle_curve_contract import CONTROL_FILE, contract, validate
    folder=Path(pack_path).resolve().parent
    if pack.get('mantleCurveContract')!=dict(controlFile=CONTROL_FILE,**contract()):
        raise ValueError('Missing or unsupported mantle curve delivery contract; stage a fresh pack')
    paths=[folder/CONTROL_FILE,folder/'config/refactored_mantle_animation_inputs.json',
        folder/'config/refactored_mantle_root_tracks.json']
    for path in paths:
        uri='res://'+path.relative_to(ROOT).as_posix()
        entries=[e for e in pack['files'] if e['path']==uri]
        if not path.is_file() or len(entries)!=1 or entries[0]['sha256']!=file_hash(path):
            raise ValueError('Missing or changed mantle curve contract payload: '+path.name)
    value=read_json(paths[0])
    if (value.get('animationInputsSha256')!=file_hash(paths[1]) or
            value.get('rootInputsSha256')!=file_hash(paths[2])):
        raise ValueError('Mantle curve contract binds different final inputs')
    return validate(value,read_json(paths[1]),read_json(paths[2]))


def validate_mantle_pack(pack_path,pack):
    from .mantle import POLICY as MANTLE_POLICY
    contracts=read_json(ROOT/'docs/architecture/kimodo-als-asset-contracts.json')['assets']
    expected_mantles={r['source'] for r in contracts if r['family']=='mantle' and not is_prop(r)}
    if {c['source'] for c in pack.get('targetMantleChecks',[])}!=expected_mantles:
        raise ValueError('Missing complete per-asset mantle quality gates')
    validate_mantle_curve_delivery(pack_path,pack)
    control_path=pack_path.parent/'config/refactored_mantle_contacts.json'
    control_entry=next((e for e in pack['files'] if e['path']=='res://'+control_path.resolve().relative_to(ROOT).as_posix()),None)
    if not control_entry or not control_path.exists() or file_hash(control_path)!=control_entry['sha256']:
        raise ValueError('Missing or changed dedicated mantle contact bake')
    from .mantle_contacts import bake as bake_mantle_contacts
    if read_json(control_path)!=bake_mantle_contacts(control_path.parent):
        raise ValueError('Mantle contact curves do not bind the final pose/root inputs')
    for check in pack.get('targetMantleChecks',[]):
        path=pack_path.parent/check['file']
        if not path.resolve().is_relative_to(pack_path.parent.resolve()):raise ValueError('Mantle gate path escapes pack')
        entry=next((e for e in pack['files'] if e['path']=='res://'+path.resolve().relative_to(ROOT).as_posix()),None)
        if not entry or file_hash(path)!=entry['sha256']:raise ValueError('Changed target mantle quality report')
        report=read_json(path)
        from .mantle_hand_repair import validate_report as validate_hand_repair
        validate_hand_repair(report.get('handSkinRepair') or {})
        if not check['passed'] or not report['passed'] or report['policy']!=MANTLE_POLICY:
            raise ValueError('Target mantle quality failed; keep candidate inactive')
        runtime=pack_path.parent/'root-ownership'/hashlib.sha256(check['source'].encode()).hexdigest()/'runtime.raw.json'
        if report['finalRawSemanticSha256']!=root_ownership.semantic_hash(read_json(runtime)):
            raise ValueError('Mantle quality does not certify the final raw')


def activate(pack_path,verification):
    pack=read_json(pack_path);evidence=read_json(verification)
    if pack.get('status')=='diagnostic':raise ValueError('Diagnostic conversion is not eligible for activation')
    if not evidence.get('e2eSummary'):raise ValueError('Missing complete E2E and directional skating evidence')
    summary=read_json(Path(evidence['e2eSummary']))
    resource='res://'+pack_path.resolve().relative_to(ROOT).as_posix()
    if (summary.get('pack')!=resource or summary.get('packSha256')!=file_hash(pack_path) or
        not summary.get('completeMatrix') or summary.get('total')!=71 or summary.get('passed')!=71 or summary.get('failed') or
        summary.get('payloadChanges') or summary.get('animationSettingsChanged')):
        raise ValueError('Complete E2E verification failed or pack changed')
    parity=summary.get('singleParallelParity',[])
    if {r['hz'] for r in parity}!={30,60,120} or not all(r['passed'] for r in parity):
        raise ValueError('Required single/parallel frame-rate parity failed')
    quality=summary.get('directionalQuality',{})
    if not quality.get('passed'):raise ValueError('Directional skating quality failed; keep candidate inactive')
    quality_path=Path(quality['report'])
    if file_hash(quality_path)!=quality['sha256']:raise ValueError('Directional quality report changed')
    report=read_json(quality_path)
    if report.get('packSha256')!=file_hash(pack_path) or not report.get('passed') or not report.get('exactBodyPhysicsParity'):
        raise ValueError('Directional quality does not certify this pack')
    from .locomotion_quality import compare
    for name,expected in report['traceHashes'].items():
        if file_hash(quality_path.parent/(name+'.json'))!=expected:raise ValueError('Directional trace changed')
    reference=read_json(quality_path.parent/'directional-original-baseline.json')
    for name in ('directional-single','directional-parallel','rendered-directional'):
        if not compare(reference,read_json(quality_path.parent/(name+'.json')))['passed']:
            raise ValueError('Directional skating quality failed: '+name)
    if 'rootMotionCurves' not in pack:raise ValueError('Bake mandatory root-motion sidecars before activation')
    validate_mantle_pack(pack_path,pack)
    from .air import validate_pack as validate_air_pack
    validate_air_pack(pack_path,pack)
    if pack.get('rootOwnershipPolicy'):
        if pack['rootOwnershipPolicy']!=root_ownership.POLICY or pack.get('finalFbxStatus')!='native-bake-and-readback-verified':
            raise ValueError('Final FBX must use the decomposed raw and pass native readback')
    payloads={r['path']:r['sha256'] for r in pack['files']}
    prefix='res://'+pack_path.resolve().parent.relative_to(ROOT).as_posix()
    for curve in pack['rootMotionCurves']:
        if payloads.get(prefix+'/'+curve['file'])!=curve['sha256']:raise ValueError('Root curve missing from release payloads')
    required={'P2B_IMPORT_OK','KIMODO_IDLE_DIAGNOSTIC_OK','ALS_REFACTORED_STANCE_DEMO_OK','MANTLE_DEMO_OK',
              'ALS_DIRECTIONAL_MOVEMENT_OK','ALS_MOVEMENT_RELEASE_OK'}
    checked=set()
    for check in evidence['checks']:
        log=Path(check['log']);text=log.read_text(encoding='utf-8-sig')
        if check['marker'] not in text or re.search(r'(?m)^ERROR:',text):raise ValueError('Runtime verification failed: '+str(log))
        checked.add(check['marker'])
    if not required<=checked:raise ValueError('Missing required ordinary Demo coverage')
    if not checked.intersection({'ALL_OVERLAYS_DISPATCH_OK','NON_PROP_OVERLAYS_DISPATCH_OK'}):
        raise ValueError('Missing required full or non-prop Overlay coverage')
    for entry in pack['files']:
        if file_hash(ROOT/entry['path'].removeprefix('res://'))!=entry['sha256']:raise ValueError('Pack changed after validation')
    pack.update(status='runtime_validated',verification=evidence,manualAcceptance=False)
    write_json(pack_path,pack)
    pointer=CONFIG/'als_resource_pack.json'
    if pointer.exists():
        backup=ROOT/'artifacts/kimodo_als'/('previous-active-'+file_hash(pointer)[:16]+'.json')
        if not backup.exists():shutil.copy2(pointer,backup)
    next_pointer=pointer.with_suffix('.json.next')
    write_json(next_pointer,dict(schemaVersion=1,pack='res://'+pack_path.resolve().relative_to(ROOT).as_posix()))
    next_pointer.replace(pointer)

def finalize_fbx(pack_path,receipt_path):
    """Accept native full-track bake/readback and refresh staged FBX hashes."""
    pack_path=Path(pack_path).resolve();pack=read_json(pack_path);folder=pack_path.parent
    pointer=CONFIG/'als_resource_pack.json'
    if pointer.exists() and read_json(pointer).get('pack')=='res://'+pack_path.relative_to(ROOT).as_posix():
        raise ValueError('Cannot rebake the active pack; use a fresh staged version')
    if pack.get('rootOwnershipPolicy')!=root_ownership.POLICY or pack.get('finalFbxStatus')!='pending-native-bake':
        raise ValueError('Expected a pending planar-ownership pack')
    request_path=folder/'final-fbx-request.json';request=read_json(request_path)
    receipt=read_json(receipt_path)
    if receipt.get('requestSha256')!=file_hash(request_path):raise ValueError('Final FBX request changed')
    resource='res://'+pack_path.relative_to(ROOT).as_posix()
    if request.get('pack')!=resource:raise ValueError('Final FBX request targets another pack')
    expected={j['source']:j for j in request['jobs']};actual={j['source']:j for j in receipt['jobs']}
    if len(expected)!=len(request['jobs']) or len(actual)!=len(receipt['jobs']) or set(actual)!=set(expected):
        raise ValueError('Incomplete or duplicate final FBX bake')
    manifest_path=folder/'v4/als_manifest.json';manifest=read_json(manifest_path)
    selected={a['objectPath']:a for a in manifest['animations'] if a.get('kimodoSource')}
    if set(selected)!=set(expected):raise ValueError('Final FBX closure differs from replaced manifest animations')
    mutable={Path(j['fbx']).resolve() for j in expected.values()}
    payloads={e['path']:e for e in pack['files']}
    if len(payloads)!=len(pack['files']):raise ValueError('Duplicate staged payload')
    prefix='res://'+folder.relative_to(ROOT).as_posix()
    if payloads.get(prefix+'/final-fbx-request.json',{}).get('sha256')!=file_hash(request_path):
        raise ValueError('Unpinned final FBX request')
    for entry in pack['files']:
        path=(ROOT/entry['path'].removeprefix('res://')).resolve()
        if not path.is_relative_to(folder):raise ValueError('Foreign staged payload')
        if path not in mutable and file_hash(path)!=entry['sha256']:
            raise ValueError('Changed staged input during FBX bake: '+entry['path'])
    for source,j in expected.items():
        saved=actual[source]
        path=Path(j['fbx']).resolve();raw_path=Path(j['raw']).resolve()
        if (not raw_path.is_relative_to(folder/'root-ownership') or
            path!=(folder/'v4'/selected[source]['outputPath']).resolve() or not path.is_relative_to(folder/'v4')):
            raise ValueError('Foreign final FBX or raw path')
        if (saved.get('raw')!=j['raw'] or saved.get('rawSha256')!=file_hash(raw_path) or saved.get('rawSha256')!=j['rawSha256'] or
            saved.get('fbx')!=j['fbx'] or saved.get('fbxSha256')!=file_hash(j['fbx'])):
            raise ValueError('Final FBX inputs or output changed: '+source)
        errors=[saved.get(k,float('inf')) for k in ('maximumPositionErrorCm','maximumRotationErrorDegrees','maximumScaleError')]
        before_errors=saved.get('beforeExportMaximumErrors',[float('inf')]*3)
        timing=read_json(raw_path)
        native_times=[saved.get(k,{}) for k in ('authoredTiming','readbackTiming')]
        matching_times=all(t.get('sampledKeyCount')==timing['sampledKeyCount'] and
            abs(t.get('playLength',float('inf'))-timing['playLength'])<1e-5 and
            t.get('frameRateNumerator')==timing['frameRateNumerator'] and
            t.get('frameRateDenominator')==timing['frameRateDenominator'] for t in native_times)
        if (saved.get('readbackPolicy')!='native-DataModel-physical-tracks' or
            len(before_errors)!=3 or not np.isfinite(before_errors).all() or
            any(e<0 or e>limit for e,limit in zip(before_errors,(.01,.1,1e-4))) or
            saved.get('passed') is not True or not np.isfinite(errors).all() or
            any(e<0 or e>limit for e,limit in zip(errors,(.01,.1,1e-4))) or not saved.get('timeDomainMatches') or not matching_times or
            saved.get('sampledKeyCount')!=read_json(raw_path)['sampledKeyCount']):
            raise ValueError('Native FBX readback failed: '+source)
    files={r['relativePath']:r for r in manifest['files']}
    for entry in manifest['animations']:
        if entry['objectPath'] not in actual:continue
        saved=actual[entry['objectPath']];path=folder/'v4'/entry['outputPath']
        if path.resolve()!=Path(saved['fbx']).resolve():raise ValueError('Foreign final FBX path')
        files[entry['outputPath']].update(sha256=file_hash(path),size=path.stat().st_size)
        entry['kimodoSource'].update(pipeline='kimodo-als-final-planar-ownership',fbxSha256=file_hash(path))
    write_json(manifest_path,manifest)
    receipt_copy=folder/'final-fbx-receipt.json'
    if Path(receipt_path).resolve()!=receipt_copy.resolve():shutil.copy2(receipt_path,receipt_copy)
    pack['finalFbxStatus']='native-bake-and-readback-verified'
    pack['finalFbxReceipt']=dict(file=receipt_copy.name,sha256=file_hash(receipt_copy))
    changed={prefix+'/v4/als_manifest.json':file_hash(manifest_path)}
    changed.update({prefix+'/v4/'+selected[s]['outputPath']:actual[s]['fbxSha256'] for s in selected})
    for entry in pack['files']:
        if entry['path'] in changed:entry['sha256']=changed[entry['path']]
    for saved in actual.values():
        sidecar=Path(saved['fbx']+'.verification.json');write_json(sidecar,saved)
        pack['files'].append(dict(path='res://'+sidecar.relative_to(ROOT).as_posix(),sha256=file_hash(sidecar)))
    pack['files'].append(dict(path=prefix+'/'+receipt_copy.name,sha256=file_hash(receipt_copy)))
    write_json(pack_path,pack)
    from .viewer_pack import register_production_pack
    register_production_pack(pack_path)


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('command',choices=('prepare','stage','finalize','finalize-fbx','activate','bake-root-curves','check-target-gaits'));parser.add_argument('--input',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);parser.add_argument('--version',default='NonProp20261007')
    args=parser.parse_args()
    if args.command=='prepare':prepare(args.input,args.output,args.version)
    elif args.command=='stage':print(json.dumps(stage(args.input,args.output),ensure_ascii=False)[:500])
    elif args.command=='finalize':finalize_requests(args.input,args.output)
    elif args.command=='finalize-fbx':finalize_fbx(args.input,args.output)
    elif args.command=='bake-root-curves':print('ROOT_MOTION_CURVES_BAKED '+str(len(bake_pack_curves(args.input,args.output))))
    elif args.command=='check-target-gaits':
        report=audit_target_jobs(args.input,args.output);print('TARGET_GAIT_CHECK '+str(report['passed']))
        if not report['passed']:raise SystemExit(1)
    else:activate(args.input,args.output)

if __name__=='__main__':main()
