"""Audit actual Linked startup phases and the complete existing character matrix."""
from pathlib import Path
import hashlib
import json
import re
import sys
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[1]
out = repo / 'artifacts/lyra-analysis'
tag = 'source-initialize-v2'
native_tag_base = 'source-initialize-v1'
read = lambda p: json.loads(p.read_bytes())
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()


def log(p):
    b = p.read_bytes()
    return b.decode('utf-16') if b.startswith((b'\xff\xfe', b'\xfe\xff')) else b.decode('utf-8-sig')


sources = read(out / f'{tag}-frozen-sources.json')
assert len(sources) == 33
for p, digest in sources.items():
    assert sha(repo / p) == digest, p
previous = read(out / f'{native_tag_base}-before.json')
for p, digest in previous.items():
    if p not in sources:
        assert sha(repo / p) == digest, p

for kind in ('requests', 'native', 'closure'):
    assert (out / f'{native_tag_base}-{kind}.json').read_bytes() == (out / f'{native_tag_base}-repeat-{kind}.json').read_bytes(), kind
closure = read(out / f'{native_tag_base}-closure.json')
for native_tag in (native_tag_base, f'{native_tag_base}-repeat'):
    assert sha(out / f'{native_tag}-requests.json') == closure['requestSha256']
    assert sha(out / f'{native_tag}-native.json') == closure['nativeSha256']
    text = log(out / f'{native_tag}-native.log')
    assert 'LYRA_SEQUENCE_INITIALIZE_NATIVE_OK profiles=3 nodes=78 seeded_rows=156 linked=1 assets_saved=0' in text
    assert 'LYRA_SEQUENCE_INITIALIZE_PROCESS_EXIT=0' in text
    assert not re.search(r'Error:|Fatal error:|Ensure condition failed', text)
assert closure['scope']['originalSequenceInitialize'] and closure['scope']['seededReinitialization']
assert closure['scope']['originalCacheBones'] and not closure['scope']['sourceUpdateOrEvaluate']
assert not closure['scope']['privateReinitializedFlagRead'] and closure['scope']['assetsSaved'] == 0
native = read(out / f'{native_tag_base}-native.json')
assert [c['profile'] for c in native['cases']] == ['unarmed', 'pistol', 'rifle']
source_nodes = {5,17,19,24,26,28,41,43,49,51,53,55,57,61,67,82,84,86,88,90,92,94,96,98,100,114}
for case in native['cases']:
    assert case['linkedInstances'] == 1 and not case['sourceUpdate']
    assert len(case['sources']) == 52
    assert {(s['node'],s['round']) for s in case['sources']} == {(n,r) for n in source_nodes for r in (0,1)}
    for row in case['sources']:
        before, after = row['before'], row['after']
        assert after == row['cached']
        assert after['internal'] == (before['internal'] if row['evaluator'] else 0)
        assert after['previousIndex'] == after['nextIndex'] == -2
        assert not after['fullWeight'] and after['validInstanceId']
        for key in ('weight','previousDistance','nextDistance','deltaPrevious','delta','deltaValid'):
            assert after[key] == before[key], (row['node'], key)

engine = Path('../UE_5.8/Engine')
ledger = read(out / f'{native_tag_base}-engine-sources.json')
assert ledger == closure['engineSourceSha256'] and len(ledger) == 16
for p, digest in ledger.items():
    assert sha(engine / p) == sha(out / f'{native_tag_base}-engine-source' / p) == digest, p
package = repo / 'artifacts/unreal/lyra-whole-main-oracle/package-source-initialize-v1'
for p, digest in closure['probeSourceSha256'].items():
    assert sha(repo / 'tools/unreal/LyraSequenceInitializeOracle' / p) == sha(package / p) == digest, p
assert 'Result: Succeeded' in log(out / 'whole-main-build-package-source-initialize-v1.log')
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
tests = ET.parse(out / f'{native_tag_base}-core.trx').find('.//t:Counters', ns).attrib
assert tests['total'] == tests['passed'] == '134' and tests['failed'] == tests['notExecuted'] == '0'
if '--preflight' in sys.argv:
    print('LYRA_SEQUENCE_INITIALIZE_PREFLIGHT_OK native=2 nodes=78 seededRows=156 core=134 sources=33 resources=870 runtimeMatrixPending=true')
    sys.exit(0)

frequencies = {'30':30, '60':60, '120':120, 'three':60, 'mixed':60, 'per-call':60}
expected_names = ['source-initialize','start-source','stop-source','pivot-source','cycle-source','idle-source','air-source','additives-source','left-source','phases'] + [f'{kind}-{suffix}' for kind in ('initial','unlink') for suffix in frequencies]
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
        if row['name'] == 'source-initialize':
            assert 'LYRA_SEQUENCE_INITIALIZE_NATIVE_GODOT_OK profiles=3 nodes=78 seededRows=156 retry=3 deferredHip=3 pendingRejected=3 sourceUpdateDuringInitialize=false evaluate=false fullWeightFieldCompared=false' in text
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
assert counts['debug'] == counts['optimize'] == read(out / 'graph-phases-v1-integrity.json')['counts']['debug']
for name in ('ordinary-ten','ordinary-emote'):
    assert ordinary['debug',name] == ordinary['optimize',name] == read(out / f'graph-phases-v1-debug-{name}.json')
restored = {}
for group in ('','-whole-main','-other-groups'):
    backup = out / f'{tag}{group}-optimize-debug-backup'
    assert len(list(backup.iterdir())) == 6
    restored[group or 'primary'] = {p.name:sha(p).upper() for p in backup.iterdir()}
    assert restored[group or 'primary'] == reports['debug']['assemblies']
    for p in backup.iterdir():
        assert sha(p) == sha(repo / '.godot/mono/temp/bin/Debug' / p.name), p.name
assert reports['debug']['assemblies']['GodotALS.dll'] != reports['optimize']['assemblies']['GodotALS.dll']
assert processes == 66
result = dict(auditPassed=True,nativeEvidenceTag=native_tag_base,runtimeEvidenceTag=tag,nativeProcesses=2,nativeProfiles=3,nativeSourceNodes=78,nativeSeededRows=156,coreTests=134,
              godotProcesses=processes,counts=counts,resources=870,protectedPackages=710,protectedProjectFiles=9,
              engineCopies=16,sourceSha256=sources,auditorSha256=sha(Path(__file__)),restoredAssemblies=restored,
              scope=dict(realProviderSourceInitialization=True, sourceInventoryPerProfile=26,
                         clocksMarkersDeltasAndAssetsCompared=True, sourceCallbacksDeferred=True,
                         hiddenHipInitializationRetained=True, nullEvaluatorUpdateTransactional=True,
                         fixedAls81Layout=True, sourceTicksDuringInitialize=False, poseEvaluationDuringInitialize=False,
                         fullSourcePrivateFields=False, blendSpaceInitialize=False, fullProviderPhaseSchedule=False,
                         unifiedUpdateEvaluateCaches=False, subsequentGraphReinitialization=False, liveRequiredBonesLOD=False,
                         rigConstructionPhaseCompared=False, newAlsDefaultFinalNative=False, partialBindings=False,
                         fullPrivateFields=False, fullPhysicsParity=False, goalComplete=False))
with (out / f'{tag}-integrity.json').open('x',encoding='utf-8') as f:
    json.dump(result,f,indent=2)
print('LYRA_SEQUENCE_INITIALIZE_AUDIT_OK native=2 nodes=78 seededRows=156 core=134 godot=66 resources=870 goalComplete=false')
