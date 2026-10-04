"""Verify the complete SkeletalControls layer and fixed Provider fourteen-entry transaction."""
import hashlib
import json
from pathlib import Path

from locomotion_paths import project_path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
prefix = 'skeletal_controls_v1'
native = json.loads((root / (prefix + '_native.json')).read_bytes())
requests = json.loads((root / (prefix + '_requests.json')).read_bytes())
policy = json.loads((root / (prefix + '_policy.json')).read_bytes())
assert native['schemaVersion'] == policy['schemaVersion'] == 1
assert native['requestSha256'] == sha(root / (prefix + '_requests.json'))
assert native['policySha256'] == sha(root / (prefix + '_policy.json'))
assert native['dependencies'] == policy['dependencies'] and policy['stage'] == 'OriginalSkeletalControls'
assert policy['alphaNodes'] == [103, 102, 104, 110, 109, 105, 107, 106] and policy['definedInitialStorage'] and policy['skeleton'] == 'ALS81' and not policy['production']
for name, digest in native['dependencies'].items():
    assert sha(root / name) == digest, name
for name, digest in native['previousFixtureSha256'].items():
    assert sha(root / name) == digest, name
assert len(native['previousFixtureSha256']) == 679
for name, digest in native['assetSha256'].items():
    assert sha(project_path('Content') / (name.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, name
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
    foot = trace['initialFoot']
    legs = trace['initialLegs']
    feedback = dict.fromkeys(policy['curveNames'], 0)
    for row, frame in zip(trace['frames'], request['frames'], strict=True):
        assert row['before'] == prior and row['feedbackBefore'] == feedback
        assert row['footBefore'] == foot and row['legBefore'] == legs
        if frame['visited']:
            assert row['handWeight'] == policy['profiles'][trace['profile']]['handFKWeight']
        assert row['inputUpdates'] == int(frame['visited'])
        assert ('output' in row) == (frame['visited'] and frame['evaluate'])
        if 'output' in row:
            for name in ('input', 'output'):
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
        foot = row['footAfter']
        legs = row['legAfter']
        if frame['evaluateMain']:
            feedback = {n: frame['finalFeedback'].get(n, 0) for n in policy['curveNames']}
assert counts == native['counts'] and counts['frames'] == 3780
for name in ('skeletal-controls-ue-export.log', 'skeletal-controls-ue-export-repeat.log'):
    text = (logs / name).read_text(encoding='utf-8', errors='replace')
    assert text.count('LYRA_SKELETAL_CONTROLS_NATIVE_OK frames=3780 poses=' + str(counts['poses']) + ' previous=679 assets_saved=0') == 1, name
    assert text.count('LYRA_EXPORT_PROCESS_EXIT_OK mode=skeletal-controls code=0') == 1, name
    assert '0 error(s)' in text and 'Traceback' not in text and 'Error:' not in text and 'Handled ensure' not in text, name
assert json.loads((logs/'skeletal-controls-native-diagnostic.json').read_bytes())['traces'] == native['traces']
for name, marker in (
    ('skeletal-controls-godot-final.log', 'LYRA_SKELETAL_CONTROLS_GODOT_OK frames=3780 poses=3051'),
    ('skeletal-controls-main-scope-final.log', 'LYRA_MAIN_SKELETAL_SCOPE_GODOT_OK frames=11340 poses=9762'),
    ('skeletal-controls-foot-regression.log', 'LYRA_FOOT_PLACEMENT_GODOT_OK frames=3780 poses=3231'),
    ('skeletal-controls-leg-regression.log', 'LYRA_LEG_IK_GODOT_OK frames=3780 retries=3780'),
    ('skeletal-controls-hand-regression.log', 'LYRA_LOGICAL_HAND_CHAIN_OK cases=48'),
    ('skeletal-controls-update-regression.log', 'LYRA_SKELETAL_UPDATE_GODOT_OK frames=3780 poses=3051'),
    ('skeletal-controls-main-regression.log', 'LYRA_MAIN_ALS_NATIVE_GODOT_OK frames=11340 poses=9762 rootPoses=12595'),
    ('skeletal-controls-aiming-regression.log', 'LYRA_MAIN_AIMING_SCOPE_GODOT_OK frames=11340 poses=9762'),
):
    text = (logs / name).read_text(encoding='utf-8', errors='replace')
    assert text.count(marker) == 1 and 'ERROR:' not in text and 'WARNING:' not in text, name
scope = (logs/'skeletal-controls-main-scope-final.log').read_text(encoding='utf-8')
for marker in ('groupEntries=14', 'lateRetry=207', 'faults=84', 'previousControls=11304', 'footActive=5115', 'footPartial=3855',
               'commonSync=true', 'previousFeedback=true', 'nativeCombined=false', 'production=false'):
    assert marker in scope, marker
import xml.etree.ElementTree as ET
for name, count in (('core',24),('import',16)):
    trx = ET.parse(logs/('skeletal-controls-'+name+'.trx'))
    counters = trx.find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters').attrib
    assert int(counters['total']) == int(counters['passed']) == count and int(counters['failed']) == int(counters['notExecuted']) == 0
for name in ('skeletal-controls-debug-final.log', 'skeletal-controls-optimize-final.log'):
    text = (logs / name).read_text(encoding='utf-8', errors='replace')
    assert '0 个警告' in text and '0 个错误' in text, name
build = (logs / 'skeletal-controls-ue-build.log').read_text(encoding='utf-8', errors='replace')
assert 'AutomationTool exiting with ExitCode=0 (Success)' in build and 'GODOT_ALS_EXTERNAL_EXPORTER_OK' in build
print('LYRA_SKELETAL_CONTROLS_FINAL_VERIFIED frames=3780 poses=3051 packages=508 previous=679 groupEntries=14 oneFCSPose=true definedInitialStorage=true nativeCombined=false production=false whole_goal=false')
