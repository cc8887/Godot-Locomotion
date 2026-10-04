"""Check executed four-root gates and preservation of existing resource bytes."""
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
artifacts = repo / 'artifacts/lyra-analysis'
content = Path('../GASP58/Content')
sha = lambda data: hashlib.sha256(data).hexdigest()
native = json.loads((root / 'main_pivot_native.json').read_bytes())
previous = json.loads((artifacts / 'main-pivot-final-verification.json').read_bytes())
for name, expected in (native['dependencies'] | native['previousFixtureSha256'] | previous['resourceSha256']).items():
    assert sha((root / name).read_bytes()) == expected, name
assert len(native['previousFixtureSha256']) == 619
assert len(native['assetSha256']) == 508
for name, expected in native['assetSha256'].items():
    path = content / (name.split('.')[0].removeprefix('/Game/') + '.uasset')
    assert sha(path.read_bytes()) == expected, name

required = {
    'four-root-smoke-final.log': ('LYRA_MAIN_GROUND_SCOPE_PIVOT_NATIVE_OK', 'LYRA_MAIN_GROUND_SCOPE_JOINT_OK'),
    'four-root-regression-main-state-history.log': ('LYRA_MAIN_STATE_HISTORY_JOINT_OK',),
    'four-root-regression-main-pivot.log': ('LYRA_MAIN_PIVOT_GODOT_OK',),
}
logs = {}
for name, markers in required.items():
    data = (artifacts / name).read_bytes()
    text = data.decode('utf-8-sig')
    assert not re.search(r'(?m)^(ERROR:|WARNING:)', text), name
    for marker in markers:
        assert marker in text, (name, marker)
    logs[name] = {'sha256': sha(data), 'results': [line for line in text.splitlines() if any(m in line for m in markers)]}
joint = logs['four-root-smoke-final.log']['results'][-1]
for fragment in ('frames=3780', 'poses=8400', 'allActive=1680', 'hidden=420', 'permutations=24', 'assets=150', 'rejected=13860', 'joint_native=false', 'production=false'):
    assert fragment in joint, fragment
single = logs['four-root-smoke-final.log']['results'][0]
for fragment in ('frames=3780', 'poses=3528', 'bones=285768', 'noTicks=3'):
    assert fragment in single, fragment
for name in ('four-root-final-debug-build.log', 'four-root-final-optimize-build.log'):
    data = (artifacts / name).read_bytes()
    text = data.decode('utf-8-sig')
    assert '0 个警告' in text and '0 个错误' in text and '已成功生成' in text, name
    logs[name] = {'sha256': sha(data)}
tests = ET.parse(artifacts / 'four-root-core-final.trx').find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters').attrib
assert tests['total'] == tests['passed'] == '76' and tests['failed'] == tests['notExecuted'] == '0'
report = dict(schemaVersion=1, protectedPackages=508, protectedExistingFixtures=621, coreTests=tests,
              commonFourRootScope=True, pivotNativeFrames=3780, jointControlledFrames=3780, jointControlledPoses=8400,
              fourRootJointNative=False, fullMainStateMachine=False, ordinaryDemo=False, wholeGoalComplete=False, logs=logs)
(artifacts / 'four-root-final-verification.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print('LYRA_MAIN_GROUND_SCOPE_FINAL_VERIFIED native_pivot=3780 joint_controlled=3780 poses=8400 packages=508 fixtures=621 core=76 joint_native=false whole_goal=false')
