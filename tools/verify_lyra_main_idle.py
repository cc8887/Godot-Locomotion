"""Audit actual Main Idle callback capture, scope feedback and regressions."""
import hashlib
import json
import re
from pathlib import Path

from locomotion_paths import project_path

repo = Path(__file__).resolve().parents[1]
artifacts = repo/'artifacts/lyra-analysis'
root = repo/'assets/generated/lyra_als'
sha = lambda data: hashlib.sha256(data).hexdigest()
path = artifacts/'main-idle-root-native.json'
capture = json.loads(path.read_bytes())
assert capture['native']['root'] == 8
assert len(capture['requests']['frames']) == len(capture['native']['frames']) == 2520
assert {f['hz'] for f in capture['requests']['frames']} == {30,60,120}
assert len(capture['packages']) == 508 and len(capture['fixtures']) == 643
for name, digest in (capture['fixtures'] | capture['dependencies']).items():
    assert sha((root/name).read_bytes()) == digest, name
for name, digest in capture['packages'].items():
    package = project_path('Content')/(name.split('.')[0].removeprefix('/Game/')+'.uasset')
    assert sha(package.read_bytes()) == digest, name
for name, digest in capture['probeSourceSha256'].items():
    for source in (repo/'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter',
                   repo/'artifacts/unreal/gasp58-lyra-masks/source/AlsV4AssetExporter/Source/AlsV4AssetExporter',
                   repo/'artifacts/unreal/gasp58-lyra-masks/package/AlsV4AssetExporter/Source/AlsV4AssetExporter'):
        assert sha((source/name).read_bytes()) == digest, (source, name)
logs = {}
for name in ('main-idle-root-ue-export.log', 'main-idle-root-ue-repeat.log'):
    data = (artifacts/name).read_bytes(); text = data.decode('utf-8-sig')
    assert text.count('LYRA_MAIN_IDLE_ROOT_NATIVE_OK frames=2520 root=8 packages=508 fixtures=643 assets_saved=0') == 1
    assert text.count('LYRA_EXPORT_PROCESS_EXIT_OK mode=main-idle-root code=0') == 1
    assert not re.search(r'(Traceback \(most recent call last\)|Assertion failed:|Ensure condition failed:|Fatal error:|: Error:)', text), name
    logs[name] = dict(sha256=sha(data), warnings=sum(': Warning:' in l for l in text.splitlines()))
for name, markers in {
    'main-idle-root-godot-first.log': ('LYRA_MAIN_IDLE_ROOT_GODOT_OK frames=2520 applied=753 hidden=83 fading=912 zero=328 rejected=753 exactBits=true',),
    'main-idle-scope-joint-ordered-final.log': (
        'LYRA_LOCOMOTION_SOURCE_SCOPE_JOINT_OK frames=3780 poses=11064 hidden=252 allRoots=1008 updateOnly=543 inactive=1212 orders=20 groups=3 sourceIds=24 rejected=34743',
        'LYRA_MAIN_IDLE_SCOPE_FEEDBACK_OK idleBeforeStart=126 idleAfterStart=126 feedbackFrames=3021 changes=23 wholeMain=false'),
    'main-idle-ground-regression.log': ('LYRA_MAIN_GROUND_NATIVE_GODOT_OK frames=3780 poses=8400 bones=680400',),
}.items():
    data = (artifacts/name).read_bytes(); text = data.decode('utf-8-sig')
    assert not re.search(r'(?m)^(ERROR:|WARNING:)', text), name
    for marker in markers: assert text.count(marker) == 1, (name, marker)
    logs[name] = dict(sha256=sha(data), results=[l for l in text.splitlines() if '_OK' in l])
for name in ('main-idle-debug-final-scope.log', 'main-idle-optimize-final.log'):
    data = (artifacts/name).read_bytes(); text = data.decode('utf-8-sig')
    assert '已成功生成' in text and '0 个错误' in text and '0 个警告' in text
    logs[name] = dict(sha256=sha(data))
data = (artifacts/'main-idle-ue-build.log').read_bytes(); text = data.decode('utf-8-sig')
assert 'BUILD SUCCESSFUL' in text and 'AutomationTool exiting with ExitCode=0' in text
assert not re.search(r'(warning C\d+|error C\d+)', text)
logs['main-idle-ue-build.log'] = dict(sha256=sha(data))
report = dict(schemaVersion=1, captureSha256=sha(path.read_bytes()), callbackFrames=2520, bitExact=True,
    packagesProtected=508, fixturesProtected=643, jointFrames=3780, jointPoses=11064,
    feedbackCommits=3021, regressionGroundPoses=8400, nativeCallbackOnly=True, nativeJointMain=False,
    mainStateSelection=False, finalMixedMainPose=False, productionLayer=False, ordinaryDemo=False,
    visualAcceptance=False, performanceAcceptance=False, wholeGoalComplete=False, logs=logs)
(artifacts/'main-idle-final-verification.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('LYRA_MAIN_IDLE_FINAL_VERIFIED native_frames=2520 joint_frames=3780 joint_poses=11064 ground_poses=8400 packages=508 fixtures=643 whole_goal=false')
