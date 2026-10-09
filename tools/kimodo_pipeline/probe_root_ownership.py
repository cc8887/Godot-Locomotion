"""Prepare a read-only active Walk/Roll probe and native FBX round-trip request.

Creates fresh derived outputs; does not modify or activate a resource pack.
"""
import argparse
import shutil
from pathlib import Path
from .graph import read_json
from .publish import ROOT, CONFIG, file_hash, write_json, world_pose
from . import root_ownership, root_curves


def prepare(output, viewer_output, version):
    output=output.resolve();viewer_output=viewer_output.resolve()
    if output.exists() or viewer_output.exists():raise ValueError('Use fresh evidence and viewer directories')
    if not viewer_output.is_relative_to(ROOT/'assets/generated/kimodo_ue/root_ownership'):
        raise ValueError('Viewer probe must stay in its dedicated generated subtree')
    pointer=CONFIG/'als_resource_pack.json';pack_path=ROOT/read_json(pointer)['pack'].removeprefix('res://')
    pack=read_json(pack_path);native=read_json(pack['nativeRetargetBundle'])
    selected=[j for j in native['jobs'] if j['edition']=='v4' and
              any(name+'.' in j['source'] for name in ('ALS_N_Walk_F','ALS_N_LandRoll_F'))]
    if len(selected)!=2:raise ValueError('Expected one actual Walk and one actual Roll')
    skel=read_json(CONFIG/'v4_recovery_movement_source_inputs.json')['skeletons'][0]
    raw_paths={}
    for index in (pack_path.parent/'raw').glob('v4_*source_inputs.json'):
        for row in read_json(index)['assets']:raw_paths[row['source']]=index.parent/row['file']
    manifest=read_json(pack_path.parent/'v4/als_manifest.json')
    assets={a['objectPath']:a for a in manifest['animations']}
    input_paths=[pointer,pack_path,Path(pack['nativeRetargetBundle']),CONFIG/'v4_recovery_movement_source_inputs.json']
    output.mkdir(parents=True);viewer_output.mkdir(parents=True)
    jobs=[];reports=[]
    for j in selected:
        source=j['source'];raw_path=raw_paths[source];raw=read_json(raw_path)
        input_paths.append(raw_path)
        authored,runtime,report=root_ownership.decompose(raw,skel,family=j['contract']['family'],
            declared_root_motion=j['contract']['originalEvaluation']['enableRootMotion'])
        report.update(source=source,inputFile=str(raw_path),inputFileSha256=file_hash(raw_path))
        asset=j['contract']['asset'];folder=output/asset;folder.mkdir()
        write_json(folder/'ownership.json',report)
        write_json(folder/'before.raw.json',raw)
        write_json(folder/'authored-root.raw.json',authored)
        write_json(folder/'runtime.raw.json',runtime)
        curve=root_curves.export(folder,authored,j)
        report['extractedRootCurve']=curve
        before=world_pose(raw,skel);after=world_pose(runtime,skel)
        report['firstFrameInputPelvisCm']=before['pelvis'][1][0].tolist()
        report['firstFrameRuntimePelvisCm']=after['pelvis'][1][0].tolist()
        report['inputBoneCount']=len(raw['tracks'])
        original=ROOT/'assets/generated/als_v4'/assets[source]['outputPath']
        active_fbx=pack_path.parent/'v4'/assets[source]['outputPath']
        input_paths.extend((original,active_fbx))
        shutil.copy2(active_fbx,viewer_output/(asset+'_before.fbx'))
        shutil.copy2(original,viewer_output/(asset+'_original.fbx'))
        variants=[('inplace',runtime)] if j['contract']['family']=='gait_cycle' else [
            ('authored',authored),('inplace',root_ownership.lock_planar_root(authored,skel))]
        for label,value in variants:
            raw_target=folder/(label+'.raw.json');write_json(raw_target,value)
            jobs.append(dict(source=source+'#'+label,sourceAsset=j['retargetedAssetPath'],
                raw=str(raw_target),rawSha256=file_hash(raw_target),fbx=str(viewer_output/(asset+'_'+label+'.fbx'))))
        write_json(folder/'ownership.json',report);reports.append(report)
    inputs=[dict(file=str(p),sha256=file_hash(p)) for p in dict.fromkeys(input_paths)]
    request=output/'native-fbx-request.json'
    write_json(request,dict(schemaVersion=1,version=version,jobs=jobs))
    write_json(output/'probe.json',dict(schemaVersion=1,policy=root_ownership.POLICY,activePack=str(pack_path),
        activePackUnchanged=True,inputs=inputs,reports=reports,nativeRequest=str(request),nativeRequestSha256=file_hash(request)))
    return request


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',required=True,type=Path)
    parser.add_argument('--viewer-output',required=True,type=Path)
    parser.add_argument('--version',required=True)
    args=parser.parse_args();print(prepare(args.output,args.viewer_output,args.version))


if __name__=='__main__':main()
