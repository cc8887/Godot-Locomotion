"""Audit actual Idle machines, raw field precision and complete pose comparisons."""
import collections
import hashlib
import json
import re
import struct
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
root = repo/'assets/generated/lyra_als'
artifacts = repo/'artifacts/lyra-analysis'
sha = lambda data: hashlib.sha256(data).hexdigest()
names = tuple('idle_runtime_v2_'+n+'.json' for n in ('requests','roots','native'))
native = json.loads((root/names[2]).read_bytes())
requests = json.loads((root/names[0]).read_bytes())
assert native['schemaVersion'] == requests['schemaVersion'] == 2
for name,key in zip(names[:2],('requestSha256','rootSha256'),strict=True):
    assert sha((root/name).read_bytes()) == native[key], name
assert len(native['assetSha256']) == 508 and len(native['previousFixtureSha256']) == 636
for name,digest in (native['dependencies'] | native['previousFixtureSha256']).items():
    assert sha((root/name).read_bytes()) == digest, name
for path,digest in native['assetSha256'].items():
    package = Path('../GASP58/Content')/(path.split('.')[0].removeprefix('/Game/')+'.uasset')
    assert sha(package.read_bytes()) == digest, path
for relative,digest in native['probeSourceSha256'].items():
    for source in (repo/'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter',
                   repo/'artifacts/unreal/gasp58-lyra-masks/source/AlsV4AssetExporter/Source/AlsV4AssetExporter',
                   repo/'artifacts/unreal/gasp58-lyra-masks/package/AlsV4AssetExporter/Source/AlsV4AssetExporter'):
        assert sha((source/relative).read_bytes()) == digest, str(source/relative)
old = json.loads((root/'idle_runtime_native.json').read_bytes())
assert sha((artifacts/'idle-runtime-v1-probe/AlsLyraIdleLibrary.cpp').read_bytes()) == old['probeSourceSha256']['Private/AlsLyraIdleLibrary.cpp']
assert [a['path'] for a in native['assets']] == requests['sequencePaths'] and len(native['assets']) == 31
assert {(t['profile'],t['hz']) for t in native['traces']} == {
    (p,hz) for p in ('unarmed','pistol','rifle') for hz in (30,60,120)}
counts = dict(frames=0,poses=0)
coverage = collections.Counter()
states = collections.Counter()
double_fields = ('IdleBreakDelayTime','TimeUntilNextIdleBreak','TurnInPlaceRotationDirection',
                 'TurnInPlaceRecoveryDirection','TurnInPlaceAnimTime')
selected = set()
for trace,authored in zip(native['traces'],requests['traces'],strict=True):
    assert trace['profile'] == authored['profile'] and trace['hz'] == authored['hz']
    assert len(trace['frames']) == len(authored['frames']) == 42*trace['hz']
    for row,frame in zip(trace['frames'],authored['frames'],strict=True):
        counts['frames'] += 1
        states[row['idle']['state']] += 1
        assert len(row['sources']) == 5 and len(row['initializations']) == 6
        assert len(row['idle']['weights']) == len(row['idle']['previousWeights']) == 4
        assert len(row['stance']['weights']) == len(row['stance']['previousWeights']) == 2
        assert row['stance']['state'] == 0 # Both original explicit stance rules are false.
        for where in ('beforeFields','fields'):
            for name in double_fields:
                bits = int(row[where][name+'Bits'],16)
                precise = struct.unpack('>d',bits.to_bytes(8,'big'))[0]
                assert precise == row[where][name]
                coverage['doubleBitFields'] += 1
                coverage['negativeZeroFields'] += bits == 0x8000000000000000
        coverage['hidden'] += not frame['visited']
        coverage['inactive'] += frame['visited'] and not frame['active']
        coverage['updateOnly'] += frame['visited'] and not frame['evaluate']
        coverage['montageActivityInputs'] += frame['montage']
        coverage['reinitializations'] += frame['initialize']
        coverage['inertiaRequests'] += len(row['requests'])
        coverage['simultaneousSources'] += len(row['updates']) > 2
        coverage['nearFullStandardBlend'] += any(1-1e-5 < e['alpha'] < 1 for e in row['idle']['active'])
        for source in row['sources']:
            if source['asset']:
                assert source['asset'] in requests['sequencePaths']
                selected.add(source['asset'])
        assert ('output' in row) == (frame['visited'] and frame['evaluate'])
        if 'output' in row:
            output = row['output']; assert len(output['pose']) == 81
            counts['poses'] += 1
            coverage['curves'] += len(output['curves'])
            coverage['attributes'] += len(output['attributes'])
            coverage['rootPresent'] += 'rootMotion' in output
            coverage['rootAbsent'] += 'rootMotion' not in output
