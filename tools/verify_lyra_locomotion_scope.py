"""Verify the common locomotion resource, native replays and joint owner gate."""
import hashlib
import json
import re
from pathlib import Path

from locomotion_paths import project_path

repo = Path(__file__).resolve().parents[1]
root = repo/'assets/generated/lyra_als'
artifacts = repo/'artifacts/lyra-analysis'
sha = lambda data: hashlib.sha256(data).hexdigest()
generation = json.loads((artifacts/'locomotion-resources-generation.json').read_bytes())
data = (root/'locomotion_resources.json').read_bytes()
assert sha(data) == generation['resourceSha256'] and len(data) == generation['resourceBytes']
resource = json.loads(data)
assert resource['groups'] == ['Locomotion', 'Stop', 'Test']
assert len(resource['assets']) == len(resource['compressedRoots']['assets']) == 194
assert set(resource['providers']) == {'unarmed', 'pistol', 'rifle'}
assert {a['path'] for a in resource['assets']} == set(resource['compressedRoots']['assets'])
assert len(generation['previousFixtureSha256']) == 642
for name, digest in (resource['dependencies'] | resource['provenance'] | generation['previousFixtureSha256']).items():
    assert sha((root/name).read_bytes()) == digest, name

# Existing UE captures are evidence for the unchanged individual closures.
# This run did not build or launch UE or capture a ten-root native traversal.
native = json.loads((root/'idle_runtime_v2_native.json').read_bytes())
packages = native['assetSha256']
assert len(packages) == 508
for name, digest in packages.items():
    package = project_path('Content')/(name.split('.')[0].removeprefix('/Game/')+'.uasset')
    assert sha(package.read_bytes()) == digest, name
del native

logs = {}
def log(name):
    data = (artifacts/name).read_bytes()
    text = data.decode('utf-8-sig')
    assert not re.search(r'(?m)^(ERROR:|WARNING:)', text), name
    logs[name] = dict(sha256=sha(data), results=[l for l in text.splitlines() if '_OK' in l])
    return text

replay = log('locomotion-scope-native-first.log')
expected = {
    'LYRA_MAIN_GROUND_NATIVE_GODOT_OK': (3780, 8400, 680400),
    'LYRA_AIR_RUNTIME_GODOT_OK': (3780, 7650, 619650),
    'LYRA_IDLE_RUNTIME_GODOT_OK': (26460, 26268, 2127708),
    'LYRA_IDLE_GATES_GODOT_OK': (26460, 3780, 306180),
}
for marker, counts in expected.items():
    matches = [l for l in replay.splitlines() if l.startswith(marker+' ')]
    assert len(matches) == 1, marker
    for name, value in zip(('frames', 'poses', 'bones'), counts, strict=True):
        assert f'{name}={value} ' in matches[0], (marker, name)
assert replay.count('LYRA_LOCOMOTION_UNIFIED_NATIVE_OK') == 1
assert replay.count('LYRA_CYCLE_LAYER_POSE_GODOT_OK') == 11
joint = log('locomotion-scope-joint-packet-final.log')
marker = 'LYRA_LOCOMOTION_SOURCE_SCOPE_JOINT_OK'
assert joint.count(marker) == 1
line = next(l for l in joint.splitlines() if l.startswith(marker))
for token in ('frames=3780', 'poses=10851', 'hidden=252', 'allRoots=1008', 'updateOnly=543',
              'inactive=1191', 'orders=20', 'groups=3', 'sourceIds=24', 'rejected=25680',
              'joint_native=false', 'wholeMain=false', 'production=false'):
    assert token in line, token
for name in ('locomotion-scope-debug-final.log', 'locomotion-scope-optimize-final.log'):
    text = log(name)
    assert '已成功生成' in text and '0 个错误' in text and '0 个警告' in text, name
report = dict(schemaVersion=1, resourceSha256=generation['resourceSha256'], absoluteSequences=194,
    leanSequences=3, providers=3, sourceGroups=3, previousFixturesProtected=642, packagesProtected=508,
    nativeReplayFrames=60480, nativeReplayPoses=46098, nativeReplayBones=3733938,
    jointFrames=3780, jointPoses=10851, jointUpdateOnlyFrames=543, jointRejected=25680,
    nativeTenRootJoint=False, fullMainMachine=False, productionLayer=False, ordinaryDemo=False,
    newUECapture=False, visualAcceptance=False, performanceAcceptance=False, wholeGoalComplete=False, logs=logs)
(artifacts/'locomotion-scope-final-verification.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print('LYRA_LOCOMOTION_SCOPE_FINAL_VERIFIED native_frames=60480 native_poses=46098 joint_frames=3780 sequences=197 packages=508 fixtures=642 whole_goal=false')
