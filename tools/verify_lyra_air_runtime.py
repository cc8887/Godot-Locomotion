"""Audit original five-Air-root captures, preserved inputs and Godot results."""
import hashlib
import json
import re
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
root = repo/'assets/generated/lyra_als'
artifacts = repo/'artifacts/lyra-analysis'
sha = lambda data: hashlib.sha256(data).hexdigest()
names = tuple('air_runtime_'+n+'.json' for n in ('requests', 'distance', 'roots', 'native'))
native = json.loads((root/names[-1]).read_bytes())
requests = json.loads((root/names[0]).read_bytes())
assert native['schemaVersion'] == requests['schemaVersion'] == 1
for name, key in zip(names[:3], ('requestSha256', 'distanceSha256', 'rootSha256'), strict=True):
    assert sha((root/name).read_bytes()) == native[key], name
assert len(native['assetSha256']) == 508 and len(native['previousFixtureSha256']) == 629
for name, digest in (native['dependencies'] | native['previousFixtureSha256']).items():
    assert sha((root/name).read_bytes()) == digest, name
for path, digest in native['assetSha256'].items():
    package = Path('../GASP58/Content')/(path.split('.')[0].removeprefix('/Game/')+'.uasset')
    assert sha(package.read_bytes()) == digest, path
for relative, digest in native['probeSourceSha256'].items():
    for source in (repo/'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter',
                   repo/'artifacts/unreal/gasp58-lyra-masks/source/AlsV4AssetExporter/Source/AlsV4AssetExporter',
                   repo/'artifacts/unreal/gasp58-lyra-masks/package/AlsV4AssetExporter/Source/AlsV4AssetExporter'):
        assert sha((source/relative).read_bytes()) == digest, str(source/relative)
assert len(native['assets']) == 21
assert [a['path'] for a in native['assets']] == requests['sequencePaths']
assert {(t['profile'], t['hz']) for t in native['traces']} == {
    (p, hz) for p in ('unarmed', 'pistol', 'rifle') for hz in (30, 60, 120)}
counts = dict(frames=0, poses=0)
coverage = dict(hiddenRoots=0, inactiveRoots=0, negativeExplicit=0, simultaneous=0,
                initializedHidden=0, loopBoundary=0, nonDefaultRateScale=0,
                rootPresent=0, rootAbsent=0, curves=0, attributes=0)
by_layer = [0]*5
assets = {a['path']: a for a in native['assets']}
for trace, authored in zip(native['traces'], requests['traces'], strict=True):
    assert trace['profile'] == authored['profile'] and trace['hz'] == authored['hz']
    for row, frame in zip(trace['frames'], authored['frames'], strict=True):
        counts['frames'] += 1
        assert len(row['roots']) == len(frame['visits']) == len(set(frame['order'])) == 5
        coverage['simultaneous'] += sum(v['visited'] for v in frame['visits']) > 1
        for n, (stage, visit) in enumerate(zip(row['roots'], frame['visits'], strict=True)):
            assert ('output' in stage) == visit['visited']
            for source in (stage['base'], stage['hip']):
                assert not source['asset'] or source['asset'] in assets
            if not visit['visited']:
                coverage['hiddenRoots'] += 1
                coverage['initializedHidden'] += visit['initialize']
                continue
            coverage['inactiveRoots'] += not visit['active']
            coverage['negativeExplicit'] += stage['base']['publicTime'] < 0
            coverage['loopBoundary'] += n in (1, 3) and stage['base']['time'] == assets[stage['base']['asset']]['length']
            coverage['nonDefaultRateScale'] += assets[stage['base']['asset']]['rateScale'] != 1
            pose = stage['output']
            assert len(pose['pose']) == 81
            coverage['rootPresent'] += 'rootMotion' in pose
            coverage['rootAbsent'] += 'rootMotion' not in pose
            coverage['curves'] += len(pose['curves'])
            coverage['attributes'] += len(pose['attributes'])
            counts['poses'] += 1
            by_layer[n] += 1
