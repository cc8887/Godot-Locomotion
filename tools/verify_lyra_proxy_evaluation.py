"""Audit original full-graph counter evidence and production regressions."""
from pathlib import Path
from locomotion_paths import engine_path, project_path

import hashlib
import json
import re
import sys
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[1]
out = repo / 'artifacts/lyra-analysis'
tag = 'proxy-evaluation-v3'
run = 'proxy-update-joint-v2-full'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read = lambda p: json.loads(p.read_bytes())


def log(path):
    b = path.read_bytes()
    return b.decode('utf-16') if b.startswith((b'\xff\xfe', b'\xfe\xff')) else b.decode('utf-8-sig')


frozen = read(out / f'{tag}-final-frozen-sources.json')
for p, digest in frozen.items():
    assert sha(repo / p) == digest, p
protected = 0
for p, digest in read(out / 'proxy-evaluation-v1-before.json').items():
    if p not in frozen and p not in ('ROADMAP.md', 'docs/verification/2026-10-03-lyra-als-interface-review.md'):
        assert sha(repo / p) == digest, p
        protected += 1

if '--native-only' in sys.argv:
    closure = read(out / f'whole-main-{run}-closure.json')
    scope = closure['scope']
    assert scope['originalFullRoot'] and scope['originalProxyPhaseReads'] and scope['als81']
    assert scope['controlledExternalFrames'] and scope['mainUpdateCounterWrites'] == 0
    assert scope['requiredBonesCounterAdaptation'] and not scope['goalComplete']
    project = project_path()
    for p, digest in closure['previousFixtureSha256'].items():
        assert sha(repo / 'assets/generated/lyra_als' / p) == digest, p
    for p, digest in closure['protectedProject'].items():
        assert sha(project / p) == digest, p
    for p, digest in closure['assetSha256'].items():
        path = p.split('.')[0]
        if path.startswith('/Game/'):
            file = project / 'Content' / (path.removeprefix('/Game/') + '.uasset')
        elif path.startswith('/ShooterCore/'):
            file = project / 'Plugins/GameFeatures/ShooterCore/Content' / (path.removeprefix('/ShooterCore/') + '.uasset')
        else:
            raise ValueError(path)
        assert sha(file) == digest, p
    for p, digest in closure['captureSourceSha256'].items():
        assert sha(repo / p) == digest, p
    probe = repo / 'tools/unreal/LyraProxyUpdateOracle'
    package = repo / 'artifacts/unreal/lyra-proxy-update-oracle/package-proxy-update-v1'
    for p, digest in closure['probeSourceSha256'].items():
        assert sha(probe / p) == sha(package / p) == digest, p
    engine_sources = read(out / 'proxy-phase-v1-closure.json')['engineSourceSha256']
    for p, digest in engine_sources.items():
        assert sha(engine_path('Engine') / p) == digest, p
    evidence = {}
    def evidence_file(p):
        evidence[p.relative_to(repo).as_posix()] = sha(p)
        return read(p)
    requests = evidence_file(out / f'whole-main-{run}-request.json')['traces']
    native = evidence_file(out / f'whole-main-{run}-native.json')
    evidence_file(out / f'whole-main-{run}-closure.json')
    assert len(requests) == len(native['traceFiles']) == 12
    frames = calls = hidden = counter_scalars = relinks = evaluation_calls = hidden_evaluations = 0
    startup = {}
    def next_counter(value, frame):
        count = (value['counter'] + 1 + 32768) % 65536 - 32768
        if count == -1:
            count = 0
        return dict(counter=count, frame=frame)
    for authored, shard in zip(requests, native['traceFiles'], strict=True):
        path = out / shard['file']
        assert sha(path) == shard['sha256']
        actual = evidence_file(path)
        assert actual['profile'] == authored['profile'] and actual['layout'] == authored['layout']
        assert actual['instanceCount'] == dict(single=1, **{'three-groups': 3}, mixed=4, **{'per-call': 14})[actual['layout']]
        initial = actual['proxyInitial']
        startup[actual['profile'] + '/' + actual['layout']] = initial
        for key in ('main',):
            assert initial[key]['update'] == initial[key]['evaluation'] == dict(counter=-1, frame=-1)
        before = initial
        routing = {entry['function']: entry['owner'] for entry in actual['calls']}
        for index, (request, row) in enumerate(zip(authored['frames'], actual['frames'], strict=True)):
            assert request['proxyExternalFrame'] == index + 1
            assert row['proxyBefore'] == before
            updated, after = row['proxyUpdated'], row['proxyAfter']
            update = next_counter(before['main']['update'], request['proxyExternalFrame'])
            evaluation = next_counter(before['main']['evaluation'], request['proxyExternalFrame']) if request['evaluate'] else before['main']['evaluation']
            assert updated['main']['update'] == after['main']['update'] == update
            assert updated['main']['evaluation'] == before['main']['evaluation']
            assert after['main']['evaluation'] == evaluation
            counter_scalars += 12
            visited = set()
            for entry in row['updates']:
                if entry['hook'] in routing:
                    visited.add(routing[entry['hook']])
                    assert entry['proxyPhases']['update'] == update
                    counter_scalars += 2
                    calls += 1
            for owner, provider in enumerate(updated['providers']):
                assert provider['owner'] == owner
                expected = update if owner in visited else before['providers'][owner]['phases']['update']
                assert provider['phases']['update'] == after['providers'][owner]['phases']['update'] == expected
                counter_scalars += 4
                hidden += owner not in visited
            evaluated = set()
            assert request['evaluate'] or not row['layerOutputs']
            for entry in row['layerOutputs']:
                if entry['hook'] not in routing:
                    assert entry['hook'] in {'Aim_Ready', 'Aim_ReadyInput', 'Aim_Ready_Additive', 'Aim_Relaxed', 'Aim_RelaxedInput', 'Aim_Relaxed_Additive', 'Main_Dynamic', 'Main_InertiaInput', 'Main_InertiaOutput', 'Main_Lower', 'Main_Upper', 'Main_UpperSource'}
                    continue
                owner = routing[entry['hook']]
                evaluated.add(owner)
                assert entry['proxyPhases']['evaluation'] == evaluation
                counter_scalars += 2
                evaluation_calls += 1
            for owner, provider in enumerate(after['providers']):
                expected = evaluation if owner in evaluated else before['providers'][owner]['phases']['evaluation']
                assert updated['providers'][owner]['phases']['evaluation'] == before['providers'][owner]['phases']['evaluation']
                assert provider['phases']['evaluation'] == expected
                counter_scalars += 4
                hidden_evaluations += owner not in evaluated
            before = after
            frames += 1
            relinks += request.get('relink', False)
    assert frames == 4320 and hidden > 0 and relinks == 120
    native_log = log(out / f'whole-main-native-{run}.log')
    assert 'WHOLE_MAIN_PROCESS_EXIT=0' in native_log and 'LYRA_WHOLE_MAIN_CAPTURE_OK' in native_log
    assert not re.search(r'Error:|Fatal error:|Ensure condition failed', native_log)
    result = dict(passed=True, nativeFrames=frames, nativeTraces=12, originalRootCalls=calls,
        hiddenOwnerUpdates=hidden, originalEvaluationRootCalls=evaluation_calls, hiddenOwnerEvaluations=hidden_evaluations, counterScalarAssertions=counter_scalars, relinks=relinks,
        originalWarnings=len(re.findall(r'(?m)^.*Log[^\n]*: Warning:', native_log)),
        assets=len(closure['previousFixtureSha256']), originalPackages=len(closure['assetSha256']),
        hostConfigurations=len(closure['protectedProject']), engineSources=len(engine_sources),
        controlledExternalFrames=True, naturalComponentCounters=False, requiredBonesCounterAdaptation=True,
        startupSnapshots=startup, evidenceSha256=evidence, fullPhaseScheduler=False, goalComplete=False)
    with (out / f'{tag}-native-integrity.json').open('x', encoding='utf-8') as f:
        json.dump(result, f, indent=2)
    print(json.dumps({k: v for k, v in result.items() if k not in ('startupSnapshots', 'evidenceSha256')}))
    sys.exit(0)

