"""Project immutable ALS poses into a SOMA generation reference, not a deliverable."""
from __future__ import annotations

import hashlib
from pathlib import Path
import numpy as np

from .graph import read_json
from .motion import complete, fk, local_from_global
from .gait import audit_candidate, foot_landmarks
from .gait_constraints import _rotation_between, solve_foot_ik


def original_input(index_path, source):
    index_path=Path(index_path).resolve()
    index=read_json(index_path)
    row=next((r for r in index['assets'] if r['source']==source),None)
    if row is None:raise ValueError('No immutable ALS source: '+source)
    payload=(index_path.parent/row['file']).resolve()
    if not payload.is_relative_to(index_path.parent):raise ValueError('ALS reference path escapes config')
    content=payload.read_bytes()
    if hashlib.sha256(content).hexdigest()!=row['sha256']:
        raise ValueError('Changed immutable ALS reference: '+source)
    return index,row,payload


def native_gait_reference(index_path, source, rig, speed, direction, cycles=6):
    index,row,payload_path=original_input(index_path,source)
    from .publish import world_pose
    payload=read_json(payload_path);raw=payload['raw']
    skeleton=index['skeletons']['/ALS/ALS/Character/SK_Als.SK_Als']
    bind=world_pose(dict(sampledKeyCount=1,tracks=[]),skeleton)
    world=world_pose(raw,skeleton)
    count=raw['sampledKeyCount'];fps=(count-1)/raw['playLength']
    direction=np.asarray(direction,dtype=float)
    if count<5 or not isinstance(cycles,int) or cycles<3 or direction.shape!=(2,) or not np.isclose(np.linalg.norm(direction),1):
        raise ValueError('Invalid native cyclic pose reference configuration')
    if not np.isfinite(speed) or speed<=0:raise ValueError('Reference speed must be positive')
    names={name:i for i,name in enumerate(rig['jointNames'])}
    # Coordinate conjugation converts Z-up centimeters into Y-up meters.
    basis=np.array([[1.,0,0],[0,0,1],[0,1,0]])
    mapping=dict(Hips='pelvis',Spine1='spine_01',Spine2='spine_02',Chest='spine_03',
                 Neck1='neck_01',Neck2='neck_01',Head='head')
    for side,suffix in (('Left','l'),('Right','r')):
        mapping.update({side+a:b+'_'+suffix for a,b in (
            ('Shoulder','clavicle'),('Arm','upperarm'),('ForeArm','lowerarm'),('Hand','hand'),
            ('Leg','thigh'),('Shin','calf'),('Foot','foot'))})
    # This reference is consumed in SOMA's T neutral basis. Invert the same
    # aligned neutral used by the outgoing retarget, not ALS's A-pose bind.
    # An A-bind delta applied to T offsets raises otherwise relaxed arms.
    from .offline_retarget import aligned_neutral, mapping as target_mapping
    aligned = aligned_neutral(skeleton, rig, {bone: name for bone, name in target_mapping().items() if name in names})
    aligned_by_name = {name.lower(): aligned[i] for i, name in enumerate(skeleton['rawBoneNames'])}
    global_rot=np.tile(np.eye(3),(count,len(names),1,1))
    for name,i in names.items():
        bone=mapping.get(name)
        if bone:
            upper = name.endswith(('Shoulder', 'Arm', 'ForeArm', 'Hand'))
            neutral = aligned_by_name[bone] if upper else bind[bone][0][0]
            global_rot[:,i]=basis@(world[bone][0]@neutral.T)@basis.T
        elif rig['parents'][i]>=0:global_rot[:,i]=global_rot[:,rig['parents'][i]]
    local=local_from_global(global_rot,rig['parents'])
    travel=np.arange(count)[:,None]/fps*speed*direction
    hips=world['pelvis'][1]
    root=np.stack([hips[:,0]*.01+travel[:,0],hips[:,2]*.01,hips[:,1]*.01+travel[:,1]],axis=1)
    neutral=complete(np.tile(np.eye(3),(1,len(names),1,1)),np.zeros((1,3)),rig,fps)
    _,neutral_joints=fk(neutral['local_rot_mats'],neutral['root_positions'],rig)
    soles,_=foot_landmarks(neutral,rig)
    for side,suffix in (('Left','l'),('Right','r')):
        foot=names[side+'Foot'];original_sole=[]
        for bone in ('foot_'+suffix,'ball_'+suffix):
            br,bp,_=bind[bone];offset=br[0].T@np.array([0.,0.,-bp[0,2]])
            r,p,_=world[bone];original_sole.append(p+np.einsum('tij,j->ti',r,offset))
        original_sole=np.transpose(np.asarray(original_sole),(1,0,2))
        offsets=soles[0,0 if side=='Left' else 1]-neutral_joints[0,foot]
        orientation=np.stack([_rotation_between(offsets[1]-offsets[0],basis@(p[1]-p[0])) for p in original_sole])
        toe_target=original_sole[:,1]@basis.T*.01
        toe_target[:,[0,2]]+=travel
        ankles=toe_target-np.einsum('tij,j->ti',orientation,offsets[1])
        toes={}
        for bone,parent in enumerate(rig['parents']):
            ancestor=parent
            while ancestor>=0 and ancestor!=foot:ancestor=rig['parents'][ancestor]
            if ancestor==foot:toes[bone]=np.eye(3)
        for frame in range(count):
            local[frame]=solve_foot_ik(local[frame],root[frame],rig,side.lower(),ankles[frame],orientation[frame],toes)
    period=count-1;frames=np.arange(cycles*period+1)
    displacement=np.array([speed*period/fps*direction[0],0.,speed*period/fps*direction[1]])
    reference=complete(local[frames%period],root[frames%period]+(frames//period)[:,None]*displacement,rig,fps)
    audit=audit_candidate(reference,rig,speed)
    receipt=dict(schemaVersion=1,scope='constraint_reference_only',productionReady=False,
        deliverable=False,source=source,sourceSha256=row['sha256'],referenceAudit=audit,
        adaptation='upper-body inverse outgoing T-neutral-aligned angular retarget; existing lower-body reference and toe-anchor IK preserved',
        sourceUnits='centimeters Z-up',outputUnits='meters Y-up',targetSpeedMps=speed,
        travelDirectionXZ=direction.tolist(),periodFrames=period,
        referenceMotionSha256=audit['sourceMotionSha256'])
    return reference,receipt
