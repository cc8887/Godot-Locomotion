"""Convert a completed/partial authoring batch all the way to Godot assets.

Rejected source branches remain missing. Conversion proceeds for completed
deliveries, retains failed target gates, and never activates a diagnostic pack.
"""
import argparse
import json
import sys
import hashlib
from pathlib import Path
from .runner import file_hash, write_json
from .graph import read_json, target_origin_reference
from .scope import is_prop
from .motion import load_motion
from .offline_retarget import convert, POLICY
from .offline_fbx import write_fbx, readback, mesh_binding_skeleton
from .publish import ROOT, CONFIG, stage, verify_compatible


def convert_bundle(authoring, receipts_path, output, pack, *, batch_id=None, resume=False, finish_existing=False):
    output=Path(output).resolve(); pack=Path(pack).resolve(); output.mkdir(parents=True,exist_ok=True)
    bundle=read_json(authoring); receipts=read_json(receipts_path)
    rig_output=receipts['rig:soma']['outputs']['rig.json']
    if file_hash(rig_output['path'])!=rig_output['sha256']:raise ValueError('Changed source rig')
    rig=read_json(rig_output['path'])
    contracts=read_json(ROOT/'docs/architecture/kimodo-als-asset-contracts.json')['assets']
    by_source={c['source']:c for c in contracts if not is_prop(c)}
    v4=read_json(CONFIG/'v4_recovery_movement_source_inputs.json')['skeletons'][0]
    ref=read_json(CONFIG/'refactored_animation_sources.json')['skeletons']['/ALS/ALS/Character/SK_Als.SK_Als']
    verify_compatible(v4,ref)
    jobs=[]; failures=[]
    retarget=output/'retarget-bundle.json'
    if resume:
        previous=read_json(output/'pipeline-result.json')
        if previous['authoringBundleSha256']!=file_hash(authoring) or previous['receiptsSha256']!=file_hash(receipts_path):raise ValueError('Resume source provenance changed')
        jobs=read_json(retarget)['jobs']
        for job in jobs:
            for key in ('raw','fbx','motionPath','bvhPath'):
                h=key+'Sha256' if key in ('raw','fbx') else key.removesuffix('Path')+'Sha256'
                if file_hash(job[key])!=job[h]:raise ValueError('Resume retarget payload changed: '+key)
    for asset in ([] if resume else bundle['assets']):
        node=asset['node'];receipt=receipts[node]
        try:
            if receipt['identity']!=asset['identity'] or receipt.get('boundInput'):raise ValueError('Delivery provenance differs')
            outputs=asset['outputs']
            for name,payload in outputs.items():
                if file_hash(payload['path'])!=payload['sha256']:raise ValueError('Changed source output: '+name)
            contract=receipt['inputs']['node']['params']['contract']; source=contract['source']
            if source not in by_source:raise ValueError('Prop/unknown identity in conversion')
            metadata=read_json(outputs['asset.json']['path'])
            motion=load_motion(outputs['motion.npz']['path'],rig)
            skeleton=ref if contract['edition']=='refactored' else v4
            raw,report=convert(motion,rig,skeleton,source,contract['originalTiming'])
            folder=output/'retarget'/hashlib.sha256(source.encode()).hexdigest();folder.mkdir(parents=True,exist_ok=True)
            raw_path=folder/'retarget.raw.json';write_json(raw_path,raw)
            fbx=write_fbx(folder/'retarget.fbx',raw,skeleton)
            report.update(source=source,sourceIdentity=asset['identity'],sourceMotionSha256=outputs['motion.npz']['sha256'],rawSha256=file_hash(raw_path))
            report['fbxReadback']=readback(fbx,raw,skeleton);write_json(folder/'conversion.json',report)
            if not report['fbxReadback']['passed']:raise ValueError('Independent retarget FBX readback failed')
            j=dict(node=node,source=source,edition=contract['edition'],contract=contract,
                motionPath=outputs['motion.npz']['path'],motionSha256=outputs['motion.npz']['sha256'],
                bvhPath=outputs['motion.bvh']['path'],bvhSha256=outputs['motion.bvh']['sha256'],
                raw=str(raw_path),rawSha256=file_hash(raw_path),fbx=str(fbx),fbxSha256=file_hash(fbx),
                retargetedAssetPath='offline:'+node,targetPlanarOrigin=target_origin_reference(contract,contracts))
            for key in ('gaitValidationReceipt','authoringTrajectory','upperBodyValidationReceipt'):
                if metadata.get(key):j[key]=metadata[key]
            for key in ('targetGaitPolicy','targetCleanup'):
                if contract.get(key):j[key]=contract[key]
            jobs.append(j)
            print('CONVERTED',node,flush=True)
        except Exception as error:
            failures.append(dict(node=node,error=str(error)))
            print('CONVERSION_FAILED',node,str(error),flush=True)
        write_json(output/'conversion-progress.json',dict(converted=len(jobs),total=len(bundle['assets']),failures=failures))
    write_json(retarget,dict(version=output.name,conversionBackend=POLICY,authoringBundle=str(Path(authoring).resolve()),jobs=jobs))
    result=dict(status='conversion_failed' if failures else 'converted',productionReady=False,
        sourceDeliveries=len(bundle['assets']),converted=len(jobs),failures=failures,
        retargetBundle=str(retarget),authoringBundleSha256=file_hash(authoring),receiptsSha256=file_hash(receipts_path))
    write_json(output/'pipeline-result.json',result)
    if failures:raise ValueError('Conversion failures retained; cannot build complete diagnostic pack')
    if finish_existing:
        if not resume:raise ValueError('Finishing an existing pack requires verified resume inputs')
        staged=read_json(pack/'resource-pack.json')
        if staged['status']!='diagnostic' or Path(staged['nativeRetargetBundle']).resolve()!=retarget:raise ValueError('Existing pack provenance differs')
    else:staged=stage(retarget,pack,diagnostic=True)
    original_manifest=read_json(ROOT/'assets/generated/als_v4/als_manifest.json')
    target_mesh=next(m for m in original_manifest['skeletalMeshes'] if m['assetName']=='Mannequin')
    mesh_path=ROOT/'assets/generated/als_v4'/target_mesh['outputPath']
    mesh_receipt=next(f for f in original_manifest['files'] if f['relativePath']==target_mesh['outputPath'])
    if file_hash(mesh_path)!=mesh_receipt['sha256']:raise ValueError('Immutable target mesh changed')
    skeletons=dict(v4=mesh_binding_skeleton(v4,mesh_path),refactored=mesh_binding_skeleton(ref,mesh_path));readbacks=[]
    manifest=read_json(Path(pack)/'v4/als_manifest.json');anims={a['objectPath']:a for a in manifest['animations']}
    for job in jobs:
        folder=Path(pack)/'root-ownership'/hashlib.sha256(job['source'].encode()).hexdigest()
        runtime=read_json(folder/'runtime.raw.json');skeleton=skeletons[job['edition']]
        fbx=Path(pack)/'v4'/anims[job['source']]['outputPath'] if job['edition']=='v4' else folder/'runtime.fbx'
        write_fbx(fbx,runtime,skeleton);report=readback(fbx,runtime,skeleton)
        report.update(source=job['source'],fbx=str(fbx),sha256=file_hash(fbx),rawSha256=file_hash(folder/'runtime.raw.json'),
            targetMesh=str(mesh_path),targetMeshSha256=file_hash(mesh_path),boneNamePolicy='immutable-target-mesh-exact-exported-names')
        write_json(folder/'fbx-readback.json',report);readbacks.append(report)
        write_json(output/'final-fbx-progress.json',dict(completed=len(readbacks),total=len(jobs),
            failed=[r['source'] for r in readbacks if not r['passed']]))
    # The manifest and release byte hashes describe the FINAL factored FBX.
    for entry in manifest['files']:
        p=Path(pack)/'v4'/entry['relativePath'];entry.update(sha256=file_hash(p),size=p.stat().st_size)
    for a in manifest['animations']:
        if a['objectPath'] in {j['source'] for j in jobs}:a['kimodoSource']['fbxSha256']=file_hash(Path(pack)/'v4'/a['outputPath'])
    write_json(Path(pack)/'v4/als_manifest.json',manifest)
    indexed={entry['path'] for entry in staged['files']}
    for p in (Path(pack)/'root-ownership').rglob('*'):
        if not p.is_file():continue
        uri='res://'+p.relative_to(ROOT).as_posix()
        if uri not in indexed:staged['files'].append(dict(path=uri,sha256=file_hash(p)));indexed.add(uri)
    for entry in staged['files']:entry['sha256']=file_hash(ROOT/entry['path'].removeprefix('res://'))
    staged.update(finalFbxStatus='offline-ufbx-readback-verified' if all(r['passed'] for r in readbacks) else 'offline-readback-failed',
        offlineFbxReadback=dict(policy='ufbx-all-physical-keys-v1',assets=len(readbacks),passed=all(r['passed'] for r in readbacks)))
    write_json(Path(pack)/'resource-pack.json',staged)
    from .viewer_pack import export_pack
    viewer=export_pack(Path(pack)/'resource-pack.json',make_latest=True,batch_id=batch_id)
    result.update(status='conversion_complete_quality_pending',pack=str(Path(pack)/'resource-pack.json'),
        converted=len(jobs),sourceClosureComplete=staged['sourceClosureComplete'],
        missing=len(staged['missingNonPropSources']),finalFbxReadback=staged['offlineFbxReadback'],
        failedTargetGaits=[c for c in staged['targetGaitChecks'] if not c['passed']],viewer=viewer,
        rootMotionCurves=len(staged['rootMotionCurves']),extractedRootMotionCurves=len(staged['extractedRootMotionCurves']))
    write_json(output/'pipeline-result.json',result)
    return result


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    for key in ('authoring','receipts','output','pack','kimodo-repo','deps'):
        parser.add_argument('--'+key,type=Path,required=True)
    parser.add_argument('--batch-id')
    parser.add_argument('--resume-conversion',action='store_true',help='Verify saved retarget payloads, then stage a fresh pack')
    parser.add_argument('--finish-existing-pack',action='store_true',help='Resume only unactivated diagnostic final-export metadata')
    args=parser.parse_args();sys.path.insert(0,str(args.kimodo_repo.resolve()));sys.path.insert(0,str(args.deps.resolve()))
    print(json.dumps(convert_bundle(args.authoring,args.receipts,args.output,args.pack,batch_id=args.batch_id,resume=args.resume_conversion,finish_existing=args.finish_existing_pack),ensure_ascii=False))


if __name__=='__main__':main()
