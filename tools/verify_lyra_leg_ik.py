"""Verify immutable original LegIK operator and continuous ALS81 bend history."""
import hashlib
import json
import xml.etree.ElementTree as ET
from pathlib import Path

from locomotion_paths import project_path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
prefix = 'leg_ik_v1'
native = json.loads((root / (prefix + '_native.json')).read_bytes())
requests = json.loads((root / (prefix + '_requests.json')).read_bytes())
policy = json.loads((root / (prefix + '_policy.json')).read_bytes())
assert native['schemaVersion'] == policy['schemaVersion'] == 1
assert native['requestSha256'] == sha(root / (prefix + '_requests.json'))
assert native['policySha256'] == sha(root / (prefix + '_policy.json'))
assert native['dependencies'] == policy['dependencies']
assert policy['stage'] == 'OriginalLegIK' and policy['skeleton'] == 'ALS81'
assert policy['runtimeCVars'] == {'a.AnimNode.LegIK.Enable': 1, 'a.AnimNode.LegIK.EnableTwoBone': 1, 'a.AnimNode.LegIK.ForceAlwaysSolve': 0}
for name, digest in native['dependencies'].items():
    assert sha(root / name) == digest, name
for name, digest in native['previousFixtureSha256'].items():
    assert sha(root / name) == digest, name
assert len(native['previousFixtureSha256']) == 667
for name, digest in native['assetSha256'].items():
    assert sha(project_path('Content') / (name.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, name
assert len(native['assetSha256']) == 508
for name, digest in native['probeSourceSha256'].items():
    for tree in (repo / 'tools/unreal/AlsV4AssetExporter',
                 repo / 'artifacts/unreal/gasp58-lyra-masks/source/AlsV4AssetExporter',
                 repo / 'artifacts/unreal/gasp58-lyra-masks/package/AlsV4AssetExporter'):
        assert sha(tree / 'Source/AlsV4AssetExporter' / name) == digest, (tree, name)
counts = dict(frames=0, poses=0, disabled=0, partial=0, recache=0, straight=0, changed=0, historyUpdates=0)
attributes = roots = 0
assert len(native['traces']) == len(requests['traces']) == 9
for trace, request in zip(native['traces'], requests['traces'], strict=True):
    assert (trace['profile'], trace['hz']) == (request['profile'], request['hz'])
    assert len(trace['frames']) == len(request['frames']) == trace['hz'] * 6
    prior = {'legs': [{'real': [0, 0, 0], 'base': [0, 0, 0]} for _ in range(2)]}
    for row, frame in zip(trace['frames'], request['frames'], strict=True):
        assert row['historyBefore'] == prior
        assert len(row['input']['pose']) == len(row['output']['pose']) == 81
        for channel in ('curves', 'attributes', 'rootMotion'):
            assert row['input'].get(channel) == row['output'].get(channel)
        assert row['changedBones'] in (0, 3, 6)
        if frame['alpha'] <= 9.999999747378752e-6:
            assert row['changedBones'] == 0 and row['history'] == prior
        counts['frames'] += 1
        counts['poses'] += 1
        counts['disabled'] += frame['alpha'] <= 9.999999747378752e-6
        counts['partial'] += 9.999999747378752e-6 < frame['alpha'] < 0.9999899864196777
        counts['recache'] += frame['recache']
        counts['straight'] += frame['straight']
        counts['changed'] += row['changedBones'] > 0
        counts['historyUpdates'] += row['history'] != prior
        prior = row['history']
        attributes += len(row['output']['attributes'])
        roots += 'rootMotion' in row['output']
assert counts == native['counts'] and counts['frames'] == 3780
assert counts['changed'] > 1000 and counts['historyUpdates'] > 100
for name in ('leg-ik-ue-export-fixed.log', 'leg-ik-ue-export-repeat.log'):
    text = (logs / name).read_text(encoding='utf-8', errors='replace')
    assert text.count('LYRA_LEG_IK_NATIVE_OK frames=3780 changed=' + str(counts['changed']) + ' previous=667 assets_saved=0') == 1, name
    assert text.count('LYRA_EXPORT_PROCESS_EXIT_OK mode=leg-ik code=0') == 1, name
    assert '0 error(s)' in text and 'Traceback' not in text and 'Error:' not in text and 'Handled ensure' not in text, name
for name, marker in (
    ('leg-ik-godot-final.log', 'LYRA_LEG_IK_GODOT_OK frames=3780 retries=3780'),
    ('leg-ik-hand-chain-regression.log', 'LYRA_LOGICAL_HAND_CHAIN_OK cases=48 stages=4 logical=81'),
    ('leg-ik-aiming-regression.log', 'LYRA_AIMING_LAYER_GODOT_OK frames=7560 poses=6075 samples=26917'),
    ('leg-ik-main-als-regression.log', 'LYRA_MAIN_ALS_NATIVE_GODOT_OK frames=11340 poses=9762 rootPoses=12595'),
    ('leg-ik-main-aiming-regression.log', 'LYRA_MAIN_AIMING_SCOPE_GODOT_OK frames=11340 poses=9762'),
):
    text = (logs / name).read_text(encoding='utf-8', errors='replace')
    assert text.count(marker) == 1 and 'ERROR:' not in text and 'WARNING:' not in text, name
text = (logs / 'leg-ik-godot-final.log').read_text(encoding='utf-8')
assert 'historyError=0 hidden=294 updateOnly=294' in text and 'rejected=27048 recache=30 disabled=1002 historyUpdates=1491' in text
assert 'positionCm=7.944109290391274E-15 quaternion=0 scale=0' in text
for name, total in (('leg-ik-core-regression.trx', 31), ('leg-ik-import-regression.trx', 88)):
    counters = ET.parse(logs / name).getroot().find('.//{*}Counters').attrib
    assert int(counters['total']) == int(counters['passed']) == int(counters['executed']) == total, name
    assert int(counters['failed']) == 0, name
for name in ('leg-ik-debug-final.log', 'leg-ik-optimize-final.log'):
    text = (logs / name).read_text(encoding='utf-8', errors='replace')
    assert '0 个警告' in text and '0 个错误' in text, name
build = (logs / 'leg-ik-ue-build-empty-guard-retry.log').read_text(encoding='utf-8', errors='replace')
assert 'AutomationTool exiting with ExitCode=0 (Success)' in build and 'GODOT_ALS_EXTERNAL_EXPORTER_OK' in build
print('LYRA_LEG_IK_FINAL_VERIFIED frames=3780 attributes=' + str(attributes) + ' roots=' + str(roots) +
      ' packages=508 previous=667 graphEntries=13 operator=true completeSkeletalControls=false production=false whole_goal=false')
