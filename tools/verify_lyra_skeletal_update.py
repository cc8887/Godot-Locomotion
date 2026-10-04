"""Verify preserved fixtures and native/Godot SkeletalControls update boundaries."""
import hashlib
import json
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
prefix = 'skeletal_update_v1'
native = json.loads((root / (prefix + '_native.json')).read_bytes())
requests = json.loads((root / (prefix + '_requests.json')).read_bytes())
policy = json.loads((root / (prefix + '_policy.json')).read_bytes())
assert native['schemaVersion'] == policy['schemaVersion'] == 1
assert native['requestSha256'] == sha(root / (prefix + '_requests.json'))
assert native['policySha256'] == sha(root / (prefix + '_policy.json'))
assert native['dependencies'] == policy['dependencies'] and policy['stage'] == 'OriginalSkeletalControlUpdate'
assert policy['alphaNodes'] == [103, 102, 104, 110, 109, 105, 107, 106] and not policy['fullFootSolverPorted']
for name, digest in native['dependencies'].items():
    assert sha(root / name) == digest, name
for name, digest in native['previousFixtureSha256'].items():
    assert sha(root / name) == digest, name
assert len(native['previousFixtureSha256']) == 670
for name, digest in native['assetSha256'].items():
    assert sha(Path('../GASP58/Content') / (name.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, name
assert len(native['assetSha256']) == 508
for name, digest in native['probeSourceSha256'].items():
    for tree in (repo / 'tools/unreal/AlsV4AssetExporter',
                 repo / 'artifacts/unreal/gasp58-lyra-masks/source/AlsV4AssetExporter',
                 repo / 'artifacts/unreal/gasp58-lyra-masks/package/AlsV4AssetExporter'):
        assert sha(tree / 'Source/AlsV4AssetExporter' / name) == digest, (tree, name)
assert len(native['traces']) == len(requests['traces']) == 9
counts = dict(frames=0, poses=0, hidden=0, updateOnly=0, initialize=0, rootPartial=0, footPartial=0, footEvaluations=0, footAccumulated=0)
for trace, request in zip(native['traces'], requests['traces'], strict=True):
    assert (trace['profile'], trace['hz']) == (request['profile'], request['hz'])
    assert len(trace['frames']) == len(request['frames']) == trace['hz'] * 6
    prior = trace['initial']
    feedback = dict.fromkeys(policy['curveNames'], 0)
    for row, frame in zip(trace['frames'], request['frames'], strict=True):
        assert row['before'] == prior and row['feedbackBefore'] == feedback
        assert row['inputUpdates'] == int(frame['visited'])
        assert ('output' in row) == (frame['visited'] and frame['evaluate'])
        if 'output' in row:
            for name in ('input', 'output', 'rootOperator', 'weaponOperator'):
                assert len(row[name]['pose']) == 81
                for channel in ('curves', 'attributes', 'rootMotion'):
                    assert row[name].get(channel) == row['input'].get(channel)
            counts['poses'] += 1
            counts['footEvaluations'] += row['updated']['alphas'][5] > 9.999999747378752e-6
        counts['frames'] += 1
        counts['hidden'] += not frame['visited']
        counts['updateOnly'] += frame['visited'] and not frame['evaluate']
        counts['initialize'] += frame['initialize']
        counts['rootPartial'] += 0 < row['updated']['alphas'][2] < 1
        counts['footPartial'] += 0 < row['updated']['alphas'][5] < 1
        counts['footAccumulated'] += row['updated']['footDelta'] > frame['delta']
        prior = row['after']
        if frame['evaluateMain']:
            feedback = {n: frame['finalFeedback'].get(n, 0) for n in policy['curveNames']}
assert counts == native['counts'] and counts['frames'] == 3780
for name in ('skeletal-update-ue-export.log', 'skeletal-update-ue-export-repeat.log'):
    text = (logs / name).read_text(encoding='utf-8', errors='replace')
    assert text.count('LYRA_SKELETAL_UPDATE_NATIVE_OK frames=3780 poses=' + str(counts['poses']) + ' previous=670 assets_saved=0') == 1, name
    assert text.count('LYRA_EXPORT_PROCESS_EXIT_OK mode=skeletal-update code=0') == 1, name
    assert '0 error(s)' in text and 'Traceback' not in text and 'Error:' not in text and 'Handled ensure' not in text, name
for name, marker in (
    ('skeletal-update-godot-final.log', 'LYRA_SKELETAL_UPDATE_GODOT_OK frames=3780 poses=' + str(counts['poses'])),
    ('skeletal-update-leg-ik-regression.log', 'LYRA_LEG_IK_GODOT_OK frames=3780 retries=3780'),
    ('skeletal-update-main-als-regression.log', 'LYRA_MAIN_ALS_NATIVE_GODOT_OK frames=11340 poses=9762 rootPoses=12595'),
    ('skeletal-update-main-aiming-regression.log', 'LYRA_MAIN_AIMING_SCOPE_GODOT_OK frames=11340 poses=9762'),
):
    text = (logs / name).read_text(encoding='utf-8', errors='replace')
    assert text.count(marker) == 1 and 'ERROR:' not in text and 'WARNING:' not in text, name
for name in ('skeletal-update-debug-final.log', 'skeletal-update-optimize-final.log'):
    text = (logs / name).read_text(encoding='utf-8', errors='replace')
    assert '0 个警告' in text and '0 个错误' in text, name
build = (logs / 'skeletal-update-ue-build-qualified.log').read_text(encoding='utf-8', errors='replace')
assert 'AutomationTool exiting with ExitCode=0 (Success)' in build and 'GODOT_ALS_EXTERNAL_EXPORTER_OK' in build
print('LYRA_SKELETAL_UPDATE_FINAL_VERIFIED frames=3780 poses=' + str(counts['poses']) +
      ' packages=508 previous=670 graphEntries=13 update=true rootWeapon=true fullFootSolver=false production=false whole_goal=false')
