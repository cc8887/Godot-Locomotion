"""Offline final-target root trajectory sidecars, independent of playback policy."""
import hashlib
import json
import numpy as np
from scipy.spatial.transform import Rotation
from .runner import file_hash,write_json

def raw_digest(raw):
    return hashlib.sha256(json.dumps(raw,sort_keys=True,separators=(',',':'),allow_nan=False).encode()).hexdigest()

def bake(raw,job):
    count=raw['sampledKeyCount'];length=raw['playLength']
    root=next(t for t in raw['tracks'] if t['bone'].lower()=='root')
    def channel(name,width):
        v=np.asarray(root[name],dtype=float)
        if v.shape not in ((1,width),(count,width)) or not np.isfinite(v).all():raise ValueError('Invalid root channel '+name)
        return np.broadcast_to(v,(count,width)).copy()
    p=channel('positions',3);q=channel('rotations',4)
    if np.any(np.linalg.norm(q,axis=1)<1e-8):raise ValueError('Zero root quaternion')
    r=Rotation.from_quat(q);relative=r[0].inv()*r
    displacement=p-p[0];translated=bool(np.max(np.linalg.norm(displacement,axis=1))>1e-4)
    rotated=bool(np.max(relative.magnitude())>1e-6)
    declared=job['contract']['originalEvaluation']['enableRootMotion']
    if not (translated or rotated or declared):return None
    distance=np.r_[0,np.cumsum(np.linalg.norm(np.diff(p,axis=0)[:,:2],axis=1))]
    spatial=np.r_[0,np.cumsum(np.linalg.norm(np.diff(p,axis=0),axis=1))]
    angular=np.r_[0,np.cumsum((r[:-1].inv()*r[1:]).magnitude())]*180/np.pi
    direction=relative.apply(np.tile([1.,0,0],(count,1)))
    yaw=np.unwrap(np.arctan2(direction[:,1],direction[:,0]))*180/np.pi
    times=np.linspace(0,length,count)
    channels={'cumulativePlanarDistanceCm':distance,'remainingPlanarDistanceCm':distance[-1]-distance,
              'signedDistanceToEndCm':distance-distance[-1],'cumulativeSpatialDistanceCm':spatial,
              'cumulativeRotationDegrees':angular,'rootYawDegrees':yaw}
    for i,name in enumerate('XYZ'):channels['rootDisplacement'+name+'Cm']=displacement[:,i]
    for i,name in enumerate('XYZW'):channels['relativeRootQuaternion'+name]=relative.as_quat()[:,i]
    return dict(schemaVersion=1,source=job['source'],asset=job['contract']['asset'],edition=job['edition'],
                targetSkeleton=raw['skeletonSource'],coordinateSystem='native target root local axes; XY ground, Z up; centimeters',
                timePolicy='final exported sampled keys; no looping, FPS conversion or playback retiming',
                rootMotionDeclared=declared,containsTranslation=translated,containsRotation=rotated,
                sampledKeyCount=count,playLengthSeconds=length,frameRateNumerator=raw['frameRateNumerator'],
                frameRateDenominator=raw['frameRateDenominator'],rootInterpolation=job['contract']['originalTiming']['interpolation'],
                authoringBvhSha256=job['bvhSha256'],nativeRawSha256=job['rawSha256'],runtimeRawSemanticSha256=raw_digest(raw),
                distanceConvention='path length is nonnegative; signedDistanceToEndCm increases from negative remaining length to zero',
                keys=[dict(timeSeconds=float(t),**{k:float(v[i]) for k,v in channels.items()}) for i,t in enumerate(times)])

def export(root,raw,job):
    value=bake(raw,job)
    if value is None:return None
    path=root/'motion_curves'/job['edition']/(job['contract']['asset']+'.root-motion.json')
    path.parent.mkdir(parents=True,exist_ok=True)
    if path.exists():
        previous=json.loads(path.read_text(encoding='utf-8'))
        if previous!=value:raise ValueError('Root curve exists with different inputs; use a new asset version')
    else:write_json(path,value)
    return dict(source=job['source'],edition=job['edition'],file=path.relative_to(root).as_posix(),sha256=file_hash(path),
                runtimeRawSemanticSha256=value['runtimeRawSemanticSha256'])
