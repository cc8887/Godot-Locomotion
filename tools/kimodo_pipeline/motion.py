"""Full-pose SOMA operations; no UE additive deltas are baked in this space."""
from __future__ import annotations

import numpy as np
from scipy.spatial.transform import Rotation, Slerp


def validate_motion(motion, rig):
    local, root = motion['local_rot_mats'], motion['root_positions']
    expected = (len(root), len(rig['jointNames']), 3, 3)
    if local.shape != expected or root.ndim != 2 or root.shape[1] != 3 or not len(root):
        raise ValueError(f'Motion dimensions {local.shape}/{root.shape}, expected {expected}/(T,3)')
    if not np.isfinite(local).all() or not np.isfinite(root).all():
        raise ValueError('Non-finite motion')
    if not np.allclose(local @ local.swapaxes(-1,-2), np.eye(3), atol=2e-5) or not np.allclose(np.linalg.det(local), 1, atol=2e-5):
        raise ValueError('Invalid rotation matrices')
    if float(motion.get('fps', 30)) <= 0:
        raise ValueError('Invalid FPS')


def fk(local, root, rig):
    parents = rig['parents']; offsets = np.asarray(rig['offsets'])
    global_rot = np.empty_like(local); positions = np.empty((len(local), len(parents),3))
    for i, p in enumerate(parents):
        if p < 0:
            global_rot[:,i] = local[:,i]; positions[:,i] = root
        else:
            global_rot[:,i] = global_rot[:,p] @ local[:,i]
            positions[:,i] = positions[:,p] + np.einsum('tij,j->ti', global_rot[:,p], offsets[i])
    return global_rot, positions


def local_from_global(global_rot, parents):
    out = np.empty_like(global_rot)
    for i, p in enumerate(parents):
        out[:,i] = global_rot[:,i] if p < 0 else global_rot[:,p].swapaxes(-1,-2) @ global_rot[:,i]
    return out


def complete(local, root, rig, fps=30):
    motion = {'local_rot_mats':np.asarray(local,dtype=np.float64),
              'root_positions':np.asarray(root,dtype=np.float64), 'fps':np.asarray(float(fps))}
    validate_motion(motion, rig)
    global_rot, pos = fk(motion['local_rot_mats'],motion['root_positions'],rig)
    motion.update(global_rot_mats=global_rot, posed_joints=pos)
    feet = [rig['jointNames'].index(n) for n in rig.get('footJointNames', [])]
    if feet:
        foot = pos[:,feet]; vel = np.zeros_like(foot)
        if len(foot)>1: vel[1:] = np.diff(foot,axis=0)*fps; vel[0]=vel[1]
        # Diagnostic geometric contact estimate, never the final FootLock curve.
        motion['contacts'] = ((np.linalg.norm(vel,axis=-1)<.1) & (foot[...,1]<.08)).astype(np.float64)
    return motion


def load_motion(path, rig, fps=None):
    with np.load(path, allow_pickle=False) as d:
        local = np.asarray(d['local_rot_mats']); root = np.asarray(d['root_positions'])
        if local.ndim == 5 and local.shape[0] == 1: local = local[0]
        if root.ndim == 3 and root.shape[0] == 1: root = root[0]
        rate = float(d['fps']) if 'fps' in d else float(fps or 30)
        if 'joint_names' in d and list(d['joint_names']) != rig['jointNames']:
            raise ValueError('NPZ joint order differs from rig')
    # Native float32 global->local exports contain small non-orthogonality.
    # Validate first, then project onto SO(3) before chained component inverses;
    # otherwise deep FK/mirroring amplifies it. Never relax validity thresholds.
    validate_motion({'local_rot_mats':local,'root_positions':root,'fps':rate},rig)
    # Preserve an already valid authored SO(3) payload byte for byte. Repeated
    # projection changed round-off bits and invalidated its executed gait
    # receipt after a save/load, blocking legitimate derived master nodes.
    orthogonality=np.max(np.abs(local@local.swapaxes(-1,-2)-np.eye(3)))
    determinant_error=np.max(np.abs(np.linalg.det(local)-1))
    if max(orthogonality,determinant_error)>1e-10:
        local=Rotation.from_matrix(local.reshape(-1,3,3)).as_matrix().reshape(local.shape)
    return complete(local,root,rig,rate)


