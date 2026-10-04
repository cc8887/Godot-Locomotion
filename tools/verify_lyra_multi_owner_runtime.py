"""Audit multi-owner graph output and production regression evidence."""
import argparse
import json
from pathlib import Path
from verify_lyra_multi_layer_native import ASSETS,EVIDENCE,PROJECT,ROOT,package_file,read,sha


def text(path):
    raw=path.read_bytes()
    return raw.decode('utf-16' if raw.startswith((b'\xff\xfe',b'\xfe\xff')) else 'utf-8-sig')


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--tag',required=True)
    parser.add_argument('--include-ordinary-layouts',action='store_true')
    args=parser.parse_args();tag=args.tag
    assert tag.replace('-','').replace('_','').isalnum()
    output=EVIDENCE/f'{tag}-integrity.json';assert not output.exists()
    evidence={};sources={};processes=0;frames=0;relinks=0;matrices=[];summaries=[]

    def load(path):
        evidence[str(path.relative_to(ROOT))]=sha(path);return read(path)

    def log(path):
        evidence[str(path.relative_to(ROOT))]=sha(path)
        value=text(path)
        assert not any(l.lstrip().startswith(('ERROR:','WARNING:')) for l in value.splitlines()),path
        return value

    closure=load(EVIDENCE/'whole-main-multi-layer-v3-30-full-closure.json')
    for name,digest in closure['previousFixtureSha256'].items():assert sha(ASSETS/name)==digest,name
    for name,digest in closure['assetSha256'].items():assert sha(package_file(name))==digest,name
    for name,digest in closure['protectedProject'].items():assert sha(PROJECT/name)==digest,name
    for name,digest in closure['probeSourceSha256'].items():
        assert sha(ROOT/'tools/unreal/LyraWholeMainOracle'/name)==digest,name
        assert sha(ROOT/'artifacts/unreal/lyra-whole-main-oracle/package-multi-layer-v1'/name)==digest,name
    for name,digest in closure['captureSourceSha256'].items():assert sha(ROOT/name)==digest,name
    references={30:'multi-layer-v3-30-full',60:'multi-layer-v3-60-repeat-full',120:'multi-layer-v3-120-full'}
    for reference in references.values():
        manifest=load(EVIDENCE/f'whole-main-{reference}-native.json')
        load(EVIDENCE/f'whole-main-{reference}-request.json')
        for entry in manifest['traceFiles']:
            assert Path(entry['file']).name==entry['file']
            assert sha(EVIDENCE/entry['file'])==entry['sha256']

    for config,directory,build in [('debug','Debug','debug'),('optimize','ExportRelease','export')]:
        build_log=log(EVIDENCE/f'{tag}-build-{build}.log')
        assert '0 个警告' in build_log and '0 个错误' in build_log
        summary=load(EVIDENCE/f'{tag}-{config}-verification.json')
        prior=load(EVIDENCE/f'main-state-owner-v2-{config}-verification.json')
        assert summary['passed'] and len(summary['runs'])==11
        for name,digest in summary['assemblies'].items():
            assert sha(ROOT/'.godot/mono/temp/bin'/directory/name)==digest.lower(),name
        if config=='optimize':
            assert summary['debugRestored']
            for name,digest in summaries[0]['assemblies'].items():
                assert sha(EVIDENCE/f'{tag}-optimize-debug-backup'/name)==digest.lower()
        for run,old in zip(summary['runs'],prior['runs'],strict=True):
            assert run['name']==old['name'] and run['passed'] and run['exitCode']==0
            path=Path(run['log']);assert sha(path)==run['logSha256'].lower();log(path)
            if 'report' in run:
                path=Path(run['report']);assert sha(path)==run['reportSha256'].lower()
                assert load(path)==load(Path(old['report'])),run['name']
            processes+=1
        summaries.append(summary)
        for hz,reference in references.items():
            report=load(EVIDENCE/f'{tag}-{hz}-{config}-verification.json')
            assert report['passed'] and report['assemblies']==summary['assemblies'] and len(report['runs'])==12
            assert report['runTag']==reference and not report['fullPrivateFieldParity'] and not report['goalComplete']
            assert {(r['layout'],r['boundary']) for r in report['runs']}=={
                (layout,boundary) for layout in ('single','three-groups','mixed','per-call')
                for boundary in ('pre-inertia','pre-rig','final')}
            if config=='optimize':
                assert report['debugRestored']
                for name,digest in summaries[0]['assemblies'].items():
                    assert sha(EVIDENCE/f'{tag}-{hz}-optimize-debug-backup'/name)==digest.lower()
            for run in report['runs']:
                expected_frames=hz*36
                assert run['passed'] and run['exitCode']==0 and run['frames']==expected_frames
                path=Path(run['log']);assert sha(path)==run['logSha256'].lower();value=log(path)
                assert f'LYRA_MULTI_OWNER_MAIN_GRAPH_OK layout={run["layout"]} frames={expected_frames} routedCalls=42 relinks={run["relinks"]} fullPrivateFieldParity=false' in value
                assert f'retry={expected_frames} controlledPhysicalInputs=true' in value
                assert f'mainStateFrames={expected_frames}' in value and 'foreignMainRejected=3' in value
                processes+=1;frames+=expected_frames;relinks+=run['relinks']
            matrices.append({'configuration':config,'hz':hz,'processes':12})
        for hz,reference in [(30,'rebind30-final'),(60,'rebind60-first'),(120,'rebind120-final')]:
            report=load(EVIDENCE/f'whole-main-{config}-{tag}-rebind-{hz}-verification.json')
            assert report['comparisonPassed'] and report['retry'] and report['runTag']==reference
            assert sha(ASSETS/'native_win64/LyraNativeMath.dll')==report['nativeMathSha256'].lower()
            assert report['assemblies']==summary['assemblies'] and len(report['runs'])==3
            if config=='optimize':
                assert report['debugRestored']
                for name,digest in summaries[0]['assemblies'].items():
                    assert sha(EVIDENCE/f'whole-main-optimize-{tag}-rebind-{hz}-debug-backup'/name)==digest.lower()
            for run in report['runs']:
                assert run['comparisonPassed'] and run['exitCode']==0
                value=log(Path(run['log']))
                assert 'rebinds=24 sameClass=24 actionRebinds=21 hiddenRebinds=6 rejectedRebinds=48' in value
                assert f'retry={hz*36} controlledPhysicalInputs=true' in value
                processes+=1

    if args.include_ordinary_layouts:
        for layout in ('three-groups','mixed','per-call'):
            layouts=[]
            for config in ('debug','optimize'):
                report=load(EVIDENCE/f'{tag}-{layout}-{config}-verification.json')
                assert report['passed'] and report['layerLayout']==layout and len(report['runs'])==11
                assert report['assemblies']==summaries[0 if config=='debug' else 1]['assemblies']
                if config=='optimize':
                    assert report['debugRestored']
                    for name,digest in summaries[0]['assemblies'].items():
                        assert sha(EVIDENCE/f'{tag}-{layout}-optimize-debug-backup'/name)==digest.lower()
                for run in report['runs']:
                    assert run['passed'] and run['exitCode']==0
                    path=Path(run['log']);assert sha(path)==run['logSha256'].lower();log(path)
                    if 'report' in run:
                        path=Path(run['report']);assert sha(path)==run['reportSha256'].lower();load(path)
                    processes+=1
                layouts.append(report)
            for debug,release in zip(layouts[0]['runs'],layouts[1]['runs'],strict=True):
                assert debug['name']==release['name']
                if 'report' in debug:assert read(Path(debug['report']))==read(Path(release['report'])),(layout,debug['name'])

    scope=load(EVIDENCE/f'{tag}-scope-verification.json')
    assert scope['passed'] and scope['debugRestored'] and len(scope['runs'])==2
    load_source=EVIDENCE/f'{tag}-scope-run.ps1'
    evidence[str(load_source.relative_to(ROOT))]=sha(load_source)
    for run,summary in zip(scope['runs'],summaries,strict=True):
        assert run['passed'] and run['exitCode']==0 and run['assemblies']==summary['assemblies']
        assert run['configuration'].lower()==summary['configuration'].lower()
        path=Path(run['log']);assert sha(path)==run['logSha256'].lower();value=log(path)
        assert 'LYRA_LOCOMOTION_SOURCE_SCOPE_JOINT_OK frames=3780' in value
        assert 'LYRA_MAIN_IDLE_SCOPE_FEEDBACK_OK' in value
        processes+=1
    for name,digest in summaries[0]['assemblies'].items():
        assert sha(EVIDENCE/f'{tag}-scope-debug-backup'/name)==digest.lower()

    worker=load(EVIDENCE/f'{tag}-worker-boundary.json')
    assert worker['auditPassed'] and worker['allInstancesWorkerUpdateRejected'] and not worker['fullPrivateFieldParity']
    assert worker['verifierSha256']==sha(ROOT/'tools/inspect_lyra_linked_worker_updates.py')
    for name,digest in worker['engineSources'].items():assert sha(Path(name))==digest,name
    files=['LyraLinkedLayerGraphSet.cs','LyraLinkedLayerContracts.cs','LyraMainSourceScope.cs','LyraLocomotionSourceScope.cs',
           'LyraMainGroundResources.cs','LyraLocomotionResources.cs','LyraItemLayerGraphInstance.cs','LyraMainLocomotionHost.cs',
           'LyraMainPoseHost.cs','LyraWholeMainDiagnosticSmoke.cs','LyraLinkedCurveFeedback.cs']
    for name in files:sources[f'src/Als.Godot/Animation/Lyra/{name}']=sha(ROOT/'src/Als.Godot/Animation/Lyra'/name)
    for name in ['src/Als.Godot/Locomotion/LyraLocomotionDemo.cs','scripts/verify-lyra-linked-layer-bindings.ps1',
                 'scripts/verify-lyra-multi-owner-graphs.ps1','tools/inspect_lyra_linked_worker_updates.py','tools/verify_lyra_multi_owner_runtime.py']:
        sources[name]=sha(ROOT/name)
    prior_integrity=load(EVIDENCE/'main-state-owner-v2-integrity.json')
    main_owner='src/Als.Godot/Animation/Lyra/LyraMainGraphStateOwner.cs'
    assert sha(ROOT/main_owner)==prior_integrity['sources'][main_owner]
    sources[main_owner]=sha(ROOT/main_owner)
    assert frames==181440 and relinks==4896 and processes==(180 if args.include_ordinary_layouts else 114)
    result=dict(auditPassed=True,finalGodotProcesses=processes,multiOwnerSubmittedFrames=frames,multiOwnerSameClassRelinks=relinks,
                matrices=matrices,ordinaryLayoutsVerified=args.include_ordinary_layouts,defaultReportsUnchanged=True,
                unchangedLyraJson=len(closure['previousFixtureSha256']),unchangedOriginalPackages=len(closure['assetSha256']),
                unchangedProjectConfiguration=len(closure['protectedProject']),
                assemblyRestoreRounds=11 if args.include_ordinary_layouts else 8,
                nativeMathSha256=sha(ASSETS/'native_win64/LyraNativeMath.dll'),fullPrivateFieldParity=False,
                fullPhysicalParity=False,newUeCapture=False,goalComplete=False,sources=sources,evidence=evidence)
    with output.open('x',encoding='utf-8',newline='\n') as stream:
        json.dump(result,stream,ensure_ascii=False,indent=2,allow_nan=False);stream.write('\n')
    print(output)


if __name__=='__main__':main()
