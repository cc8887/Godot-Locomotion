"""Audit phase-counter evidence, production regressions and protected inputs."""
from pathlib import Path
import hashlib
import json
import re
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[1]
out = repo / 'artifacts/lyra-analysis'
tag = 'proxy-phase-v4'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read = lambda p: json.loads(p.read_bytes())


def log(path):
    b = path.read_bytes()
    return b.decode('utf-16') if b.startswith((b'\xff\xfe', b'\xfe\xff')) else b.decode('utf-8-sig')


frozen = read(out/f'{tag}-frozen-sources.json')
for p, digest in frozen.items(): assert sha(repo/p) == digest, p
protected = 0
for p, digest in read(out/'proxy-phase-v1-before.json').items():
    if p not in frozen and p not in ('ROADMAP.md', 'docs/verification/2026-10-03-lyra-als-interface-review.md'):
        assert sha(repo/p) == digest, p
        protected += 1
for kind in ('requests', 'native', 'closure'):
    assert (out/f'proxy-phase-v1-{kind}.json').read_bytes() == (out/f'proxy-phase-v1-repeat-{kind}.json').read_bytes(), kind
closure = read(out/'proxy-phase-v1-closure.json')
assert sha(out/'proxy-phase-v1-requests.json') == closure['requestSha256']
assert sha(out/'proxy-phase-v1-native.json') == closure['nativeSha256']
scope = closure['scope']
assert scope['originalProxyEntryPoints'] and scope['originalLinkedNodes']
assert scope['proxyCounterWrites'] == scope['assetsSaved'] == 0
assert scope['controlledExternalFrames'] and not scope['naturalComponentCounters'] and not scope['fullLyraGraphs'] and not scope['goalComplete']
engine = Path('../UE_5.8/Engine')
project = Path('../GASP58')
for p, digest in closure['engineSourceSha256'].items(): assert sha(engine/p) == digest, p
for p, digest in closure['previousFixtureSha256'].items(): assert sha(repo/'assets/generated/lyra_als'/p) == digest, p
for p, digest in closure['protectedProject'].items(): assert sha(project/p) == digest, p
for p, digest in closure['assetSha256'].items():
    asset = p.split('.')[0]
    if asset.startswith('/Game/'):
        file = project/'Content'/(asset.removeprefix('/Game/')+'.uasset')
    elif asset.startswith('/ShooterCore/'):
        file = project/'Plugins/GameFeatures/ShooterCore/Content'/(asset.removeprefix('/ShooterCore/')+'.uasset')
    else: raise ValueError(asset)
    assert sha(file) == digest, p
probe = repo/'tools/unreal/LyraProxyPhaseOracle'
package = repo/'artifacts/unreal/lyra-proxy-phase-oracle/package-proxy-phase-v4'
for p, digest in closure['probeSourceSha256'].items():
    assert sha(probe/p) == sha(package/p) == digest, p
native = read(out/'proxy-phase-v1-native.json')['result']
assert len(native['rows']) == 31 and len(native['initial']['proxies']) == 3
warnings = []
for suffix in ('', '-repeat'):
    text = log(out/f'proxy-phase-v1{suffix}-native.log')
    assert 'LYRA_PROXY_PHASE_NATIVE_OK steps=31 proxies=3 counter_writes=0 assets_saved=0' in text
    assert 'LYRA_PROXY_PHASE_PROCESS_EXIT=0' in text
    assert not re.search('Error:|Fatal error:|Ensure condition failed', text)
    warnings.append(len(re.findall('Warning:', text)))
assert warnings[0] == warnings[1]
trx = ET.parse(out/'proxy-phase-v2-core.trx')
counter = next(e for e in trx.getroot().iter() if e.tag.endswith('Counters'))
assert int(counter.attrib['total']) == int(counter.attrib['passed']) == 170
assert int(counter.attrib['failed']) == int(counter.attrib['notExecuted']) == 0
for config in ('debug', 'optimize'):
    text = log(out/f'{tag}-{config}-build.log')
    assert '0 个警告' in text and '0 个错误' in text
    assert not re.search('error [A-Z]+[0-9]+', text)

summaries = []
for config in ('debug', 'optimize'):
    data = read(out/f'{tag}-{config}-verification.json')
    assert data['passed'] and len(data['runs']) == 27
    if config == 'optimize':
        assert data['debugRestored']
        debug = read(out/f'{tag}-debug-verification.json')
        assert data['assemblies']['GodotALS.dll'] != debug['assemblies']['GodotALS.dll']
        for filename, digest in debug['assemblies'].items():
            assert sha(repo/'.godot/mono/temp/bin/Debug'/filename).upper() == digest.upper(), filename
    summaries.append(data)
    for r in data['runs']:
        assert r['passed'] and r['exitCode'] == 0
        assert sha(Path(r['log'])).upper() == r['logSha256'].upper()
        assert not re.search(r'(?m)^\s*(ERROR|WARNING):', log(Path(r['log'])))
        if 'report' in r:
            assert sha(Path(r['report'])).upper() == r['reportSha256'].upper()
    for group in ('whole-main', 'other-groups'):
        full = read(out/f'{tag}-{group}-{config}-verification.json')
        assert full['passed'] and len(full['runs']) == 2
        for r in full['runs']:
            assert r['passed'] and r['exitCode'] == 0
            assert not re.search(r'(?m)^\s*(ERROR|WARNING):', log(Path(r['log'])))
        if config == 'optimize': assert full['debugRestored']

for d, o in zip(summaries[0]['runs'], summaries[1]['runs'], strict=True):
    assert d['name'] == o['name']
    if 'report' in d:
        assert read(Path(d['report'])) == read(Path(o['report']))
        old = out/f'cache-owner-v4-debug-{d["name"]}.json'
        assert read(Path(d['report'])) == read(old), d['name']

host = (repo/'src/Als.Godot/Animation/Lyra/LyraMainPoseHost.cs').read_text(encoding='utf-8')
assert '_evaluation%65535' not in host
assert 'new LyraMainSlotComposition(_bank,_montageCatalog,profile,CacheOwner)' in host
assert '_proxyCommitted=_proxyCandidate' in host and '_proxyCandidate=_proxyCommitted' in host
result = dict(schemaVersion=1, passed=True, sources=len(frozen), otherProtectedFiles=protected,
    nativeSteps=31, proxies=3, nativeCounterScalarComparisons=768, wrapRootEntries=65536,
    corePassed=170, godotProcesses=62, debugRestored=True, originalWarnings=warnings,
    assets=len(closure['previousFixtureSha256']), originalPackages=len(closure['assetSha256']),
    hostConfigurations=len(closure['protectedProject']), engineSources=len(closure['engineSourceSha256']),
    productionEvaluationCounters=True, rebindSlotCacheOwner=True,
    controlledExternalFrames=True, naturalComponentCounters=False, fullPhaseScheduler=False, goalComplete=False)
with (out/f'{tag}-integrity.json').open('x', encoding='utf-8') as f: json.dump(result, f, indent=2)
print(json.dumps(result, ensure_ascii=False))
