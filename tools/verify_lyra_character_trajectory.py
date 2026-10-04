"""Audit genuine input-driven comparison, including retained parity failures."""
import hashlib
import json
import math
import re
from pathlib import Path

from locomotion_paths import engine_path, project_path

ROOT = Path(__file__).resolve().parents[1]
E = ROOT / 'artifacts/lyra-analysis'
PROJECT = project_path()
TAG = 'cmc-trajectory-v1-wall-contacts-final'
OUTPUT = E / 'character-trajectory-v1-integrity.json'
assert not OUTPUT.exists(), 'Preserve trajectory audit'


def load(path):
    return json.loads(path.read_bytes())


def sha(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def distance(a, b):
    return math.sqrt(sum((x - y) ** 2 for x, y in zip(a, b, strict=True)))


closure = load(E / 'whole-main-cmc60-motor-v2-closure.json')
assets = ROOT / 'assets/generated/lyra_als'
for rel, digest in closure['previousFixtureSha256'].items():
    assert sha(assets / rel) == digest, rel
for group in ('protectedProject', 'movementSourceSha256'):
    for rel, digest in closure[group].items():
        assert sha(PROJECT / rel) == digest, rel
for name, digest in closure['assetSha256'].items():
    path = name.split('.')[0]
    if path.startswith('/Game/'):
        original = PROJECT / 'Content' / (path.removeprefix('/Game/') + '.uasset')
    else:
        original = PROJECT / 'Plugins/GameFeatures/ShooterCore/Content' / (path.removeprefix('/ShooterCore/') + '.uasset')
    assert sha(original) == digest, name

reference_path = E / 'character-motor-trajectory-v1-reference.json'
reference = load(reference_path)
assert reference['actualOriginalCMC'] and reference['controlsOnlyDriveGodot']
assert reference['start'] == [0, 0, 92]
assert [t['hz'] for t in reference['traces']] == [30, 60, 120]
assert sum(len(t['frames']) for t in reference['traces']) == 1680
prior = load(E / 'character-movement-integrity.json')
captures = {c['tag']: c for c in prior['captures']}
for evidence in reference['evidence']:
    for suffix, key in [('native', 'nativeSha256'), ('request', 'requestSha256'), ('closure', 'closureSha256')]:
        assert sha(E / f"whole-main-{evidence['tag']}-{suffix}.json") == evidence[key]
    assert evidence['nativeSha256'] == captures[evidence['tag']]['nativeSha256']
    assert evidence['closureSha256'] == captures[evidence['tag']]['closureSha256']

motor_path = assets / 'character_motor_v2.json'
motor_prior = load(E / 'character-motor-v2-integrity.json')
# The earlier audit's source/DLL hashes are historical; only reuse its recorded
# immutable motor hash, not a claim about today's assemblies.
assert motor_prior['auditPassed']
assert sha(motor_path) == motor_prior['motorResourceSha256']
assert sha(E / 'whole-main-cmc60-motor-v2-native.json') == motor_prior['nativeCaptureSha256']
assert sha(ROOT / 'tests/Als.Core.Tests/Fixtures/Physics/lyra_character_falling_native.json') == motor_prior['fixtureSha256']
unchanged_motor_sources = {s:digest for s,digest in motor_prior['sourceSha256'].items() if s not in {
    'src/Als.Godot/Locomotion/LyraSceneMovementService.cs',
    'src/Als.Godot/Locomotion/LyraSceneCharacter.cs',
    'src/Als.Godot/Locomotion/LyraCharacterMovementPhysicsSmoke.cs',
}}
for source, digest in unchanged_motor_sources.items():
    assert sha(ROOT/source) == digest, source
motor = load(motor_path)
assert motor['schemaVersion'] == 2 and motor['profile']['gravityZ'] == -980

reports = []
for config, directory in [('debug', 'Debug'), ('optimize', 'ExportRelease')]:
    motor_summary_path = E / f'character-motor-{config}-{TAG}-verification.json'
    motor_summary = load(motor_summary_path)
    assert motor_summary['passed'] and len(motor_summary['runs']) == 9
    assert not motor_summary['goalComplete'] and not motor_summary['nativeWorldTrajectoryParity']
    trajectory_summary_path = E / f'character-trajectory-{config}-{TAG}-verification.json'
    trajectory_summary = load(trajectory_summary_path)
    assert trajectory_summary['diagnosticsCompleted'] and len(trajectory_summary['runs']) == 3
    assert not trajectory_summary['comparisonPassed'] and not trajectory_summary['goalComplete']
    assert trajectory_summary['referenceSha256'].lower() == sha(reference_path)
    assert trajectory_summary['assemblies'] == motor_summary['assemblies']
    for name, digest in motor_summary['assemblies'].items():
        assert sha(ROOT / '.godot/mono/temp/bin' / directory / name).upper() == digest
        if config == 'optimize':
            assert motor_summary['debugRestored'] and trajectory_summary['debugRestored']
            for prefix in ('character-motor', 'character-trajectory'):
                backup = E / f'{prefix}-{config}-{TAG}-debug-backup' / name
                assert sha(backup) == sha(ROOT / '.godot/mono/temp/bin/Debug' / name)
    for run in motor_summary['runs']:
        log = Path(run['log'])
        assert run['passed'] and run['exitCode'] == 0 and sha(log).upper() == run['logSha256']
        assert not re.search(r'^\s*(ERROR|WARNING):', log.read_text(encoding='utf-8-sig'), re.M)
    for hz in (30, 60, 120):
        prefix = f'character-motor-{config}-{TAG}'
        physics = load(E / f'{prefix}-physics-{hz}.json')
        assert physics['frames'] == hz * 4 and physics['moves'] == physics['retries'] == hz * 24
        assert physics['wallTangentFrames'] == round(hz * .6) and physics['wallMinimumTangentFraction'] > 0
        assert physics['characterMotorSha256'].lower() == sha(motor_path)
        ordinary = load(E / f'{prefix}-ordinary-{hz}.json')
        assert ordinary['characters'] == 10 and ordinary['player']['model']['capsuleMoves'] == hz * 8
        if config == 'optimize':
            for case, data in [('physics', physics), ('ordinary', ordinary)]:
                assert data == load(E / f'character-motor-debug-{TAG}-{case}-{hz}.json')
    for run, trace in zip(trajectory_summary['runs'], reference['traces'], strict=True):
        assert run['hz'] == trace['hz'] and run['exitCode'] == 1 and not run['comparisonPassed']
        report_path, log_path = Path(run['report']), Path(run['log'])
        assert sha(report_path).upper() == run['reportSha256'] and sha(log_path).upper() == run['logSha256']
        text = log_path.read_text(encoding='utf-8-sig')
        assert 'CHARACTER_TRAJECTORY_PROCESS_EXIT=1' in text and not re.search(r'^\s*(ERROR|WARNING):', text, re.M)
        data = load(report_path)
        assert data['actualJolt'] and data['authoredWorldControls'] and not data['replayedPhysicalObservations']
        assert data['frames'] == data['moves'] == data['retries'] == trace['hz'] * 8
        mismatches = ground_mismatches = stance_mismatches = 0
        maxima = dict(p=0, xy=0, z=0, v=0, a=0)
        for row, ref in zip(data['rows'], trace['frames'], strict=True):
            assert row['native'] == ref['physical'] and row['control'] == ref['control']
            actual, native = row['actual'], ref['physical']
            differences = [x-y for x, y in zip(actual['location'], native['location'], strict=True)]
            expected = dict(p=distance(actual['location'], native['location']), xy=math.hypot(*differences[:2]),
                            z=abs(differences[2]), v=distance(actual['velocity'], native['velocity']),
                            a=distance(actual['acceleration'], native['acceleration']))
            for key, value in expected.items():
                assert abs(row[key] - value) < 1e-9
                maxima[key] = max(maxima[key], value)
            ground = actual['Ground'] == native['ground']
            stance = actual['Crouching'] == native['crouching']
            match = expected['p'] <= .01 and expected['v'] <= .001 and expected['a'] <= .001 and ground and stance
            assert row['ground'] == ground and row['stance'] == stance and row['match'] == match
            ground_mismatches += not ground
            stance_mismatches += not stance
            mismatches += not match
        assert mismatches == data['mismatchFrames'] and ground_mismatches == data['groundMismatchFrames']
        assert stance_mismatches == data['stanceMismatchFrames'] == 0
        assert maxima['a'] == data['maxAccelerationCmps2'] == 0
        for key, name in [('p','maxPositionCm'),('xy','maxPlanarCm'),('z','maxVerticalCm'),('v','maxVelocityCmps')]:
            assert abs(maxima[key] - data[name]) < 1e-9
        assert not data['comparisonPassed'] and not data['completeAcceptance']
        if config == 'optimize':
            assert data == load(E / f'character-trajectory-debug-{TAG}-{trace["hz"]}.json')
    reports.extend([dict(path=p.name, sha256=sha(p)) for p in (motor_summary_path, trajectory_summary_path)])

negative = E / 'character-trajectory-v1-contacts-default-wall-negative.log'
negative_text = negative.read_text(encoding='utf-8-sig')
assert 'Near-normal wall approach suppressed its tangent:' in negative_text
assert re.search(r'expected=[0-9.eE+-]+ actual=0\s', negative_text)
assert 'CHARACTER_TRAJECTORY_NEGATIVE_EXIT=1' in negative_text
assert not (E / 'character-trajectory-v1-contacts-default-wall-negative.json').exists()

for config in ('debug', 'optimize'):
    build = (E / f'character-trajectory-v1-contacts-final-{config}-build.log').read_text(encoding='utf-8-sig')
    assert '0 个警告' in build and '0 个错误' in build and '已成功生成' in build

sources = [
    'src/Als.Godot/Locomotion/LyraSceneCharacter.cs',
    'src/Als.Godot/Locomotion/LyraSceneMovementService.cs',
    'src/Als.Godot/Locomotion/LyraCharacterTrajectorySmoke.cs',
    'src/Als.Godot/Locomotion/LyraCharacterMovementPhysicsSmoke.cs',
    'scenes/tests/lyra_character_trajectory_smoke.tscn',
    'scripts/compare-lyra-character-trajectory.ps1',
    'tools/export_lyra_movement_trajectory.py',
    'tools/verify_lyra_character_trajectory.py',
]
engine = engine_path('Engine/Source/Runtime/Engine/Private/Components')
result = dict(auditPassed=True, wallTangentRegressionPassed=True, diagnosticsCompleted=True,
              comparisonPassed=False, nativeWorldTrajectoryParity=False, goalComplete=False,
              physicalReferenceFrames=1680, diagnosticMoves=3360, diagnosticRetries=3360,
              regressionProcesses=18, diagnosticProcesses=6,
              protectedJson=len(closure['previousFixtureSha256']), protectedPackages=len(closure['assetSha256']),
              protectedConfig=len(closure['protectedProject']), protectedMovementSources=len(closure['movementSourceSha256']),
              referenceSha256=sha(reference_path), motorSha256=sha(motor_path), reports=reports,
              unchangedMotorSourceSha256=unchanged_motor_sources,
              sources={s:sha(ROOT/s) for s in sources}, negativeLogSha256=sha(negative),
              engineSourceSha256={s:sha(engine/s) for s in ('CharacterMovementComponent.cpp','PrimitiveComponent.cpp')})
with OUTPUT.open('x', encoding='utf-8', newline='\n') as stream:
    json.dump(result, stream, indent=2, allow_nan=False)
print(json.dumps(result, separators=(',', ':')))
