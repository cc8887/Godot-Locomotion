"""Audit native request/weight callbacks, rollback and committed regressions."""
import collections
import json
import xml.etree.ElementTree as ET
from pathlib import Path
from verify_lyra_multi_layer_native import ROOT, EVIDENCE, ASSETS, PROJECT, read, sha, package_file


def main():
    tag='immediate-callbacks-v1'
    target=EVIDENCE/f'{tag}-integrity.json'
    assert not target.exists(), 'Preserve audit.'
    evidence={}

    def load(path):
        evidence[str(path.relative_to(ROOT))]=sha(path)
        return read(path)

    def log(path,native=False):
        evidence[str(path.relative_to(ROOT))]=sha(path)
        text=path.read_text(encoding='utf-8-sig')
        if native:
            assert 'LYRA_MONTAGE_IMMEDIATE_NATIVE_OK traces=27 frames=5670 assets_saved=0' in text
            assert 'MONTAGE_DELEGATES_PROCESS_EXIT=0' in text
            assert not any(x in text for x in ('Error:', 'Fatal error:', 'Ensure condition failed'))
        else:
            assert not any(line.lstrip().startswith(('ERROR:', 'WARNING:')) for line in text.splitlines()),path
            assert 'MONTAGE_DELEGATES_GODOT_EXIT=0' in text or 'MULTI_OWNER_PROCESS_EXIT=0' in text
        return text

    sources=load(EVIDENCE/f'{tag}-frozen-sources-v2.json')
    for name,digest in sources.items():assert sha(ROOT/name)==digest,name
    native=load(EVIDENCE/f'{tag}-native.json')
    repeat=load(EVIDENCE/f'{tag}-repeat-native.json')
    request=load(EVIDENCE/f'{tag}-requests.json')
    assert native==repeat and sha(EVIDENCE/f'{tag}-native.json')==sha(EVIDENCE/f'{tag}-repeat-native.json')
    assert request==load(EVIDENCE/f'{tag}-repeat-requests.json')
    assert native['requestSha256']==sha(EVIDENCE/f'{tag}-requests.json')
    assert native['catalogSha256']==sha(ASSETS/'montage_catalog_v2.json')
    for suffix in ('','-repeat'):log(EVIDENCE/f'{tag}{suffix}-native.log',True)
    assert len(native['previousFixtureSha256'])==869
    assert len(native['assetSha256'])==710 and len(native['protectedProject'])==9
    for name,digest in native['previousFixtureSha256'].items():assert sha(ASSETS/name)==digest,name
    for name,digest in native['assetSha256'].items():assert sha(package_file(name))==digest,name
    for name,digest in native['protectedProject'].items():assert sha(PROJECT/name)==digest,name
    package=ROOT/'artifacts/unreal/lyra-whole-main-oracle/package-immediate-callbacks-v1'
    for name,digest in native['probeSourceSha256'].items():
        assert sha(ROOT/'tools/unreal/LyraMontageImmediateOracle'/name)==digest
        assert sha(package/name)==digest
    previous=load(EVIDENCE/'montage-bank-callbacks-v1-native.json')
    for name,digest in previous['probeSourceSha256'].items():
        assert sha(ROOT/'tools/unreal/LyraMontageBankOracle'/name)==digest
        if name.endswith('.cpp'):assert sha(package/name)==digest
    assert 'Result: Succeeded' in (EVIDENCE/'whole-main-build-package-immediate-callbacks-v1.log').read_text(encoding='utf-8-sig')

    callbacks=frames=0
    stages=collections.Counter()
    assert len(native['trace']['traces'])==27
    for q,t in zip(request['traces'],native['trace']['traces'],strict=True):
        assert q['name']==t['name'] and len(q['frames'])==len(t['frames'])==q['hz']*3
        all_calls=[]
        for row in t['frames']:
            assert not row['queuing']
            for c in row['immediate']+row['calls']:
                assert not c['queuing']
                stages[c['stage']]+=1;callbacks+=1;all_calls.append(c)
            frames+=1
        assert any(c['listener']=='return' for c in all_calls),q['name']
        if q['mode'] in ('in-play','in-play-many','in-play-root'):
            child=2
            returned=next(c for c in all_calls if c['listener']=='return')
            created=next(i for i in returned['live'] if i['instance']==child)
            assert created['position']==created['weight']==0
            same_tick=next(row for row in t['frames'] if any(c['listener']=='return' for c in row['immediate']))
            advanced=next(i for i in same_tick['before'] if i['instance']==child)
            assert advanced['position']>0 and advanced['weight']>0
            if q['mode']=='in-play-many':assert len(same_tick['before'])==10
            if q['mode']=='in-play-root':assert same_tick['beforeRoot']==child
        if q['mode']=='in-rebind':
            assert any(c['listener']=='rebound' for c in all_calls)
            assert any(c['listener']=='global-rebound' for c in all_calls)
            assert not any(c['listener']=='global' and c['kind']==1 for c in all_calls)
        if q['mode'].startswith('request-'):
            assert any(c['listener']=='return' and c['stage']=='requests' for c in all_calls)
    assert frames==5670 and callbacks==405 and stages=={'weight':195,'dispatch':180,'requests':30}

    runs={}
    for config in ('debug','optimize'):
        summary=load(EVIDENCE/f'{tag}-{config}-verification.json')
        assert summary['passed'] and len(summary['runs'])==12
        build=EVIDENCE/f'{tag}-build-{config}-final-v2.log'
        evidence[str(build.relative_to(ROOT))]=sha(build)
        text=build.read_text(encoding='utf-8-sig')
        assert '0 个错误' in text and '0 个警告' in text
        for run in summary['runs']:
            assert run['passed'] and run['exitCode']==0
            path=Path(run['log']);assert sha(path)==run['logSha256'].lower()
            text=log(path)
            if run['name']=='native-immediate':
                assert 'traces=27 frames=5670 retries=5670 callbacks=405 checks=106368 effects=405 immediateMutation=true' in text
            if run['name']=='native-bank-callbacks':
                assert 'traces=12 frames=5880 retries=5880 callbacks=171 checks=33021 immediateMutation=false' in text
            if 'report' in run:
                report=load(Path(run['report']));assert sha(Path(run['report']))==run['reportSha256'].lower()
                other='optimize' if config=='debug' else 'debug'
                assert report==load(EVIDENCE/f'{tag}-{other}-{run["name"]}.json')
                assert report==load(EVIDENCE/f'montage-bank-callbacks-v1-final-{config}-{run["name"]}.json')
        whole=load(EVIDENCE/f'{tag}-whole-main-{config}-verification.json')
        assert whole['passed'] and len(whole['runs'])==1
        for field in ('workerFields','preUpdateFields','movementFields','graphFields','leftSettings','montageEventFields'):
            assert whole[field]
        assert whole['assemblies']==summary['assemblies']
        for run in whole['runs']:
            assert run['passed'] and run['boundary']=='final' and run['layout']=='per-call' and run['frames']==1080
            assert sha(Path(run['log']))==run['logSha256'].lower()
            assert 'retry=1080 controlledPhysicalInputs=true' in log(Path(run['log']))
        runs[config]=len(summary['runs'])+len(whole['runs'])
    for suffix in ('','-whole-main'):
        summary=read(EVIDENCE/f'{tag}{suffix}-optimize-verification.json');assert summary['debugRestored']
        for name in summary['assemblies']:
            assert sha(EVIDENCE/f'{tag}{suffix}-optimize-debug-backup'/name)==sha(ROOT/'.godot/mono/temp/bin/Debug'/name)
    for config,directory in (('debug','Debug'),('optimize','ExportRelease')):
        for name,digest in read(EVIDENCE/f'{tag}-{config}-verification.json')['assemblies'].items():
            assert sha(ROOT/'.godot/mono/temp/bin'/directory/name)==digest.lower()
    trx=EVIDENCE/f'{tag}-core-final-v2.trx'
    evidence[str(trx.relative_to(ROOT))]=sha(trx)
    counter=ET.parse(trx).find('.//{*}Counters')
    assert counter is not None and counter.attrib['passed']==counter.attrib['total']=='549' and counter.attrib['failed']=='0'
    result=dict(auditPassed=True,nativeTraces=27,nativeFrames=frames,callbacks=callbacks,callbackStages=dict(stages),
        retryFramesPerConfiguration=5670,godotRuns=runs,corePassed=549,frozenSources=len(sources),
        protectedJson=869,protectedPackages=710,protectedProject=9,synchronousCandidateBankMutation=True,
        transactionalCallbackEffects=True,fullMontageEventDispatch=False,resourceNotifyTermination=False,
        goalComplete=False,evidenceSha256=evidence)
    with target.open('x',encoding='utf-8',newline='\n') as stream:json.dump(result,stream,indent=2)
    print(json.dumps({k:v for k,v in result.items() if k!='evidenceSha256'}))


if __name__=='__main__':main()
