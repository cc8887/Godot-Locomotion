"""Verify the independent LeftHand node capture and its Main composition evidence."""
import hashlib
import json
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'
prefix = 'left_hand_layer_v4'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
native = json.loads((root / (prefix + '_native.json')).read_bytes())
requests = json.loads((root / (prefix + '_requests.json')).read_bytes())
policy = json.loads((root / (prefix + '_policy.json')).read_bytes())
assert native['schemaVersion'] == policy['schemaVersion'] == 1
assert sha(root / (prefix + '_requests.json')) == native['requestSha256']
assert sha(root / (prefix + '_policy.json')) == native['policySha256']
assert native['dependencies'] == policy['dependencies']
for name, digest in native['dependencies'].items():
    assert sha(root / name) == digest, name
for name, digest in native['previousFixtureSha256'].items():
    assert sha(root / name) == digest, name
assert len(native['previousFixtureSha256']) == 655
for name, digest in native['assetSha256'].items():
    asset = Path('../GASP58/Content') / (name.split('.')[0].removeprefix('/Game/') + '.uasset')
    assert sha(asset) == digest, name
assert len(native['assetSha256']) == 508
for name, digest in native['probeSourceSha256'].items():
    for tree in (repo / 'tools/unreal/AlsV4AssetExporter',
                 repo / 'artifacts/unreal/gasp58-lyra-masks/source/AlsV4AssetExporter',
                 repo / 'artifacts/unreal/gasp58-lyra-masks/package/AlsV4AssetExporter'):
        assert sha(tree / 'Source/AlsV4AssetExporter' / name) == digest, (tree, name)
assert native['counts'] == dict(frames=3780, poses=3078, hidden=189, updateOnly=513, applied=1401, feedbackChanges=516)
assert len(requests['sequencePaths']) == 6 and len(native['traces']) == 9
assert policy['stage'] == 'OriginalLeftHandLayer' and policy['skeleton'] == 'ALS81' and policy['nullSequence']
changed = 0
for trace, request in zip(native['traces'], requests['traces'], strict=True):
    assert trace['profile'] == request['profile'] and trace['hz'] == request['hz']
    assert policy['policies'][trace['profile']] == dict(mask=trace['mask'], curveBindings=trace['curveBindings'])
    assert len(trace['mask']) == 81 and trace['mask'][0] == 0 and any(trace['mask'])
    for row, frame in zip(trace['frames'], request['frames'], strict=True):
        assert row['childAssetNull'] and row['inputUpdates'] == int(frame['active'])
        assert ('output' in row) == (frame['active'] and frame['evaluate'])
        if 'output' in row:
            assert len(row['input']['pose']) == len(row['output']['pose']) == 81
            # Count visible transform changes, not just a nonzero alpha.
            changed += row['input']['pose'] != row['output']['pose']
assert changed == 1401, changed
for name in ('left-hand-layer-ue-export-cache.log', 'left-hand-layer-ue-export-repeat.log'):
    text = (logs / name).read_text(encoding='utf-8', errors='replace')
    assert text.count('LYRA_LEFT_HAND_LAYER_NATIVE_OK frames=3780 poses=3078 applied=1401 previous=655 assets_saved=0') == 1, name
    assert 'LYRA_EXPORT_PROCESS_EXIT_OK mode=left-hand-layer code=0' in text, name
    assert 'Success - 0 error(s), 794 warning(s)' in text, name
    assert 'Traceback' not in text and 'Error:' not in text and 'Handled ensure' not in text, name
for name, marker in (
    ('left-hand-layer-godot-final.log', 'LYRA_LEFT_HAND_LAYER_GODOT_OK frames=3780 poses=3078 applied=1401 updateOnly=513 hidden=189 retries=3780 rejected=6156'),
    ('left-hand-layer-main-pipeline-final.log', 'LYRA_MAIN_LEFT_HAND_PIPELINE_OK frames=11340 poses=9762 graphEntries=11 ownSync=true lateRetries=207'),
    ('left-hand-layer-main-native-regression.log', 'LYRA_ITEM_LAYER_EXECUTION_OK frames=11340 calls=12595 entries=10 contractEntries=14 rejected=510'),
):
    text = (logs / name).read_text(encoding='utf-8', errors='replace')
    assert text.count(marker) == 1 and 'ERROR:' not in text and 'WARNING:' not in text, name
pipeline = (logs / 'left-hand-layer-main-pipeline-final.log').read_text(encoding='utf-8')
assert 'entries=10 contractEntries=14 rejected=618' in pipeline
assert 'boundary=LeftHandPose_OverrideState fixedProvider=true nativeMainAndLayerComponents=true nativeJointBoundary=false production=false' in pipeline
for name in ('left-hand-layer-debug-final.log', 'left-hand-layer-optimize-final.log'):
    text = (logs / name).read_text(encoding='utf-8', errors='replace')
    assert '0 个警告' in text and '0 个错误' in text, name
build = (logs / 'left-hand-layer-ue-build-cache.log').read_text(encoding='utf-8', errors='replace')
assert 'AutomationTool exiting with ExitCode=0 (Success)' in build and 'GODOT_ALS_EXTERNAL_EXPORTER_OK' in build
print('LYRA_LEFT_HAND_LAYER_FINAL_VERIFIED frames=3780 poses=3078 changed=1401 graphEntries=11 '
      'mainFrames=11340 mainPoses=9762 packages=508 previous=655 nativeLayer=true nativeJointBoundary=false production=false whole_goal=false')
