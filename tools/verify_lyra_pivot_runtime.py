"""Audit actual Pivot Update/Sync/Evaluate, protected bytes and runtime gates."""
import ast
import hashlib
import json
import re
import struct
import xml.etree.ElementTree as ET
from pathlib import Path

from locomotion_paths import project_path

repo = Path(__file__).resolve().parents[1]
root = repo/'assets/generated/lyra_als'
artifacts = repo/'artifacts/lyra-analysis'
content = project_path('Content')
sha = lambda data: hashlib.sha256(data).hexdigest()
names = ['pivot_runtime_'+s+'.json' for s in ('requests', 'distance', 'roots', 'native')]
requests, distance, roots, native = [json.loads((root/n).read_bytes()) for n in names]
request_sha = sha((root/names[0]).read_bytes())
assert native['requestSha256'] == distance['requestSha256'] == roots['requestSha256'] == request_sha
for key, name in (('distanceSha256', names[1]), ('rootSha256', names[2]), ('contractSha256', 'pivot_layer_graph.json')):
    assert native[key] == sha((root/name).read_bytes())
assert len(native['assetSha256']) == 508 and len(native['previousFixtureSha256']) == 614
for name, expected in (native['dependencies'] | native['previousFixtureSha256']).items():
    assert sha((root/name).read_bytes()) == expected, name
for path, expected in native['assetSha256'].items():
    assert sha((content/(path.split('.')[0].removeprefix('/Game/')+'.uasset')).read_bytes()) == expected, path
assert len(native['assets']) == len(roots['assets']) == 42 and len(distance['assets']) == 36
assert list(distance['assets']) == [a['path'] for a in native['assets'][:36]]
assert sum(len(a['probes']) for a in roots['assets'].values()) == 378
assert len(native['traces']) == len(requests['traces']) == 9
assert {(t['profile'], t['hz']) for t in requests['traces']} == {
    (p, hz) for p in ('unarmed', 'pistol', 'rifle') for hz in (30, 60, 120)}
graphs = json.loads((root/'pivot_layer_graph.json').read_bytes())
assert len(graphs['graphs']) == 9
for graph in graphs['graphs'].values():
    assert len(graph['nodes']) == 16
    by_id = {n['index']: n for n in graph['nodes']}
    assert by_id[58]['settings']['bUpdateBasePoseFirst'] is False
    assert all(by_id[n]['type'].endswith('.AnimNode_OrientationWarping') for n in (62, 68))
    assert all(by_id[n]['type'].endswith('.AnimNode_StrideWarping') for n in (65, 71))

f32_bits = lambda v: struct.unpack('<I', struct.pack('<f', v))[0]
counts = dict(frames=0, poses=0, bones=0, hipFire=0, hidden=0, firstTransitions=0,
              transitions=0, initializations=0, rootMotionAttributes=0)
for trace, authored in zip(native['traces'], requests['traces'], strict=True):
    assert trace['profile'] == authored['profile'] and trace['hz'] == authored['hz']
    assert len(trace['frames']) == len(authored['frames']) == trace['hz']*6
    assert authored['machineNode'] == 59 and authored['nodeIndices'] == [61, 67]
    assert authored['orientationNodes'] == [62, 68] and authored['strideNodes'] == [65, 71]
    assert authored['blendNode'] == 58 and authored['hipFireIndex'] == 57
    for row, frame in zip(trace['frames'], authored['frames'], strict=True):
        assert 'sources' not in frame and 'order' not in frame
        assert sum(s['visits'] for s in row['sources']) == int(frame['active'])
        counts['frames'] += 1
        counts['transitions'] += len(row['requests'])
        counts['firstTransitions'] += bool(row['requests']) and not any(s['inertialScope'] for s in row['sources'])
        counts['initializations'] += sum(s['initializations'] for s in row['sources'])
        counts['hipFire'] += row['hipFireActive']
        if not frame['active']:
            assert 'output' not in row and 'machineOutput' not in row
            counts['hidden'] += 1
            continue
        counts['poses'] += 1
        active = row['sources'][row['state']]
        assert active['visits'] == 1 and active['orientationAngleBits'] == f32_bits(frame['main']['LocalVelocityDirectionAngleWithOffset'])
        assert active['strideSpeedBits'] == f32_bits(frame['main']['DisplacementSpeed'])
        assert active['strideAlphaBits'] == f32_bits(max(0, min(1, row['shared']['StrideWarpingPivotAlpha'])))
        for stage in ('output', 'machineOutput'):
            output = row[stage]
            assert len(output['pose']) == 81 and len(output['attributes']) == 4
            assert output['rootMotion']['name'] == 'RootMotionDelta'
            counts['bones'] += 81
            counts['rootMotionAttributes'] += 1
