"""Audit actual Linked startup phases and the complete existing character matrix."""
from pathlib import Path
import hashlib
import json
import re
import sys
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[1]
out = repo / 'artifacts/lyra-analysis'
tag = 'graph-phases-v1'
read = lambda p: json.loads(p.read_bytes())
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()


def log(p):
    b = p.read_bytes()
    return b.decode('utf-16') if b.startswith((b'\xff\xfe', b'\xfe\xff')) else b.decode('utf-8-sig')


sources = read(out / f'{tag}-frozen-sources.json')
assert len(sources) == 36
for p, digest in sources.items():
    assert sha(repo / p) == digest, p
previous = read(out / 'default-main-v5-frozen-sources-accepted.json')
previous.update(read(out / 'unlink-production-v9-frozen-sources.json'))
previous['tools/verify_lyra_character_unlink.py'] = read(out / 'unlink-production-v9-audit-correction.json')['correctedSha256']
previous.update(read(out / 'layer-phases-v5-frozen-sources.json'))
previous.update(read(out / 'initial-self-v1-frozen-sources.json'))
for p, digest in previous.items():
    if p not in sources:
        assert sha(repo / p) == digest, p

for kind in ('requests', 'native', 'closure'):
    assert (out / f'{tag}-{kind}.json').read_bytes() == (out / f'{tag}-repeat-{kind}.json').read_bytes(), kind
closure = read(out / f'{tag}-closure.json')
for native_tag in (tag, f'{tag}-repeat'):
    assert sha(out / f'{native_tag}-requests.json') == closure['requestSha256']
    assert sha(out / f'{native_tag}-native.json') == closure['nativeSha256']
    text = log(out / f'{native_tag}-native.log')
    assert 'LYRA_GRAPH_PHASE_NATIVE_OK profiles=3 cache_steps=30 linked=1 assets_saved=0' in text
    assert 'LYRA_GRAPH_PHASE_PROCESS_EXIT=0' in text
    assert not re.search(r'Error:|Fatal error:|Ensure condition failed', text)
assert closure['scope']['originalMainAndProviderPhases'] and closure['scope']['originalNamedGroup']
assert closure['scope']['counterRegressions'] and closure['scope']['counterWrap']
assert closure['scope']['equalCounterDifferentFrame'] and not closure['scope']['sourceUpdateOrEvaluate']
assert closure['scope']['assetsSaved'] == 0
native = read(out / f'{tag}-native.json')
assert [c['profile'] for c in native['cases']] == ['unarmed', 'pistol', 'rifle']
for c in native['cases']:
    assert c['linkedInstances'] == 1 and len(c['initialize']) == 56
    assert len(c['sourceTimes']) == 26 and all(s['time'] == 0 for s in c['sourceTimes'])
    assert len(c['machines']) == 5
    assert {(s['owner'], s['node']) for s in c['machines']} == {('main', 7), ('provider', 1), ('provider', 13), ('provider', 15), ('provider', 59)}
    for m in c['machines']:
        assert m['state'] == m['elapsed'] == 0
        assert m['weights'] == [1] + [0] * (len(m['weights']) - 1)
    assert [(s['counter'], s['frame']) for s in c['cacheSteps']] == [(1,10),(1,10),(2,11),(2,11),(1,10),(1,10),(1,12),(32767,13),(-32768,14),(-32768,14)]
    assert [len(s['order']) for s in c['cacheSteps']] == [56,34,56,34,56,34,56,56,56,34]

engine = Path('../UE_5.8/Engine')
ledger = read(out / f'{tag}-engine-sources.json')
assert ledger == closure['engineSourceSha256'] and len(ledger) == 12
for p, digest in ledger.items():
    assert sha(engine / p) == sha(out / f'{tag}-engine-source' / Path(p).name) == digest, p
package = repo / 'artifacts/unreal/lyra-whole-main-oracle/package-graph-phases-v2'
for p, digest in closure['probeSourceSha256'].items():
    assert sha(repo / 'tools/unreal/LyraGraphPhasesOracle' / p) == sha(package / p) == digest, p
