"""Actual four Main roots, common native Sync and immutable capture audit."""
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

from locomotion_paths import project_path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
artifacts = repo / 'artifacts/lyra-analysis'
sha = lambda data: hashlib.sha256(data).hexdigest()
resource_names = ('main_ground_scope_v2_requests.json', 'main_ground_scope_v2_native.json')
native = json.loads((root / resource_names[1]).read_bytes())
assert native['requestSha256'] == sha((root / resource_names[0]).read_bytes())
for name, digest in (native['dependencies'] | native['previousFixtureSha256']).items():
    assert sha((root / name).read_bytes()) == digest, name
assert len(native['previousFixtureSha256']) == 623
assert len(native['assetSha256']) == 508
for path, digest in native['assetSha256'].items():
    package = project_path('Content') / (path.split('.')[0].removeprefix('/Game/') + '.uasset')
    assert sha(package.read_bytes()) == digest, path
for relative, digest in native['probeSourceSha256'].items():
    for source in (repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter',
                   repo / 'artifacts/unreal/gasp58-lyra-masks/source/AlsV4AssetExporter/Source/AlsV4AssetExporter',
                   repo / 'artifacts/unreal/gasp58-lyra-masks/package/AlsV4AssetExporter/Source/AlsV4AssetExporter'):
        assert sha((source / relative).read_bytes()) == digest, str(source / relative)
assert len(native['assets']) == 150
assert native['counts'] == dict(frames=3780, poses=8400, allActive=1680, hidden=420)
assert {(t['profile'], t['hz']) for t in native['traces']} == {
    (p, hz) for p in ('unarmed', 'pistol', 'rifle') for hz in (30, 60, 120)}

logs = {}
for name in ('ground-native-v2-ue-export.log', 'ground-native-v2-ue-export-second.log'):
    data = (artifacts / name).read_bytes()
    text = data.decode('utf-8-sig')
    assert text.count('LYRA_MAIN_GROUND_SCOPE_NATIVE_OK traces=9 frames=3780 poses=8400 packages=508 previous=623 assets_saved=0') == 1, name
    assert text.count('LYRA_EXPORT_PROCESS_EXIT_OK mode=main-ground-scope code=0') == 1, name
    assert not re.search(r'(Traceback \(most recent call last\)|Assertion failed:|Ensure condition failed:|Fatal error:|: Error:)', text), name
    logs[name] = dict(sha256=sha(data), warnings=[line for line in text.splitlines() if ': Warning:' in line])

required = {
    'ground-native-v2-godot-final.log': ('LYRA_MAIN_GROUND_NATIVE_GODOT_OK', 'LYRA_MAIN_LEAN_CLOCKS_OK'),
    'ground-native-regression-state-history.log': ('LYRA_MAIN_STATE_HISTORY_JOINT_OK',),
}
for name, markers in required.items():
    data = (artifacts / name).read_bytes()
    text = data.decode('utf-8-sig')
    assert not re.search(r'(?m)^(ERROR:|WARNING:)', text), name
    for marker in markers:
        assert marker in text, (name, marker)
    logs[name] = dict(sha256=sha(data), results=[line for line in text.splitlines() if any(m in line for m in markers)])
result = logs['ground-native-v2-godot-final.log']['results'][-1]
for part in ('frames=3780', 'poses=8400', 'bones=680400', 'sourceClocks=18900', 'hipClocks=15120',
             'permutations=24', 'assets=144', 'intersections=31', 'retry=true', 'wholeMachine=false', 'production=false'):
    assert part in result, part
assert 'clockChecks=11340' in logs['ground-native-v2-godot-final.log']['results'][0]
for name in ('ground-native-v2-debug-build.log', 'ground-native-v2-optimize-build.log'):
    data = (artifacts / name).read_bytes()
    text = data.decode('utf-8-sig')
    assert '已成功生成' in text and '0 个错误' in text and '0 个警告' in text, name
    logs[name] = dict(sha256=sha(data))
build = (artifacts / 'ground-native-v2-ue-build.log').read_bytes()
assert 'BUILD SUCCESSFUL' in build.decode('utf-8-sig')
logs['ground-native-v2-ue-build.log'] = dict(sha256=sha(build))
# Core production code is unchanged since the common-scope gate. Keep its
# actual prior result explicit; this is not a new full-suite run.
core = ET.parse(artifacts / 'four-root-core-final.trx').find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters').attrib
assert core['total'] == core['passed'] == '76' and core['failed'] == core['notExecuted'] == '0'
report = dict(schemaVersion=1, resourceSha256={n: sha((root / n).read_bytes()) for n in resource_names},
              protectedPackages=508, protectedPreviousFixtures=623, nativeFrames=3780, nativePoses=8400,
              nativeBones=680400, exactMovementClocks=18900, exactHipClocks=15120, exactLeanClocks=11340,
              priorCoreTests=core, fourRootJointNative=True, explicitStateTraversal=True,
              fullMainStateMachine=False, finalStateMixedPose=False, ordinaryDemo=False, wholeGoalComplete=False, logs=logs)
(artifacts / 'ground-native-final-verification.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print('LYRA_MAIN_GROUND_NATIVE_FINAL_VERIFIED frames=3780 poses=8400 bones=680400 packages=508 fixtures=623 movement_clocks=18900 hip_clocks=15120 whole_goal=false')
