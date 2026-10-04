"""Verify original Main composition operators without closing the whole Main gate."""
import hashlib
import json
import struct
from pathlib import Path

from locomotion_paths import project_path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
f32 = lambda v: struct.unpack('<f', struct.pack('<f', v))[0]
prefix = 'main_composition_v2'
native = json.loads((root / (prefix + '_native.json')).read_bytes())
requests = json.loads((root / (prefix + '_requests.json')).read_bytes())
policy = json.loads((root / (prefix + '_policy.json')).read_bytes())
assert native['schemaVersion'] == policy['schemaVersion'] == 1
assert native['requestSha256'] == sha(root / (prefix + '_requests.json'))
assert native['policySha256'] == sha(root / (prefix + '_policy.json'))
assert native['dependencies'] == policy['dependencies']
assert policy['stage'] == 'OriginalMainCompositionOperators' and policy['nodes'] == [0, 3, 76, 72]
assert policy['skeleton'] == 'ALS81'
for name, digest in native['dependencies'].items():
    assert sha(root / name) == digest, name
for name, digest in native['previousFixtureSha256'].items():
    assert sha(root / name) == digest, name
assert len(native['previousFixtureSha256']) == 685
for name, digest in native['assetSha256'].items():
    assert sha(project_path('Content') / (name.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, name
assert len(native['assetSha256']) == 508
for name, digest in native['probeSourceSha256'].items():
    for tree in (repo / 'tools/unreal/AlsV4AssetExporter',
                 repo / 'artifacts/unreal/gasp58-lyra-masks/source/AlsV4AssetExporter',
                 repo / 'artifacts/unreal/gasp58-lyra-masks/package/AlsV4AssetExporter'):
        assert sha(tree / 'Source/AlsV4AssetExporter' / name) == digest, (tree, name)
assert len(native['traces']) == len(requests['traces']) == 9
counts = dict(frames=0, poses=0, hidden=0, updateOnly=0)
for trace, request in zip(native['traces'], requests['traces'], strict=True):
    assert (trace['profile'], trace['hz']) == (request['profile'], request['hz'])
    assert len(trace['frames']) == len(request['frames']) == trace['hz'] * 6
    authored = {r['bone'].lower(): r['scale'] for r in trace['sourceMask']}
    names = json.loads((root / 'logical_controls/calibration.json').read_bytes())['layout']['logicalBoneNames']
    assert trace['mask'] == [authored.get(n.lower(), 0) for n in names]
    assert len(trace['mask']) == 81 and trace['mask'][0] == 0
    dynamic = recovery = yaw = 0
    for row, frame in zip(trace['frames'], request['frames'], strict=True):
        if frame['visited']:
            dynamic = min(1, max(0, f32(frame['dynamicWeight'])))
            recovery = f32(.65)
            yaw = f32(frame['rootYaw'])
            ids = [1, 0] + ([2] if dynamic > f32(1e-5) else []) + [4, 3]
            assert [r['leaf'] for r in row['updates']] == ids
            for u in row['updates']:
                alpha = dynamic if u['leaf'] == 2 else recovery if u['leaf'] == 3 else 1
                assert u['weight'] == f32(frame['weight'] * alpha)
        else:
            assert not row['updates']
        assert (row['dynamicAlpha'], row['recoveryAlpha'], row['yaw'], row['pitch'], row['splitWeight']) == (dynamic, recovery, yaw, 0, 1)
        assert ('upper' in row) == ('final' in row) == (frame['visited'] and frame['evaluate'])
        if 'upper' in row:
            assert len(row['upper']['pose']) == len(row['final']['pose']) == 81
            counts['poses'] += 1
        counts['frames'] += 1
        counts['hidden'] += not frame['visited']
        counts['updateOnly'] += frame['visited'] and not frame['evaluate']
assert counts == native['counts'] == dict(frames=3780, poses=3078, hidden=189, updateOnly=513)
for name in ('main-composition-ue-export2.log', 'main-composition-ue-export-repeat.log'):
    text = (logs / name).read_text(encoding='utf-8', errors='replace')
    assert text.count('LYRA_MAIN_COMPOSITION_NATIVE_OK frames=3780 poses=3078 previous=685 assets_saved=0') == 1
    assert text.count('LYRA_EXPORT_PROCESS_EXIT_OK mode=main-composition code=0') == 1
    assert '0 error(s)' in text and 'Traceback' not in text and 'Error:' not in text and 'Handled ensure' not in text, name
assert json.loads((logs / 'main-composition-native-diagnostic.json').read_bytes())['traces'] == native['traces']
for name, marker in (
    ('main-composition-godot2.log', 'LYRA_MAIN_COMPOSITION_GODOT_OK frames=3780 poses=3078'),
    ('main-composition-scope-final2.log', 'LYRA_MAIN_COMPOSITION_SCOPE_GODOT_OK frames=11340 poses=9762'),
    ('main-composition-main-regression.log', 'LYRA_MAIN_ALS_NATIVE_GODOT_OK frames=11340 poses=9762 rootPoses=12595'),
):
    text = (logs / name).read_text(encoding='utf-8', errors='replace')
    assert text.count(marker) == 1 and 'ERROR:' not in text and 'WARNING:' not in text, name
scope = (logs / 'main-composition-scope-final2.log').read_text(encoding='utf-8')
for marker in ('groupEntries=14', 'lateRetry=207', 'faults=84', 'commonSync=true', 'previousFeedback=true',
               'inactiveSlotBoundaries=true', 'inertia=false', 'controlRig=false', 'nativeCombined=false', 'production=false'):
    assert marker in scope, marker
for name in ('main-composition-debug-final2.log', 'main-composition-optimize-final2.log'):
    text = (logs / name).read_text(encoding='utf-8', errors='replace')
    assert '0 个警告' in text and '0 个错误' in text, name
build = (logs / 'main-composition-ue-build2.log').read_text(encoding='utf-8', errors='replace')
assert 'AutomationTool exiting with ExitCode=0 (Success)' in build and 'GODOT_ALS_EXTERNAL_EXPORTER_OK' in build
# Failed v1 remains immutable evidence, rather than being overwritten by v2.
assert all((root / ('main_composition_v1_' + k + '.json')).is_file() for k in ('requests', 'policy', 'native'))
assert 'Handled ensure' in (logs / 'main-composition-ue-export.log').read_text(encoding='utf-8', errors='replace')
print('LYRA_MAIN_COMPOSITION_FINAL_VERIFIED frames=3780 poses=3078 packages=508 previous=685 groupEntries=14 nodes=0,3,76,72 nativeCombined=false production=false whole_goal=false')