assert 'Result: Succeeded' in log(out / 'whole-main-build-package-graph-phases-v2.log')
assert read(package / 'Binaries/Win64/UnrealEditor.modules')['BuildId'] == read(Path('../GASP58/Binaries/Win64/UnrealEditor.modules'))['BuildId']
assets = repo / 'assets/generated/lyra_als'
assert {p.relative_to(assets).as_posix(): sha(p) for p in assets.rglob('*.json')} == closure['previousFixtureSha256']
assert len(closure['previousFixtureSha256']) == 870
project = Path('../GASP58')
assert not (project / 'Plugins/LyraWholeMainOracle').exists()
for p, digest in closure['protectedProject'].items():
    assert sha(project / p) == digest, p
assert len(closure['protectedProject']) == 9
for p, digest in closure['assetSha256'].items():
    path = p.split('.')[0]
    if path.startswith('/Game/'):
        file = project / 'Content' / (path.removeprefix('/Game/') + '.uasset')
    elif path.startswith('/ShooterCore/'):
        file = project / 'Plugins/GameFeatures/ShooterCore/Content' / (path.removeprefix('/ShooterCore/') + '.uasset')
    else:
        raise ValueError(path)
    assert sha(file) == digest, p
assert len(closure['assetSha256']) == 710
text = log(out / f'{tag}-build-optimize.log')
assert re.search(r'^\s*0\s*(个警告|Warning)', text, re.M)
assert re.search(r'^\s*0\s*(个错误|Error)', text, re.M)
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
tests = ET.parse(out / f'{tag}-core.trx').find('.//t:Counters', ns).attrib
assert tests['total'] == tests['passed'] == '131' and tests['failed'] == tests['notExecuted'] == '0'
if '--preflight' in sys.argv:
    print('LYRA_GRAPH_PHASE_PREFLIGHT_OK native=2 profiles=3 steps=30 core=131 sources=36 resources=870 runtimeMatrixPending=true')
    sys.exit(0)

frequencies = {'30':30, '60':60, '120':120, 'three':60, 'mixed':60, 'per-call':60}
expected_names = ['phases'] + [f'{kind}-{suffix}' for kind in ('initial','unlink') for suffix in frequencies]
expected_names += ['main-default','routes','named','weapon','live','ordinary-ten','ordinary-emote']
counts, reports, ordinary = {}, {}, {}
processes = 0
for config in ('debug','optimize'):
    report = read(out / f'{tag}-{config}-verification.json')
    assert report['passed'] and report['fullMainUnlink'] and not report['goalComplete']
    assert [r['name'] for r in report['runs']] == expected_names
    assert report['debugRestored'] == (config == 'optimize')
    values = {}
    for row in report['runs']:
        path = Path(row['log']); text = log(path)
        assert sha(path) == row['logSha256'].lower() and row['exitCode'] == 0 and row['passed'], row['name']
        assert 'LYRA_DEFAULT_ROUTES_EXIT=0' in text and not re.search(r'^\s*(ERROR|WARNING):', text, re.M)
        if row['name'] == 'phases':
            assert 'LYRA_GRAPH_PHASES_NATIVE_GODOT_OK profiles=3 cacheSteps=30 machines=15 sourceUpdate=false evaluate=false layout=81 lifecycle=persistent' in text
        kind, _, suffix = row['name'].partition('-')
        if kind in ('initial','unlink'):
            match = re.search(r'LYRA_CHARACTER_UNLINK_OK hz=(\d+) frames=(\d+) self=(\d+) retry=(\d+) rejected=(\d+) switches=(\d+) montageSelf=(\d+) rigChanged=(\d+) rigCompleted=(\d+) airSelf=(\d+)', text)
            assert match is not None, row['name']
            hz, frames, self_frames, retry, rejected, switches, montage, rig, completed, air = map(int, match.groups())
            assert hz == frequencies[suffix]
            assert (frames,self_frames,retry,switches) == (36*hz,(18 if kind=='initial' else 12)*hz,18*hz,30 if kind=='initial' else 24)
            assert rejected > 0 and montage > 0 and rig > 0 and completed == self_frames and air > 0
            values[row['name']] = dict(hz=hz,frames=frames,self=self_frames,retry=retry,rejected=rejected,switches=switches,montage=montage,rig=rig,rigCompleted=completed,air=air)
        if 'report' in row:
            p = Path(row['report']); assert sha(p) == row['reportSha256'].lower()
            ordinary[config,row['name']] = read(p)
        processes += 1
    counts[config],reports[config] = values,report
    for group,layouts in (('whole-main',['single','per-call']),('other-groups',['three-groups','mixed'])):
        whole = read(out / f'{tag}-{group}-{config}-verification.json')
        assert whole['passed'] and len(whole['runs']) == 2 and not whole['goalComplete']
        assert [r['layout'] for r in whole['runs']] == layouts
        for flag in ('workerFields','preUpdateFields','movementFields','graphFields'):
            assert whole[flag]
        if group == 'whole-main':
            assert whole['leftSettings'] and whole['montageEventFields']
        for row in whole['runs']:
            path = Path(row['log']); text = log(path)
            assert sha(path) == row['logSha256'].lower() and row['exitCode'] == 0 and row['passed']
            assert not re.search(r'^\s*(ERROR|WARNING):', text, re.M)
            assert 'LYRA_MULTI_OWNER_MAIN_GRAPH_OK' in text and 'LYRA_WHOLE_MAIN_DIAGNOSTIC_OK frames=1080' in text and 'retry=1080' in text
            assert row['frames'] == 1080 and row['boundary'] == 'final'
            assert whole['assemblies'] == report['assemblies']
            processes += 1
