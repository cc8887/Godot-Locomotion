"""Audit the actual Main/Provider cache owners and bounded native evidence."""
from pathlib import Path
import hashlib
import json
import re
import sys
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[1]
out = repo / 'artifacts/lyra-analysis'
tag = 'cache-owner-v4'
native_tag = 'cache-owner-v2'
read = lambda p: json.loads(p.read_bytes())
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()

def log(p):
    b = p.read_bytes()
    return b.decode('utf-16') if b.startswith((b'\xff\xfe', b'\xfe\xff')) else b.decode('utf-8-sig')

sources = read(out / f'{tag}-frozen-sources.json')
for p,digest in sources.items():
    assert sha(repo / p) == digest,p
baseline = read(out / 'cache-owner-v1-before.json')
docs = ('ROADMAP.md','docs/verification/2026-10-03-lyra-als-interface-review.md')
protected_count = 0
for p,digest in baseline.items():
    if p not in sources and p not in docs:
        assert sha(repo / p) == digest,p
        protected_count += 1
for kind in ('requests','native','closure'):
    assert (out / f'{native_tag}-{kind}.json').read_bytes() == (out / f'{native_tag}-repeat-{kind}.json').read_bytes(),kind
closure = read(out / f'{native_tag}-closure.json')
assert sha(out / f'{native_tag}-requests.json') == closure['requestSha256']
assert sha(out / f'{native_tag}-native.json') == closure['nativeSha256']
warnings = {}
for suffix in ('','-repeat'):
    text = log(out / f'{native_tag}{suffix}-native.log')
    assert 'LYRA_CACHE_OWNER_NATIVE_OK profiles=3 nodes=9 steps=90 assets_saved=0' in text
    assert 'LYRA_CACHE_OWNER_PROCESS_EXIT=0' in text
    assert not re.search('Error:|Fatal error:|Ensure condition failed',text)
    warnings[suffix or 'first'] = len(re.findall('Warning:',text))
assert len(set(warnings.values())) == 1
scope = closure['scope']
assert scope['originalSaveCachedPose'] and scope['originalCompiledOwners'] and scope['controlledLeaves']
assert scope['assetsSaved'] == 0 and not scope['naturalComponentCounters'] and not scope['goalComplete']
native = read(out / f'{native_tag}-native.json')
assert [c['profile'] for c in native['cases']] == ['unarmed','pistol','rifle']
assert sum(len(c['rows']) for c in native['cases']) == 90
for case in native['cases']:
    assert len(case['initial']) == 3
    for row in case['rows']:
        assert len(row['states']) == 3
        assert all(s['update'] == dict(counter=-1,frame=-1) for s in row['states'])
engine = Path('../UE_5.8/Engine')
ledger = read(out / 'cache-owner-v1-engine-sources.json')
assert ledger == closure['engineSourceSha256'] and len(ledger) == 31
for p,digest in ledger.items():
    assert sha(engine / p) == sha(out / 'cache-owner-v1-engine-source' / p) == digest,p
package = repo / 'artifacts/unreal/lyra-whole-main-oracle/package-cache-owner-v5'
assert len(closure['probeSourceSha256']) == 11
for p,digest in closure['probeSourceSha256'].items():
    assert sha(repo / 'tools/unreal/LyraCacheOwnerOracle' / p) == sha(package / p) == digest,p
assert 'Result: Succeeded' in log(out / 'whole-main-build-package-cache-owner-v5.log')
project = Path('../GASP58')
assert read(package / 'Binaries/Win64/UnrealEditor.modules')['BuildId'] == read(project / 'Binaries/Win64/UnrealEditor.modules')['BuildId']
assert not (project / 'Plugins/LyraWholeMainOracle').exists()
assets = repo / 'assets/generated/lyra_als'
assert {p.relative_to(assets).as_posix():sha(p) for p in assets.rglob('*.json')} == closure['previousFixtureSha256']
assert len(closure['previousFixtureSha256']) == 870
assert len(closure['protectedProject']) == 9
for p,digest in closure['protectedProject'].items():
    assert sha(project / p) == digest,p
assert len(closure['assetSha256']) == 710
for p,digest in closure['assetSha256'].items():
    path = p.split('.')[0]
    if path.startswith('/Game/'):
        file = project / 'Content' / (path.removeprefix('/Game/')+'.uasset')
    elif path.startswith('/ShooterCore/'):
        file = project / 'Plugins/GameFeatures/ShooterCore/Content' / (path.removeprefix('/ShooterCore/')+'.uasset')
    else:
        raise ValueError(path)
    assert sha(file) == digest,p
