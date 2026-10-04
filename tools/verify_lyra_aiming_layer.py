"""Verify immutable original Aiming capture and its staged shared-group integration."""
import hashlib
import json
from pathlib import Path

from locomotion_paths import project_path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
prefix = 'aiming_layer_v1'
native = json.loads((root / (prefix + '_native.json')).read_bytes())
requests = json.loads((root / (prefix + '_requests.json')).read_bytes())
policy = json.loads((root / (prefix + '_policy.json')).read_bytes())
assert native['schemaVersion'] == policy['schemaVersion'] == 1
assert native['requestSha256'] == sha(root / (prefix + '_requests.json'))
assert native['policySha256'] == sha(root / (prefix + '_policy.json'))
assert native['dependencies'] == policy['dependencies']
assert policy['stage'] == 'OriginalFullBody_Aiming' and policy['skeleton'] == 'ALS81'
for name, digest in native['dependencies'].items():
    assert sha(root / name) == digest, name
for name, digest in native['previousFixtureSha256'].items():
    assert sha(root / name) == digest, name
assert len(native['previousFixtureSha256']) == 664
for name, digest in native['assetSha256'].items():
    assert sha(project_path('Content') / (name.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, name
assert len(native['assetSha256']) == 508
for name, digest in native['probeSourceSha256'].items():
    for tree in (repo / 'tools/unreal/AlsV4AssetExporter',
                 repo / 'artifacts/unreal/gasp58-lyra-masks/source/AlsV4AssetExporter',
                 repo / 'artifacts/unreal/gasp58-lyra-masks/package/AlsV4AssetExporter'):
        assert sha(tree / 'Source/AlsV4AssetExporter' / name) == digest, (tree, name)
assert native['counts'] == dict(frames=7560, poses=6075, hidden=474, updateOnly=1011, mixed=1939, twoSources=6291)
# The immutable capture's twoSources counter counts retained nonzero node
# weights as well. Actual simultaneous visits are measured by mixed instead.
assert [s['profile'] for s in policy['spaces']] == ['unarmed', 'pistol', 'rifle']
for space in policy['spaces']:
    assert len(space['samples']) == 15 and not space['useGrid'] and not space['meshBlend']
    assert space['weightSpeed'] == 0 and space['ease'] and space['legacyLength']
    assert space['perBoneOverrides'] == 0 and 'BSA_NONE' in space['axisToScale']
    expected_time = 0.20000000298023224 if space['profile'] == 'unarmed' else 0
    assert space['filters'][0]['time'] == expected_time
    assert all(f['time'] == 0 for f in space['filters'][1:])
    if expected_time:
        assert 'BSIT_SPRING_DAMPER' in space['filters'][0]['type'] and space['filters'][0]['damping'] == 1
    assert all(not a['wrap'] for a in space['axes'])
    for sample in space['samples']:
        assert sample['rate'] == 1 and not sample['singleFrame'] and not sample['mirror'] and not sample['sync']['markers']
assert len(native['traces']) == len(requests['traces']) == 9
poses = attributes = curve_flags = 0
for trace, request in zip(native['traces'], requests['traces'], strict=True):
    assert (trace['profile'], trace['hz']) == (request['profile'], request['hz'])
    assert len(trace['frames']) == len(request['frames'])
    for row, frame in zip(trace['frames'], request['frames'], strict=True):
        assert row['inputUpdates'] == int(frame['visited'])
        assert ('output' in row) == (frame['visited'] and frame['evaluate'])
        if 'output' in row:
            assert row['inputEvaluations'] == 1 and len(row['output']['pose']) == len(row['input']['pose']) == 81
            assert row['output']['curves'] == row['input']['curves']
            attributes += len(row['output']['attributes'])
            curve_flags |= row['output']['curves']['Distance']['flags']
            poses += 1
assert poses == 6075 and attributes == 24300 and curve_flags == 3
for name in ('aiming-layer-ue-export.log', 'aiming-layer-ue-export-repeat.log'):
    text = (logs / name).read_text(encoding='utf-8', errors='replace')
    assert text.count('LYRA_AIMING_LAYER_NATIVE_OK frames=7560 poses=6075 mixed=1939 previous=664 assets_saved=0') == 1, name
    assert text.count('LYRA_EXPORT_PROCESS_EXIT_OK mode=aiming-layer code=0') == 1, name
    assert '0 error(s)' in text and 'Traceback' not in text and 'Error:' not in text and 'Handled ensure' not in text, name
for name, marker in (
    ('aiming-layer-godot-final.log', 'LYRA_AIMING_LAYER_GODOT_OK frames=7560 poses=6075 samples=26917'),
    ('aiming-layer-main-scope-final.log', 'LYRA_MAIN_AIMING_SCOPE_GODOT_OK frames=11340 poses=9762'),
    ('aiming-layer-main-additives-final.log', 'LYRA_MAIN_ADDITIVES_PIPELINE_OK frames=11340 poses=9762 recoveryTicks=530 recoveryPoses=456 graphEntries=12'),
    ('aiming-layer-logical-regression.log', 'LYRA_LOGICAL_SOURCE_NATIVE_OK sources=234 raw=69 logical=81 skin=68 cases=936'),
):
    text = (logs / name).read_text(encoding='utf-8', errors='replace')
    assert text.count(marker) == 1 and 'ERROR:' not in text and 'WARNING:' not in text, name
scope = (logs / 'aiming-layer-main-scope-final.log').read_text(encoding='utf-8')
assert 'groupEntries=13 commonSync=true previousFeedback=true cachedInputContext=true stagedInput=true nativeCombined=false production=false' in scope
for name in ('aiming-layer-debug-final.log', 'aiming-layer-optimize-final.log'):
    text = (logs / name).read_text(encoding='utf-8', errors='replace')
    assert '0 个警告' in text and '0 个错误' in text, name
build = (logs / 'aiming-layer-ue-build-root.log').read_text(encoding='utf-8', errors='replace')
assert 'AutomationTool exiting with ExitCode=0 (Success)' in build and 'GODOT_ALS_EXTERNAL_EXPORTER_OK' in build
print('LYRA_AIMING_LAYER_FINAL_VERIFIED frames=7560 poses=6075 attributes=24300 graphEntries=13 '
      'mainFrames=11340 mainPoses=9762 packages=508 previous=664 nativeLayer=true nativeCombined=false production=false whole_goal=false')