def save_motion(path, motion, rig):
    validate_motion(motion,rig)
    np.savez_compressed(path, **motion, joint_names=np.asarray(rig['jointNames']))


def freeze(motion, frame, count, rig):
    if not 0<=frame<len(motion['root_positions']) or count<1:
        raise ValueError('Freeze frame/count out of range')
    return complete(np.repeat(motion['local_rot_mats'][frame:frame+1],count,axis=0),
                    np.repeat(motion['root_positions'][frame:frame+1],count,axis=0),rig,float(motion['fps']))


def canonicalize(motion,rig):
    local=motion['local_rot_mats'].copy();root=motion['root_positions'].copy()
    _,pos=fk(local,root,rig)
    right,left=(rig['jointNames'].index(n) for n in rig['hipJointNames'])
    diff=pos[0,right]-pos[0,left];heading=np.arctan2(diff[2],-diff[0])
    turn=Rotation.from_euler('y',-heading).as_matrix()
    local[:,0]=turn@local[:,0]
    root=(root-root[0]*[1,0,1])@turn.T
    return complete(local,root,rig,float(motion['fps']))


def resample(motion, count, rig):
    if count < 1: raise ValueError('Empty resample')
    n=len(motion['root_positions']); local=motion['local_rot_mats']
    if n==1: return freeze(motion,0,count,rig)
    times=np.linspace(0,n-1,count)
    out=np.stack([Slerp(np.arange(n),Rotation.from_matrix(local[:,j]))(times).as_matrix()
                  for j in range(local.shape[1])],axis=1)
    root=np.stack([np.interp(times,np.arange(n),motion['root_positions'][:,i]) for i in range(3)],axis=1)
    return complete(out,root,rig,float(motion['fps']))


def upper_overlay(base, style, reference, rig):
    """Transfer one style's local upper-body residual, preserving stance legs/root."""
    count=len(base['root_positions'])
    style=resample(style,count,rig);reference=resample(reference,count,rig)
    upper=rig['jointNames'].index('Spine1');mask=[]
    for i in range(len(rig['parents'])):
        ancestor=i
        while ancestor>=0 and ancestor!=upper:ancestor=rig['parents'][ancestor]
        if ancestor==upper:mask.append(i)
    local=base['local_rot_mats'].copy()
    delta=style['local_rot_mats'][:,mask]@reference['local_rot_mats'][:,mask].swapaxes(-1,-2)
    local[:,mask]=delta@local[:,mask]
    return complete(local,base['root_positions'].copy(),rig,float(base['fps']))


def mirror_map(rig):
    names=rig['jointNames']; lookup={n:i for i,n in enumerate(names)}
    permutation=[]
    for name in names:
        pair='Right'+name[4:] if name.startswith('Left') else 'Left'+name[5:] if name.startswith('Right') else name
        if pair not in lookup: raise ValueError(f'No mirrored bone for {name}')
        permutation.append(lookup[pair])
    for i,p in enumerate(rig['parents']):
        if p>=0 and rig['parents'][permutation[i]]!=permutation[p]:
            raise ValueError('Mirror permutation does not preserve hierarchy')
    return permutation


def mirror(motion, base, rig, mask='all'):
    """Reflect world/component residuals relative to the SAME destination base.

    R_delta = R_motion * inverse(R_base); R_out = F * R_delta[pair] * F * R_base.
    This preserves the base even when the rest pose or right-hand grip is asymmetric.
    """
    permutation=mirror_map(rig); f=np.diag([-1.,1.,1.])
    m,_=fk(motion['local_rot_mats'],motion['root_positions'],rig)
    b=resample(base,len(m),rig); bg,_=fk(b['local_rot_mats'],b['root_positions'],rig)
    residual=m @ bg.swapaxes(-1,-2)
    out=(f @ residual[:,permutation] @ f) @ bg
    if mask=='legs':
        selected=np.array([n.startswith(('LeftLeg','LeftShin','LeftFoot','LeftToe','RightLeg','RightShin','RightFoot','RightToe')) for n in rig['jointNames']])
        out[:,~selected]=m[:,~selected]
    elif mask!='all':raise ValueError(f'Unknown mirror mask: {mask}')
    root=(motion['root_positions']-b['root_positions']) @ f + b['root_positions']
    return complete(local_from_global(out,rig['parents']),root,rig,float(motion['fps']))


