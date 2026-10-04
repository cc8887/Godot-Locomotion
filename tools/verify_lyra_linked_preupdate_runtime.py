"""Audit the three game-thread cached reads and original animation regressions."""
import json
from pathlib import Path
from verify_lyra_multi_layer_native import ASSETS, EVIDENCE, PROJECT, ROOT, package_file, read, sha
from verify_lyra_multi_owner_runtime import text


def main():
    tag='linked-private-v1'
    output=EVIDENCE/f'{tag}-integrity.json'
    assert not output.exists(), 'Preserve evidence.'
    evidence={}
    def load(path):
        evidence[str(path.relative_to(ROOT))]=sha(path)
        return read(path)
    def log(path):
        evidence[str(path.relative_to(ROOT))]=sha(path)
        value=text(path)
        assert not any(line.lstrip().startswith(('ERROR:','WARNING:')) for line in value.splitlines()), path
        return value

    frozen=load(EVIDENCE/f'{tag}-frozen-sources.json')
    for name,digest in frozen.items():assert sha(ROOT/name)==digest,name
    fresh='linked-private-v1-30-full'
    closure=load(EVIDENCE/f'whole-main-{fresh}-closure.json')
    for name,digest in closure['previousFixtureSha256'].items():assert sha(ASSETS/name)==digest,name
    for name,digest in closure['assetSha256'].items():assert sha(package_file(name))==digest,name
    for name,digest in closure['protectedProject'].items():assert sha(PROJECT/name)==digest,name
    for name,digest in closure['probeSourceSha256'].items():
        assert sha(ROOT/'tools/unreal/LyraWholeMainOracle'/name)==digest,name
        assert sha(ROOT/'artifacts/unreal/lyra-whole-main-oracle/package-multi-layer-v1'/name)==digest,name
    for name,digest in closure['captureSourceSha256'].items():assert sha(ROOT/name)==digest,name
    provenance=load(EVIDENCE/f'whole-main-{fresh}-linked-input-provenance.json')
    assert sha(ROOT/'tools/unreal/capture_lyra_whole_main.py')==provenance['originalCaptureSha256']
    assert sha(ROOT/'tools/unreal/capture_lyra_linked_private.py')==provenance['wrapperSha256']
    assert sha(ROOT/'scripts/capture-lyra-linked-private.ps1')==provenance['runnerSha256']
    assert provenance['authoredInputChangesOnly'] and not provenance['probeChanged']
    assert len(provenance['coverage'])==12
    for row in provenance['coverage']:
        assert row['frames']==360 and all(row[k]>0 for k in (
            'firingFrames','adsFrames','crouchFrames','hiddenBodyFrames','bodyResumeTransitions','montageCacheTrueFrames'))
    native_log=text(EVIDENCE/f'whole-main-native-{fresh}.log')
    assert 'WHOLE_MAIN_PROCESS_EXIT=0' in native_log and 'LYRA_WHOLE_MAIN_CAPTURE_OK traces=12 frames=4320 assets_saved=0' in native_log
    evidence[f'artifacts/lyra-analysis/whole-main-native-{fresh}.log']=sha(EVIDENCE/f'whole-main-native-{fresh}.log')
    refs={30:'multi-layer-v3-30-full',60:'multi-layer-v3-60-repeat-full',120:'multi-layer-v3-120-full'}
    for reference in (*refs.values(),fresh):
        manifest=load(EVIDENCE/f'whole-main-{reference}-native.json')
        load(EVIDENCE/f'whole-main-{reference}-request.json')
        for entry in manifest['traceFiles']:
            assert Path(entry['file']).name==entry['file']
            assert sha(EVIDENCE/entry['file'])==entry['sha256']
    for name in ('build-debug','build-export-release'):
        value=log(EVIDENCE/f'{tag}-{name}.log')
        assert '0 个警告' in value and '0 个错误' in value
    processes=frames=worker_comparisons=preupdate_comparisons=restores=0
    counts={'single':1,'three-groups':3,'mixed':4,'per-call':14}
    results=[]
    for config,directory in (('debug','Debug'),('optimize','ExportRelease')):
        ordinary=load(EVIDENCE/f'{tag}-ordinary-{config}-verification.json')
        assert ordinary['passed'] and len(ordinary['runs'])==11
        for name,digest in ordinary['assemblies'].items():
            assert sha(ROOT/'.godot/mono/temp/bin'/directory/name)==digest.lower(),name
        reports=[]
        for hz,reference in refs.items():
            report=load(EVIDENCE/f'{tag}-matrix-{hz}-{config}-verification.json')
            assert report['runTag']==reference
            assert {(r['layout'],r['boundary']) for r in report['runs']}=={(layout,'pre-rig') for layout in counts}
            reports.append(report)
        report=load(EVIDENCE/f'{tag}-authored-30-{config}-verification.json')
        assert report['runTag']==fresh
        assert {(r['layout'],r['boundary']) for r in report['runs']}=={(layout,b) for layout in counts for b in ('pre-rig','final')}
        reports.append(report)
        for report in reports:
            assert report['passed'] and report['workerFields'] and report['workerFieldCount']==8
            assert report['preUpdateFields'] and report['preUpdateFieldCount']==3
            assert report['assemblies']==ordinary['assemblies']
            assert not report['fullPrivateFieldParity'] and not report['goalComplete']
            if config=='optimize':assert report['debugRestored']
            for run in report['runs']:
                assert run['passed'] and run['exitCode']==0
                path=Path(run['log']);assert sha(path)==run['logSha256'].lower()
                value=log(path);n=int(run['frames']);base=n*counts[run['layout']]*5
                assert f'LYRA_LINKED_WORKER_FIELDS_OK layout={run["layout"]} frames={n} fields=8 comparisons={base*8}' in value
                assert f'LYRA_LINKED_PREUPDATE_FIELDS_OK layout={run["layout"]} frames={n} fields=3 comparisons={base*3}' in value
                assert f'retry={n} controlledPhysicalInputs=true' in value and 'MULTI_OWNER_PROCESS_EXIT=0' in value
                processes+=1;frames+=n;worker_comparisons+=base*8;preupdate_comparisons+=base*3
        prior=load(EVIDENCE/f'linked-worker-v1-ordinary-{config}-verification.json')
        for run,old in zip(ordinary['runs'],prior['runs'],strict=True):
            assert run['passed'] and run['exitCode']==0 and run['name']==old['name']
            path=Path(run['log']);assert sha(path)==run['logSha256'].lower();log(path)
            if 'report' in run:assert load(Path(run['report']))==load(Path(old['report'])),run['name']
            processes+=1
        components=load(EVIDENCE/f'{tag}-components-{config}-verification.json')
        assert components['passed'] and len(components['runs'])==1
        run=components['runs'][0];assert run['name']=='per-call-ten' and run['passed'] and run['exitCode']==0
        assert 'WORKER_PROCESS_EXIT=0' in log(ROOT/run['log']);processes+=1
        ten=load(EVIDENCE/f'{tag}-components-{config}-per-call-ten.json')
        assert ten==load(EVIDENCE/f'linked-worker-v1-components-{config}-per-call-ten.json')
        results.append((ordinary,ten))
    for a,b in zip(results[0][0]['runs'],results[1][0]['runs'],strict=True):
        if 'report' in a:assert read(Path(a['report']))==read(Path(b['report'])),a['name']
    assert results[0][1]==results[1][1]
    debug=results[0][0]['assemblies']
    backups=[f'{tag}-matrix-{hz}-optimize-debug-backup' for hz in refs]
    backups += [f'{tag}-authored-30-optimize-debug-backup',f'{tag}-ordinary-optimize-debug-backup',f'{tag}-components-optimize-debug-backup']
    for backup in backups:
        for name,digest in debug.items():assert sha(EVIDENCE/backup/name)==digest.lower(),(backup,name)
        restores+=1
    assert processes==64 and frames==77760 and preupdate_comparisons==6415200 and worker_comparisons==17107200
    baseline=load(EVIDENCE/'linked-worker-v1-integrity.json')
    math_digest=sha(ASSETS/'native_win64/LyraNativeMath.dll')
    assert math_digest==read(EVIDENCE/'multi-owner-v4-integrity.json')['nativeMathSha256']
    inventory=load(EVIDENCE/f'{tag}-private-fields.json')
    assert inventory['auditPassed'] and all(len(t['fields'])==47 for t in inventory['trajectories'])
    fresh_inventory=load(EVIDENCE/f'{tag}-authored-private-fields.json')
    assert fresh_inventory['auditPassed'] and all(len(t['fields'])==47 and t['authoredFiringFrames']>0 for t in fresh_inventory['trajectories'])
    result=dict(auditPassed=True,finalGodotProcesses=processes,nativeSubmittedFrames=frames,
        preUpdateFields=3,preUpdateFieldComparisons=preupdate_comparisons,workerFields=8,workerFieldComparisons=worker_comparisons,
        nativeAuthoredTraces=12,nativeAuthoredFrames=4320,nativeCoverage=provenance['coverage'],
        defaultReportsUnchanged=True,perCallTenReportsUnchanged=True,assemblyRestoreRounds=restores,
        unchangedLyraJson=len(closure['previousFixtureSha256']),unchangedOriginalPackages=len(closure['assetSha256']),
        unchangedProjectConfiguration=len(closure['protectedProject']),nativeMathSha256=math_digest,
        previousStageAudit=baseline['auditPassed'],fullPrivateFieldParity=False,fullPhysicalParity=False,
        newUeCapture=True,goalComplete=False,sources=frozen,evidence=evidence,
        verifierSha256=sha(Path(__file__)),nativeWarnings=native_log.count(': Warning:'))
    with output.open('x',encoding='utf-8',newline='\n') as stream:
        json.dump(result,stream,ensure_ascii=False,indent=2,allow_nan=False);stream.write('\n')
    print(output)


if __name__=='__main__':main()
