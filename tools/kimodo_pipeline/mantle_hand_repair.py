"""Bounded target-skin hand clearance, baked into generated physical tracks.

The immutable mesh supplies geometry, not replacement animation. Support wrist
anchors stay fixed; finger rotations and release-arm IK preserve bone lengths.
"""
from __future__ import annotations
import copy
from pathlib import Path
import numpy as np
from scipy.optimize import minimize
from scipy.spatial.transform import Rotation
from .graph import read_json
from .runner import file_hash
from .mantle import profile, _hand_frames, _arm_rig
from .gait_constraints import solve_foot_ik

POLICY = 'mantle-target-skin-hand-clearance-v1'
FINGER_LIMIT_DEGREES = 45.
ARM_LIMIT_CM = 10.
CLEARANCE_CM = .2
AIR_CLEARANCE_CM = 4.  # Approach/release air gap, blended back to the support anchor.
# Same 2cm depth limit as the existing full-skin Godot gate. Clearance is
# the solver's desired margin, not permission to weaken runtime acceptance.
MAXIMUM_DEPTH_CM = 2.
FINGERS = ('thumb','index','middle','ring','pinky')


class TargetSkin:
    def __init__(self, mesh_path, skeleton):
        import ufbx
        from .offline_fbx import FBX_BASIS
        scene = ufbx.load_file(str(mesh_path))
        self.bindings = []; self.count = 0
        self.mesh_path = str(Path(mesh_path).resolve()); self.mesh_sha256 = file_hash(mesh_path)
        mesh_list=scene.meshes
        # ufbx 0.0.5 exposes borrowed native wrappers. Retain all owning lists
        # for this reader's lifetime, as the existing FBX readback does.
        self._owners=[scene,mesh_list]
        for mesh in mesh_list:
            if len(mesh.skin_deformers) != 1: raise ValueError('Expected one linear target skin')
            deformers=mesh.skin_deformers;skin = deformers[0];clusters=skin.clusters
            vertex_list=mesh.vertices
            self._owners.extend([mesh,deformers,skin,clusters,vertex_list])
            vertices = np.array([[p.x,p.y,p.z] for p in vertex_list])
            for cluster in clusters:
                self._owners.append(cluster)
                matrix = cluster.geometry_to_bone
                linear = np.array([list(v) for v in (matrix.c0,matrix.c1,matrix.c2)]).T
                indices = np.array(list(cluster.vertices),dtype=int)+self.count
                local = (vertices[indices-self.count]@linear.T+np.array(matrix.c3))@FBX_BASIS
                self.bindings.append((cluster.bone_node.name.lower(),indices,local,np.array(list(cluster.weights))))
            self.count += len(vertices)
        total=np.zeros(self.count)
        for bone,indices,_,weights in self.bindings:
            if bone not in {n.lower() for n in skeleton['rawBoneNames']}:
                raise ValueError('Unresolved target skin bone: '+bone)
            if not np.isfinite(weights).all() or np.any(weights<0):raise ValueError('Invalid target skin weights')
            total[indices]+=weights
        if np.max(np.abs(total-1))>1e-3:raise ValueError('Target skin weights are not normalized')
        self.sides = {}; self.fingers = {}
        for side in ('l','r'):
            mask = np.zeros(self.count)
            for name,indices,_,weights in self.bindings:
                if name=='hand_'+side or (name.endswith('_'+side) and name.startswith(FINGERS)):
                    mask[indices] += weights
            self.sides[side] = np.flatnonzero(mask>=.5)
            for finger in FINGERS:
                mask = np.zeros(self.count)
                for name,indices,_,weights in self.bindings:
                    if name.startswith(finger+'_') and name.endswith('_'+side):mask[indices] += weights
                self.fingers[finger+'_'+side] = np.flatnonzero(mask>=.5)
        self._subsets = {}

    def subset(self, indices):
        key = tuple(indices)
        if key not in self._subsets:
            inverse = np.full(self.count,-1,dtype=int);inverse[indices]=np.arange(len(indices))
            self._subsets[key] = [(name,inverse[v][inverse[v]>=0],p[inverse[v]>=0],w[inverse[v]>=0])
                for name,v,p,w in self.bindings if np.any(inverse[v]>=0)]
        return self._subsets[key]

    def evaluate(self, world, indices):
        result = np.zeros((len(indices),3))
        for name,indices,local,weights in self.subset(indices):
            rotation,position = world[name][:2]
            result[indices] += (local@rotation.T+position)*weights[:,None]
        return result