def offset(motion, angles, axis, rig):
    """Apply specified world-axis rotations at joints, propagating down their subtree."""
    local=motion['local_rot_mats'].copy(); roots=motion['root_positions'].copy()
    for name, degrees in angles.items():
        if name not in rig['jointNames']:raise ValueError(f'Unknown offset joint {name}')
        j=rig['jointNames'].index(name);parent=rig['parents'][j]
        g,_=fk(local,roots,rig); rot=Rotation.from_euler(axis,degrees,degrees=True).as_matrix()
        desired=rot@g[:,j]
        local[:,j]=desired if parent<0 else g[:,parent].swapaxes(-1,-2)@desired
    return complete(local,roots,rig,float(motion['fps']))


def parameter(base, count, distribution, limit, mode, rig, source=None):
    if count%2!=1:raise ValueError('Parameter grid needs an exact neutral center')
    base=freeze(base,0,1,rig); slots=[]
    for u in np.linspace(0,1,count):
        # Native SOMA: positive Y rotation turns left; negative X looks up.
        # Runtime yaw u=0 is left, pitch u=0 is up.
        value=(1-2*u)*limit if mode=='yaw' else (2*u-1)*limit
        start=base if source is None else freeze(resample(source,count,rig),len(slots),1,rig)
        slots.append(offset(start,{n:value*w for n,w in distribution.items()},'y' if mode=='yaw' else 'x',rig))
    return complete(np.concatenate([s['local_rot_mats'] for s in slots]),
                    np.concatenate([s['root_positions'] for s in slots]),rig,float(base['fps']))


def loop_candidate(motion, seconds, start_fraction, rig, remove_heading=False, remove_translation=False):
    """Close a candidate seam; approval must still check support/stride/derivatives."""
    fps=float(motion['fps']);count=max(3,round(seconds*fps)+1)
    start=min(round((len(motion['root_positions'])-1)*start_fraction),len(motion['root_positions'])-count)
    start=max(0,start);end=min(start+count,len(motion['root_positions']))
    m=complete(motion['local_rot_mats'][start:end].copy(),motion['root_positions'][start:end].copy(),rig,fps)
    local=m['local_rot_mats'];root=m['root_positions']
    if remove_translation:
        # A cropped traveling candidate starts meters away from the character
        # origin. Remove that constant as well as travel; descendants (gait
        # poses, additive references and mirrors) share this canonical origin.
        origin=root[0,[0,2]].copy()
        travel=root[-1,[0,2]]-origin
        root[:,[0,2]]-=origin+np.linspace(0,1,len(root))[:,None]*travel
    if remove_heading:
        # Unwrap heading from root orientation using its transformed +Z axis.
        forward=local[:,0,:,2];yaw=np.unwrap(np.arctan2(forward[:,0],forward[:,2]))
        undo=Rotation.from_euler('y',-(yaw-yaw[0])[:,None]).as_matrix();local[:,0]=undo@local[:,0]
    window=min(7,len(root)-1);start_blend=len(root)-window-1
    for i in range(window+1):
        u=i/window;weight=u*u*(3-2*u);t=start_blend+i
        for j in range(local.shape[1]):
            local[t,j]=Slerp([0,1],Rotation.from_matrix(np.stack([local[t,j],local[0,j]])))([weight]).as_matrix()[0]
        root[t]=(1-weight)*root[t]+weight*root[0]
    return complete(local,root,rig,fps)