for config in ('debug','optimize'):
    text = log(out / f'{tag}-build-{config}.log')
    assert re.search(r'^\s*0\s*(个警告|Warning)',text,re.M)
    assert re.search(r'^\s*0\s*(个错误|Error)',text,re.M)
properties = read(out / f'{tag}-optimize-properties.json')['Properties']
assert properties['Optimize'] == 'true' and 'EXPORTRELEASE' in properties['DefineConstants'].split(';')
assert 'DEBUG' not in properties['DefineConstants'].split(';')
assert properties['OutputPath'].replace('\\','/').rstrip('/') == (repo / '.godot/mono/temp/bin/ExportRelease').as_posix()
ns = {'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
tests = ET.parse(out / 'cache-owner-v2-core.trx').find('.//t:Counters',ns).attrib
assert tests['total'] == tests['passed'] == '187' and tests['failed'] == tests['notExecuted'] == '0'
if '--preflight' in sys.argv:
    print(f'LYRA_CACHE_OWNER_PREFLIGHT_OK native=2 rows=90 core=187 sources={len(sources)} protected={protected_count} resources=870 runtimeMatrixPending=true')
    sys.exit(0)
expected_names = ['cache-owner','main-cache','main-cache-pose','slot-composition','main-pose','main-pose-feedback','main-rig','main-composition','phases']
suffixes = ('30','60','120','three','mixed','per-call')
expected_names += [f'{kind}-{suffix}' for kind in ('initial','unlink') for suffix in suffixes]
expected_names += ['main-default','routes','weapon','live','ordinary-ten','ordinary-emote']
reports = {}
counts = {}
ordinary = {}
processes = 0
markers = {
    'cache-owner':'LYRA_CACHE_OWNER_NATIVE_GODOT_OK profiles=3 nodes=9 rows=90 counterFields=1620 sourceEvaluations=234 nested=12 retries=36 rejected=234',
    'main-cache':'LYRA_MAIN_CACHE_GODOT_OK frames=2520',
    'main-cache-pose':'LYRA_MAIN_CACHE_POSE_GODOT_OK frames=1260',
    'slot-composition':'LYRA_MAIN_SLOT_COMPOSITION_GODOT_OK frames=47610 poses=19944',
    'main-pose':'LYRA_MAIN_POSE_HOST_GODOT_OK frames=11340 poses=9762',
    'main-pose-feedback':'LYRA_MAIN_POSE_HOST_FEEDBACK_GODOT_OK frames=11340 poses=9762',
    'main-rig':'LYRA_MAIN_RIG_HOST_GODOT_OK frames=7560',
    'main-composition':'LYRA_MAIN_COMPOSITION_SCOPE_GODOT_OK frames=11340 poses=9762',
    'phases':'LYRA_GRAPH_PHASES_NATIVE_GODOT_OK profiles=3 cacheSteps=30 machines=15',
}
for config in ('debug','optimize'):
    report = read(out / f'{tag}-{config}-verification.json')
    assert report['passed'] and not report['goalComplete']
    assert [r['name'] for r in report['runs']] == expected_names
    assert report['debugRestored'] == (config == 'optimize')
    reports[config] = report
    values = {}
    for row in report['runs']:
        text = log(Path(row['log']))
        assert sha(Path(row['log'])) == row['logSha256'].lower() and row['exitCode'] == 0 and row['passed'],row['name']
        assert 'LYRA_DEFAULT_ROUTES_EXIT=0' in text and not re.search(r'^\s*(ERROR|WARNING):',text,re.M)
        if row['name'] in markers:
            assert markers[row['name']] in text,row['name']
        kind,_,suffix = row['name'].partition('-')
        if kind in ('initial','unlink'):
            match = re.search(r'LYRA_CHARACTER_UNLINK_OK hz=(\d+) frames=(\d+) self=(\d+) retry=(\d+) rejected=(\d+) switches=(\d+) montageSelf=(\d+) rigChanged=(\d+) rigCompleted=(\d+) airSelf=(\d+)',text)
            assert match,row['name']
            values[row['name']] = list(map(int,match.groups()))
        if 'report' in row:
            p = Path(row['report']);assert sha(p) == row['reportSha256'].lower()
            ordinary[config,row['name']] = read(p)
        processes += 1
    counts[config] = values
    for group,layouts in (('whole-main',['single','per-call']),('other-groups',['three-groups','mixed'])):
        whole = read(out / f'{tag}-{group}-{config}-verification.json')
        assert whole['passed'] and len(whole['runs']) == 2 and not whole['goalComplete']
        assert [r['layout'] for r in whole['runs']] == layouts
        assert whole['assemblies'] == report['assemblies']
        for flag in ('workerFields','preUpdateFields','movementFields','graphFields'):
            assert whole[flag]
        if group == 'whole-main':
            assert whole['leftSettings'] and whole['montageEventFields']
        for row in whole['runs']:
            text = log(Path(row['log']))
            assert sha(Path(row['log'])) == row['logSha256'].lower() and row['exitCode'] == 0 and row['passed']
            assert not re.search(r'^\s*(ERROR|WARNING):',text,re.M)
            assert 'LYRA_WHOLE_MAIN_DIAGNOSTIC_OK frames=1080' in text and 'retry=1080' in text
            assert row['frames'] == 1080 and row['boundary'] == 'final'
            processes += 1
assert counts['debug'] == counts['optimize']
old_counts = read(out / 'source-initialize-v2-integrity.json')['counts']['debug']
for key,value in counts['debug'].items():
    assert value == list(old_counts[key].values()),key
for name in ('ordinary-ten','ordinary-emote'):
    assert ordinary['debug',name] == ordinary['optimize',name] == read(out / f'source-initialize-v2-debug-{name}.json')
restored = {}
for group in ('','-whole-main','-other-groups'):
    backup = out / f'{tag}{group}-optimize-debug-backup'
    assert len(list(backup.iterdir())) == 6
    restored[group or 'primary'] = {p.name:sha(p).upper() for p in backup.iterdir()}
    assert restored[group or 'primary'] == reports['debug']['assemblies']
    for p in backup.iterdir():
        assert sha(p) == sha(repo / '.godot/mono/temp/bin/Debug' / p.name),p.name
assert reports['debug']['assemblies']['GodotALS.dll'] != reports['optimize']['assemblies']['GodotALS.dll']
assert reports['optimize']['assemblies'] == {name:sha(repo / '.godot/mono/temp/bin/ExportRelease' / name).upper() for name in reports['optimize']['assemblies']}
assert processes == 62
failed_sources = read(out / 'cache-owner-v3-frozen-sources.json')
assert len(failed_sources) == 27
for p,digest in failed_sources.items():
    assert sha(out / 'cache-owner-v3-superseded-sources' / p) == digest,p
failed_report = read(out / 'cache-owner-v3-debug-verification.json')
assert not failed_report['passed'] and len(failed_report['runs']) == 1 and not failed_report['runs'][0]['passed']
for name,digest in failed_report['assemblies'].items():
    assert sha(out / 'cache-owner-v3-superseded-debug' / name).upper() == digest
assert len(list((out / 'cache-owner-v3-superseded-exportrelease').iterdir())) == 6
result = dict(auditPassed=True,nativeEvidenceTag=native_tag,runtimeEvidenceTag=tag,nativeProcesses=2,nativeRows=90,
              coreTests=187,godotProcesses=processes,counts=counts,nativeWarnings=warnings,sourceSha256=sources,
              protectedOtherSources=protected_count,resources=870,protectedPackages=710,protectedProjectFiles=9,engineCopies=31,
              restoredAssemblies=restored,auditorSha256=sha(Path(__file__)),failedRuntimeSources=27,failedRuntimeAssemblies=12,
              scope=dict(actualMainProviderCacheOwners=True,mainCacheNodes=[78,83],providerCacheNode=78,
                         phaseUpdateEvaluateHistoryUnified=True,mainCompositionPath=True,
                         fullChannelMainRegression=True,cancelCommitIsolation=True,controlledNativeCounters=True,
                         naturalAbsoluteCounters=False,fullProviderPhaseSchedule=False,subsequentGraphReinitialization=False,
                         liveRequiredBonesLOD=False,newAlsDefaultFinalNative=False,partialBindings=False,
                         fullPrivateFields=False,fullPhysicsParity=False,goalComplete=False))
with (out / f'{tag}-integrity.json').open('x',encoding='utf-8') as f:
    json.dump(result,f,indent=2)
print(f'LYRA_CACHE_OWNER_AUDIT_OK native=2 rows=90 core=187 godot={processes} resources=870 goalComplete=false')