assert counts == native['counts'] == dict(frames=26460,poses=26268)
assert set(states) == {0,1,2,3}
assert coverage['hidden'] == coverage['updateOnly'] == 96 and coverage['inactive'] == 159
assert coverage['negativeZeroFields'] > 0 and coverage['nearFullStandardBlend'] > 0
gate_names = tuple('idle_runtime_gates_'+n+'.json' for n in ('requests','roots','native'))
gate_native = json.loads((root/gate_names[2]).read_bytes())
gate_requests = json.loads((root/gate_names[0]).read_bytes())
assert gate_native['schemaVersion'] == gate_requests['schemaVersion'] == 2
assert gate_native['counts'] == dict(frames=26460,poses=3780)
for name,key in zip(gate_names[:2],('requestSha256','rootSha256'),strict=True):
    assert sha((root/name).read_bytes()) == gate_native[key]
assert len(gate_native['previousFixtureSha256']) == 639 and gate_native['assetSha256'] == native['assetSha256']
assert gate_native['probeSourceSha256'] == native['probeSourceSha256']
for name,digest in (gate_native['dependencies'] | gate_native['previousFixtureSha256']).items():
    assert sha((root/name).read_bytes()) == digest, name
assert gate_requests['sequencePaths'] == requests['sequencePaths']
assert len(gate_native['traces']) == len(gate_requests['traces']) == 54
gates = ('IsCrouching','GameplayTag_IsADS','GameplayTag_IsFiring','montage','HasVelocity','IsJumping')
assert {(t['profile'],t['hz'],t['gate']) for t in gate_requests['traces']} == {
    (p,hz,g) for p in ('unarmed','pistol','rifle') for hz in (30,60,120) for g in gates}
gate_coverage = collections.Counter()
for trace,authored in zip(gate_native['traces'],gate_requests['traces'],strict=True):
    assert len(trace['frames']) == len(authored['frames']) == trace['hz']*7
    cancellations = 0
    for row,frame in zip(trace['frames'],authored['frames'],strict=True):
        assert row['idle']['state'] in (0,3) and row['stance']['state'] == 0
        if row['beforeIdle']['state'] == 3 and row['idle']['state'] == 0:
            if authored['gate'] == 'GameplayTag_IsFiring':
                # The zero-duration transition finishes and clears the stack
                # after ordered source updates, before state weights are read.
                assert row['idle']['active'] == [] and row['idle']['weights'] == [1,0,0,0]
                assert any(u['machine'] == 0 and u['state'] == 3 for u in row['updates'])
                gate_coverage['immediateFiringCancellation'] += 1
            else:
                assert row['idle']['active'][-1]['path'] == [7]
                gate_coverage['blendedCancellation'] += 1
            assert frame['montage'] if authored['gate'] == 'montage' else frame['main'][authored['gate']]
            cancellations += 1
        assert ('output' in row) == frame['evaluate']
        if 'output' in row:
            assert len(row['output']['pose']) == 81
            gate_coverage['poses'] += 1
            gate_coverage['curves'] += len(row['output']['curves'])
            gate_coverage['attributes'] += len(row['output']['attributes'])
    assert cancellations == 1
    gate_coverage[authored['gate']] += cancellations
assert gate_coverage['poses'] == 3780 and all(gate_coverage[g] == 9 for g in gates)
assert gate_coverage['immediateFiringCancellation'] == 9 and gate_coverage['blendedCancellation'] == 45
rule_probe = json.loads((artifacts/'idle-runtime-compiled-rules.json').read_bytes())
assert rule_probe['packages'] == native['assetSha256'] and len(rule_probe['fixtures']) == 642
for name,digest in rule_probe['fixtures'].items():
    assert sha((root/name).read_bytes()) == digest
for relative,digest in rule_probe['probeSourceSha256'].items():
    for source in (repo/'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter',
                   repo/'artifacts/unreal/gasp58-lyra-masks/source/AlsV4AssetExporter/Source/AlsV4AssetExporter',
                   repo/'artifacts/unreal/gasp58-lyra-masks/package/AlsV4AssetExporter/Source/AlsV4AssetExporter'):
        assert sha((source/relative).read_bytes()) == digest
rule_count = 0
for trace,authored in zip(rule_probe['result']['traces'],rule_probe['requests']['traces'],strict=True):
    for row,frame in zip(trace['rows'],authored['rows'],strict=True):
        rules = {(r['machine'],r['edge']):r for r in row['rules']}
        assert rules[1,5]['delegate'] == 33 and rules[1,5]['result'] == frame['main']['GameplayTag_IsFiring']
        assert rules[1,7]['result'] == (frame['montage'] or any(v for k,v in frame['main'].items() if k != 'RootYawOffset'))
        assert not rules[2,0]['result'] and not rules[2,1]['result']
        rule_count += 1
assert rule_count == 192
logs = {}
for name in ('idle-runtime-ue-v2.log','idle-runtime-ue-v2-repeat.log'):
    data = (artifacts/name).read_bytes(); text = data.decode('utf-8-sig')
    assert text.count('LYRA_IDLE_RUNTIME_NATIVE_OK frames=26460 poses=26268 assets=31 packages=508 previous=636 assets_saved=0') == 1, name
    assert text.count('LYRA_EXPORT_PROCESS_EXIT_OK mode=idle-runtime code=0') == 1, name
    assert not re.search(r'(Traceback \(most recent call last\)|Assertion failed:|Ensure condition failed:|Fatal error:|: Error:)',text), name
    logs[name] = dict(sha256=sha(data),warningCount=sum(': Warning:' in line for line in text.splitlines()))