assert counts == native['counts'] == dict(frames=3780, poses=7650)
assert by_layer == [1620, 1611, 1605, 1410, 1404]
assert coverage['hiddenRoots'] == 11250 and coverage['inactiveRoots'] == 2046
assert coverage['negativeExplicit'] == 578 and coverage['nonDefaultRateScale'] > 0
assert coverage['simultaneous'] > 0 and coverage['initializedHidden'] > 0

logs = {}
for name in ('air-runtime-ue-export.log', 'air-runtime-ue-repeat.log'):
    data = (artifacts/name).read_bytes(); text = data.decode('utf-8-sig')
    assert text.count('LYRA_AIR_RUNTIME_NATIVE_OK frames=3780 poses=7650 assets=21 packages=508 previous=629 assets_saved=0') == 1, name
    assert text.count('LYRA_EXPORT_PROCESS_EXIT_OK mode=air-runtime code=0') == 1, name
    assert not re.search(r'(Traceback \(most recent call last\)|Assertion failed:|Ensure condition failed:|Fatal error:|: Error:)', text), name
    logs[name] = dict(sha256=sha(data), warnings=[line for line in text.splitlines() if ': Warning:' in line])
for name, markers in {
    'air-runtime-godot-final.log': ('LYRA_AIR_RUNTIME_GODOT_OK',),
    'air-runtime-ground-regression.log': ('LYRA_MAIN_GROUND_NATIVE_GODOT_OK',),
    'air-runtime-machine-regression.log': ('LYRA_MAIN_MACHINE_RUNTIME_GODOT_OK',),
    'air-runtime-binding-regression.log': ('LYRA_LINKED_BINDING_OK',),
    'air-runtime-logical-regression.log': ('LYRA_LOGICAL_SOURCE_NATIVE_OK',),
}.items():
    data = (artifacts/name).read_bytes(); text = data.decode('utf-8-sig')
    assert not re.search(r'(?m)^(ERROR:|WARNING:)', text), name
    for marker in markers: assert text.count(marker) == 1, (name, marker)
    if name == 'air-runtime-godot-final.log':
        assert text.count('LYRA_CYCLE_LAYER_POSE_GODOT_OK') == 5
        assert 'clocks=37800 sourceIds=10 assets=21 hidden=11250 inactive=2046 updateOnly=1920 negativeExplicit=578 rejected=3636 retry=true wholeMachine=false production=false' in text
    logs[name] = dict(sha256=sha(data), results=[line for line in text.splitlines() if '_OK' in line])
for name in ('air-runtime-debug-build-final.log', 'air-runtime-optimize-build-final.log'):
    data = (artifacts/name).read_bytes(); text = data.decode('utf-8-sig')
    assert '已成功生成' in text and '0 个错误' in text and '0 个警告' in text, name
    logs[name] = dict(sha256=sha(data))
build_data = (artifacts/'air-runtime-ue-build-final.log').read_bytes()
build_text = build_data.decode('utf-8-sig')
assert 'BUILD SUCCESSFUL' in build_text and 'AutomationTool exiting with ExitCode=0' in build_text
assert not re.search(r'(warning C\d+|error C\d+)', build_text)
logs['air-runtime-ue-build-final.log'] = dict(sha256=sha(build_data))
report = dict(schemaVersion=1, resourceSha256={n: sha((root/n).read_bytes()) for n in names},
              protectedPackages=508, protectedPreviousFixtures=629, counts=counts,
              layerPoseCounts=by_layer, coverage=coverage, actualFiveAirGraphs=True,
              controlledMainInputs=True, controlledRootVisits=True, sharedGroundAirScope=False,
              wholeMachinePose=False, ordinaryDemo=False, wholeGoalComplete=False, logs=logs)
(artifacts/'air-runtime-final-verification.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print('LYRA_AIR_RUNTIME_FINAL_VERIFIED frames=3780 poses=7650 bones=619650 packages=508 fixtures=629 whole_goal=false')
