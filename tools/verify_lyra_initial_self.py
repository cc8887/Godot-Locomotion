"""Audit startup Main phases, real characters and the protected export baseline."""
from pathlib import Path
import hashlib
import json
import re
import sys
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[1]
out = repo / 'artifacts/lyra-analysis'
tag = 'initial-self-v1'
read = lambda p: json.loads(p.read_bytes())
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()


def log(p):
    b = p.read_bytes()
    return b.decode('utf-16') if b.startswith((b'\xff\xfe', b'\xfe\xff')) else b.decode('utf-8-sig')


sources = read(out / f'{tag}-frozen-sources.json')
assert len(sources) == 18
for p, digest in sources.items():
    assert sha(repo / p) == digest, p
previous = read(out / 'default-main-v5-frozen-sources-accepted.json')
previous.update(read(out / 'unlink-production-v9-frozen-sources.json'))
previous['tools/verify_lyra_character_unlink.py'] = read(out / 'unlink-production-v9-audit-correction.json')['correctedSha256']
previous.update(read(out / 'layer-phases-v5-frozen-sources.json'))
for p, digest in previous.items():
    if p not in sources:
        assert sha(repo / p) == digest, p

for kind in ('requests', 'native', 'closure'):
    assert (out / f'main-phases-v3-{kind}.json').read_bytes() == (out / f'main-phases-v3-repeat-{kind}.json').read_bytes(), kind
closure = read(out / 'main-phases-v3-closure.json')
for native_tag in ('main-phases-v3', 'main-phases-v3-repeat'):
    assert sha(out / f'{native_tag}-requests.json') == closure['requestSha256']
    assert sha(out / f'{native_tag}-native.json') == closure['nativeSha256']
    text = log(out / f'{native_tag}-native.log')
    assert 'LYRA_MAIN_PHASE_NATIVE_OK phases=3 linked=0 full_main=true assets_saved=0' in text
    assert 'LYRA_MAIN_PHASE_PROCESS_EXIT=0' in text
    assert not re.search(r'Error:|Fatal error:|Ensure condition failed', text)
assert closure['scope']['originalFullMainRoot'] and closure['scope']['initialSelf']
assert closure['scope']['fullLayout'] and closure['scope']['repeatedCacheCounter']
assert not closure['scope']['defaultFinalPoseEvaluated'] and not closure['scope']['rigConstructionCompared']
assert not closure['scope']['providerFullPhases'] and closure['scope']['assetsSaved'] == 0

native = read(out / 'main-phases-v3-native.json')
assert native['state'] == native['elapsed'] == native['linkedInstances'] == 0
assert native['weights'] == [1] + [0] * 11
assert native['initialize'] == native['cacheBones']
assert len(native['initialize']) == 30 and len(native['repeatedCacheBones']) == 15
for stage in ('initialize', 'cacheBones', 'repeatedCacheBones'):
    assert native[stage][0:4] == ['node:85', 'node:73', 'node:4', 'self:FullBody_SkeletalControls']
    assert not any(f'node:{i}' in native[stage] for i in (12, 16, 22))
assert 'node:7' in native['initialize'] and 'self:FullBody_IdleState' in native['initialize']
assert 'node:7' not in native['repeatedCacheBones'] and 'node:83' not in native['repeatedCacheBones']
request = read(out / 'main-phases-v3-requests.json')
assert len(request['nodes']) == 49 and request['mesh'].endswith('/SKM_Manny.SKM_Manny')

engine = Path('../UE_5.8/Engine')
ledger = read(out / 'main-phases-v3-engine-sources.json')
assert ledger == closure['engineSourceSha256'] and len(ledger) == 9
for p, digest in ledger.items():
    assert sha(engine / p) == digest, p
    assert sha(out / 'main-phases-v3-engine-source' / Path(p).name) == digest, p
