"""Extract controls and real CMC observations, without posing animation inputs."""
import gc
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
E = ROOT / 'artifacts/lyra-analysis'
OUTPUT = E / 'character-motor-trajectory-v1-reference.json'
assert not OUTPUT.exists(), 'Preserve reference evidence'


def sha(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


prior = json.loads((E / 'character-movement-integrity.json').read_bytes())
captures = {c['tag']: c for c in prior['captures']}
result = dict(schemaVersion=1, start=[0, 0, 92], traces=[], evidence=[], actualOriginalCMC=True,
              controlsOnlyDriveGodot=True, nativeWorldTrajectoryParity=False)
profile = json.loads((ROOT / 'assets/generated/lyra_als/character_motor_v2.json').read_bytes())['profile']
for hz, tag in [(30, 'cmc30-ground-events'), (60, 'cmc60-ground-events-final'), (120, 'cmc120-ground-events')]:
    native_path = E / f'whole-main-{tag}-native.json'
    request_path = E / f'whole-main-{tag}-request.json'
    assert sha(native_path) == captures[tag]['nativeSha256']
    closure_path = E / f'whole-main-{tag}-closure.json'
    assert sha(closure_path) == captures[tag]['closureSha256']
    requests = json.loads(request_path.read_bytes())
    native = json.loads(native_path.read_bytes())
    physical = None
    for t, n in zip(requests['traces'], native['traces'], strict=True):
        assert t['hz'] == n['hz'] == hz and t['profile'] == n['profile']
        assert t['case'] == 'physics' and n['actualCharacterMovement']
        assert len(t['frames']) == len(n['frames']) == hz * 8
        assert all(profile[k] == v for k, v in n['motorProfile'].items())
        rows = [dict(delta=f['delta'], control=f['control'], ads=f['mainProperties']['GameplayTag_IsADS'],
                     physical=r['physicalInput']) for f, r in zip(t['frames'], n['frames'], strict=True)]
        if physical is None:
            physical = rows
        else:
            assert rows == physical, f'Provider physics differs at {hz}Hz'
    result['traces'].append(dict(hz=hz, obstacles=requests['traces'][0]['obstacles'], frames=physical))
    result['evidence'].append(dict(tag=tag, nativeSha256=sha(native_path), requestSha256=sha(request_path), closureSha256=sha(closure_path)))
    del native, requests, rows, n, t
    gc.collect()
with OUTPUT.open('x', encoding='utf-8', newline='\n') as stream:
    json.dump(result, stream, separators=(',', ':'), allow_nan=False)
print(OUTPUT, sha(OUTPUT), 'frames=', sum(len(t['frames']) for t in result['traces']))
