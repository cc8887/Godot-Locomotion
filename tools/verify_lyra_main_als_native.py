"""Verify continuous ALS Main evidence without rewriting hashed assets."""
import hashlib
import json
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
native = json.loads((root / 'main_als_locomotion_v1_native.json').read_bytes())
assert sha(root / 'main_als_locomotion_v1_requests.json') == native['requestSha256']
for name, digest in native['dependencies'].items():
    assert sha(root / name) == digest, name
for name, digest in native['previousFixtureSha256'].items():
    assert sha(root / name) == digest, name
assert len(native['previousFixtureSha256']) == 643
for path, digest in native['assetSha256'].items():
    p = Path('../GASP58/Content') / (path.split('.')[0].removeprefix('/Game/') + '.uasset')
    assert sha(p) == digest, path
assert len(native['assetSha256']) == 508
for name, digest in native['probeSourceSha256'].items():
    for tree in (repo / 'tools/unreal/AlsV4AssetExporter',
                 repo / 'artifacts/unreal/gasp58-lyra-masks/source/AlsV4AssetExporter',
                 repo / 'artifacts/unreal/gasp58-lyra-masks/package/AlsV4AssetExporter'):
        assert sha(tree / 'Source/AlsV4AssetExporter' / name) == digest, (tree, name)
assert native['schemaVersion'] == 1 and native['skeleton'] == 'ALS81'
assert native['outputBoundary'] == 'LocomotionSM' and native['fixedProvider']
assert not native['wholeMain'] and not native['production']
assert native['counts']['frames'] == 11340 and native['counts']['poses'] == 9762
assert len(native['states']) == 10 and len(native['traces']) == 18
assert sorted({t['hz'] for t in native['traces']}) == [30, 60, 120]
assert {t['case'] for t in native['traces']} == {'movement', 'turn'}
assert len({t['profile'] for t in native['traces']}) == 3
assert sum(len(t['frames']) for t in native['traces']) == 11340
assert sum(len(r['evaluatedRoots']) for t in native['traces'] for r in t['frames']) == native['counts']['rootPoses']
for name in ('main-als-native-ue-export-cache.log', 'main-als-native-ue-export-repeat.log'):
    text = (logs / name).read_text(encoding='utf-8', errors='replace')
    assert text.count('LYRA_MAIN_ALS_NATIVE_OK frames=11340 poses=9762 ') == 1, name
    assert 'LYRA_EXPORT_PROCESS_EXIT_OK mode=main-als-native code=0' in text, name
    assert 'LogWindows: Error:' not in text and 'Traceback' not in text, name
for name, marker in (
    ('main-als-native-godot-final.log', 'LYRA_MAIN_ALS_NATIVE_GODOT_OK frames=11340 poses=9762'),
    ('main-linked-feedback-own-regression-final.log', 'LYRA_MAIN_OWN_TURN_FEEDBACK_OK frames=3780'),
    ('main-linked-feedback-idle-regression.log', 'LYRA_MAIN_IDLE_ROOT_GODOT_OK frames=2520'),
    ('main-linked-feedback-scope-regression-final.log', 'LYRA_LOCOMOTION_SOURCE_SCOPE_JOINT_OK frames=3780')):
    text = (logs / name).read_text(encoding='utf-8', errors='replace')
    assert marker in text, name
    assert 'ERROR:' not in text and 'WARNING:' not in text, name
for name in ('main-als-native-debug-final.log', 'main-als-native-optimize-final.log'):
    text = (logs / name).read_text(encoding='utf-8', errors='replace')
    assert '0 个警告' in text and '0 个错误' in text, name
assert 'nativeJoint=true boundary=LocomotionSM' in (logs / 'main-als-native-godot-final.log').read_text(encoding='utf-8')
print('LYRA_MAIN_ALS_NATIVE_FINAL_VERIFIED frames=11340 poses=9762 rootPoses=' + str(native['counts']['rootPoses']) +
      ' packages=508 previous=643 source=true own_rules=true own_sync=true whole_goal=false')
