"""Audit own Main host execution, existing native regressions and asset hashes."""
import hashlib
import json
import re
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
artifacts = repo / 'artifacts/lyra-analysis'
sha = lambda data: hashlib.sha256(data).hexdigest()
baseline_path = artifacts / 'main-idle-root-native.json'
baseline = json.loads(baseline_path.read_bytes())
assert len(baseline['fixtures']) == 643 and len(baseline['packages']) == 508
for name, digest in baseline['fixtures'].items():
    assert sha((root / name).read_bytes()) == digest, name
for name, digest in baseline['packages'].items():
    path = Path('../GASP58/Content') / (name.split('.')[0].removeprefix('/Game/') + '.uasset')
    assert sha(path.read_bytes()) == digest, name

logs = {}
markers = {
    'main-own-host-godot-final.log': (
        'LYRA_MAIN_OWN_LOCOMOTION_OK frames=7560 poses=6330 bones=512730 states=10 roots=10 transitions=216 automatic=30 blendFrames=2380 relevant=5476 negativeExplicit=84 hidden=165 updateOnly=1230 emptyValidSync=2385 groups=0,1 feedbackChanges=0 updateWithoutEvaluate=4 rejected=49065 nativeJoint=false fixedProvider=true production=false finalLayers=false',
        'LYRA_MAIN_OWN_TURN_FEEDBACK_OK frames=3780 changes=1576 testGroupFrames=1880 correctionFrames=1716 mixedIdleFrames=95 updateOnly=348 lateRetries=294 nativeJoint=false feedbackBoundary=LocomotionSM'),
    'main-own-host-machine-regression.log': ('LYRA_MAIN_MACHINE_RUNTIME_GODOT_OK frames=7560 states=10 transitions=213 automaticEdges=36 updates=9924',),
    'main-own-host-ground-regression.log': ('LYRA_MAIN_GROUND_NATIVE_GODOT_OK frames=3780 poses=8400 bones=680400',),
    'main-own-host-air-regression.log': ('LYRA_AIR_RUNTIME_GODOT_OK frames=3780 poses=7650 bones=619650 clocks=37800',),
    'main-own-host-idle-root-regression.log': ('LYRA_MAIN_IDLE_ROOT_GODOT_OK frames=2520 applied=753 hidden=83 fading=912 zero=328 rejected=753 exactBits=true',),
    'main-own-host-blend-regression.log': ('LYRA_LOCOMOTION_POSE_DIAGNOSTICS failures=0', 'LYRA_LOCOMOTION_POSE_OK'),
    'main-own-host-scope-regression.log': (
        'LYRA_LOCOMOTION_SOURCE_SCOPE_JOINT_OK frames=3780 poses=11064 hidden=252 allRoots=1008 updateOnly=543 inactive=1212 orders=20 groups=3 sourceIds=24 rejected=34743',
        'LYRA_MAIN_IDLE_SCOPE_FEEDBACK_OK idleBeforeStart=126 idleAfterStart=126 feedbackFrames=3021 changes=23 wholeMain=false'),
}
for name, expected in markers.items():
    data = (artifacts / name).read_bytes()
    text = data.decode('utf-8-sig')
    assert not re.search(r'(?m)^(ERROR:|WARNING:)', text), name
    for marker in expected:
        assert text.count(marker) == 1, (name, marker)
    logs[name] = dict(sha256=sha(data), results=[line for line in text.splitlines() if '_OK' in line])
for name in ('main-own-host-debug-final.log', 'main-own-host-optimize-final.log'):
    data = (artifacts / name).read_bytes()
    text = data.decode('utf-8-sig')
    assert '已成功生成' in text and '0 个错误' in text and '0 个警告' in text, name
    logs[name] = dict(sha256=sha(data))
paths = (
    'src/Als.Godot/Animation/Lyra/LyraMainLocomotionHost.cs',
    'src/Als.Godot/Animation/Lyra/LyraMainLocomotionHostSmoke.cs',
    'src/Als.Godot/Animation/Lyra/LyraMainSourceScope.cs',
    'src/Als.Godot/Animation/Lyra/LyraLocomotionSourceScope.cs',
    'src/Als.Godot/Animation/Lyra/LyraLocomotionResources.cs',
    'scenes/tests/lyra_main_locomotion_host_smoke.tscn',
    'docs/verification/2026-10-01-lyra-main-own-locomotion.md',
    'tools/verify_lyra_main_own_host.py',
)
report = dict(schemaVersion=1, baselineSha256=sha(baseline_path.read_bytes()),
    fixturesProtected=643, packagesProtected=508, ownMainFrames=7560, ownMixedPoses=6330,
    states=10, roots=10, turnFrames=3780, turnMixedPoses=3432,
    turnFeedbackChanges=1576, mixedIdleFeedbackFrames=95, updatedUnevaluatedRoots=4,
    sourceObservation='Godot committed owners', syncObservation='Godot batch and persistent buffer keys',
    feedbackBoundary='LocomotionSM', fixedProvider=True, nativeJoint=False,
    productionLayers=False, unifiedNotifies=False, finalUpperbodyInertia=False,
    ordinaryDemo=False, visualAcceptance=False, performanceAcceptance=False, wholeGoalComplete=False,
    sourceSha256={name: sha((repo / name).read_bytes()) for name in paths}, logs=logs)
(artifacts / 'main-own-host-final-verification.json').write_text(
    json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print('LYRA_MAIN_OWN_HOST_FINAL_VERIFIED own_frames=11340 mixed_poses=9762 states=10 roots=10 updated_unevaluated=4 packages=508 fixtures=643 native_joint=false whole_goal=false')
