"""Register real target-stage artifacts; never fabricate stages for a source batch."""
import argparse
import sys
import json
from datetime import datetime, timezone
from pathlib import Path

import numpy as np

from .viewer_export import ROOT, _hash, _write, register_index
from .publish import world_pose
from .root_ownership import semantic_hash


def preview_raw(raw, skeleton, name, provenance):
    count = raw['sampledKeyCount']; names = skeleton['rawBoneNames']
    tracks = {t['bone'].lower(): t for t in raw['tracks']}
    channels = {}
    for field, reference in [('positions','position'),('rotations','rotation'),('scales','scale')]:
        values = []
        for i, name_ in enumerate(names):
            channel = tracks.get(name_.lower(),{}).get(field) or [skeleton['referencePose'][i][reference]]
            array = np.asarray(channel,dtype=float)
            if len(array) not in (1,count) or not np.isfinite(array).all():
                raise ValueError('Target channel has an incompatible timeline: '+name_)
            values.append(np.broadcast_to(array,(count,array.shape[1])))
        channels[field] = np.stack(values,axis=1).tolist()
    world = world_pose(raw,skeleton)
    pelvis = world['pelvis'][1][:,:2]; root = world['root'][1][:,:2]
    metrics = dict(maximumRootHorizontalCm=float(np.max(np.linalg.norm(root,axis=1))),
        maximumPelvisRootHorizontalCm=float(np.max(np.linalg.norm(pelvis-root,axis=1))))
    return dict(format='kimodo-viewer-motion-v1',units='cm',up='z',name=name,
        fps=raw['frameRateNumerator']/raw['frameRateDenominator'],frameCount=count,
        jointNames=names,parents=skeleton['rawParents'],
        offsetsCm=[r['position'] for r in skeleton['referencePose'][:len(names)]],
        localPositionsCm=channels['positions'],localQuaternions=channels['rotations'],
        localScales=channels['scales'],displayRootOnly=False,productionReady=False,
        source=dict(provenance,metrics=metrics))


