"""Audit the real Ended prelude, live state order and production regressions."""
import json
import struct
import xml.etree.ElementTree as ET
from pathlib import Path
from verify_lyra_multi_layer_native import ROOT,EVIDENCE,ASSETS,PROJECT,sha,read,package_file


def main():
    tag='notify-termination-v1';capture='notify-termination-v3'
    destination=EVIDENCE/f'{tag}-integrity.json'
    assert not destination.exists(),'Preserve audit.'
    hashes={}
    def load(path):
        hashes[str(path.relative_to(ROOT))]=sha(path)
        return read(path)
    def clean(path,native=False):
        hashes[str(path.relative_to(ROOT))]=sha(path)
        value=path.read_text(encoding='utf-8-sig')
        if native:
            assert 'LYRA_NOTIFY_TERMINATION_NATIVE_OK cases=11 assets_saved=0' in value
            assert 'MONTAGE_DELEGATES_PROCESS_EXIT=0' in value
            assert not any(s in value for s in ('Error:','Fatal error:','Ensure condition failed'))
        else:
            assert not any(line.lstrip().startswith(('ERROR:','WARNING:')) for line in value.splitlines()),path
            assert 'MONTAGE_DELEGATES_GODOT_EXIT=0' in value or 'MULTI_OWNER_PROCESS_EXIT=0' in value
        return value
    frozen=load(EVIDENCE/f'{tag}-frozen-sources.json')
    for name,digest in frozen.items():assert sha(ROOT/name)==digest,name
    native=load(EVIDENCE/f'{capture}-native.json')
    assert native==load(EVIDENCE/f'{capture}-repeat-native.json')
    assert sha(EVIDENCE/f'{capture}-native.json')==sha(EVIDENCE/f'{capture}-repeat-native.json')
    request=load(EVIDENCE/f'{capture}-requests.json')
    assert request==load(EVIDENCE/f'{capture}-repeat-requests.json')
    assert sha(EVIDENCE/f'{capture}-requests.json')==sha(EVIDENCE/f'{capture}-repeat-requests.json')==native['requestSha256']
    for suffix in ('','-repeat'):clean(EVIDENCE/f'{capture}{suffix}-native.log',True)
    assert native['catalogSha256']==sha(ASSETS/'montage_catalog_v2.json')
    assert len(native['previousFixtureSha256'])==869 and len(native['assetSha256'])==710 and len(native['protectedProject'])==9
    for name,digest in native['previousFixtureSha256'].items():assert sha(ASSETS/name)==digest,name
    for name,digest in native['assetSha256'].items():assert sha(package_file(name))==digest,name
    for name,digest in native['protectedProject'].items():assert sha(PROJECT/name)==digest,name
    package=ROOT/'artifacts/unreal/lyra-whole-main-oracle/package-notify-termination-v4'
    for name,digest in native['probeSourceSha256'].items():
        assert sha(ROOT/'tools/unreal/LyraMontageNotifyEndOracle'/name)==sha(package/name)==digest,name
    previous=load(EVIDENCE/'immediate-callbacks-v1-native.json')
    for name,digest in previous['probeSourceSha256'].items():
        assert sha(ROOT/'tools/unreal/LyraMontageImmediateOracle'/name)==digest,name
        if name.endswith('.cpp'):assert sha(package/name)==digest,name
    build=EVIDENCE/'whole-main-build-package-notify-termination-v4.log'
    hashes[str(build.relative_to(ROOT))]=sha(build)
    assert 'Result: Succeeded' in build.read_text(encoding='utf-8-sig')
    expected={
        'reverse':([4,2,0],[3,1]),'filtered':([],[2,1]),'no-context':([],[0]),
        'clear':([1],[]),'nested':([2,0],[2,1]),'append':([0],[2,1]),
        'rebind':([0],[]),'queued':([4,2,0],[3,1]),'original':([],[]),
        'original-concurrent':([],[1]),'original-mw':([],[2])}
    calls=0
    for q,n in zip(request['cases'],native['trace']['cases'],strict=True):
        assert q['mode']==n['mode']
        notify,remaining=expected[n['mode']]
        assert [c['id'] for c in n['calls'] if c['listener']=='notify']==notify
        assert [c['id'] for c in n['after']]==remaining
        if n['mode']=='queued':assert n['pendingCalls']==[]
        if n['mode']=='clear':assert len(n['calls'])==1
        if n['mode']=='nested':assert [c['instance'] for c in n['calls'] if c['listener']=='instance']==[2]
        if n['mode']=='rebind':assert n['calls'][-1]['listener']=='new-global'
        calls+=len(n['calls'])
    assert len(expected)==len(native['trace']['cases'])==11 and calls==31
    window='notify-termination-windows-v1'
    wq=load(EVIDENCE/f'{window}-requests.json');wn=load(EVIDENCE/f'{window}-native.json')
    assert wn==load(EVIDENCE/f'{window}-repeat-native.json')
    assert sha(EVIDENCE/f'{window}-native.json')==sha(EVIDENCE/f'{window}-repeat-native.json')
    assert wn['requestSha256']==sha(EVIDENCE/f'{window}-requests.json')
    for name,digest in wq['dependencies'].items():assert sha(ASSETS/name)==digest
    for name,digest in wn['pluginSourceSha256'].items():
        assert sha(ROOT/'tools/unreal/LyraMontageNotifyOracle'/name)==digest
        if name.startswith('Source/'):assert sha(ROOT/'artifacts/unreal/lyra-montage-notify-oracle/package-fourth'/name)==digest
    def compare(a,b,path='traces'):
        if isinstance(a,dict):
            assert a.keys()==b.keys(),path
            for key in a:compare(a[key],b[key],path+'/'+key)
        elif isinstance(a,list):
            assert len(a)==len(b),path
            for index,(x,y) in enumerate(zip(a,b)):compare(x,y,path+'/'+str(index))
        elif path.endswith('/current'):assert struct.pack('f',a)==struct.pack('f',b),(path,a,b)
        else:assert a==b,(path,a,b)
    compare(load(EVIDENCE/f'{window}-godot-output.json')['traces'],wn['trace']['traces'])
    assert len(wq['traces'])==15 and sum(len(t['frames']) for t in wq['traces'])==33235
    assert len(wq['assets'])==45 and sum(len(a['tracks']) for a in wn['trace']['assets'])==60
    for suffix in ('','-repeat'):
        path=EVIDENCE/f'{window}{suffix}-native.log';hashes[str(path.relative_to(ROOT))]=sha(path)
        text=path.read_text(encoding='utf-8-sig')
        assert 'LYRA_MONTAGE_NOTIFY_NATIVE_OK traces=15 frames=33235' in text
        assert 'LYRA_EXPORT_PROCESS_EXIT_OK mode=montage-notify-queue code=0' in text
        assert not any(s in text for s in ('LogPython: Error:','Fatal error:','Ensure condition failed'))
    # The old accepted assembly fails the same obsolete immutable-request gate.
    baseline=EVIDENCE/f'{tag}-montage-windows-baseline.log';hashes[str(baseline.relative_to(ROOT))]=sha(baseline)
    assert 'Immutable bytes differ:' in baseline.read_text(encoding='utf-8-sig')
    restoration=load(EVIDENCE/f'{tag}-baseline-restore.json')
    old_assemblies=load(EVIDENCE/'immediate-callbacks-v1-debug-verification.json')['assemblies']
    assert len(restoration)==6
    for name,row in restoration.items():
        assert sha(EVIDENCE/f'{tag}-baseline-debug-backup'/name)==row['backupSha256']==row['restoredSha256']
        assert sha(EVIDENCE/'immediate-callbacks-v1-optimize-debug-backup'/name)==row['priorAssemblySha256']==old_assemblies[name].lower()
    runs={}
    for config in ('debug','optimize'):
        summary=load(EVIDENCE/f'{tag}-{config}-verification.json')
        assert summary['passed'] and len(summary['runs'])==15
        build=EVIDENCE/f'{tag}-build-{config}-final.log';hashes[str(build.relative_to(ROOT))]=sha(build)
        text=build.read_text(encoding='utf-8-sig');assert '0 个警告' in text and '0 个错误' in text
        for run in summary['runs']:
            assert run['passed'] and run['exitCode']==0
            path=Path(run['log']);assert sha(path)==run['logSha256'].lower()
            text=clean(path)
            if run['name']=='native-notify-termination':
                assert 'cases=11 callbacks=31 productionAdapterHz=3 retries=3 stateEnds=3 fullNotifyDispatch=false' in text
            if run['name']=='native-notify-windows':assert 'native=True' in text
            if 'report' in run:
                data=load(Path(run['report']));assert sha(Path(run['report']))==run['reportSha256'].lower()
                other='debug' if config=='optimize' else 'optimize'
                assert data==load(EVIDENCE/f'{tag}-{other}-{run["name"]}.json')
                assert data==load(EVIDENCE/f'immediate-callbacks-v1-{config}-{run["name"]}.json')
        whole=load(EVIDENCE/f'{tag}-whole-main-{config}-verification.json')
        assert whole['passed'] and len(whole['runs'])==1 and whole['assemblies']==summary['assemblies']
        for key in ('workerFields','preUpdateFields','movementFields','graphFields','leftSettings','montageEventFields'):assert whole[key]
        for run in whole['runs']:
            assert run['passed'] and run['layout']=='per-call' and run['boundary']=='final' and run['frames']==1080
            assert sha(Path(run['log']))==run['logSha256'].lower()
            assert 'retry=1080 controlledPhysicalInputs=true' in clean(Path(run['log']))
        runs[config]=len(summary['runs'])+len(whole['runs'])
    for suffix in ('','-whole-main'):
        summary=read(EVIDENCE/f'{tag}{suffix}-optimize-verification.json');assert summary['debugRestored']
        for name in summary['assemblies']:
            assert sha(EVIDENCE/f'{tag}{suffix}-optimize-debug-backup'/name)==sha(ROOT/'.godot/mono/temp/bin/Debug'/name)
    for config,directory in (('debug','Debug'),('optimize','ExportRelease')):
        for name,digest in read(EVIDENCE/f'{tag}-{config}-verification.json')['assemblies'].items():
            assert sha(ROOT/'.godot/mono/temp/bin'/directory/name)==digest.lower()
    trx=EVIDENCE/f'{tag}-core-final.trx';hashes[str(trx.relative_to(ROOT))]=sha(trx)
    counters=ET.parse(trx).find('.//{*}Counters');assert counters is not None
    assert counters.attrib['total']==counters.attrib['passed']=='557' and counters.attrib['failed']=='0'
    result=dict(auditPassed=True,nativeCases=11,nativeCallbacks=31,controlledPhysicalBankAdapterHz=3,
        refreshedNotifyWindowTraces=15,refreshedNotifyWindowFrames=33235,notifyAssets=45,notifyTracks=60,
        stateEndsPerConfiguration=3,corePassed=557,godotRuns=runs,frozenSources=len(frozen),
        protectedJson=869,protectedPackages=710,protectedProject=9,
        montageEndedStatePrelude=True,fullNotifyDispatch=False,worldTeardownAccepted=False,goalComplete=False,evidenceSha256=hashes)
    with destination.open('x',encoding='utf-8',newline='\n') as stream:json.dump(result,stream,indent=2)
    print(json.dumps({k:v for k,v in result.items() if k!='evidenceSha256'}))


if __name__=='__main__':main()