assert counts['debug'] == counts['optimize'] == read(out / 'initial-self-v1-integrity.json')['counts']['debug']
for name in ('ordinary-ten','ordinary-emote'):
    assert ordinary['debug',name] == ordinary['optimize',name] == read(out / f'initial-self-v1-debug-{name}.json')
restored = {}
for group in ('','-whole-main','-other-groups'):
    backup = out / f'{tag}{group}-optimize-debug-backup'
    assert len(list(backup.iterdir())) == 6
    restored[group or 'primary'] = {p.name:sha(p).upper() for p in backup.iterdir()}
    assert restored[group or 'primary'] == reports['debug']['assemblies']
    for p in backup.iterdir():
        assert sha(p) == sha(repo / '.godot/mono/temp/bin/Debug' / p.name), p.name
assert reports['debug']['assemblies']['GodotALS.dll'] != reports['optimize']['assemblies']['GodotALS.dll']
assert processes == 48
result = dict(auditPassed=True,nativeProcesses=2,nativeProfiles=3,nativeCacheSteps=30,coreTests=131,
              godotProcesses=processes,counts=counts,resources=870,protectedPackages=710,protectedProjectFiles=9,
              engineCopies=12,sourceSha256=sources,auditorSha256=sha(Path(__file__)),restoredAssemblies=restored,
              scope=dict(realProviderStartup=True,unvisitedPivotInitialized=True,persistentPhaseCounters=True,
                         actualStateWeightBoneGates=True,originalMainProviderTraceCompared=True,
                         fixedAls81Layout=True,sourceTicksDuringPhases=False,poseEvaluationDuringPhases=False,
                         fullProviderPhaseSchedule=False,subsequentGraphReinitialization=False,liveRequiredBonesLOD=False,
                         rigConstructionPhaseCompared=False,newAlsDefaultFinalNative=False,partialBindings=False,
                         fullPrivateFields=False,fullPhysicsParity=False,goalComplete=False))
with (out / f'{tag}-integrity.json').open('x',encoding='utf-8') as f:
    json.dump(result,f,indent=2)
print('LYRA_GRAPH_PHASE_AUDIT_OK native=2 profiles=3 cacheSteps=30 core=131 godot=48 resources=870 goalComplete=false')
