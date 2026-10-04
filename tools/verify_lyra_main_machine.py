"""Audit original Main Update capture and its strict Godot machine comparison."""
import hashlib
import json
import re
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
root = repo/'assets/generated/lyra_als'
artifacts = repo/'artifacts/lyra-analysis'
sha = lambda data: hashlib.sha256(data).hexdigest()
names = ('main_machine_runtime_v2_requests.json', 'main_machine_runtime_v2_native.json')
requests = json.loads((root/names[0]).read_bytes())
native = json.loads((root/names[1]).read_bytes())
assert native['requestSha256'] == sha((root/names[0]).read_bytes())
assert native['sourceSkeleton'] and not native['statePosesCaptured'] and not native['fullGodotSourceHost']
assert len(native['assetSha256']) == 508 and len(native['previousFixtureSha256']) == 627
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
assert {(t['profile'], t['hz']) for t in native['traces']} == {
    (p, hz) for p in ('unarmed', 'pistol', 'rifle') for hz in (30, 60, 120)}
counts = dict(frames=0, updates=0, transitions=0, hidden=0, inactiveParent=0, states=[], maxDepth=0)
states = set()
provider_changes = 0
for trace, authored in zip(native['traces'], requests['traces'], strict=True):
    previous = authored['class']
    for row, frame in zip(trace['frames'], authored['frames'], strict=True):
        assert row['providerClass'] == frame['providerClass']
        provider_changes += previous != row['providerClass']; previous = row['providerClass']
        counts['frames'] += 1; counts['updates'] += len(row['updates'])
        counts['hidden'] += not frame['active']
        counts['inactiveParent'] += frame['active'] and not frame['contextActive']
        counts['transitions'] += frame['active'] and row['beforeState'] != row['state']
        counts['maxDepth'] = max(counts['maxDepth'], len(row['active']))
        states.add(row['state'])
counts['states'] = sorted(states)
assert counts == native['counts'] and counts['frames'] == 7560
assert len(states) == 10 and counts['maxDepth'] >= 2 and provider_changes > 0
assert native['syncValidFrames'] == sum(row['syncValid'] for trace in native['traces'] for row in trace['frames']) > 0

logs = {}
for name in ('main-machine-v2-ue-export-final.log', 'main-machine-v2-ue-export-second.log'):
    data = (artifacts/name).read_bytes(); text = data.decode('utf-8-sig')
    assert text.count('LYRA_MAIN_MACHINE_NATIVE_OK frames=7560 ') == 1, name
    assert text.count('LYRA_EXPORT_PROCESS_EXIT_OK mode=main-machine code=0') == 1, name
    assert not re.search(r'(Traceback \(most recent call last\)|Assertion failed:|Ensure condition failed:|Fatal error:|: Error:)', text), name
    logs[name] = dict(sha256=sha(data), warnings=[line for line in text.splitlines() if ': Warning:' in line])
for name, markers in {
    'main-machine-godot-final.log': ('LYRA_MAIN_MACHINE_RUNTIME_GODOT_OK',),
    'main-machine-selection-regression.log': ('LYRA_LOCOMOTION_MACHINE_OK',),
    'main-machine-pose-regression.log': ('LYRA_LOCOMOTION_POSE_OK',),
    'main-machine-ground-regression.log': ('LYRA_MAIN_GROUND_NATIVE_GODOT_OK',),
}.items():
    data = (artifacts/name).read_bytes(); text = data.decode('utf-8-sig')
    assert not re.search(r'(?m)^(ERROR:|WARNING:)', text), name
    for marker in markers: assert text.count(marker) == 1, (name, marker)
    logs[name] = dict(sha256=sha(data), results=[line for line in text.splitlines() if any(m in line for m in markers)])
for name in ('main-machine-debug-build-final.log', 'main-machine-optimize-build-final.log'):
    data = (artifacts/name).read_bytes(); text = data.decode('utf-8-sig')
    assert '已成功生成' in text and '0 个错误' in text and '0 个警告' in text, name
    logs[name] = dict(sha256=sha(data))
build_data = (artifacts/'main-machine-v2-ue-build.log').read_bytes()
build_text = build_data.decode('utf-8-sig')
assert 'BUILD SUCCESSFUL' in build_text and 'AutomationTool exiting with ExitCode=0' in build_text
assert not re.search(r'(warning C\d+|error C\d+)', build_text)
logs['main-machine-v2-ue-build.log'] = dict(sha256=sha(build_data))
report = dict(schemaVersion=1, resourceSha256={n: sha((root/n).read_bytes()) for n in names},
              protectedPackages=508, protectedPreviousFixtures=627, counts=counts,
              providerChanges=provider_changes, originalSourceUpdate=True, sourceObservationsFromNative=True,
              completeGodotSourceHost=False, wholeMachinePose=False, ordinaryDemo=False,
              wholeGoalComplete=False, logs=logs)
(artifacts/'main-machine-final-verification.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print('LYRA_MAIN_MACHINE_FINAL_VERIFIED frames=7560 states='+str(len(states))+' packages=508 fixtures=627 whole_goal=false')
