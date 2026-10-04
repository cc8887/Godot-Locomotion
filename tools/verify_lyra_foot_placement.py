"""Preserve raw nonrepeatability evidence; qualify only the defined-storage operator."""
import hashlib
import json
from pathlib import Path

from locomotion_paths import project_path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
load = lambda p: json.loads(p.read_bytes())
n = load(root / 'foot_placement_v2_native.json')
r = load(root / 'foot_placement_v2_requests.json')
p = load(root / 'foot_placement_v2_policy.json')
assert n['requestSha256'] == sha(root / 'foot_placement_v2_requests.json')
assert n['policySha256'] == sha(root / 'foot_placement_v2_policy.json')
assert p['stage'] == 'OriginalFootPlacement' and p['skeleton'] == 'ALS81'
assert p['definedInitialStorage'] and p['resolvedAlphaInput'] and not p['production']
assert p['manualSpeedFallback'] == 60 and p['lockType'] == 'Unlocked'
assert n['dependencies'] == p['dependencies']
for name, digest in n['dependencies'].items():
    assert sha(root / name) == digest, name
for name, digest in n['previousFixtureSha256'].items():
    assert sha(root / name) == digest, name
assert len(n['previousFixtureSha256']) == 676
for name, digest in n['assetSha256'].items():
    assert sha(project_path('Content') / (name.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, name
assert len(n['assetSha256']) == 508
for prefix in ('foot_placement_v1', 'foot_placement_v2'):
    for name, digest in load(root / (prefix + '_native.json'))['probeSourceSha256'].items():
        for tree in (repo / 'tools/unreal/AlsV4AssetExporter',
                     repo / 'artifacts/unreal/gasp58-lyra-masks/source/AlsV4AssetExporter',
                     repo / 'artifacts/unreal/gasp58-lyra-masks/package/AlsV4AssetExporter'):
            assert sha(tree / 'Source/AlsV4AssetExporter' / name) == digest, (tree, name)
counts = dict(frames=0, poses=0, active=0, hits=0, grounded=0, hidden=0, updateOnly=0, initialize=0)
assert len(n['traces']) == len(r['traces']) == 9
for trace, request in zip(n['traces'], r['traces'], strict=True):
    assert (trace['profile'], trace['hz']) == (request['profile'], request['hz'])
    assert len(trace['frames']) == len(request['frames']) == trace['hz'] * 6
    prior = trace['initial']
    for row, frame in zip(trace['frames'], request['frames'], strict=True):
        assert row['before'] == prior
        assert ('output' in row) == (frame['visited'] and frame['evaluate'])
        counts['frames'] += 1
        counts['hidden'] += not frame['visited']
        counts['updateOnly'] += frame['visited'] and not frame['evaluate']
        counts['initialize'] += frame['initialize']
        if 'output' in row:
            counts['poses'] += 1
            counts['active'] += row['changedBones'] == 3
            counts['hits'] += sum(h['walkable'] for h in row['hits'])
            counts['grounded'] += row['after']['onGround']
            assert len(row['input']['pose']) == len(row['output']['pose']) == 81
            for channel in ('curves', 'attributes', 'rootMotion'):
                assert row['input'].get(channel) == row['output'].get(channel)
        prior = row['after']
assert counts == n['counts'] and counts['frames'] == 3780 and counts['poses'] == 3231
assert load(logs / 'foot-placement-v2-native-diagnostic.json')['traces'] == n['traces']
# The unseeded probe is intentionally still a failed repeatability gate.
old = load(root / 'foot_placement_v1_native.json')['traces']
repeat = load(logs / 'foot-placement-native-diagnostic.json')['traces']
initial = sum(a['initial'] != b['initial'] for a, b in zip(old, repeat, strict=True))
poses = sum(a.get('output') != b.get('output') for ta, tb in zip(old, repeat, strict=True)
            for a, b in zip(ta['frames'], tb['frames'], strict=True))
assert initial == 3 and poses == 126
evidence = load(logs / 'foot-placement-uninitialized-comparison.json')
assert evidence['initialDifferingTraces'] == initial and evidence['poseDifferingFrames'] == poses
failure = (logs / 'foot-placement-ue-export-repeat.log').read_text(encoding='utf-8', errors='replace')
assert 'Immutable FootPlacement fixture differs: native' in failure
assert 'LYRA_EXPORT_PROCESS_EXIT_OK' not in failure
for name in ('foot-placement-seeded-ue-export.log', 'foot-placement-seeded-ue-export-repeat.log'):
    text = (logs / name).read_text(encoding='utf-8', errors='replace')
    assert text.count('LYRA_FOOT_PLACEMENT_NATIVE_OK frames=3780 poses=3231 previous=676 assets_saved=0') == 1
    assert text.count('LYRA_EXPORT_PROCESS_EXIT_OK mode=foot-placement code=0') == 1
    assert '0 error(s)' in text and 'Traceback' not in text and 'Error:' not in text and 'Handled ensure' not in text
for name, marker in (
    ('foot-placement-godot-final.log', 'LYRA_FOOT_PLACEMENT_GODOT_OK frames=3780 poses=3231'),
    ('foot-placement-skeletal-update-regression.log', 'LYRA_SKELETAL_UPDATE_GODOT_OK frames=3780'),
    ('foot-placement-leg-ik-regression.log', 'LYRA_LEG_IK_GODOT_OK frames=3780'),
    ('foot-placement-main-als-regression.log', 'LYRA_MAIN_ALS_NATIVE_GODOT_OK frames=11340 poses=9762 rootPoses=12595'),
    ('foot-placement-main-aiming-regression.log', 'LYRA_MAIN_AIMING_SCOPE_GODOT_OK frames=11340 poses=9762'),
):
    text = (logs / name).read_text(encoding='utf-8', errors='replace')
    assert text.count(marker) == 1 and 'ERROR:' not in text and 'WARNING:' not in text, name
for name in ('foot-placement-debug-final.log', 'foot-placement-optimize-final.log'):
    text = (logs / name).read_text(encoding='utf-8', errors='replace')
    assert '0 个警告' in text and '0 个错误' in text, name
build = (logs / 'foot-placement-seeded-ue-build.log').read_text(encoding='utf-8', errors='replace')
assert 'AutomationTool exiting with ExitCode=0 (Success)' in build and 'GODOT_ALS_EXTERNAL_EXPORTER_OK' in build
print('LYRA_FOOT_PLACEMENT_FINAL_VERIFIED frames=3780 poses=3231 packages=508 previous=676 '
      'definedInitialStorage=true unseededRepeat=false graphEntries=13 production=false whole_goal=false')
