"""Author long idle/turn and real linked-property input for the frozen probe."""
import copy
import hashlib
import json
import os
from pathlib import Path

capture_root = Path(__file__).resolve().parents[2]
original_capture = capture_root / 'tools/unreal/capture_lyra_whole_main.py'
original_bytes = original_capture.read_bytes()
original_sha = hashlib.sha256(original_bytes).hexdigest()
original_text = original_bytes.decode('utf-8')
assert os.environ['LYRA_WHOLE_CASE'] == 'multi-layer'
save_anchor = "    save('request',requests)"
assert original_text.count(save_anchor) == 1


def author_idle_turn(request):
    assert len(request['traces']) == 12
    selected = [t for t in request['traces'] if t['layout'] in ('single', 'per-call')]
    assert len(selected) == 6
    for trace in selected:
        hz = trace['hz']
        prototype = trace['frames'][0]
        frames = []
        for i in range(hz * 24):
            t = i / hz
            frame = copy.deepcopy(prototype)
            yaw = 90. if 8 <= t < 13 else -90. if t >= 18 else 0.
            firing = 7.8 <= t < 8.1 or 12.8 <= t < 13.1
            frame['observation'].update(location=[0., 0., 88.], rotation=[0., yaw, 0.],
                velocity=[0., 0., 0.], acceleration=[0., 0., 0.], movementMode=1,
                crouching=False, ads=False, firing=firing, dashing=False, enabled=True,
                aimPitch=0., gravityScale=1.)
            frame['mainProperties'].update(GameplayTag_IsADS=False, GameplayTag_IsFiring=firing,
                GameplayTag_IsDashing=False, bEnableRootYawOffset=True, GroundDistance=0.)
            frame['layerProperties'] = {'EnableLeftHandPoseOverride':
                3 <= t < 4.5 or 10 <= t < 12 or 19 <= t < 22}
            frame['relink'] = i % 37 == 19
            frames.append(frame)
        trace['frames'] = frames
    request['traces'] = selected


rewritten = original_text.replace(save_anchor, "    author_idle_turn(requests)\n" + save_anchor)
exec(compile(rewritten, str(original_capture), 'exec'), globals())
assert hashlib.sha256(original_capture.read_bytes()).hexdigest() == original_sha

capture_out = capture_root / 'artifacts/lyra-analysis'
capture_tag = os.environ['LYRA_WHOLE_RUN_TAG']
request = json.loads((capture_out / f'whole-main-{capture_tag}-request.json').read_bytes())
manifest = json.loads((capture_out / f'whole-main-{capture_tag}-native.json').read_bytes())
coverage = []
idle_requests = json.loads((capture_root / 'assets/generated/lyra_als/idle_runtime_v2_requests.json').read_bytes())
break_counts = {t['profile']: len(t['breaks']) for t in idle_requests['traces']}
names = ('CurrentIdleBreakIndex', 'TurnInPlaceRotationDirection', 'TurnInPlaceRecoveryDirection',
         'TurnInPlaceAnimTime', 'LeftHandPoseOverrideWeight')
for authored, entry in zip(request['traces'], manifest['traceFiles'], strict=True):
    path = capture_out / entry['file']
    assert hashlib.sha256(path.read_bytes()).hexdigest() == entry['sha256']
    native = json.loads(path.read_bytes())
    changes = {name: 0 for name in names}
    values = {name: set() for name in names}
    for frame in native['frames']:
        for before, updated, after in zip(frame['instancesBefore'], frame['instancesUpdated'], frame['instancesAfter'], strict=True):
            assert before['owner'] == updated['owner'] == after['owner']
            for name in names:
                changes[name] += before['fields'][name] != updated['fields'][name]
                values[name].update((before['fields'][name], updated['fields'][name], after['fields'][name]))
    item = dict(profile=authored['profile'], layout=authored['layout'], frames=len(native['frames']),
                changes=changes, values={name: sorted(v) for name, v in values.items()},
                enabledFrames=sum(f['layerProperties']['EnableLeftHandPoseOverride'] for f in authored['frames']))
    assert all(changes[name] > 0 for name in names if name != 'CurrentIdleBreakIndex'), item
    if break_counts[authored['profile']] > 1:
        assert changes['CurrentIdleBreakIndex'] > 0, item
    else:
        assert values['CurrentIdleBreakIndex'] == {0}, item
    for name in ('TurnInPlaceRotationDirection', 'TurnInPlaceRecoveryDirection'):
        assert {-1, 1}.issubset(values[name]), item
    item['idleBreakCount'] = break_counts[authored['profile']]
    assert item['enabledFrames'] > 0
    coverage.append(item)
provenance = dict(originalCaptureSha256=original_sha,
    wrapperSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
    runnerSha256=hashlib.sha256((capture_root / 'scripts/capture-lyra-linked-idle-turn.ps1').read_bytes()).hexdigest(),
    authoredInputChangesOnly=True, probeChanged=False, coverage=coverage)
with (capture_out / f'whole-main-{capture_tag}-idle-turn-provenance.json').open('x', encoding='utf-8', newline='\n') as target:
    target.write(json.dumps(provenance, separators=(',', ':')) + '\n')