native_integrity = read(out / f'{tag}-native-integrity.json')
assert native_integrity['passed']
for p, digest in native_integrity['evidenceSha256'].items():
    assert sha(repo / p) == digest, p
core = ET.parse(out / 'proxy-update-v4-core.trx').getroot()
counts = core.find('.//{*}Counters').attrib
assert counts['passed'] == '170' and counts['failed'] == counts['notExecuted'] == '0'
for config in ('debug', 'optimize'):
    build = log(out / f'{tag}-{config}-build.log')
    assert '0 个警告' in build and '0 个错误' in build
summaries = []
runs = []
joint_comparisons = 0
for config in ('debug', 'optimize'):
    main = read(out / f'{tag}-{config}-verification.json')
    summaries.append(main)
    assert main['passed'] and len(main['runs']) == 27
    if config == 'optimize':
        assert main['debugRestored']
    groups = [main]
    for group, count in (('whole-main', 2), ('other-groups', 2), ('joint', 4)):
        summary = read(out / f'{tag}-{group}-{config}-verification.json')
        assert summary['passed'] and len(summary['runs']) == count
        if config == 'optimize':
            assert summary['debugRestored']
        groups.append(summary)
    for group in groups:
        for row in group['runs']:
            assert row['passed'] and row['exitCode'] == 0
            text = log(Path(row['log']))
            assert sha(Path(row['log'])).upper() == row['logSha256'].upper()
            assert not re.search(r'(?m)^\s*(ERROR|WARNING):', text)
            if row.get('proxyUpdate'):
                found = re.search(r'LYRA_PROXY_UPDATE_JOINT_NATIVE_OK .* comparisons=(\d+)', text)
                assert found
                joint_comparisons += int(found[1])
                assert row['proxyEvaluation']
                assert 'LYRA_PROXY_EVALUATION_JOINT_NATIVE_OK' in text
            if row.get('name') in ('main-pose', 'main-pose-feedback'):
                found = re.search(r'LYRA_PROVIDER_EVALUATION_HISTORY_OK frames=11340 ownerChecks=(\d+) rejectedCacheEntries=621 repeated=174 retry=207 updateOnly=1578', text)
                assert found and int(found[1]) > 0
            if 'report' in row:
                assert sha(Path(row['report'])).upper() == row['reportSha256'].upper()
            runs.append(row)
