"""Verify original Engine cache captures and the reusable Main pose host evidence."""
import hashlib
import json
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
native = json.loads((root / 'main_cache_pose_v1_native.json').read_bytes())
requests = json.loads((root / 'main_cache_pose_v1_requests.json').read_bytes())
build = (logs / 'main-cache-pose-ue-build-engine-entry2.log').read_text(encoding='utf-8')
assert 'BUILD SUCCESSFUL' in build and 'AutomationTool exiting with ExitCode=0' in build
assert 'GODOT_ALS_EXTERNAL_EXPORTER_OK plugin=' in build
assert native['schemaVersion'] == 1
assert native['requestSha256'] == sha(root / 'main_cache_pose_v1_requests.json')
for field in ('dependencies', 'previousFixtureSha256'):
    for name, digest in native[field].items():
        assert sha(root / name) == digest, (field, name)
for name, digest in native['assetSha256'].items():
    assert sha(Path('../GASP58/Content') / (name.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, name
for name, digest in native['probeSourceSha256'].items():
    assert sha(repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter' / name) == digest, name
    for tree in ('source', 'package'):
        assert sha(repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter' / name) == digest, (tree, name)
assert {(t['profile'], t['hz']) for t in native['traces']} == {(p, h) for p in ('unarmed', 'pistol', 'rifle') for h in (30, 60, 120)}
frames = poses = evaluations = hidden = 0
for trace, request in zip(native['traces'], requests['traces'], strict=True):
    assert (trace['profile'], trace['hz']) == (request['profile'], request['hz'])
    for result, frame in zip(trace['frames'], request['frames'], strict=True):
        frames += 1
        assert result['evaluations'] == (2 if frame['evaluate'] else 0)
        assert len(result['outputs']) == (4 if frame['evaluate'] else 0)
        if frame['evaluate']:
            a, b, c, d = result['outputs']
            assert a == b and c == d and a != c
            assert all(len(p['pose']) == 81 for p in (a, b, c, d))
        else:
            hidden += 1
        poses += len(result['outputs'])
        evaluations += result['evaluations']
assert native['counts'] == dict(frames=frames, poses=poses, evaluations=evaluations)
assert frames == 1260 and hidden > 0
for name in ('main-cache-pose-ue-export-engine-entry.log', 'main-cache-pose-ue-export-repeat.log'):
    text = (logs / name).read_text(encoding='utf-8')
    assert text.count(f'LYRA_MAIN_CACHE_POSE_NATIVE_OK frames={frames} poses={poses}') == 1, name
    assert 'LYRA_EXPORT_PROCESS_EXIT_OK mode=main-cache-pose code=0' in text, name
    assert 'Success - 0 error(s)' in text and 'Handled ensure' not in text and ': Error:' not in text, name
for name, marker, mode in (
    ('main-cache-pose-godot.log', f'LYRA_MAIN_CACHE_POSE_GODOT_OK frames={frames} poses={poses}', 'main-cache-pose'),
    ('main-pose-host-godot-final.log', 'LYRA_MAIN_POSE_HOST_GODOT_OK frames=11340 poses=9762', 'main-pose-host'),
    ('main-pose-host-feedback-godot.log', 'LYRA_MAIN_POSE_HOST_FEEDBACK_GODOT_OK frames=11340 poses=9762', 'main-pose-feedback'),
):
    text = (logs / name).read_text(encoding='utf-8')
    assert text.count(marker) == 1 and 'ERROR:' not in text and 'WARNING:' not in text, name
    assert f'LYRA_TEST_PROCESS_EXIT mode={mode} code=0' in text, name
host = (logs / 'main-pose-host-godot-final.log').read_text(encoding='utf-8')
assert 'groupEntries=14 commonSync=true fullChannels=true cachePoseEvaluation=true' in host
assert 'lateRetry=207 faults=84' in host
assert 'inactiveSlots=true inertia=false controlRig=false nativeCombined=false production=false' in host
feedback = (logs / 'main-pose-host-feedback-godot.log').read_text(encoding='utf-8')
assert 'actualFinalFeedback=True' in feedback and 'feedbackFaults=45' in feedback
assert 'lateRetry=207 faults=129' in feedback
assert 'feedbackCurves=58572' in feedback
for name, mode in (('main-pose-host-debug.log', 'debug'), ('main-pose-host-optimize.log', 'optimize')):
    text = (logs / name).read_text(encoding='utf-8-sig')
    assert '0 个警告' in text and '0 个错误' in text, name
    assert f'LYRA_BUILD_PROCESS_EXIT mode={mode} code=0' in text, name
print(f'LYRA_MAIN_CACHE_POSE_FINAL_VERIFIED frames={frames} poses={poses} evaluations={evaluations} '
      f'packages={len(native["assetSha256"])} previous={len(native["previousFixtureSha256"])} '
      'groupEntries=14 cachePoseEvaluation=true inertia=false controlRig=false production=false whole_goal=false')
