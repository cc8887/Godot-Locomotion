"""Audit the versioned ISPC correction without replacing retained failures."""
import hashlib
import gc
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
evidence = repo / 'artifacts/lyra-analysis'
assets = repo / 'assets/generated/lyra_als'
project = Path('../GASP58')
output = evidence / 'character-movement-ispc-integrity.json'
assert not output.exists(), 'Preserve audit evidence'


def load(path):
    return json.loads(path.read_bytes())


def sha(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()


def text(path):
    return path.read_text(encoding='utf-8-sig', errors='replace')


def clean(path):
    value = text(path)
    assert 'WHOLE_MAIN_PROCESS_EXIT=0' in value, path
    assert not re.search(r'^\s*(ERROR|WARNING):', value, re.M), path
    return value


previous_path = evidence / 'character-movement-integrity.json'
previous = load(previous_path)
assert previous['auditPassed'] and not previous['comparisonPassed']
protected = None
for capture in previous['captures']:
    tag = capture['tag']
    closure_path = evidence / f'whole-main-{tag}-closure.json'
    assert sha(closure_path) == capture['closureSha256']
    assert sha(evidence / f'whole-main-{tag}-native.json') == capture['nativeSha256']
    closure = load(closure_path)
    fields = {k: closure[k] for k in ('previousFixtureSha256', 'protectedProject', 'assetSha256', 'movementSourceSha256')}
    if protected is None:
        protected = fields
    else:
        assert protected == fields
for rel, digest in protected['previousFixtureSha256'].items():
    assert sha(assets / rel) == digest, rel
for kind in ('protectedProject', 'movementSourceSha256'):
    for rel, digest in protected[kind].items():
        assert sha(project / rel) == digest, rel
for name, digest in protected['assetSha256'].items():
    p = name.split('.')[0]
    path = project / 'Content' / (p.removeprefix('/Game/') + '.uasset') if p.startswith('/Game/') else project / 'Plugins/GameFeatures/ShooterCore/Content' / (p.removeprefix('/ShooterCore/') + '.uasset')
    assert sha(path) == digest, name
for retained in previous['reports']:
    assert sha(evidence / retained['path']) == retained['sha256']
    for run in retained['result']['runs']:
        assert sha(Path(run['log'])) == run['logSha256']

reports = []
for configuration in ('debug', 'optimize'):
    directory = repo / '.godot/mono/temp/bin' / ('Debug' if configuration == 'debug' else 'ExportRelease')
    for hz in (30, 60, 120):
        tag = f'cmc{hz}-ispc-mixing-verified'
        prefix = f'whole-main-{configuration}-{tag}'
        report_path = evidence / (prefix + '-verification.json')
        report = load(report_path)
        assert report['comparisonPassed'] and report['retry'] and not report['completeAcceptance']
        assert len(report['runs']) == 3
        assert report['runTag'] == {30: 'cmc30-ground-events', 60: 'cmc60-ground-events-final', 120: 'cmc120-ground-events'}[hz]
        assert len(report['regressions']) == (7 if (configuration, hz) in (('debug', 30), ('optimize', 60)) else 0)
        assert sha(assets / 'native_win64/LyraNativeMath.dll').upper() == report['nativeMathSha256'].upper()
        for run in report['runs']:
            assert run['comparisonPassed'] and run['exitCode'] == 0
            value = clean(Path(run['log']))
            assert f'frames={hz * 24} profiles=3 ' in value
            assert f'retry={hz * 24} controlledPhysicalInputs=false replayNativeMovement=true actualGodotPhysics=false completeAcceptance=false' in value
        for regression in report['regressions']:
            assert regression['passed'] and regression['exitCode'] == 0
            clean(Path(regression['log']))
        for name, digest in report['assemblies'].items():
            assert sha(directory / name).upper() == digest.upper()
            if configuration == 'optimize':
                assert report['debugRestored']
                assert sha(evidence / (prefix + '-debug-backup') / name) == sha(repo / '.godot/mono/temp/bin/Debug' / name)
        reports.append(dict(path=report_path.name, sha256=sha(report_path)))
    prefix = f'whole-main-{configuration}-cmc-rebind-ispc-mixing-verified'
    report_path = evidence / (prefix + '-verification.json')
    report = load(report_path)
    assert report['comparisonPassed'] and report['retry'] and report['runTag'] == 'rebind60-first'
    for run in report['runs']:
        assert run['comparisonPassed'] and run['exitCode'] == 0
        value = clean(Path(run['log']))
        assert 'frames=2160 profiles=3 ' in value and 'rebinds=24 sameClass=24 ' in value
    for name, digest in report['assemblies'].items():
        assert sha(directory / name).upper() == digest.upper()
    if configuration == 'optimize':
        assert report['debugRestored']
        for name in report['assemblies']:
            assert sha(evidence / (prefix + '-debug-backup') / name) == sha(repo / '.godot/mono/temp/bin/Debug' / name)
    reports.append(dict(path=report_path.name, sha256=sha(report_path)))
    value = text(evidence / f'cmc-ispc-mixing-{configuration}-report-fix-build.log')
    assert '0 个警告' in value and '0 个错误' in value

ordinary = [load(evidence / f'whole-main-{configuration}-cmc{hz}-ispc-mixing-verified-ordinary-ten.json')
            for configuration, hz in (('debug', 30), ('optimize', 60))]
assert ordinary[0] == ordinary[1]
trx = repo / 'tests/Als.Core.Tests/TestResults/lyra-cmc-ispc-mixing.trx'
counters = ET.parse(trx).find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters').attrib
assert int(counters['total']) == int(counters['passed']) == 24
assert int(counters['failed']) == int(counters['notExecuted']) == 0
native_row = load(evidence / 'cmc120-pistol451-original-row.json')
native120 = load(evidence / 'whole-main-cmc120-ground-events-native.json')
assert next(t for t in native120['traces'] if t['profile'] == 'pistol')['frames'][451] == native_row
del native120
gc.collect()
before_pose_path = evidence / 'cmc120-pistol451-pose-inspection.json'
before_pose = load(before_pose_path)
assert before_pose['stages']['Aim_Input'][1]['Position']['Z'] != next(
    x for x in native_row['layerOutputs'] if x['hook'] == 'Aim_RelaxedInput')['output']['pose'][1]['position'][2]
pose_report_path = evidence / 'cmc120-pistol451-ispc-mixing-verified-pose.json'
pose_report = load(pose_report_path)
assert (pose_report['profile'], pose_report['frame']) == ('pistol', 451)
native_stages = {x['hook']: x['output']['pose'] for x in native_row['layerOutputs']}
native_stages['Aim_Input'] = native_stages['Aim_RelaxedInput']
native_stages['Main_Final'] = native_row['output']['pose']
for key, poses in pose_report['stages'].items():
    if key not in native_stages:
        continue
    for actual, expected in zip(poses, native_stages[key], strict=True):
        for field, channels in (('Position', 'XYZ'), ('Rotation', 'XYZW'), ('Scale', 'XYZ')):
            assert [actual[field][c] for c in channels] == expected[field.lower()], key
pose_log = clean(evidence / 'cmc120-ispc-mixing-pose-verified.log')
assert 'frames=2880 profiles=3 ' in pose_log and 'retry=2880 ' in pose_log
diagnostic = repo / 'src/Als.Godot/Animation/Lyra/LyraWholeMainDiagnosticSmoke.cs'
assert 'FileMode.CreateNew' in text(diagnostic)
assert 'poseOverride' not in text(diagnostic) and 'counterfactual' not in text(diagnostic)
sources = ['src/Als.Core/Locomotion/AlsPrecisePoseBlender.cs', 'src/Als.Core/Locomotion/AlsMeshSpacePoseBlend.cs',
           'src/Als.Godot/Animation/Lyra/LyraAimingLayerHost.cs',
           'src/Als.Godot/Animation/Lyra/LyraItemLayerGraphInstance.cs', str(diagnostic.relative_to(repo)),
           'tools/verify_lyra_character_movement_ispc.py']
engine = Path('../UE_5.8/Engine/Source/Runtime')
kernel_sources = ['Engine/Private/Animation/AnimationRuntime.cpp', 'Engine/Private/Animation/AnimationRuntime.ispc',
                  'Core/Public/Math/Vector.isph']
result = dict(schemaVersion=1, auditPassed=True, comparisonPassed=True, completeAcceptance=False, goalComplete=False,
    referenceFramesPerConfiguration=5040, retryPerConfiguration=5040, reports=reports,
    originalCapturesAndFailuresRetained=True, previousAuditSha256=sha(previous_path),
    protectedCounts=previous['protectedCounts'], ordinaryReportsExactlyEqual=True,
    managedTests=24, managedTrxSha256=sha(trx), sourceSha256={p: sha(repo / p) for p in sources},
    failedFrameStagesExactlyEqual=True, poseReportSha256=sha(pose_report_path),
    preFixPoseReportSha256=sha(before_pose_path), nativeKernelSourceSha256={p: sha(engine / p) for p in kernel_sources},
    excludedPoseDiagnostic='cmc120-ispc-mixing-pose.log: read-only report retried its CreateNew write; fixed with attempt==0 and independently reverified',
    scope=dict(originalCharacterMovement=True, animationReplaysNativePhysicalObservations=True,
               rigReplaysNativePhysicalQueryResults=True, productionCharacterMotorMigrated=False,
               nativeJoltMotorParity=False),
    fixed='ISPC mesh VectorLerp translation/scale and BlendTransformAccumulate translation/scale; original IK and tolerance retained')
with output.open('x', encoding='utf-8', newline='\n') as stream:
    json.dump(result, stream, separators=(',', ':'), allow_nan=False)
    stream.write('\n')
print('LYRA_CHARACTER_MOVEMENT_ISPC_INTEGRITY_OK referenceFramesPerConfiguration=5040 completeMatrixPassed=true goalComplete=false')
