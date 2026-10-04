"""New authored gameplay inputs for the frozen whole-Main native probe.

The earlier capture script and all its published inputs remain byte-identical.
Only resource preparation and authored inputs change in this invocation.
"""
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
montage_anchor = "montage_catalog=load('montage_catalog_v2.json') if case in ('actions','rebind') else None"
save_anchor = "    save('request',requests)"
assert original_text.count(montage_anchor) == original_text.count(save_anchor) == 1

def author_linked_inputs(request):
    assert 'montagePaths' in request and len(request['traces']) == 12
    for trace in request['traces']:
        hz = trace['hz']
        assert len(trace['frames']) == hz * 12
        fire_asset = 39 if trace['profile'] == 'rifle' else 34
        for i, frame in enumerate(trace['frames']):
            t = i / hz
            firing = any(a <= t < b for a, b in ((.6, 1.1), (2.7, 3.2), (6.3, 6.8), (8.8, 9.4)))
            ads = any(a <= t < b for a, b in ((1., 1.4), (2.1, 3.6), (7.7, 8.3)))
            crouching = 4.7 <= t < 5.9 or 8.6 <= t < 9.6
            frame['observation'].update(firing=firing, ads=ads, crouching=crouching)
            frame['mainProperties'].update(GameplayTag_IsFiring=firing, GameplayTag_IsADS=ads)
            frame['commands'] = []
        # Commands follow Evaluate. FingerGuns suppresses real source traversal;
        # stop/recovery and same-class relink retain the linked instance state.
        for t, asset, stop in ((.6, fire_asset, False), (2.7, fire_asset, False),
                               (4., 0, False), (4.3, 17, False), (6., 0, True),
                               (6.3, fire_asset, False), (8.8, fire_asset, False)):
            trace['frames'][round(t * hz)]['commands'].append(dict(
                asset=asset, stop=stop, blend=.2, rate=1., start=0., stopGroup=True))

rewritten = original_text.replace(montage_anchor,
    "montage_catalog=load('montage_catalog_v2.json') if case in ('actions','rebind','multi-layer') else None")
rewritten = rewritten.replace(save_anchor, "    author_linked_inputs(requests)\n" + save_anchor)
exec(compile(rewritten, str(original_capture), 'exec'), globals())
assert hashlib.sha256(original_capture.read_bytes()).hexdigest() == original_sha

capture_out = capture_root / 'artifacts/lyra-analysis'
capture_tag = os.environ['LYRA_WHOLE_RUN_TAG']
captured_request = json.loads((capture_out / f'whole-main-{capture_tag}-request.json').read_bytes())
captured_manifest = json.loads((capture_out / f'whole-main-{capture_tag}-native.json').read_bytes())
coverage = []
body_functions = {'FullBody_IdleState', 'FullBody_CycleState', 'FullBody_StartState', 'FullBody_PivotState',
    'FullBody_StopState', 'FullBody_JumpStartState', 'FullBody_JumpStartLoopState', 'FullBody_JumpApexState',
    'FullBody_FallLoopState', 'FullBody_FallLandState'}
for authored, entry in zip(captured_request['traces'], captured_manifest['traceFiles'], strict=True):
    shard = capture_out / entry['file']
    assert hashlib.sha256(shard.read_bytes()).hexdigest() == entry['sha256']
    native_rows = json.loads(shard.read_bytes())['frames']
    hidden = [not any(u['hook'] in body_functions for u in row['updates']) for row in native_rows]
    # Identify update keys from the original probe, without changing them.
    item = dict(profile=authored['profile'], layout=authored['layout'],
        frames=len(native_rows), firingFrames=sum(f['observation']['firing'] for f in authored['frames']),
        adsFrames=sum(f['observation']['ads'] for f in authored['frames']),
        crouchFrames=sum(f['observation']['crouching'] for f in authored['frames']),
        hiddenBodyFrames=sum(hidden), bodyResumeTransitions=sum(a and not b for a, b in zip(hidden, hidden[1:])),
        montageCacheTrueFrames=sum(any(i['fields']['K2Node_PropertyAccess_48'] for i in row['instancesUpdated']) for row in native_rows),
        montageOverlapFrames=sum(len(row['frozen'])>1 for row in native_rows))
    assert item['firingFrames'] and item['adsFrames'] and item['crouchFrames']
    assert item['hiddenBodyFrames'] and item['bodyResumeTransitions'] and item['montageCacheTrueFrames'] and item['montageOverlapFrames']
    coverage.append(item)
provenance = dict(originalCaptureSha256=original_sha,
    wrapperSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
    runnerSha256=hashlib.sha256((capture_root / 'scripts/capture-lyra-linked-private-v2.ps1').read_bytes()).hexdigest(),
    authoredInputChangesOnly=True, probeChanged=False, coverage=coverage)
with (capture_out / f'whole-main-{capture_tag}-linked-input-provenance.json').open('x', encoding='utf-8', newline='\n') as target:
    target.write(json.dumps(provenance, separators=(',', ':')) + '\n')