def export_pack(pack_path, *, label=None, make_latest=False, root=ROOT, assets=None, batch_id=None):
    root=Path(root).resolve(); pack_path=Path(pack_path).resolve(); folder=pack_path.parent
    if not pack_path.is_relative_to(root/'assets/generated'):
        raise ValueError('Viewer target pack must be in this checkout')
    pack=json.loads(pack_path.read_text(encoding='utf-8'))
    native_path=Path(pack['nativeRetargetBundle'])
    native=json.loads(native_path.read_text(encoding='utf-8'))
    jobs={j['source']:j for j in native['jobs'] if j['edition']=='v4'}
    skeleton=json.loads((root/'assets/config/v4_recovery_movement_source_inputs.json').read_text(encoding='utf-8'))['skeletons'][0]
    manifest=json.loads((folder/'v4/als_manifest.json').read_text(encoding='utf-8'))
    animations={a['objectPath']:a for a in manifest['animations']}
    payloads={f['path']:f['sha256'] for f in pack['files']}
    def checked(path, expected):
        if _hash(path)!=expected:raise ValueError('Viewer target receipt changed: '+str(path))
        return json.loads(Path(path).read_text(encoding='utf-8'))
    bid=batch_id or folder.name+'-'+_hash(pack_path)[:12]
    if not bid or any(c not in 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-.' for c in bid) or bid in ('.','..'):
        raise ValueError('Unsafe viewer batch identity')
    target=root/'artifacts/kimodo_viewer/batches'/bid
    previous=json.loads((target/'index.json').read_text(encoding='utf-8')) if batch_id and (target/'index.json').exists() else None
    if batch_id:
        if not previous:raise ValueError('Same-batch merge requires an existing authoring batch')
        source_roots={Path(e['source']['runDirectory']).resolve() for e in previous['entries']
            if e['phase'] in ('generated','master') and e.get('source',{}).get('runDirectory')}
        if not source_roots or any(not any(Path(j['motionPath']).resolve().is_relative_to(p) for p in source_roots) for j in native['jobs']):
            raise ValueError('Target products do not belong to this authoring batch')
    entries=[]
    for receipt in pack['rootOwnershipReceipts']:
        job=jobs.get(receipt['source'])
        if not job or (assets and job['contract']['asset'] not in assets):continue
        name=job['contract']['asset']; report_path=folder/receipt['file']
        report=checked(report_path,receipt['sha256'])
        variants=[('target-retarget',Path(job['raw']),job['rawSha256'],None),
            ('target-root',report_path.parent/'authored-root.raw.json',None,report['authoredRootRawSemanticSha256']),
            ('target-runtime',report_path.parent/'runtime.raw.json',None,report['runtimeRawSemanticSha256'])]
        for phase,path,expected,semantic in variants:
            uri='res://'+path.relative_to(root).as_posix()
            raw=checked(path,expected or payloads[uri])
            if semantic and semantic_hash(raw)!=semantic:raise ValueError('Ownership output semantic digest changed')
            provenance=dict(batch=bid,phase=phase,sourceAsset=job['source'],rawPath=str(path),rawSha256=_hash(path),
                packPath=str(pack_path),packSha256=_hash(pack_path),ownershipPath=str(report_path),
                ownershipSha256=receipt['sha256'],ownership=report,nativeBundleSha256=_hash(native_path))
            rel=Path('batches')/bid/phase/(report_path.parent.name+'.motion.json')
            preview=root/'artifacts/kimodo_viewer'/rel
            payload=preview_raw(raw,skeleton,name,provenance);_write(preview,payload)
            entries.append(dict(name=name,path=rel.as_posix(),url='/authoring-file/'+rel.as_posix(),phase=phase,
                node=job['node'],kind='source-motion',qualification='目标阶段制品 · 未发布',
                size=preview.stat().st_size,sha256=_hash(preview),source=payload['source'],sidecars=[]))
        if pack.get('finalFbxStatus') in ('native-bake-and-readback-verified','offline-ufbx-readback-verified'):
            fbx=folder/'v4'/animations[job['source']]['outputPath'];uri='res://'+fbx.relative_to(root).as_posix()
            expected=payloads[uri]
            if _hash(fbx)!=expected:raise ValueError('Final FBX payload changed')
            entries.append(dict(name=name,path=fbx.relative_to(root/'assets/generated').as_posix(),
                url='/file/'+fbx.relative_to(root/'assets/generated').as_posix(),phase='target-fbx',kind='fbx',
                node=job['node'],qualification='最终 FBX 回读通过 · 未发布',size=fbx.stat().st_size,sha256=expected,
                source=dict(batch=bid,phase='target-fbx',packPath=str(pack_path),packSha256=_hash(pack_path)),sidecars=[]))
    if previous:
        phases={e['phase'] for e in entries}
        entries=[e for e in previous['entries'] if e['phase'] not in phases]+entries
    index=dict(schemaVersion=1,id=bid,label=label or (previous['label'] if previous else folder.name),status='已转换 · 质量待验收 · 非活动包',
        createdUtc=datetime.now(timezone.utc).isoformat(),productionReady=False,entries=entries)
    if previous:
        index['runDirectory']=previous.get('runDirectory') or next(iter(source_roots)).as_posix()
    return register_index(root,target,index,make_latest=make_latest)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pack',type=Path,required=True);parser.add_argument('--label')
    parser.add_argument('--make-latest',action='store_true')
    args=parser.parse_args();print(json.dumps(export_pack(args.pack,label=args.label,make_latest=args.make_latest)))


def register_production_pack(pack_path):
    """Visibility failure is reported separately from publication acceptance."""
    if not Path(pack_path).resolve().is_relative_to(ROOT/'assets/generated'):
        return None
    try:
        return export_pack(pack_path,make_latest=True)
    except Exception as error:
        evidence=ROOT/'artifacts/kimodo_viewer/export-failures'/(_hash(pack_path)+'.json')
        _write(evidence,dict(pack=str(pack_path),error=str(error)))
        print('VIEWER_TARGET_EXPORT_FAILED: '+str(error),file=sys.stderr,flush=True)


if __name__=='__main__':main()
