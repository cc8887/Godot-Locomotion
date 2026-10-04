"""Preserve the original floor queries and CDO as new immutable resources."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
E = ROOT / 'artifacts/lyra-analysis'
TAG = 'cmc60-floor-v2'
native_path = E / f'whole-main-{TAG}-native.json'
closure_path = E / f'whole-main-{TAG}-closure.json'
resource_path = ROOT / 'assets/generated/lyra_als/character_floor_v1.json'
reference_path = E / 'character-floor-v1-reference.json'
assert not resource_path.exists() and not reference_path.exists(), 'Preserve floor resources'


def sha(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


native = json.loads(native_path.read_bytes())
old = json.loads((E / 'whole-main-cmc60-motor-v2-native.json').read_bytes())
request = json.loads((E / f'whole-main-{TAG}-request.json').read_bytes())
motor_path = ROOT / 'assets/generated/lyra_als/character_motor_v2.json'
motor = json.loads(motor_path.read_bytes())
profile = native['traces'][0]['motorProfile']
rows = native['traces'][0]['floorKernel']
assert len(rows) == 144 and len(native['traces']) == 3
for trace, prior in zip(native['traces'], old['traces'], strict=True):
    assert trace['actualCharacterMovement'] and trace['profile'] == prior['profile']
    assert all(profile[k] == value for k, value in motor['profile'].items())
    assert trace['motorProfile'] == profile and trace['floorKernel'] == rows
    assert trace['velocityKernel'] == prior['velocityKernel'] and trace['fallingKernel'] == prior['fallingKernel']
    assert [f['physicalInput'] for f in trace['frames']] == [f['physicalInput'] for f in prior['frames']]
assert not profile['bUseFlatBaseForFloorChecks'] and profile['bAlwaysCheckFloor']
resource = dict(schemaVersion=1, evidenceSha256=sha(native_path), closureSha256=sha(closure_path),
                baseMotorSha256=sha(motor_path), profile=profile)
reference = dict(schemaVersion=1, actualOriginalCMC=True, originalPhysicalPrefixUnchanged=True,
                 evidenceSha256=sha(native_path), closureSha256=sha(closure_path), start=[0,0,92],
                 floor=dict(center=[0,0,-5],extent=[10000,10000,5]),
                 obstacles=request['traces'][0]['obstacles'], rows=rows)
for path, data in [(resource_path, resource), (reference_path, reference)]:
    with path.open('x', encoding='utf-8', newline='\n') as stream:
        json.dump(data, stream, separators=(',', ':'), allow_nan=False)
    print(path, sha(path))
print('nativeFloorQueries=144 providers=3 physicalPrefixUnchanged=true')