assert len(runs) == 70 and joint_comparisons == 1573104
for debug, optimize in zip(summaries[0]['runs'], summaries[1]['runs'], strict=True):
    assert debug['name'] == optimize['name']
    if 'report' in debug:
        assert read(Path(debug['report'])) == read(Path(optimize['report']))
        assert read(Path(debug['report'])) == read(out / f'proxy-phase-v4-debug-{debug["name"]}.json')
for p, digest in summaries[0]['assemblies'].items():
    assert sha(repo / '.godot/mono/temp/bin/Debug' / p).upper() == digest.upper()
result = dict(passed=True, frozenSources=len(frozen), otherProtectedFiles=protected,
    previousUnchangedCorePassed=170, godotProcesses=len(runs), jointCounterScalarComparisons=joint_comparisons,
    jointNativeFrames=native_integrity['nativeFrames'], debugRestored=True,
    productionMainUpdate=True, productionProviderUpdateInheritance=True, productionProviderEvaluationInheritance=True, cacheReadsRequireActualEntry=True,
    mainAndProvidersOwnTransactions=True, controlledExternalFrames=True,
    naturalComponentCounters=False, fullPhaseScheduler=False, goalComplete=False)
with (out / f'{tag}-integrity.json').open('x', encoding='utf-8') as f:
    json.dump(result, f, indent=2)
print(json.dumps(result))