def clearance(points, height):
    # Nominal ledge front at native Y=50cm. Hand can lie above the top or
    # wrap outside the front; do not force all fingers into a flat pose.
    return np.maximum(points[:,2]-height,50.-points[:,1])


def repair(raw, skeleton, name, skin):
    spec=profile(name); count=raw['sampledKeyCount']; names=skeleton['rawBoneNames'];parents=skeleton['rawParents']
    if any(p>=i or p< -1 for i,p in enumerate(parents)):raise ValueError('Hand repair requires parent-first physical bones')
    lookup={n.lower():i for i,n in enumerate(names)};tracks={t['bone'].lower():t for t in raw['tracks']}
    rotations=np.stack([Rotation.from_quat(np.broadcast_to(tracks[n.lower()]['rotations'],(count,4))).as_matrix() for n in names],axis=1)
    offsets=np.stack([np.broadcast_to(tracks[n.lower()]['positions'],(count,3)) for n in names],axis=1)
    if any(not np.allclose(t.get('scales') or [[1,1,1]],1,atol=1e-6) for t in raw['tracks']):
        raise ValueError('Hand repair requires unit scale; stretching is forbidden')
    for n,i in lookup.items():
        if n.startswith(FINGERS) or n.startswith(('upperarm_','lowerarm_','hand_')):
            if not np.allclose(offsets[:,i],skeleton['referencePose'][i]['position'],atol=.001,rtol=0):
                raise ValueError('Hand repair requires immutable arm/finger offsets: '+n)
    original=rotations.copy(); result=copy.deepcopy(raw);records=[]
    def world_at(frame, local):
        gr=np.empty_like(local); gp=np.empty((len(names),3))
        for i,p in enumerate(parents):
            gr[i]=local[i] if p<0 else gr[p]@local[i]
            gp[i]=offsets[frame,i] if p<0 else gp[p]+gr[p]@offsets[frame,i]
        return {n.lower():(gr[i],gp[i]) for i,n in enumerate(names)}
    limit=np.deg2rad(FINGER_LIMIT_DEGREES)
    # Only configured support hands are edited. Free hands/body have separate
    # geometry gates and may still reject the whole animation.
    for side_name in spec['hands']:
        side=side_name[0];contact_keys=_hand_frames(spec,side_name,count,raw['playLength'])
        contact=set(contact_keys.tolist());hand='hand_'+side
        support_world=[world_at(int(frame),original[frame])[hand] for frame in contact_keys]
        anchor_rotation=Rotation.from_matrix(np.stack([w[0] for w in support_world])).mean().as_matrix()
        anchor_position=np.mean([w[1] for w in support_world],axis=0)
        def contact_world(world):
            wrist=world[hand];delta=anchor_rotation@wrist[0].T
            return {n:(delta@r,anchor_position+delta@(p-wrist[1])) for n,(r,p) in world.items()}
        for frame in range(count):
            local=rotations[frame].copy();before=world_at(frame,local)
            points=skin.evaluate(before,skin.sides[side]);before_clearance=float(clearance(points,spec['heightCm']).min())
            before_depth=max(0.,-before_clearance)
            margin=CLEARANCE_CM if frame in contact else AIR_CLEARANCE_CM
            transition=contact_keys[0]-1<=frame<=contact_keys[-1]+1
            # Runtime palm orientation blends towards this fixed anchor during
            # one-frame contact ramps. Make both endpoint finger poses safe in
            # that basis as well, rather than fixing only integer FK samples.
            projected_clearance=float(clearance(skin.evaluate(contact_world(before),skin.sides[side]),spec['heightCm']).min()) if transition else margin
            if before_clearance>=margin and projected_clearance>=CLEARANCE_CM:continue
            for finger in FINGERS:
                selected=skin.fingers[finger+'_'+side]
                if not len(selected):raise ValueError('Missing target finger skin: '+finger+'_'+side)
                indices=[lookup[f'{finger}_{part:02}_{side}'] for part in (1,2,3)]
                baseline=local[indices].copy()
                def proposed(x):
                    pose=local.copy();pose[indices]=baseline@Rotation.from_rotvec(x.reshape(3,3)).as_matrix()
                    return pose
                def constraints(x):
                    world=world_at(frame,proposed(x));values=skin.evaluate(world,selected)
                    # Outside support the following arm pass supplies actual
                    # clearance. Requiring a sunk, still-unlifted wrist to
                    # satisfy both planes here would reject reachable release
                    # fingers before the permitted arm correction can run.
                    constraints=[limit**2-np.sum(x.reshape(3,3)**2,axis=1)]
                    if not transition or frame in contact:
                        constraints.append(clearance(values,spec['heightCm'])-CLEARANCE_CM)
                    if transition:constraints.append(clearance(skin.evaluate(contact_world(world),selected),spec['heightCm'])-CLEARANCE_CM)
                    return np.concatenate(constraints)
                if np.min(constraints(np.zeros(9)))>=-1e-6:continue
                fitted=minimize(lambda x:np.sum(x*x),np.zeros(9),method='SLSQP',
                    constraints=[dict(type='ineq',fun=constraints)],
                    bounds=[(-limit,limit)]*9,options=dict(ftol=1e-9,maxiter=100))
                # Keep a bounded improvement even if the desired 0.2 cm air
                # margin was unattainable. The independent 2 cm depth gate
                # below still decides acceptance; fixed palm geometry cannot
                # be repaired by forcing larger finger rotations.
                if (np.isfinite(fitted.x).all() and
                    np.all(np.linalg.norm(fitted.x.reshape(3,3),axis=1)<=limit+1e-8) and
                    np.min(constraints(fitted.x)[3:])>np.min(constraints(np.zeros(9))[3:])+1e-6):
                    local=proposed(fitted.x)
            # After release/approach, remaining palm/skin intersections can
            # need wrist translation. Support wrist must remain unchanged.
            arm_shift=0.;requested_shift=0.; after_fingers=world_at(frame,local)
            min_clearance=float(clearance(skin.evaluate(after_fingers,skin.sides[side]),spec['heightCm']).min())
            if frame not in contact and min_clearance<margin:
                hand='hand_'+side; wrist=after_fingers[hand][1];orientation=after_fingers[hand][0]
                # A tall ledge's approach can be below the top but only a few
                # centimeters inside its front plane. Evaluate both escape
                # planes; vertical-only IK cannot repair that configuration.
                points=skin.evaluate(after_fingers,skin.sides[side])
                escapes=[(max(0.,margin+spec['heightCm']-points[:,2].min()),np.array([0.,0.,1.])),
                         (max(0.,margin+points[:,1].max()-50.),np.array([0.,-1.,0.]))]
                if any(distance>0 for distance,_ in escapes):
                    rig_names=[]
                    for n in names:
                        alias=n.lower()
                        for s,suffix in (('Left','l'),('Right','r')):
                            for bone,end in (('upperarm','Arm'),('lowerarm','ForeArm'),('hand','Hand')):
                                if alias==bone+'_'+suffix:alias=s+end
                        rig_names.append(alias)
                    rig=dict(jointNames=rig_names,parents=parents,offsets=(offsets[frame]/100).tolist())
                    best_clearance=min_clearance;best_pose=local
                    for distance,direction in sorted(escapes,key=lambda e:e[0]):
                        shift=min(distance,ARM_LIMIT_CM)
                        try:
                            candidate=solve_foot_ik(local,offsets[frame,0]/100,_arm_rig(rig),side_name,
                                (wrist+direction*shift)/100,orientation,{})
                        except ValueError:continue
                        candidate_clearance=float(clearance(skin.evaluate(world_at(frame,candidate),skin.sides[side]),spec['heightCm']).min())
                        if candidate_clearance>best_clearance+1e-6:
                            best_clearance= candidate_clearance;best_pose=candidate
                            arm_shift=shift;requested_shift=distance
                    local=best_pose
            rotations[frame]=local
            after=world_at(frame,local);points=skin.evaluate(after,skin.sides[side])
            after_depth=max(0.,-float(clearance(points,spec['heightCm']).min()))
            projected_depth=max(0.,-float(clearance(skin.evaluate(contact_world(after),skin.sides[side]),spec['heightCm']).min())) if transition else 0.
            angles=Rotation.from_matrix(original[frame].transpose(0,2,1)@local).magnitude()*180/np.pi
            records.append(dict(frame=frame,side=side_name,support=frame in contact,
                beforeDepthCm=before_depth,afterDepthCm=after_depth,transitionAnchorChecked=bool(transition),
                transitionAnchorDepthCm=projected_depth,armCorrectionCm=arm_shift,requestedArmCorrectionCm=requested_shift,
                maximumFingerCorrectionDegrees=float(max(angles[lookup[f'{f}_{p:02}_{side}']] for f in FINGERS for p in (1,2,3))),
                wristDisplacementCm=float(np.linalg.norm(after['hand_'+side][1]-before['hand_'+side][1]))))
    changed={f'{f}_{p:02}_{s[0]}' for s in spec['hands'] for f in FINGERS for p in (1,2,3)}
    changed|={b+'_'+s[0] for s in spec['hands'] for b in ('upperarm','lowerarm','hand')}
    for n in changed:result_track=next(t for t in result['tracks'] if t['bone'].lower()==n);result_track['rotations']=Rotation.from_matrix(rotations[:,lookup[n]]).as_quat().tolist()
    report=dict(schemaVersion=1,policy=POLICY,semanticName=name,meshSha256=skin.mesh_sha256,
        clearanceCm=CLEARANCE_CM,airClearanceCm=AIR_CLEARANCE_CM,maximumDepthCm=MAXIMUM_DEPTH_CM,maximumFingerCorrectionDegrees=FINGER_LIMIT_DEGREES,
        maximumArmCorrectionCm=ARM_LIMIT_CM,records=records,
        passed=all(r['afterDepthCm']<=MAXIMUM_DEPTH_CM and r['transitionAnchorDepthCm']<=MAXIMUM_DEPTH_CM and r['maximumFingerCorrectionDegrees']<=FINGER_LIMIT_DEGREES+1e-3
            and r['wristDisplacementCm']<=ARM_LIMIT_CM+1e-3 and (not r['support'] or r['wristDisplacementCm']<=1e-5) for r in records),
        scope='configured support hands at authored keys; final runtime skin remains independently gated',productionReady=False)
    return result,report


