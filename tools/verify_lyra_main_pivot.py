"""Verify actual Main Pivot oracle, resource bytes and executed runtime gates."""
import ast
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

from locomotion_paths import project_path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
artifacts = repo / 'artifacts/lyra-analysis'
content = project_path('Content')
sha = lambda data: hashlib.sha256(data).hexdigest()
native = json.loads((root / 'main_pivot_native.json').read_bytes())
requests = json.loads((root / 'main_pivot_requests.json').read_bytes())
assert native['requestSha256'] == sha((root / 'main_pivot_requests.json').read_bytes())
for key, name in (('distanceSha256', 'pivot_runtime_distance.json'),
                  ('rootSha256', 'pivot_runtime_roots.json'), ('contractSha256', 'pivot_layer_graph.json')):
    assert native[key] == sha((root / name).read_bytes())
assert len(native['assetSha256']) == 508 and len(native['previousFixtureSha256']) == 619
for name, expected in (native['dependencies'] | native['previousFixtureSha256']).items():
    assert sha((root / name).read_bytes()) == expected, name
for path, expected in native['assetSha256'].items():
    assert sha((content / (path.split('.')[0].removeprefix('/Game/') + '.uasset')).read_bytes()) == expected, path
assert len(native['assets']) == 42
assert len(native['traces']) == len(requests['traces']) == 9
assert {(t['profile'], t['hz']) for t in native['traces']} == {
    (p, hz) for p in ('unarmed', 'pistol', 'rifle') for hz in (30, 60, 120)}
counts = dict(frames=0, poses=0, hidden=0, transitions=0, firstTransitions=0,
              initializations=0, hipFire=0, tickless=0, attributes=0, rootAbsent=0, latches=0)
for trace, authored in zip(native['traces'], requests['traces'], strict=True):
    assert trace['profile'] == authored['profile'] and trace['hz'] == authored['hz']
    assert trace['mainPivotBinding'] == dict(stateRoot=20, applyAdditive=23, linked=21, lean=22,
                                           state=4, becomeRelevant='SetUpPivotState', update='UpdatePivotState')
    assert len(trace['frames']) == len(authored['frames']) == trace['hz'] * 6
    for row, frame in zip(trace['frames'], authored['frames'], strict=True):
        assert 'authoredSnapshot' not in frame['observation']
        assert 'observation' in row and 'lean' in row and 'mainPivot' in row
        counts['frames'] += 1
        counts['poses'] += 'output' in row
        counts['hidden'] += 'output' not in row
        transition = [r for r in row['requests'] if r['useBlendMode']]
        counts['transitions'] += len(transition)
        counts['firstTransitions'] += bool(transition) and not any(s['inertialScope'] for s in row['sources'])
        counts['initializations'] += sum(s['initializations'] for s in row['sources'])
        counts['hipFire'] += row['hipFireActive']
        counts['tickless'] += sum(s['active'] and not s['tickRegistered'] for s in row['sources'])
        counts['latches'] += row['mainPivot']['becameRelevant']
        if 'output' in row:
            counts['rootAbsent'] += 'rootMotion' not in row['output']
            for stage in ('output', 'machineOutput'):
                assert len(row[stage]['pose']) == 81
                assert len(row[stage]['attributes']) in (0, 4)
                counts['attributes'] += len(row[stage]['attributes'])
assert counts == dict(frames=3780, poses=3528, hidden=252, transitions=60, firstTransitions=3,
                      initializations=285, hipFire=2157, tickless=3, attributes=28200, rootAbsent=3, latches=216)