for p, digest in closure['probeSourceSha256'].items():
    assert sha(repo / 'tools/unreal/LyraMainPhasesOracle' / p) == digest, p
    assert sha(repo / 'artifacts/unreal/lyra-whole-main-oracle/package-main-phases-v2' / p) == digest, p
package = repo / 'artifacts/unreal/lyra-whole-main-oracle/package-main-phases-v2'
assert 'Result: Succeeded' in log(out / 'whole-main-build-package-main-phases-v2.log')
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
        raise ValueError(p)
    assert sha(file) == digest, p
assert len(closure['assetSha256']) == 710

for config in ('debug', 'optimize'):
    text = log(out / f'main-phases-v3-build-{config}.log')
    assert re.search(r'^\s*0\s*(个警告|Warning)', text, re.M)
    assert re.search(r'^\s*0\s*(个错误|Error)', text, re.M)
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
tests = ET.parse(out / f'{tag}-core.trx').find('.//t:Counters', ns).attrib
assert tests['total'] == tests['passed'] == '131' and tests['failed'] == tests['notExecuted'] == '0'

if '--preflight' in sys.argv:
    print('LYRA_INITIAL_SELF_PREFLIGHT_OK native=2 core=131 sources=18 resources=870 runtimeMatrixPending=true')
    sys.exit(0)

frequencies = {'30': 30, '60': 60, '120': 120, 'three': 60, 'mixed': 60, 'per-call': 60}
expected_names = [f'{kind}-{suffix}' for kind in ('initial', 'unlink') for suffix in frequencies]
expected_names += ['main-default', 'routes', 'named', 'weapon', 'live', 'ordinary-ten', 'ordinary-emote']
counts, reports, ordinary = {}, {}, {}
processes = 0
for config in ('debug', 'optimize'):
    report = read(out / f'{tag}-{config}-verification.json')
    assert report['passed'] and report['fullMainUnlink'] and not report['goalComplete']
    assert [r['name'] for r in report['runs']] == expected_names
    assert report['debugRestored'] == (config == 'optimize')
    values = {}
    for row in report['runs']:
        path = Path(row['log'])
        text = log(path)
        assert sha(path) == row['logSha256'].lower() and row['exitCode'] == 0 and row['passed'], row['name']
        assert 'LYRA_DEFAULT_ROUTES_EXIT=0' in text and not re.search(r'^\s*(ERROR|WARNING):', text, re.M)
        kind, _, suffix = row['name'].partition('-')
        if kind in ('initial', 'unlink'):
            match = re.search(r'LYRA_CHARACTER_UNLINK_OK hz=(\d+) frames=(\d+) self=(\d+) retry=(\d+) rejected=(\d+) switches=(\d+) montageSelf=(\d+) rigChanged=(\d+) rigCompleted=(\d+) airSelf=(\d+)', text)
            assert match is not None, row['name']
            hz, frames, self_frames, retry, rejected, switches, montage, rig, completed, air = map(int, match.groups())
            assert hz == frequencies[suffix]
            assert (frames, self_frames, retry, switches) == (36*hz, (18 if kind == 'initial' else 12)*hz, 18*hz, 30 if kind == 'initial' else 24)
            assert rejected > 0 and montage > 0 and rig > 0 and completed == self_frames and air > 0
            if kind == 'initial':
                assert f'LYRA_INITIAL_SELF_CHARACTER_OK hz={hz} frames={frames} self={self_frames} retry={retry} switches=30 nativeMainPhases=true' in text
            values[row['name']] = dict(hz=hz, frames=frames, self=self_frames, retry=retry, rejected=rejected, switches=switches, montage=montage, rig=rig, rigCompleted=completed, air=air)
        if 'report' in row:
            p = Path(row['report'])
            assert sha(p) == row['reportSha256'].lower()
            ordinary[config, row['name']] = read(p)
        processes += 1
    counts[config], reports[config] = values, report
    for group, layouts in (('whole-main', ['single', 'per-call']), ('other-groups', ['three-groups', 'mixed'])):
        whole = read(out / f'{tag}-{group}-{config}-verification.json')
        assert whole['passed'] and len(whole['runs']) == 2 and not whole['goalComplete']
        assert [r['layout'] for r in whole['runs']] == layouts
        for flag in ('workerFields', 'preUpdateFields', 'movementFields', 'graphFields'):
            assert whole[flag]
        if group == 'whole-main':
            assert whole['leftSettings'] and whole['montageEventFields']
        for row in whole['runs']:
            path = Path(row['log'])
            text = log(path)
            assert sha(path) == row['logSha256'].lower() and row['exitCode'] == 0 and row['passed']
            assert not re.search(r'^\s*(ERROR|WARNING):', text, re.M)
            assert 'LYRA_MULTI_OWNER_MAIN_GRAPH_OK' in text and 'LYRA_WHOLE_MAIN_DIAGNOSTIC_OK frames=1080' in text and 'retry=1080' in text
            assert row['frames'] == 1080 and row['boundary'] == 'final'
            assert whole['assemblies'] == report['assemblies']
            processes += 1

