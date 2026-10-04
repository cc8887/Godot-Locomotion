"""Audit native motor provenance and actual scene verification independently."""
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
EVIDENCE = ROOT / 'artifacts/lyra-analysis'
PROJECT = Path('../GASP58')
ASSETS = ROOT / 'assets/generated/lyra_als'
TAG = 'cmc-motor-v2-final'
OUTPUT = EVIDENCE / 'character-motor-v2-integrity.json'
assert not OUTPUT.exists(), 'Preserve audit evidence'


def load(path):
    return json.loads(path.read_bytes())


def sha(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()


closure = load(EVIDENCE / 'whole-main-cmc60-motor-v2-closure.json')
for rel, digest in closure['previousFixtureSha256'].items():
    assert sha(ASSETS / rel) == digest, rel
for kind in ('protectedProject', 'movementSourceSha256'):
    for rel, digest in closure[kind].items():
        assert sha(PROJECT / rel) == digest, rel
for name, digest in closure['assetSha256'].items():
    path = name.split('.')[0]
    original = PROJECT / 'Content' / (path.removeprefix('/Game/') + '.uasset') if path.startswith('/Game/') else PROJECT / 'Plugins/GameFeatures/ShooterCore/Content' / (path.removeprefix('/ShooterCore/') + '.uasset')
    assert sha(original) == digest, name
probe_root = ROOT / 'tools/unreal/LyraWholeMainOracle'
package = ROOT / 'artifacts/unreal/lyra-whole-main-oracle/package-cmc-motor-v2'
for rel, digest in closure['probeSourceSha256'].items():
    assert sha(probe_root / rel) == digest == sha(package / rel), rel
native_path = EVIDENCE / 'whole-main-cmc60-motor-v2-native.json'
native = load(native_path)
resource_path = ASSETS / 'character_motor_v2.json'
resource = load(resource_path)
fixture_path = ROOT / 'tests/Als.Core.Tests/Fixtures/Physics/lyra_character_falling_native.json'
fixture = load(fixture_path)
assert resource['schemaVersion'] == 2 and resource['evidenceSha256'] == fixture['evidenceSha256'] == sha(native_path)
for trace in native['traces']:
    assert trace['actualCharacterMovement'] and trace['motorProfile'] == resource['profile'] == fixture['profile']
    assert trace['fallingKernel'] == fixture['fallingKernel'] and len(trace['fallingKernel']) == 175
    assert trace['velocityKernel'] == fixture['kernel'] and len(trace['velocityKernel']) == 250
assert len(native['traces']) == 3
assert 'LYRA_WHOLE_MAIN_CAPTURE_OK traces=3 frames=180 assets_saved=0' in (EVIDENCE / 'whole-main-native-cmc60-motor-v2.log').read_text(encoding='utf-8-sig')

prior_path = EVIDENCE / 'character-movement-ispc-integrity.json'
prior = load(prior_path)
assert prior['auditPassed'] and prior['comparisonPassed'] and not prior['goalComplete']
reports = []
for config, directory in [('debug', 'Debug'), ('optimize', 'ExportRelease')]:
    report_path = EVIDENCE / f'character-motor-{config}-{TAG}-verification.json'
    report = load(report_path)
    assert report['passed'] and len(report['runs']) == 9 and not report['completeAcceptance'] and not report['nativeWorldTrajectoryParity']
    assert {r['name'] for r in report['runs']} == {'physics-30', 'physics-60', 'physics-120', 'ordinary-30', 'ordinary-60', 'ordinary-120', 'root-60', 'warp-60', 'emote-60'}
    for run in report['runs']:
        log = Path(run['log'])
        assert run['passed'] and run['exitCode'] == 0 and sha(log).upper() == run['logSha256']
        text = log.read_text(encoding='utf-8-sig')
        assert 'CHARACTER_MOTOR_PROCESS_EXIT=0' in text and not re.search(r'^\s*(ERROR|WARNING):', text, re.M)
    for name, digest in report['assemblies'].items():
        assert sha(ROOT / '.godot/mono/temp/bin' / directory / name).upper() == digest
        if config == 'optimize':
            assert report['debugRestored']
            backup = EVIDENCE / f'character-motor-{config}-{TAG}-debug-backup' / name
            assert sha(backup) == sha(ROOT / '.godot/mono/temp/bin/Debug' / name)
    for hz in (30, 60, 120):
        prefix = f'character-motor-{config}-{TAG}'
        physics = load(EVIDENCE / f'{prefix}-physics-{hz}.json')
        assert physics['actualJolt'] and physics['frames'] == hz * 4 and physics['roles'] == 6
        assert physics['moves'] == physics['retries'] == hz * 24
        assert physics['midpointErrorM'] < 2e-6 and physics['jumpVelocityErrorMps'] < 1e-6
        assert 1.24 < physics['jumpHeightM'] < 1.30
        assert physics['blockedStandFrames'] and physics['releasedStandFrames'] and physics['wallContacts']
        assert physics['characterMotorSha256'] == sha(resource_path).upper()
        ordinary = load(EVIDENCE / f'{prefix}-ordinary-{hz}.json')
        assert ordinary['characters'] == 10
        model = ordinary['player']['model']
        assert model['sharedShooterMovement'] and model['frames'] == model['published'] == model['retries'] == model['capsuleMoves'] == hz * 8
        assert model['characterMotorSha256'] == sha(resource_path).upper()
        assert len(ordinary['companions']) == 9
        assert all(c['published'] == c['capsuleMoves'] == hz * 8 for c in ordinary['companions'])
        if config == 'optimize':
            assert ordinary == load(EVIDENCE / f'character-motor-debug-{TAG}-ordinary-{hz}.json')
            assert physics == load(EVIDENCE / f'character-motor-debug-{TAG}-physics-{hz}.json')
    reports.append(dict(path=report_path.name, sha256=sha(report_path)))

trx_path = ROOT / 'artifacts/tests/cmc-motor-v2/cmc-motor-v2-related.trx'
trx = ET.parse(trx_path)
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
counters = trx.find('.//t:Counters', ns).attrib
assert counters['passed'] == '12' and counters['failed'] == '0' and counters['notExecuted'] == '0'
source_files = [
    'src/Als.Core/Locomotion/AlsCharacterFalling.cs',
    'src/Als.Core/Locomotion/AlsCharacterVelocity.cs',
    'src/Als.Godot/Locomotion/LyraCharacterMovementSettings.cs',
    'src/Als.Godot/Locomotion/LyraSceneMovementService.cs',
    'src/Als.Godot/Locomotion/LyraSceneCharacter.cs',
    'src/Als.Godot/Locomotion/LyraLocomotionDemo.cs',
    'src/Als.Godot/Locomotion/LyraRootMovementMotor.cs',
    'src/Als.Godot/Animation/Lyra/LyraCharacterAnimation.cs',
    'src/Als.Godot/Locomotion/LyraCharacterMovementPhysicsSmoke.cs',
    'src/Als.Godot/Animation/Lyra/LyraMotionWarpingPhysicsSmoke.cs',
    'tests/Als.Core.Tests/AlsCharacterVelocityTests.cs',
]
result = dict(schemaVersion=1, auditPassed=True, sceneServicePassed=True, nativeKernelSamples=425,
              managedTests=12, managedTrxSha256=sha(trx_path), reports=reports,
              protectedCounts={k: len(closure[k]) for k in ('previousFixtureSha256', 'protectedProject', 'assetSha256', 'movementSourceSha256')},
              motorResourceSha256=sha(resource_path), nativeCaptureSha256=sha(native_path), fixtureSha256=sha(fixture_path),
              priorAnimationAuditSha256=sha(prior_path), sourceSha256={p: sha(ROOT / p) for p in source_files},
              ordinaryAndPhysicsReportsExactlyEqual=True, nativeWorldTrajectoryParity=False, completeAcceptance=False, goalComplete=False)
with OUTPUT.open('x', encoding='utf-8', newline='\n') as stream:
    json.dump(result, stream, indent=2)
print(json.dumps(result, indent=2))
