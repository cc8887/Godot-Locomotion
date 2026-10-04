"""Audit original CMC-driven animation comparisons and retained input bytes."""
import gc
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

from locomotion_paths import project_path

repo = Path(__file__).resolve().parents[1]
evidence = repo / 'artifacts/lyra-analysis'
assets = repo / 'assets/generated/lyra_als'
project = project_path()


def sha(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()


def load(path):
    return json.loads(path.read_bytes())


def text(path):
    return path.read_text(encoding='utf-8-sig', errors='replace')


def clean(path):
    value = text(path)
    assert 'WHOLE_MAIN_PROCESS_EXIT=0' in value, path
    assert not re.search(r'^\s*(ERROR|WARNING):', value, re.M), path
    return value


def check_assemblies(report, prefix, configuration):
    directory = repo / '.godot/mono/temp/bin' / ('Debug' if configuration == 'debug' else 'ExportRelease')
    for name, digest in report['assemblies'].items():
        assert sha(directory / name).upper() == digest.upper(), (configuration, name)
        if configuration == 'optimize':
            assert report['debugRestored']
            assert sha(evidence / (prefix + '-debug-backup') / name) == sha(repo / '.godot/mono/temp/bin/Debug' / name)


def frame_hashes(native):
    # Execution-event instrumentation must preserve physical inputs and all
    # animation outputs. Compare without retaining two large native documents.
    keys = ('physicalInput', 'mainUpdated', 'layerUpdated', 'output', 'updates',
            'layerOutputs', 'evaluators', 'sequencePlayers', 'upperWeights', 'rigCollision')
    return {t['profile']: [hashlib.sha256(json.dumps({k: f[k] for k in keys},
                sort_keys=True, separators=(',', ':')).encode()).hexdigest() for f in t['frames']]
            for t in native['traces']}


output = evidence / 'character-movement-integrity.json'
assert not output.exists(), 'Preserve previous audit'
reports, captures, summary = [], [], []
protected = None
native60_hashes = None
for hz, tag in ((30, 'cmc30-ground-events'), (60, 'cmc60-ground-events-final'), (120, 'cmc120-ground-events')):
    closure = load(evidence / f'whole-main-{tag}-closure.json')
    launch = load(evidence / f'whole-main-{tag}-launch.json')
    scope = closure['scope']
    assert scope['case'] == 'physics' and scope['originalFullRoot'] and scope['originalUnifiedSync']
    assert scope['actualCharacterMovementSimulation'] and scope['authoredControls']
    assert not scope['controlledPhysicalInputs'] and not scope['goalComplete']
    assert scope['als81'] and scope['originalTargetMasks'] and scope['finalControlRig']
    native_log = evidence / f'whole-main-native-{tag}.log'
    value = text(native_log)
    assert f'LYRA_WHOLE_MAIN_CAPTURE_OK traces=3 frames={hz * 24} assets_saved=0' in value
    assert 'WHOLE_MAIN_PROCESS_EXIT=0' in value
    assert not re.search(r'LogAbilitySystem: Error|Assertion failed|LogPython: Error', value)
    hashes = {k: closure[k] for k in ('previousFixtureSha256', 'protectedProject', 'assetSha256', 'movementSourceSha256')}
    if protected is None:
        protected = hashes
    else:
        assert protected == hashes, 'Capture inputs drifted'
    for rel, digest in closure['probeSourceSha256'].items():
        assert sha(repo / 'tools/unreal/LyraWholeMainOracle' / rel) == digest, rel
    for rel, digest in launch['sourceSha256'].items():
        assert sha(Path(launch['package']) / 'Source' / rel).upper() == digest.upper(), rel
    native_path = evidence / f'whole-main-{tag}-native.json'
    request = load(evidence / f'whole-main-{tag}-request.json')
    native = load(native_path)
    coverage = load(evidence / f'whole-main-{tag}-physics-coverage.json')['traces']
    rules = load(evidence / f'whole-main-{tag}-stance-rules.json')
    assert len(native['traces']) == len(request['traces']) == len(coverage) == 3
    for t, r, c in zip(native['traces'], request['traces'], coverage, strict=True):
        assert t['profile'] == r['profile'] == c['profile']
        assert t['actualCharacterMovement'] and len(t['frames']) == len(r['frames']) == hz * 8
        assert t['motorProfile']['movementClass'] == '/Script/LyraGame.LyraCharacterMovementComponent'
        assert len(t['velocityKernel']) == 250
        assert c['airFrames'] > 0 and c['crouchFrames'] == hz
        assert c['barrierContacts'] >= hz // 2 and c['maximumX'] <= 590.1 and c['rigHitFrames'] > 0
        for index in range(3):
            f = t['frames'][index]
            assert f['rigBeforeEvaluation']['initRequired'] == (index == 0)
            assert f['rigBeforeEvaluation']['constructionRequired'] == (index < 2)
            assert any(e['event'] == 'Construction' for e in f['rigEvents']) == (index < 2)
            assert sum(e['initialized'] for e in f['rigEvents']) == (1 if index == 0 else 0)
        for f in t['frames']:
            assert len(f['output']['pose']) == 81
            assert len(f['rigCollision']['queries']) == 8
    for authored, actual in zip(rules['request']['traces'], rules['native']['traces'], strict=True):
        assert len(authored['rows']) == len(actual['rows']) == 64
        for a, n in zip(authored['rows'], actual['rows'], strict=True):
            stance = [r for r in n['rules'] if r['machine'] == 2]
            assert len(stance) == 2 and all(r['result'] == a['main']['CrouchStateChange'] for r in stance)
    if hz == 60:
        native60_hashes = frame_hashes(native)
    del native
    gc.collect()
    summary.append(dict(hz=hz, frames=hz * 24, coverage=coverage, originalStanceRuleCases=192))
    captures.append(dict(tag=tag, nativeSha256=sha(native_path), closureSha256=sha(evidence / f'whole-main-{tag}-closure.json'), logSha256=sha(native_log)))

old60 = load(evidence / 'whole-main-cmc60-ground-native.json')
assert frame_hashes(old60) == native60_hashes, 'Event instrumentation changed execution'
del old60
gc.collect()

matrix = [(30, 'cmc30-ground-events', 'cmc30-lifecycle-final'),
          (60, 'cmc60-ground', 'cmc60-lifecycle-first'),
          (120, 'cmc120-ground-events', 'cmc120-lifecycle-final')]
for configuration in ('debug', 'optimize'):
    for hz, run_tag, tag in matrix:
        if configuration == 'optimize' and hz == 60:
            tag = 'cmc60-lifecycle-optimize'
        prefix = f'whole-main-{configuration}-{tag}'
        path = evidence / (prefix + '-verification.json')
        report = load(path)
        assert report['comparisonPassed'] == (hz != 120) and report['retry'] and not report['completeAcceptance']
        assert report['runTag'] == run_tag and len(report['runs']) == 3
        for run in report['runs']:
            known_failure = hz == 120 and run['boundary'] == 'final'
            assert run['comparisonPassed'] == (not known_failure)
            if known_failure:
                value = text(Path(run['log']))
                assert run['exitCode'] == 1 and 'WHOLE_MAIN_PROCESS_EXIT=1' in value
                assert 'pistol/451/bone50' in run['firstFailure']
                assert 'quaternion=1.6216930576451093E-10' in run['firstFailure']
                assert 'ERROR: Whole Main diagnostic failed:' in value
            else:
                assert run['exitCode'] == 0
                value = clean(Path(run['log']))
                assert f'frames={hz * 24} profiles=3 ' in value
                assert f'retry={hz * 24} controlledPhysicalInputs=false replayNativeMovement=true actualGodotPhysics=false completeAcceptance=false' in value
            run['logSha256'] = sha(Path(run['log']))
        for regression in report['regressions']:
            assert regression['passed'] and regression['exitCode'] == 0
            clean(Path(regression['log']))
            regression['logSha256'] = sha(Path(regression['log']))
        check_assemblies(report, prefix, configuration)
        reports.append(dict(path=path.name, sha256=sha(path), result=report))
    prefix = f'whole-main-{configuration}-cmc-rebind-lifecycle-final'
    path = evidence / (prefix + '-verification.json')
    report = load(path)
    assert report['runTag'] == 'rebind60-first' and report['comparisonPassed'] and report['retry']
    for run in report['runs']:
        assert run['comparisonPassed'] and run['exitCode'] == 0
        value = clean(Path(run['log']))
        assert 'frames=2160 profiles=3 ' in value and 'rebinds=24 sameClass=24 ' in value
        run['logSha256'] = sha(Path(run['log']))
    check_assemblies(report, prefix, configuration)
    reports.append(dict(path=path.name, sha256=sha(path), result=report))
    prefix = f'cmc-components-{configuration}-lifecycle-final'
    path = evidence / (prefix + '-verification.json')
    report = load(path)
    assert {r['name'] for r in report['runs']} == {'idle-original', 'rig-target', 'rig-scene'}
    for run in report['runs']:
        assert run['passed'] and run['exitCode'] == 0
        clean(Path(run['log']))
        run['logSha256'] = sha(Path(run['log']))
    check_assemblies(report, prefix, configuration)
    reports.append(dict(path=path.name, sha256=sha(path), result=report))

for rel, digest in protected['previousFixtureSha256'].items():
    assert sha(assets / rel) == digest, rel
for kind in ('protectedProject', 'movementSourceSha256'):
    for rel, digest in protected[kind].items():
        assert sha(project / rel) == digest, rel
for name, digest in protected['assetSha256'].items():
    p = name.split('.')[0]
    path = project / 'Content' / (p.removeprefix('/Game/') + '.uasset') if p.startswith('/Game/') else project / 'Plugins/GameFeatures/ShooterCore/Content' / (p.removeprefix('/ShooterCore/') + '.uasset')
    assert sha(path) == digest, name
for configuration in ('debug', 'optimize'):
    value = text(evidence / f'cmc-rig-lifecycle-{configuration}-build.log')
    assert '0 个警告' in value and '0 个错误' in value
ordinary = [load(evidence / f'whole-main-{c}-{t}-ordinary-ten.json') for c, t in
            (('debug', 'cmc30-lifecycle-final'), ('optimize', 'cmc60-lifecycle-optimize'))]
assert ordinary[0] == ordinary[1], 'Ordinary Jolt regression differs by build'
scene = [load(evidence / f'cmc-components-{c}-lifecycle-final-rig-scene.json') for c in ('debug', 'optimize')]
assert scene[0] == scene[1] and scene[0]['actualGodotPhysics'] and not scene[0]['nativePhysicsParity']
motor = load(assets / 'character_motor_v1.json')
assert sha(evidence / motor['evidenceNative']) == motor['evidenceSha256']
fixture = load(repo / 'tests/Als.Core.Tests/Fixtures/Physics/lyra_character_velocity_native.json')
assert fixture['profile'] == motor['profile'] and fixture['kernel'] == motor['kernel']
trx = repo / 'tests/Als.Core.Tests/TestResults/lyra-cmc-kernel-third.trx'
counters = ET.parse(trx).find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters').attrib
assert int(counters['total']) == int(counters['passed']) == 1 and int(counters['failed']) == 0
sources = ['src/Als.Core/Locomotion/AlsCharacterVelocity.cs', 'tests/Als.Core.Tests/AlsCharacterVelocityTests.cs',
           'src/Als.Godot/Animation/Lyra/LyraIdleLayerHost.cs', 'src/Als.Godot/Animation/Lyra/LyraLocomotionSourceScope.cs',
           'src/Als.Godot/Animation/Lyra/LyraFootPlantRigPoseHost.cs', 'src/Als.Godot/Animation/Lyra/LyraMainPoseHost.cs',
           'src/Als.Godot/Animation/Lyra/LyraWholeMainDiagnosticSmoke.cs', 'scripts/capture-lyra-whole-main.ps1',
           'scripts/verify-lyra-whole-main-diagnostic.ps1', 'scripts/verify-lyra-cmc-components.ps1',
           'tools/unreal/capture_lyra_whole_main.py', 'tools/verify_lyra_character_movement.py']
retained = ['whole-main-debug-cmc60-first-verification.json', 'whole-main-debug-cmc60-stance-first-verification.json',
            'whole-main-debug-cmc60-ground-first-verification.json', 'whole-main-debug-cmc60-collision-replay-verification.json']
ik = load(evidence / 'cmc120-ik-native-diagnostic.json')
assert len(ik['inputs']) == len(ik['native']['calls']) == 2
for a, n in zip(ik['inputs'], ik['native']['calls'], strict=True):
    for bone in 'ABC':
        for field, channels in (('Position', 'XYZ'), ('Rotation', 'XYZW'), ('Scale', 'XYZ')):
            native_field = {'Position': 'p', 'Rotation': 'q', 'Scale': 's'}[field]
            assert [a['Solved' + bone][field][c] for c in channels] == n[bone][native_field]
assert 'LYRA_CMC_IK_MATH_DIAGNOSTIC_OK calls=2 assets_saved=0' in text(evidence / 'cmc120-ik-native-diagnostic.log')
assert 'CMC_IK_NATIVE_PROCESS_EXIT=0' in text(evidence / 'cmc120-ik-native-diagnostic.log')
for name in ('cmc120-ik-diagnostic-restored.log', 'cmc120-stage-diagnostic-restored.log'):
    assert 'source3=true binary6=true' in text(evidence / name)
for name in ('LyraMainPoseHost.cs', 'LyraFootPlantRigPoseHost.cs', 'LyraWholeMainDiagnosticSmoke.cs'):
    assert sha(repo / 'src/Als.Godot/Animation/Lyra' / name) == sha(evidence / 'cmc120-ik-diagnostic-baseline' / name)
counterfactual = text(evidence / 'cmc120-input-counterfactual.log')
assert 'CMC_STAGE_POSES ' in counterfactual and 'Rig physical query 123 differs before replay.' in counterfactual
result = dict(schemaVersion=1, auditPassed=True, comparisonPassed=False, completeAcceptance=False, goalComplete=False,
    scope=dict(originalCharacterMovement=True, animationReplaysNativePhysicalObservations=True,
               rigReplaysNativePhysicalQueryResults=True, ordinaryJoltRegression=True,
               productionCharacterMotorMigrated=False, nativeJoltMotorParity=False),
    summary=summary, captures=captures, reports=reports, originalKernelCases=250,
    originalKernelTestSha256=sha(trx), originalMotorAssetSha256=sha(assets / 'character_motor_v1.json'),
    eventInstrumentationPreservesNativeFrames=True, ordinaryReportsExactlyEqual=True, rigSceneReportsExactlyEqual=True,
    protectedCounts={k: len(v) for k, v in protected.items()}, sourceSha256={p: sha(repo / p) for p in sources},
    retainedEvidence={name: sha(evidence / name) for name in retained},
    openFailure='Both builds: Pistol/120Hz/frame451/bone50 quaternion 1.6216930576451093e-10 exceeds unchanged 1e-10 gate; pre-inertia and pre-rig pass',
    diagnostic=dict(originalIkOnGodotInputsExactlyEqual=True, sourceInputCounterfactualNotAcceptance=True,
                    inputCounterfactualSha256=sha(evidence / 'cmc120-input-counterfactual.log'),
                    originalIkSha256=sha(evidence / 'cmc120-ik-native-diagnostic.json')),
    excludedEvidence='cmc60-collision-replay used stale DLL after failed compile; preserved, excluded from acceptance')
with output.open('x', encoding='utf-8', newline='\n') as stream:
    json.dump(result, stream, separators=(',', ':'), allow_nan=False)
    stream.write('\n')
print('LYRA_CHARACTER_MOVEMENT_INTEGRITY_OK referenceFramesPerConfiguration=5040 nativeKernelCases=250 completeMatrixPassed=false goalComplete=false')