assert counts['debug'] == counts['optimize']
baseline_counts = read(out / 'unlink-production-v9-integrity.json')['counts']['debug']
for name, baseline in baseline_counts.items():
    assert {k: counts['debug'][name][k] for k in baseline} == baseline, name
totals = {}
for kind, self_total in (('initial', 7020), ('unlink', 4680)):
    rows = [v for k, v in counts['debug'].items() if k.startswith(kind + '-')]
    totals[kind] = {field: sum(v[field] for v in rows) for field in ('frames', 'self', 'retry', 'switches', 'air')}
    assert totals[kind]['frames'] == 14040 and totals[kind]['self'] == self_total and totals[kind]['retry'] == 7020
for name in ('ordinary-ten', 'ordinary-emote'):
    assert ordinary['debug', name] == ordinary['optimize', name]
    assert ordinary['debug', name] == read(out / f'unlink-production-v9-debug-{name}.json')

restored = {}
for group in ('', '-whole-main', '-other-groups'):
    backup = out / f'{tag}{group}-optimize-debug-backup'
    assert len(list(backup.iterdir())) == 6
    restored[group or 'primary'] = {p.name: sha(p).upper() for p in backup.iterdir()}
    assert restored[group or 'primary'] == reports['debug']['assemblies']
    for p in backup.iterdir():
        assert sha(p) == sha(repo / '.godot/mono/temp/bin/Debug' / p.name), p.name
assert reports['debug']['assemblies']['GodotALS.dll'] != reports['optimize']['assemblies']['GodotALS.dll']
assert processes == 46
result = dict(auditPassed=True, nativeProcesses=2, nativePhases=3, coreTests=131,
              godotProcesses=processes, counts=counts, totals=totals,
              resources=870, protectedPackages=710, protectedProjectFiles=9, engineCopies=9,
              sourceSha256=sources, auditorSha256=sha(Path(__file__)), restoredAssemblies=restored,
              scope=dict(initialSelfCharacter=True, zeroLinkedStartup=True,
                         originalMainStartupTraversal=True, realMainMachineInitialization=True,
                         repeatedCacheCounter=True, finalRigExecuted=True,
                         providerFullPhaseSchedule=False, subsequentGraphReinitialization=False,
                         liveRequiredBonesLOD=False, rigConstructionPhaseCompared=False,
                         newAlsDefaultFinalNative=False, partialBindings=False,
                         fullPrivateFields=False, fullPhysicsParity=False, goalComplete=False))
with (out / f'{tag}-integrity.json').open('x', encoding='utf-8') as f:
    json.dump(result, f, indent=2)
print('LYRA_INITIAL_SELF_AUDIT_OK native=2 phases=3 core=131 godot=46 resources=870 goalComplete=false')