def immutable_target_skin(skeleton):
    root=Path(__file__).resolve().parents[2];manifest=read_json(root/'assets/generated/als_v4/als_manifest.json')
    mesh=next(m for m in manifest['skeletalMeshes'] if m['assetName']=='Mannequin')
    entry=next(f for f in manifest['files'] if f['relativePath']==mesh['outputPath'])
    path=root/'assets/generated/als_v4'/mesh['outputPath']
    if file_hash(path)!=entry['sha256']:raise ValueError('Immutable target mesh changed')
    return TargetSkin(path,skeleton)


def validate_report(report):
    """A claimed pass cannot enlarge the budgets or omit transition checks."""
    if (report.get('policy')!=POLICY or not report.get('passed') or
        report.get('maximumDepthCm')!=MAXIMUM_DEPTH_CM or
        report.get('maximumArmCorrectionCm')!=ARM_LIMIT_CM or
        report.get('maximumFingerCorrectionDegrees')!=FINGER_LIMIT_DEGREES):
        raise ValueError('Missing or failed bounded mantle hand-skin repair')
    for row in report.get('records',[]):
        if 'transitionAnchorChecked' not in row or 'transitionAnchorDepthCm' not in row:
            raise ValueError('Missing mantle hand transition checks')
        for key,limit in [('afterDepthCm',MAXIMUM_DEPTH_CM),('transitionAnchorDepthCm',MAXIMUM_DEPTH_CM),
                          ('wristDisplacementCm',ARM_LIMIT_CM),('maximumFingerCorrectionDegrees',FINGER_LIMIT_DEGREES+.001)]:
            value=row.get(key,float('nan'))
            if not np.isfinite(value) or value<0 or value>limit+1e-5:
                raise ValueError('Mantle hand correction/depth budget exceeded: '+key)
        if row['support'] and row['wristDisplacementCm']>1e-5:
            raise ValueError('Mantle support wrist was moved by hand-skin repair')
