"""Independently audit default-root captures, managed replay and protected inputs."""
import hashlib
import json
from pathlib import Path
from locomotion_paths import engine_path, project_path

import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[1]
out = repo/'artifacts/lyra-analysis'
tag = 'layer-fallback-v4'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read = lambda p: json.loads(p.read_bytes())
for suffix in ('requests', 'native', 'closure'):
    assert (out/f'{tag}-{suffix}.json').read_bytes() == (out/f'{tag}-repeat-{suffix}.json').read_bytes(), suffix
closure = read(out/f'{tag}-closure.json')
assert sha(out/f'{tag}-requests.json') == closure['requestSha256']
assert sha(out/f'{tag}-native.json') == closure['nativeSha256']
for p, d in closure['probeSourceSha256'].items():
    assert sha(repo/'tools/unreal/LyraLayerFallbackOracle'/p) == d, p
    assert sha(repo/'artifacts/unreal/lyra-whole-main-oracle/package-layer-fallback-v4'/p) == d, p
engine = engine_path('Engine')
for p, d in closure['engineSourceSha256'].items():
    assert sha(engine/p) == d, p
    assert sha(out/f'{tag}-engine-source'/Path(p).name) == d, p
assert read(out/f'{tag}-engine-source/sha256.json') == closure['engineSourceSha256']
assets = repo/'assets/generated/lyra_als'
current = {p.relative_to(assets).as_posix(): sha(p) for p in assets.rglob('*.json')}
assert current == closure['previousFixtureSha256']
project = project_path()
for p, d in closure['protectedProject'].items(): assert sha(project/p) == d, p
for p, d in closure['assetSha256'].items():
    path = p.split('.')[0]
    if path.startswith('/Game/'): file = project/'Content'/(path.removeprefix('/Game/')+'.uasset')
    elif path.startswith('/ShooterCore/'): file = project/'Plugins/GameFeatures/ShooterCore/Content'/(path.removeprefix('/ShooterCore/')+'.uasset')
    else: raise ValueError(path)
    assert sha(file) == d, p

native = read(out/f'{tag}-native.json')
functions = native['functions']
assert len(functions) == 15
main = next(f for f in functions if f['name'] == 'AnimGraph')
assert main['implemented'] and main['hasRootProperty']
defaults = [f for f in functions if f['name'] != 'AnimGraph']
for f in defaults:
    assert not f['implemented'] and f['hasRootProperty']
    assert len(f['nodes']) == 1
    n = f['nodes'][0]
    assert n['type'] == '/Script/Engine.AnimNode_Root' and not n['links']
    assert n['propertyIndex'] == f['rootPropertyIndex']
cases = {c['name']: c for c in native['cases']}
assert len(cases) == len(native['cases']) == 40
for f in defaults:
    a = cases['initial:'+f['name']]
    b = cases['unlinked:'+f['name']]
    assert {k: v for k, v in a.items() if k != 'name'} == {k: v for k, v in b.items() if k != 'name'}
    assert a['self'] and not any(a[k] for k in ('firstUpdates','secondUpdates','firstEvaluations','secondEvaluations'))
    assert a['curve'] == -7 and a['curveFlags'] == 1 and a['attribute'] == -21
    assert a['pose'] == cases['unbound:0:0:0']['pose']
assert sum(cases['initial:'+f['name']]['inputs'] == 1 for f in defaults) == 3
for inputs in range(3):
    for additive in range(2):
        for prefilled in range(2):
            c = cases[f'unbound:{inputs}:{additive}:{prefilled}']
            assert c['firstUpdates'] == c['firstEvaluations'] == int(inputs > 0)
            assert c['secondUpdates'] == c['secondEvaluations'] == 0
            if inputs:
                assert c['curve'] == 11.25 and c['curveFlags'] == 2 and c['attribute'] == 33
                expected = cases[f'unbound:0:{additive}:0']['pose'].copy()
                expected[0] = expected[0] | {'position':[11,-11,5.5]}
                assert c['pose'] == expected
            else:
                assert c['hasCurve'] == c['hasAttribute'] == bool(prefilled)
                assert c['curve'] == (-7 if prefilled else 0)
                assert c['curveFlags'] == prefilled and c['attribute'] == (-21 if prefilled else 0)
                if additive:
                    assert all(p == dict(position=[0,0,0],rotation=[0,0,0,1],scale=[0,0,0]) for p in c['pose'])

def log_text(path):
    b = path.read_bytes()
    return b.decode('utf-16') if b.startswith((b'\xff\xfe',b'\xfe\xff')) else b.decode('utf-8-sig')

for repeat in ('', '-repeat'):
    text = log_text(out/f'{tag}{repeat}-native.log')
    assert 'LYRA_LAYER_FALLBACK_PROCESS_EXIT=0' in text and 'LYRA_LAYER_FALLBACK_NATIVE_OK cases=40' in text
    assert not any(s in text for s in ('Error:', 'Fatal error:', 'Ensure condition failed'))
trx = ET.parse(out/f'{tag}-core-final.trx')
counters = trx.find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters').attrib
assert int(counters['total']) == int(counters['passed']) == 59
assert int(counters['failed']) == int(counters['notExecuted']) == 0

owned = [
    'src/Als.Core/Animation/AlsLinkedLayerExecution.cs',
    'tests/Als.Core.Tests/AlsLinkedLayerExecutionNativeTests.cs',
    'scripts/build-lyra-layer-fallback-oracle.ps1',
    'scripts/capture-lyra-layer-fallback.ps1',
    'tools/unreal/capture_lyra_layer_fallback.py',
    'tools/verify_lyra_layer_fallback.py',
]
source = repo/'tools/unreal/LyraLayerFallbackOracle'
owned.extend(p.relative_to(repo).as_posix() for p in source.rglob('*') if p.is_file())
result = dict(auditPassed=True, nativeCases=40, defaultRoots=14, originalSelfCalls=14, originalUnlinkCalls=14,
    invalidTargetCases=12, poseBones=len(cases['unbound:0:0:0']['pose']), managedTests=59,
    generatedJson=len(current), originalPackages=len(closure['assetSha256']), projectFiles=len(closure['protectedProject']),
    sourceSha256={p: sha(repo/p) for p in owned},
    scope=dict(nativeDefaultRootsEvaluated=True, originalInputsControlled=True, managedRoutingImplemented=True,
        fullMainEvaluated=False, productionFallbackIntegrated=False, godotRuntimeRetested=False, goalComplete=False))
with (out/f'{tag}-integrity.json').open('x', encoding='utf-8') as f:
    f.write(json.dumps(result, indent=2)+'\n')
print('LYRA_LAYER_FALLBACK_AUDIT_OK native=40 managed=59 defaults=14 protected=869/710/9 productionIntegrated=false')
