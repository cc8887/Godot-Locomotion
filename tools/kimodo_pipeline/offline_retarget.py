"""SOMA77 full poses to the exported ALS physical skeleton, in pure Python.

Explicit neutral-pose alignment and coordinate conjugation are required:
SOMA is meters/Y-up/T-pose, ALS is centimeters/Z-up/A-pose. Bone offsets and
bind transforms are never replaced with SOMA offsets. This is an authoring
conversion; target gait and final-skin acceptance remain independent gates.
"""
import numpy as np
from scipy.spatial.transform import Rotation
from .motion import fk
from .publish import world_pose
from .gait_constraints import _rotation_between

BASIS = np.array([[1.,0,0],[0,0,1],[0,1,0]])
POLICY = 'soma77-als-neutral-aligned-offline-v1'


def mapping():
    result = dict(pelvis='Hips', spine_01='Spine1', spine_02='Spine2',
                  spine_03='Chest', neck_01='Neck2', head='Head')
    for side, suffix in [('Left','l'), ('Right','r')]:
        for source, target in [('Shoulder','clavicle'), ('Arm','upperarm'), ('ForeArm','lowerarm'),
                               ('Hand','hand'), ('Leg','thigh'), ('Shin','calf'),
                               ('Foot','foot'), ('ToeBase','ball')]:
            result[target+'_'+suffix] = side+source
        for finger in ['Thumb','Index','Middle','Ring','Pinky']:
            for part in range(1,4):
                # SOMA index/middle/ring/pinky 1 is the metacarpal; ALS starts
                # at the knuckle. Thumb uses the three matching phalanges.
                source_part = part if finger=='Thumb' else part+1
                result[f'{finger.lower()}_{part:02}_{suffix}'] = f'{side}Hand{finger}{source_part}'
    return result


def aligned_neutral(skeleton, rig, bone_map):
    names = skeleton['rawBoneNames']; parents = skeleton['rawParents']
    source_names = {n:i for i,n in enumerate(rig['jointNames'])}
    neutral = np.tile(np.eye(3), (1,len(source_names),1,1))
    _, source_positions = fk(neutral, np.zeros((1,3)), rig)
    bind = world_pose(dict(sampledKeyCount=1, tracks=[]), skeleton)
    aligned = np.zeros((len(names),3,3))
    chains = {}
    for suffix in ['l','r']:
        for a,b in [('clavicle','upperarm'),('upperarm','lowerarm'),('lowerarm','hand'),
                    ('thigh','calf'),('calf','foot'),('foot','ball')]:
            chains[a+'_'+suffix] = b+'_'+suffix
        for finger in ['thumb','index','middle','ring','pinky']:
            for part in (1,2): chains[f'{finger}_{part:02}_{suffix}'] = f'{finger}_{part+1:02}_{suffix}'
    for i, name in enumerate(names):
        key = name.lower(); parent = parents[i]
        local = Rotation.from_quat(skeleton['referencePose'][i]['rotation']).as_matrix()
        world = local if parent<0 else aligned[parent]@local
        child = chains.get(key)
        if child and child in bone_map and key in bone_map:
            child_index = next(j for j,n in enumerate(names) if n.lower()==child)
            target_direction = world@np.asarray(skeleton['referencePose'][child_index]['position'])
            source_direction = BASIS@(source_positions[0,source_names[bone_map[child]]]-source_positions[0,source_names[bone_map[key]]])
            world = _rotation_between(target_direction, source_direction)@world
        aligned[i] = world
    return aligned


def convert(motion, rig, skeleton, source, timing):
    names = skeleton['rawBoneNames']; parents = skeleton['rawParents']; bone_map = mapping()
    source_names = {n:i for i,n in enumerate(rig['jointNames'])}
    count = len(motion['root_positions'])
    if count != timing['sampledKeyCount'] or abs(float(motion['fps'])-timing['frameRateNumerator']/timing['frameRateDenominator'])>1e-6:
        raise ValueError('Offline retarget does not resample or retime delivery')
    aligned = aligned_neutral(skeleton,rig,bone_map)
    global_source, source_positions = fk(motion['local_rot_mats'], motion['root_positions'], rig)
    world = np.zeros((count,len(names),3,3)); local = np.zeros_like(world)
    bind = world_pose(dict(sampledKeyCount=1,tracks=[]),skeleton)
    # Uniform limb scale preserves target limb lengths. No per-frame scale or
    # changing pelvis origin is used to hide an unreachable/source gait issue.
    source_leg = sum(np.linalg.norm(rig['offsets'][source_names['Left'+n]]) for n in ['Shin','Foot'])
    target_leg = sum(np.linalg.norm(skeleton['referencePose'][next(i for i,n in enumerate(names) if n.lower()==b)]['position']) for b in ['calf_l','foot_l'])
    # Both limb lengths must be in meters BEFORE forming a dimensionless
    # ratio. ALS bind offsets are cm; source rig offsets are meters.
    scale = (target_leg/100.)/source_leg
    for i,name in enumerate(names):
        key = name.lower(); parent = parents[i]
        if key in bone_map:
            delta = BASIS@global_source[:,source_names[bone_map[key]]]@BASIS.T
            world[:,i] = delta@aligned[i]
        elif parent<0: world[:,i] = aligned[i]
        else:
            # Twist bones and IK containers inherit their parent's authored
            # delta. Root ownership regenerates IK effectors from final FK.
            world[:,i] = world[:,parent]@aligned[parent].T@aligned[i]
        local[:,i] = world[:,i] if parent<0 else world[:,parent].transpose(0,2,1)@world[:,i]
    raw = dict(source=source, skeletonSource=skeleton['source'], **timing, tracks=[])
    for i,name in enumerate(names):
        ref = skeleton['referencePose'][i]
        position = np.broadcast_to(ref['position'], (count,3)).copy()
        if name.lower()=='pelvis':
            position = motion['root_positions']@BASIS.T*(100*scale)
        raw['tracks'].append(dict(bone=name,positions=position.tolist(),
            rotations=Rotation.from_matrix(local[:,i]).as_quat().tolist(),scales=[ref['scale']]))
    report = dict(policy=POLICY,sourceUnits='m Y-up',targetUnits='cm Z-up',physicalBones=len(names),
        mappedBones=bone_map,unmappedBones=[n for n in names if n.lower() not in bone_map],
        uniformScale=float(scale),neutralAlignment='T-pose segment directions; immutable ALS bind offsets',
        timingPreserved=True,productionReady=False,
        limitation='angular retarget; gait/sole contact and final-skin acceptance are independent and can fail')
    return raw,report