def remove_planar_path(motion, path, seconds, rig):
    """Keep body dynamics while removing the SAME numeric authoring trajectory."""
    if path['kind'] not in ('linear','accelerate'):
        raise ValueError('In-place conversion requires a numeric planar path')
    root=motion['root_positions'].copy();t=np.arange(len(root))/float(motion['fps'])
    velocity=path['velocity']
    distance=velocity*t if path['kind']=='linear' else .5*velocity*t*t/seconds
    root[:,[0,2]]-=distance[:,None]*np.asarray(path['direction'])
    root[:,[0,2]]-=root[0,[0,2]].copy()
    return complete(motion['local_rot_mats'].copy(),root,rig,float(motion['fps']))


def materialize_constraints(params, resolve, rig):
    count=int(params['seconds']*params['fps'])
    if count<2:raise ValueError('Candidate too short')
    constraints=[]; anchors=params.get('anchors',[])
    if len(anchors)>=20:raise ValueError('Too many fullbody anchors')
    frame_records={}
    for a in anchors:
        source=resolve(a['source']);n=len(source['root_positions'])
        source_frame=a.get('sourceFrame',round(a.get('fraction',0)*(n-1)))
        pose=freeze(source,source_frame,1,rig)
        frame=round(a['at']*(count-1))
        if frame in frame_records:raise ValueError('Conflicting fullbody target frame')
        frame_records[frame]=pose
    path=params.get('rootPath')
    if path:
        frames=list(range(count)) if path['kind'] in ('linear','accelerate') else [0,count-1]
        t=np.asarray(frames)/params['fps'];direction=np.asarray(path.get('direction',[0.,0.]))
        v=path.get('velocity',0)
        distance=v*t if path['kind']=='linear' else .5*v*t*t/params['seconds'] if path['kind']=='accelerate' else np.zeros_like(t)
        heading=np.deg2rad(path.get('headingDegrees',0))*(np.linspace(0,1,len(frames)) if path['kind']=='turn' else np.ones(len(frames)))
        origin=np.zeros(2)  # Every candidate uses one canonical XZ origin.
        positions=origin+distance[:,None]*direction
        constraints.append({'type':'root2d','frame_indices':frames,'smooth_root_2d':positions.tolist(),
                            'global_root_heading':np.stack([np.cos(heading),np.sin(heading)],axis=-1).tolist()})
        for frame,pose in frame_records.items():
            # Full-body FK conditions include root XZ; match the path to avoid contradictions.
            if frame in frames:
                index=frames.index(frame)
                pose['root_positions'][0,[0,2]]=positions[index]
                if 'endHeightMeters' in path:
                    pose['root_positions'][0,1]+=path['endHeightMeters']*frame/(count-1)
                if path['kind']=='turn' or path.get('headingDegrees',0):
                    # Rotate the entire endpoint with actor heading, not just its XZ.
                    pose['local_rot_mats'][0,0]=Rotation.from_euler('y',heading[index]).as_matrix()@pose['local_rot_mats'][0,0]
    if frame_records:
        frames=sorted(frame_records)
        local=np.concatenate([frame_records[f]['local_rot_mats'] for f in frames])
        axis=Rotation.from_matrix(local.reshape(-1,3,3)).as_rotvec().reshape(len(frames),local.shape[1],3)
        constraints.append({'type':'fullbody','frame_indices':frames,'local_joints_rot':axis.tolist(),
                            'root_positions':np.concatenate([frame_records[f]['root_positions'] for f in frames]).tolist()})
    for effector in params.get('endEffectors',[]):
        frames=sorted({round(u*(count-1)) for u in effector['at']})
        if len(frames)>=20:raise ValueError('Too many end-effector targets')
        pose=freeze(resolve(effector['source']),0,len(frames),rig)
        if path:
            path_constraint=constraints[0];path_frames=path_constraint['frame_indices']
            for axis_index,component in enumerate((0,2)):
                pose['root_positions'][:,component]=np.interp(frames,path_frames,np.asarray(path_constraint['smooth_root_2d'])[:,axis_index])
        local=pose['local_rot_mats']
        constraints.append({'type':effector['type'],'frame_indices':frames,
                            'local_joints_rot':Rotation.from_matrix(local.reshape(-1,3,3)).as_rotvec().reshape(len(frames),local.shape[1],3).tolist(),
                            'root_positions':pose['root_positions'].tolist()})
    return constraints
