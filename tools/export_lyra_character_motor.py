"""Derive a compact immutable motor resource from a real native probe capture."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'artifacts/lyra-analysis/whole-main-cmc60-motor-v2-native.json'
capture = json.loads(SOURCE.read_bytes())
traces = capture['traces']
assert len(traces) == 3
first = traces[0]
for trace in traces:
    assert trace['actualCharacterMovement']
    assert trace['motorProfile'] == first['motorProfile']
    assert trace['velocityKernel'] == first['velocityKernel']
    assert trace['fallingKernel'] == first['fallingKernel']
    assert len(trace['velocityKernel']) == 250 and len(trace['fallingKernel']) == 175
profile = first['motorProfile']
assert profile['gravityZ'] == -980 and profile['terminalVelocity'] == 4000
assert profile['JumpMaxHoldTime'] == 0 and profile['JumpMaxCount'] == 1
evidence = dict(evidenceNative=SOURCE.relative_to(ROOT).as_posix(),
                evidenceSha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest())
resource = dict(schemaVersion=2, profile=profile, **evidence,
                scope='Shooter CDO and original uncollided velocity substeps; collision/apex/terrain acceptance remains open')
fixture = dict(schemaVersion=2, profile=profile, kernel=first['velocityKernel'],
               fallingKernel=first['fallingKernel'], **evidence)
outputs = [(ROOT / 'assets/generated/lyra_als/character_motor_v2.json', resource),
           (ROOT / 'tests/Als.Core.Tests/Fixtures/Physics/lyra_character_falling_native.json', fixture)]
assert all(not path.exists() for path, _ in outputs), 'Preserve immutable prior outputs'
for path, data in outputs:
    with path.open('x', encoding='utf-8', newline='\n') as stream:
        json.dump(data, stream, separators=(',', ':'), ensure_ascii=False)
    print(path.relative_to(ROOT), hashlib.sha256(path.read_bytes()).hexdigest())