logs = {}
for name, markers in (
    ('main-pivot-godot-final.log', ('LYRA_MAIN_PIVOT_GODOT_OK', 'LYRA_MAIN_LEAN_CLOCKS_OK')),
    ('main-pivot-regression-pivot_runtime.log', ('LYRA_PIVOT_RUNTIME_GODOT_OK',)),
    ('main-pivot-regression-pivot_machine.log', ('LYRA_PIVOT_MACHINE_GODOT_OK', 'LYRA_PIVOT_MACHINE_REENTRY_GODOT_OK')),
    ('main-pivot-regression-pivot_source.log', ('LYRA_PIVOT_SOURCE_GODOT_OK',)),
    ('main-pivot-regression-main_state_history.log', ('LYRA_MAIN_STATE_HISTORY_JOINT_OK',)),
    ('main-pivot-regression-main_start_lean.log', ('LYRA_MAIN_START_LEAN_GODOT_OK',)),
    ('main-pivot-regression-main_cycle_lean.log', ('LYRA_MAIN_CYCLE_LEAN_GODOT_OK',)),
    ('als-interface-logical-current.log', ('LYRA_LOGICAL_SOURCE_NATIVE_OK',)),
    ('als-interface-binding-current.log', ('LYRA_LINKED_BINDING_OK',)),
    ('als-interface-resources-current.log', ('LYRA_IDLE_RECOVERY_RESOURCES_GODOT_OK',))):
    text = (artifacts / name).read_text(encoding='utf-8-sig')
    assert not re.search(r'(?m)^(ERROR|WARNING):', text), name
    success = []
    for marker in markers:
        lines = [line for line in text.splitlines() if marker in line]
        assert len(lines) == 1, (name, marker)
        success.extend(lines)
    logs[name] = dict(sha256=sha((artifacts / name).read_bytes()), success=success)
for name in ('main-pivot-ue-export-first-success.log', 'main-pivot-ue-export.log'):
    text = (artifacts / name).read_text(encoding='utf-8-sig')
    assert text.count('LYRA_MAIN_PIVOT_NATIVE_OK traces=9 frames=3780 poseFrames=3528 functions=10 packages=508 previous=619 assets_saved=0') == 1
    assert 'LYRA_EXPORT_PROCESS_EXIT_OK mode=main-pivot code=0' in text
    assert not re.search(r'Error:|Fatal error|Assertion failed|Ensure condition failed', text), name
    logs[name] = dict(sha256=sha((artifacts / name).read_bytes()), errors=0,
                     warningLinesIncludingSummary=len(re.findall(r'Warning:', text)),
                     invalidFootstepTagLines=len(re.findall(r'Warning:.*Footstep', text)),
                     transientDependencyLines=len(re.findall(r'Warning:.*ConditionalPostLoad Dependency', text)),
                     emptySequenceLines=len(re.findall(r'Warning:.*does not have an anim sequence', text)))
for name in ('main-pivot-godot-build.log', 'main-pivot-optimize-build.log'):
    text = (artifacts / name).read_text(encoding='utf-8-sig')
    assert '0 个警告' in text and '0 个错误' in text
    logs[name] = dict(sha256=sha((artifacts / name).read_bytes()), errors=0, warnings=0)
assert 'BUILD SUCCESSFUL' in (artifacts / 'main-pivot-ue-build.log').read_text(encoding='utf-8-sig')
logs['main-pivot-ue-build.log'] = dict(sha256=sha((artifacts / 'main-pivot-ue-build.log').read_bytes()))
tests = ET.parse(artifacts / 'main-pivot-core.trx').find(
    './/{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters').attrib
assert tests['total'] == tests['passed'] == '87' and tests['failed'] == tests['notExecuted'] == '0'
for relative in ('Source/AlsV4AssetExporter/Private/AlsLyraCycleLibrary.cpp',
                 'Source/AlsV4AssetExporter/Public/AlsLyraGraphLibrary.h'):
    expected = sha((repo / 'tools/unreal/AlsV4AssetExporter' / relative).read_bytes())
    for stage in ('package', 'source'):
        assert sha((repo / 'artifacts/unreal/gasp58-lyra-masks' / stage / 'AlsV4AssetExporter' / relative).read_bytes()) == expected
ast.parse((repo / 'tools/unreal/export_lyra_main_pivot.py').read_text(encoding='utf-8'))
report = dict(schemaVersion=1, counts=counts, boneComparisons=571536, coreTests=tests,
              protectedPackages=508, protectedPreviousFixtures=619, logs=logs,
              resourceSha256={n: sha((root / n).read_bytes()) for n in ('main_pivot_requests.json', 'main_pivot_native.json')},
              scope='Original Main macro and StateResult20 -> ApplyAdditive23 -> original Linked21 Pivot provider / Lean22 on ALS81. Explicit root traversal and weights.',
              fullPivotProvider=True, mainPivotRoot=True, commonFourRootScope=False,
              fullMainStateMachine=False, ordinaryDemo=False, wholeGoalComplete=False)
(artifacts / 'main-pivot-final-verification.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print('LYRA_MAIN_PIVOT_FINAL_VERIFIED frames=3780 poses=7056 bones=571536 packages=508 fixtures=619 core=87 whole_goal=false')
