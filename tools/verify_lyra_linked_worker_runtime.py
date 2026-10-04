"""Audit the eight-field linked worker subset and its runtime evidence."""
import json
from pathlib import Path
from verify_lyra_multi_layer_native import ASSETS, EVIDENCE, PROJECT, ROOT, package_file, read, sha
from verify_lyra_multi_owner_runtime import text


def main():
    tag='linked-worker-v1'
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
    for name,digest in frozen.items():
        if name.endswith('/LyraWholeMainDiagnosticSmoke.cs'):
            tested=EVIDENCE/f'{tag}-tested-diagnostic.cs'
            assert sha(tested)==digest.lower(),name
            expected=tested.read_text(encoding='utf-8').replace(
                "// UE's JSON writer serializes both signed zeros as 0.",
                '// Compare signed zeros numerically; all nonzero values require identical bits.')
            assert (ROOT/name).read_text(encoding='utf-8')==expected,name
            evidence[str(tested.relative_to(ROOT))]=sha(tested)
        else:assert sha(ROOT/name)==digest.lower(),name
    closure=load(EVIDENCE/'whole-main-multi-layer-v3-30-full-closure.json')
    for name,digest in closure['previousFixtureSha256'].items():assert sha(ASSETS/name)==digest,name
    for name,digest in closure['assetSha256'].items():assert sha(package_file(name))==digest,name
    for name,digest in closure['protectedProject'].items():assert sha(PROJECT/name)==digest,name
    for name,digest in closure['probeSourceSha256'].items():
        assert sha(ROOT/'tools/unreal/LyraWholeMainOracle'/name)==digest,name
        assert sha(ROOT/'artifacts/unreal/lyra-whole-main-oracle/package-multi-layer-v1'/name)==digest,name
    refs={30:'multi-layer-v3-30-full',60:'multi-layer-v3-60-repeat-full',120:'multi-layer-v3-120-full'}
    for reference in refs.values():
        manifest=load(EVIDENCE/f'whole-main-{reference}-native.json')
        load(EVIDENCE/f'whole-main-{reference}-request.json')
        for entry in manifest['traceFiles']:
            assert Path(entry['file']).name==entry['file']
            assert sha(EVIDENCE/entry['file'])==entry['sha256']
    for name in ('signed-zero-build-debug','build-export-release'):
        value=log(EVIDENCE/f'{tag}-{name}.log')
        assert '0 个警告' in value and '0 个错误' in value
    summaries=[];processes=frames=comparisons=restores=0
    counts={'single':1,'three-groups':3,'mixed':4,'per-call':14}
    for config,directory in (('debug','Debug'),('optimize','ExportRelease')):
        ordinary=load(EVIDENCE/f'{tag}-ordinary-{config}-verification.json')
        assert ordinary['passed'] and len(ordinary['runs'])==11
        for name,digest in ordinary['assemblies'].items():
            assert sha(ROOT/'.godot/mono/temp/bin'/directory/name)==digest.lower(),name
        reports=[]
        for hz,reference in refs.items():
            report=load(EVIDENCE/f'{tag}-matrix-{hz}-{config}-verification.json')
            assert report['passed'] and report['workerFields'] and report['workerFieldCount']==8
            assert report['runTag']==reference and report['assemblies']==ordinary['assemblies']
            assert {(r['layout'],r['boundary']) for r in report['runs']}=={(layout,'pre-rig') for layout in counts}
            reports.append(report)
        report=load(EVIDENCE/f'{tag}-final-30-{config}-verification.json')
        assert report['passed'] and report['workerFields'] and report['assemblies']==ordinary['assemblies']
        assert {(r['layout'],r['boundary']) for r in report['runs']}=={('single','final'),('per-call','final')}
        reports.append(report)
        for report in reports:
            assert not report['fullPrivateFieldParity'] and not report['goalComplete']
            if config=='optimize':assert report['debugRestored']
            for run in report['runs']:
                assert run['passed'] and run['exitCode']==0
                path=Path(run['log']);assert sha(path)==run['logSha256'].lower()
                value=log(path);n=int(run['frames']);fields=n*counts[run['layout']]*8*5
                assert f'LYRA_LINKED_WORKER_FIELDS_OK layout={run["layout"]} frames={n} fields=8 comparisons={fields}' in value
                assert f'retry={n} controlledPhysicalInputs=true' in value
                assert 'MULTI_OWNER_PROCESS_EXIT=0' in value
                processes+=1;frames+=n;comparisons+=fields
        prior=load(EVIDENCE/f'multi-owner-v4-{config}-verification.json')
        for run,old in zip(ordinary['runs'],prior['runs'],strict=True):
            assert run['passed'] and run['exitCode']==0 and run['name']==old['name']
            path=Path(run['log']);assert sha(path)==run['logSha256'].lower();log(path)
            if 'report' in run:assert load(Path(run['report']))==load(Path(old['report'])),run['name']
            processes+=1
        components=load(EVIDENCE/f'{tag}-components-{config}-verification.json')
        assert components['passed'] and len(components['runs'])==4
        for run in components['runs']:
            assert run['passed'] and run['exitCode']==0
            assert 'WORKER_PROCESS_EXIT=0' in log(ROOT/run['log'])
            processes+=1
        ten=load(EVIDENCE/f'{tag}-components-{config}-per-call-ten.json')
        summaries.append((ordinary,ten))
    for a,b in zip(summaries[0][0]['runs'],summaries[1][0]['runs'],strict=True):
        if 'report' in a:assert read(Path(a['report']))==read(Path(b['report'])),a['name']
    assert summaries[0][1]==summaries[1][1], 'Per-call ten-character reports differ.'
    debug=summaries[0][0]['assemblies']
    backups=[f'{tag}-matrix-{hz}-optimize-debug-backup' for hz in refs]
    backups += [f'{tag}-final-30-optimize-debug-backup',f'{tag}-ordinary-optimize-debug-backup',f'{tag}-components-optimize-debug-backup']
    for backup in backups:
        for name,digest in debug.items():assert sha(EVIDENCE/backup/name)==digest.lower(),(backup,name)
        restores+=1
    assert processes==58 and frames==64800 and comparisons==14601600
    baseline=load(EVIDENCE/'multi-owner-v4-integrity.json')
    assert sha(ASSETS/'native_win64/LyraNativeMath.dll')==baseline['nativeMathSha256']
    evidence[f'artifacts/lyra-analysis/{tag}-run.ps1']=sha(EVIDENCE/f'{tag}-run.ps1')
    result=dict(auditPassed=True,finalGodotProcesses=processes,nativeSubmittedFrames=frames,
                workerFieldComparisons=comparisons,workerFields=8,defaultReportsUnchanged=True,
                perCallTenReportsEqual=True,assemblyRestoreRounds=restores,
                unchangedLyraJson=len(closure['previousFixtureSha256']),
                unchangedOriginalPackages=len(closure['assetSha256']),
                unchangedProjectConfiguration=len(closure['protectedProject']),
                fullPrivateFieldParity=False,fullPhysicalParity=False,newUeCapture=False,goalComplete=False,
                sources={name:sha(ROOT/name) for name in frozen},evidence=evidence,
                verifierSha256=sha(Path(__file__)),diagnosticCommentOnlyCorrection=True)
    with output.open('x',encoding='utf-8',newline='\n') as stream:
        json.dump(result,stream,ensure_ascii=False,indent=2,allow_nan=False);stream.write('\n')
    print(output)


if __name__=='__main__':main()
