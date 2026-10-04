"""Audit the actual PivotSM fixture, runtime gates and protected resource bytes."""
import ast
import hashlib
import json
import re
import struct
from pathlib import Path

from locomotion_paths import project_path

repo = Path(__file__).resolve().parents[1]
root = repo/'assets/generated/lyra_als'
artifacts = repo/'artifacts/lyra-analysis'
content = project_path('Content')
sha = lambda data: hashlib.sha256(data).hexdigest()
fixtures, matrices = {}, {}
for prefix, previous_count in (('pivot_machine', 608), ('pivot_machine_reentry', 611)):
    names = [prefix+'_'+suffix+'.json' for suffix in ('requests', 'definitions', 'native')]
    requests, definitions, native = [json.loads((root/name).read_bytes()) for name in names]
    assert native['requestSha256'] == definitions['requestSha256'] == sha((root/names[0]).read_bytes())
    assert len(native['assetSha256']) == 508 and len(native['previousFixtureSha256']) == previous_count
    for name, expected in (native['dependencies'] | native['previousFixtureSha256']).items():
        assert sha((root/name).read_bytes()) == expected, name
    for path, expected in native['assetSha256'].items():
        package = content/(path.split('.')[0].removeprefix('/Game/')+'.uasset')
        assert sha(package.read_bytes()) == expected, path
    assert len(native['traces']) == len(requests['traces']) == 9 and len(definitions['assets']) == 36
    assert {(t['profile'], t['hz']) for t in requests['traces']} == {
        (profile, hz) for profile in ('unarmed', 'pistol', 'rifle') for hz in (30, 60, 120)}
    counts = dict(frames=0, active=0, transitions=0, initializations=0, firstTransitions=0, inertialScopes=0)
    for trace, authored in zip(native['traces'], requests['traces']):
        assert trace['profile'] == authored['profile'] and trace['hz'] == authored['hz']
        assert authored['machineNode'] == 59 and authored['nodeIndices'] == [61, 67] and authored['ruleIndex'] == 72
        assert len(trace['frames']) == len(authored['frames']) == authored['hz']*6
        for row, frame in zip(trace['frames'], authored['frames']):
            assert 'sources' not in frame and 'order' not in frame, 'Machine input must not author child traversal'
            visits = [s['visits'] for s in row['sources']]
            assert sum(visits) == int(frame['active']) and all(v in (0, 1) for v in visits)
            assert row['state'] in (0, 1) and (not frame['active'] or visits[row['state']] == 1)
            assert row['stateWeightA'] == int(row['state'] == 0) and row['stateWeightB'] == int(row['state'] == 1)
            weight_bits = struct.unpack('<I', struct.pack('<f', frame['weight']))[0]
            assert all(s['visitWeightBits'] == (weight_bits if s['visits'] else 0) for s in row['sources'])
            assert len(row['requests']) <= 1
            for request in row['requests']:
                assert request['durationBits'] == 1053609165 and request['useBlendMode'] and request['blendMode'] == 2
                assert request['profile'] == '/Game/Characters/Heroes/Mannequin/Meshes/SK_Mannequin.SK_Mannequin:FastFeet'
            inertial = any(s['inertialScope'] for s in row['sources'])
            counts['frames'] += 1
            counts['active'] += sum(visits)
            counts['transitions'] += len(row['requests'])
            counts['initializations'] += sum(s['initializations'] for s in row['sources'])
            counts['firstTransitions'] += bool(row['requests']) and not inertial
            counts['inertialScopes'] += inertial
    assert counts['frames'] == 3780 and counts['active'] == 3528 and counts['transitions'] == 810
    assert counts['firstTransitions'] == (108 if prefix.endswith('reentry') else 0)
    assert counts['initializations'] == (1035 if prefix.endswith('reentry') else 927)
    assert counts['inertialScopes'] == counts['transitions'] - counts['firstTransitions']
    matrices[prefix] = counts
    fixtures.update({name: sha((root/name).read_bytes()) for name in names})