assert counts == dict(frames=3780, poses=3528, bones=571536, hipFire=2157, hidden=252,
                     firstTransitions=108, transitions=810, initializations=1035, rootMotionAttributes=7056)

logs = {}
for name, markers in (
        ('pivot-runtime-godot-final.log', ('LYRA_PIVOT_RUNTIME_GODOT_OK',)),
        ('pivot-runtime-regression-pivot_machine.log', ('LYRA_PIVOT_MACHINE_GODOT_OK', 'LYRA_PIVOT_MACHINE_REENTRY_GODOT_OK')),
        ('pivot-runtime-regression-pivot_source.log', ('LYRA_PIVOT_SOURCE_GODOT_OK',)),
        ('pivot-runtime-regression-start_runtime.log', ('LYRA_START_RUNTIME_GODOT_OK',)),
        ('pivot-runtime-regression-stop_runtime.log', ('LYRA_STOP_RUNTIME_GODOT_OK',)),
        ('pivot-runtime-regression-main_state_history.log', ('LYRA_MAIN_STATE_HISTORY_JOINT_OK',))):
    text = (artifacts/name).read_text(encoding='utf-8-sig')
    assert not re.search(r'(?m)^(ERROR|WARNING):', text), name
    successes = []
    for marker in markers:
        lines = [line for line in text.splitlines() if marker in line]
        assert len(lines) == 1, (name, marker)
        successes.extend(lines)
    logs[name] = dict(sha256=sha((artifacts/name).read_bytes()), success=successes)
for name in ('pivot-runtime-ue-export-first-success.log', 'pivot-runtime-ue-export.log'):
    text = (artifacts/name).read_text(encoding='utf-8-sig')
    assert text.count('LYRA_PIVOT_RUNTIME_NATIVE_OK traces=9 frames=3780 poseFrames=3528 logical=81 sequences=42 packages=508 assets_saved=0') == 1
    assert not re.search(r'Error:|Fatal error|Assertion failed|Ensure condition failed', text), name
    assert 'LYRA_EXPORT_PROCESS_EXIT_OK mode=pivot-runtime code=0' in text
    logs[name] = dict(sha256=sha((artifacts/name).read_bytes()), errors=0,
                     warningLinesIncludingSummary=len(re.findall(r'Warning:', text)),
                     invalidFootstepTagLines=len(re.findall(r'Warning:.*Footstep', text)),
                     transientDependencyLines=len(re.findall(r'Warning:.*ConditionalPostLoad Dependency', text)))
for name in ('pivot-runtime-godot-build.log', 'pivot-runtime-optimize-build.log'):
    text = (artifacts/name).read_text(encoding='utf-8-sig')
    assert '0 个警告' in text and '0 个错误' in text
    logs[name] = dict(sha256=sha((artifacts/name).read_bytes()), errors=0, warnings=0)
assert 'BUILD SUCCESSFUL' in (artifacts/'pivot-runtime-ue-build.log').read_text(encoding='utf-8-sig')
logs['pivot-runtime-ue-build.log'] = dict(sha256=sha((artifacts/'pivot-runtime-ue-build.log').read_bytes()))
tests = ET.parse(artifacts/'pivot-runtime-core.trx').find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters').attrib
assert tests['total'] == tests['passed'] == '87' and tests['failed'] == tests['notExecuted'] == '0'
for relative in ('Source/AlsV4AssetExporter/Private/AlsLyraCycleLibrary.cpp',
                 'Source/AlsV4AssetExporter/Public/AlsLyraGraphLibrary.h'):
    expected = sha((repo/'tools/unreal/AlsV4AssetExporter'/relative).read_bytes())
    for stage in ('package', 'source'):
        assert sha((repo/'artifacts/unreal/gasp58-lyra-masks'/stage/'AlsV4AssetExporter'/relative).read_bytes()) == expected
for script in ('export_lyra_pivot_runtime.py',):
    ast.parse((repo/'tools/unreal'/script).read_text(encoding='utf-8'))
files = names+['pivot_layer_graph.json']
report = dict(schemaVersion=1, counts=counts, coreTests=tests, protectedPackages=508, protectedPreviousFixtures=614,
              newResourceSha256={n: sha((root/n).read_bytes()) for n in files}, logs=logs,
              scope='Original Pivot16-node provider Update/Sync/Evaluate, independent state Warps followed by outer HipFire on ALS81. Explicit Main observations.',
              pivotMachinePose=True, outerHipFire=True, mainPivotRoot=False, ordinaryDemo=False, wholeGoalComplete=False)
(artifacts/'pivot-runtime-final-verification.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print('LYRA_PIVOT_RUNTIME_FINAL_VERIFIED frames=3780 poses=7056 bones=571536 packages=508 fixtures=614 core=87 whole_goal=false')
