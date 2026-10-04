"""Audit original traversal evidence, current sources, resources and runtime gates."""
from pathlib import Path
import hashlib
import json
import re
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[1]
out = repo / 'artifacts/lyra-analysis'
read = lambda p: json.loads(p.read_bytes())
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()

def log(p):
    b = p.read_bytes()
    return b.decode('utf-16') if b.startswith((b'\xff\xfe', b'\xfe\xff')) else b.decode('utf-8-sig')

sources = read(out/'layer-phases-v5-frozen-sources.json')
for p, digest in sources.items():
    assert sha(repo/p) == digest, p
previous = read(out/'default-main-v5-frozen-sources-accepted.json')
previous.update(read(out/'unlink-production-v9-frozen-sources.json'))
previous['tools/verify_lyra_character_unlink.py'] = read(out/'unlink-production-v9-audit-correction.json')['correctedSha256']
for p, digest in previous.items():
    if p not in sources:
        assert sha(repo/p) == digest, p

closures = [read(out/f'{tag}-closure.json') for tag in ('layer-phases-v5','layer-phases-v5-repeat')]
for kind in ('requests','native','closure'):
    assert (out/f'layer-phases-v5-{kind}.json').read_bytes() == (out/f'layer-phases-v5-repeat-{kind}.json').read_bytes(), kind
closure = closures[0]
engine = Path('../UE_5.8/Engine')
ledger = read(out/'layer-phases-v5-engine-sources.json')
assert ledger == closure['engineSourceSha256'] and len(ledger) == 7
for p, digest in ledger.items():
    assert sha(engine/p) == digest
    assert sha(out/'layer-phases-v5-engine-source'/Path(p).name) == digest
for tag, c in zip(('layer-phases-v5','layer-phases-v5-repeat'), closures):
    assert sha(out/f'{tag}-requests.json') == c['requestSha256']
    assert sha(out/f'{tag}-native.json') == c['nativeSha256']
    text = log(out/f'{tag}-native.log')
    assert 'LYRA_LAYER_FALLBACK_NATIVE_OK cases=68 stages=4 initialize_cache=true' in text
    assert 'LYRA_LAYER_FALLBACK_PROCESS_EXIT=0' in text
    assert not re.search(r'Error:|Fatal error:|Ensure condition failed', text)
    assert c['scope']['originalExternalInitializeCache'] and not c['scope']['externalPoseEvaluated']

native = read(out/'layer-phases-v5-native.json')
assert len(native['cases']) == 68 and len(native['functions']) == 15
stage_counts = {}
for row in native['cases']:
    stage = row['name'].split(':')[0]
    stage_counts[stage] = stage_counts.get(stage,0)+1
    root = stage != 'unbound'
    expected = (['root'] if root else [])+['input:11' if i == 0 else 'input:22' for i in range(row['inputs'])]
    assert row['initializeOrder'] == row['cacheOrder'] == expected, row['name']
    assert row['rootInitializations'] == row['rootCaches'] == int(root)
    assert row['firstInitializations'] == row['firstCaches'] == int(row['inputs'] > 0)
    assert row['secondInitializations'] == row['secondCaches'] == int(row['inputs'] > 1)
    assert row['poseEvaluated'] == (stage not in ('linked','relinked'))
assert stage_counts == dict(initial=14,linked=14,unlinked=14,relinked=14,unbound=12)

assets = repo/'assets/generated/lyra_als'
assert {p.relative_to(assets).as_posix():sha(p) for p in assets.rglob('*.json')} == closure['previousFixtureSha256']
assert len(closure['previousFixtureSha256']) == 870
project = Path('../GASP58')
for p, digest in closure['protectedProject'].items():
    assert sha(project/p) == digest, p
for p, digest in closure['assetSha256'].items():
    path = p.split('.')[0]
    if path.startswith('/Game/'):
        file = project/'Content'/(path.removeprefix('/Game/')+'.uasset')
    elif path.startswith('/ShooterCore/'):
        file = project/'Plugins/GameFeatures/ShooterCore/Content'/(path.removeprefix('/ShooterCore/')+'.uasset')
    else:
        raise ValueError(p)
    assert sha(file) == digest, p
for p, digest in closure['probeSourceSha256'].items():
    assert sha(repo/'tools/unreal/LyraLayerPhasesOracle'/p) == digest
    assert sha(out.parent/'unreal/lyra-whole-main-oracle/package-layer-phases-v5'/p) == digest

for name in ('layer-phases-v2-build-debug.log','layer-phases-v4-build-optimize.log'):
    text = log(out/name)
    assert re.search(r'^\s*0\s*(个警告|Warning)',text,re.M)
    assert re.search(r'^\s*0\s*(个错误|Error)',text,re.M)
reports = {}
for config in ('debug','optimize'):
    report = read(out/f'layer-phases-v4-{config}-verification.json')
    assert report['passed'] and not report['fullMainInitialization'] and not report['goalComplete']
    assert [r['name'] for r in report['runs']] == ['routes','main-default','unlink-30']
    for row in report['runs']:
        text = log(Path(row['log']))
        assert sha(Path(row['log'])) == row['logSha256'].lower()
        assert row['exitCode'] == 0 and row['passed'] and 'LYRA_LAYER_PHASE_EXIT=0' in text
        assert not re.search(r'^\s*(ERROR|WARNING):',text,re.M)
        if row['name'] == 'routes':
            assert 'calls=3360 externalRoots=672 inputs=360 rejected=48' in text
        if row['name'] == 'main-default':
            assert 'LYRA_MAIN_DEFAULT_ROOT_OK frames=1260 retry=1260' in text
        if row['name'] == 'unlink-30':
            assert 'frames=1080 self=360 retry=540 rejected=3444 switches=24 montageSelf=126 rigChanged=354 rigCompleted=360 airSelf=54' in text
    reports[config] = report
backup = out/'layer-phases-v4-optimize-debug-backup'
assert len(list(backup.iterdir())) == 6
for p in backup.iterdir():
    assert sha(p) == sha(repo/'.godot/mono/temp/bin/Debug'/p.name)
assert reports['debug']['assemblies'] == {p.name:sha(p).upper() for p in backup.iterdir()}
assert reports['debug']['assemblies']['GodotALS.dll'] != reports['optimize']['assemblies']['GodotALS.dll']

ns = {'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
tests = ET.parse(out/'layer-phases-v5-core.trx').find('.//t:Counters',ns).attrib
assert tests['total'] == tests['passed'] == '131' and tests['failed'] == tests['notExecuted'] == '0'
result = dict(auditPassed=True,nativeProcesses=2,nativeCases=68,coreTests=131,godotProcesses=6,
              sourceSha256=sources,auditorSha256=sha(Path(__file__)),
              resources=870,protectedPackages=len(closure['assetSha256']),engineCopies=7,
              scope=dict(originalFourBindingStages=True,callRouteInitializeCacheBones=True,
                         allInputTraversal=True,subgraphEntrySeparate=True,
                         fullMainInitializeCacheBones=False,initialSelfCharacter=False,
                         newAlsDefaultFinalNative=False,goalComplete=False))
with (out/'layer-phases-v5-integrity.json').open('x',encoding='utf-8') as f:
    json.dump(result,f,indent=2)
print('LYRA_LAYER_PHASE_AUDIT_OK native=2 cases=68 core=131 godot=6 resources=870 goalComplete=false')