logs = {}
for name, markers in (
        ('pivot-machine-godot-final.log', ('LYRA_PIVOT_MACHINE_GODOT_OK', 'LYRA_PIVOT_MACHINE_REENTRY_GODOT_OK')),
        ('pivot-machine-source-regression-final.log', ('LYRA_PIVOT_SOURCE_GODOT_OK',)),
        ('pivot-machine-main-regression.log', ('LYRA_MAIN_STATE_HISTORY_JOINT_OK',))):
    text = (artifacts/name).read_text(encoding='utf-8-sig')
    assert not re.search(r'(?m)^(ERROR|WARNING):', text), name
    successes = []
    for marker in markers:
        lines = [line for line in text.splitlines() if marker in line]
        assert len(lines) == 1, (name, marker)
        successes.extend(lines)
    logs[name] = dict(sha256=sha((artifacts/name).read_bytes()), success=successes)
for name, marker in (
        ('pivot-machine-ue-export.log', 'LYRA_PIVOT_MACHINE_NATIVE_OK'),
        ('pivot-machine-reentry-ue-export.log', 'LYRA_PIVOT_MACHINE_REENTRY_NATIVE_OK'),
        ('pivot-source-ue-export.log', 'LYRA_PIVOT_SOURCE_NATIVE_OK')):
    text = (artifacts/name).read_text(encoding='utf-8-sig')
    assert text.count(marker+' traces=9 frames=3780 packages=508 assets=36 assets_saved=0') == 1, name
    assert not re.search(r'Error:|Fatal error|Assertion failed|Ensure condition failed', text), name
    assert 'LogExit: Exiting.' in text, name
    logs[name] = dict(sha256=sha((artifacts/name).read_bytes()), errors=0,
                     warningLinesIncludingSummary=len(re.findall(r'Warning:', text)))
for name in ('pivot-machine-debug-final-build.log', 'pivot-machine-optimize-build.log'):
    text = (artifacts/name).read_text(encoding='utf-8-sig')
    assert '0 个警告' in text and '0 个错误' in text, name
    logs[name] = dict(sha256=sha((artifacts/name).read_bytes()), errors=0, warnings=0)
assert 'BUILD SUCCESSFUL' in (artifacts/'pivot-machine-ue-build-cleanup.log').read_text(encoding='utf-8-sig')
logs['pivot-machine-ue-build-cleanup.log'] = dict(sha256=sha((artifacts/'pivot-machine-ue-build-cleanup.log').read_bytes()))
for relative in ('Source/AlsV4AssetExporter/Private/AlsLyraCycleLibrary.cpp',
                 'Source/AlsV4AssetExporter/Public/AlsLyraGraphLibrary.h'):
    expected = sha((repo/'tools/unreal/AlsV4AssetExporter'/relative).read_bytes())
    for stage in ('package', 'source'):
        assert sha((repo/'artifacts/unreal/gasp58-lyra-masks'/stage/'AlsV4AssetExporter'/relative).read_bytes()) == expected
ast.parse((repo/'tools/unreal/export_lyra_pivot_source.py').read_text(encoding='utf-8'))
report = dict(schemaVersion=1, matrices=matrices, protectedPackages=508, protectedPreviousFixtures=611,
              newResourceSha256=fixtures, logs=logs,
              scope='Original PivotSM update, actual state-child visits/entry initialization, callbacks, requests and common Sync with explicit Main observations.',
              pivotMachineUpdate=True, pivotMachinePose=False, outerHipFire=False, mainPivotRoot=False,
              ordinaryDemo=False, wholeGoalComplete=False)
(artifacts/'pivot-machine-final-verification.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print('LYRA_PIVOT_MACHINE_FINAL_VERIFIED frames=7560 packages=508 fixtures=611 firstTransitions=108 whole_goal=false')