for name in ('idle-runtime-gates-ue.log','idle-runtime-gates-ue-repeat.log'):
    data = (artifacts/name).read_bytes(); text = data.decode('utf-8-sig')
    assert text.count('LYRA_IDLE_RUNTIME_NATIVE_OK frames=26460 poses=3780 assets=31 packages=508 previous=639 assets_saved=0') == 1
    assert text.count('LYRA_EXPORT_PROCESS_EXIT_OK mode=idle-runtime code=0') == 1
    assert not re.search(r'(Traceback \(most recent call last\)|Assertion failed:|Ensure condition failed:|Fatal error:|: Error:)',text)
    logs[name] = dict(sha256=sha(data),warningCount=sum(': Warning:' in line for line in text.splitlines()))
for name,marker in {
    'idle-runtime-godot-final.log':'LYRA_IDLE_RUNTIME_GODOT_OK',
    'idle-runtime-machine-regression.log':'LYRA_MAIN_MACHINE_RUNTIME_GODOT_OK',
    'idle-runtime-pose-regression.log':'LYRA_LOCOMOTION_POSE_OK',
    'idle-runtime-ground-regression.log':'LYRA_MAIN_GROUND_NATIVE_GODOT_OK',
    'idle-runtime-air-regression.log':'LYRA_AIR_RUNTIME_GODOT_OK',
}.items():
    data = (artifacts/name).read_bytes(); text = data.decode('utf-8-sig')
    assert text.count(marker) == 1 and not re.search(r'(?m)^(ERROR:|WARNING:)',text), name
    if name == 'idle-runtime-godot-final.log':
        assert 'frames=26460 poses=26268 bones=2127708 clocks=132300' in text
        assert 'feedback=ownSubmittedCurve retry=true wholeMain=false production=false' in text
        assert text.count('LYRA_IDLE_GATES_GODOT_OK frames=26460 poses=3780 bones=306180 clocks=132300') == 1
        assert 'edges=0,5,7' in text
    logs[name] = dict(sha256=sha(data),results=[line for line in text.splitlines() if '_OK' in line])
for name in ('idle-runtime-debug-build-final.log','idle-runtime-optimize-build-final.log'):
    data = (artifacts/name).read_bytes(); text = data.decode('utf-8-sig')
    assert '已成功生成' in text and '0 个警告' in text and '0 个错误' in text, name
    logs[name] = dict(sha256=sha(data))
data = (artifacts/'idle-runtime-ue-build-v2.log').read_bytes(); text = data.decode('utf-8-sig')
assert 'BUILD SUCCESSFUL' in text and 'AutomationTool exiting with ExitCode=0' in text
assert not re.search(r'(warning C\d+|error C\d+)',text)
logs['idle-runtime-ue-build-v2.log'] = dict(sha256=sha(data))
for name in ('idle-runtime-rule-probe-ue.log','idle-runtime-rule-probe-ue-repeat.log'):
    data = (artifacts/name).read_bytes(); text = data.decode('utf-8-sig')
    assert text.count('LYRA_IDLE_COMPILED_RULES_NATIVE_OK cases=192 edge5=firing edge7=anyGate stance=false packages=508 fixtures=642 assets_saved=0') == 1
    assert text.count('LYRA_EXPORT_PROCESS_EXIT_OK mode=idle-rules code=0') == 1
    assert not re.search(r'(Traceback \(most recent call last\)|Assertion failed:|Ensure condition failed:|Fatal error:|: Error:)',text)
    logs[name] = dict(sha256=sha(data),warningCount=sum(': Warning:' in line for line in text.splitlines()))
data = (artifacts/'idle-runtime-rule-probe-build.log').read_bytes(); text = data.decode('utf-8-sig')
assert 'BUILD SUCCESSFUL' in text and 'AutomationTool exiting with ExitCode=0' in text
assert not re.search(r'(warning C\d+|error C\d+)',text)
logs['idle-runtime-rule-probe-build.log'] = dict(sha256=sha(data))
report = dict(schemaVersion=1,resourceSha256={n:sha((root/n).read_bytes()) for n in names+gate_names},
              counts=counts,coverage=dict(coverage),stateFrames=dict(states),selectedAssets=len(selected),
              gateCounts=gate_native['counts'],gateCoverage=dict(gate_coverage),compiledRuleCases=rule_count,protectedPackages=508,protectedPriorFixtures=642,ownSubmittedCurveFeedback=True,
              controlledMainInputs=True,wholeMain=False,production=False,wholeGoalComplete=False,logs=logs)
(artifacts/'idle-runtime-final-verification.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('LYRA_IDLE_RUNTIME_FINAL_VERIFIED frames=52920 poses=30048 bones=2433888 packages=508 fixtures=642 compiled_rules=192 whole_goal=false')
