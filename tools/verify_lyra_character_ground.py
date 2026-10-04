"""Audit original movement geometry, scene receipts and retained parity failures."""
import hashlib
import json
import math
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
E = ROOT / 'artifacts/lyra-analysis'
ASSETS = ROOT / 'assets/generated/lyra_als'
PROJECT = Path('../GASP58')
TAG = 'cmc-ground-v1-final3'
OUTPUT = E / 'character-ground-v1-integrity.json'
assert not OUTPUT.exists(), 'Preserve ground audit'


def load(path):
    return json.loads(path.read_bytes())


def sha(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def log_text(path):
    raw = path.read_bytes()
    return raw.decode('utf-16' if raw.startswith((b'\xff\xfe', b'\xfe\xff')) else 'utf-8-sig')


def distance(a, b):
    return math.sqrt(sum((x-y)**2 for x, y in zip(a, b, strict=True)))


def f32(value):
    # System.Text.Json writes Single's shortest round-trip representation.
    # Restore its original type before comparing it against native double.
    return struct.unpack('<f', struct.pack('<f', value))[0]


closure_path = E / 'whole-main-cmc60-ground-sweep-v1-closure.json'
closure = load(closure_path)
for rel, digest in closure['previousFixtureSha256'].items():
    assert sha(ASSETS / rel) == digest, rel
for group in ('protectedProject', 'movementSourceSha256'):
    for rel, digest in closure[group].items():
        assert sha(PROJECT / rel) == digest, rel
for name, digest in closure['assetSha256'].items():
    path = name.split('.')[0]
    original = PROJECT / ('Content/' + path.removeprefix('/Game/') + '.uasset' if path.startswith('/Game/') else
                          'Plugins/GameFeatures/ShooterCore/Content/' + path.removeprefix('/ShooterCore/') + '.uasset')
    assert sha(original) == digest, name
probe = ROOT / 'tools/unreal/LyraWholeMainOracle'
package = ROOT / 'artifacts/unreal/lyra-whole-main-oracle/package-cmc-ground-sweep-v1'
for rel, digest in closure['probeSourceSha256'].items():
    assert sha(probe / rel) == sha(package / rel) == digest, rel
assert not (PROJECT / 'Plugins/LyraWholeMainOracle').exists()

native_path = E / 'whole-main-cmc60-ground-sweep-v1-native.json'
reference_path = E / 'character-ground-v1-reference.json'
native, prior = load(native_path), load(E / 'whole-main-cmc60-floor-v2-native.json')
reference = load(reference_path)
assert reference['actualOriginalCMC'] and reference['actualMoveAlongFloor'] and not reference['fullPhysWalking']
assert reference['evidenceSha256'] == sha(native_path) and reference['closureSha256'] == sha(closure_path)
assert len(reference['rows']) == 32
for trace, old in zip(native['traces'], prior['traces'], strict=True):
    assert trace['actualCharacterMovement'] and trace['movementKernel'] == reference['rows']
    for key in ('motorProfile', 'floorKernel', 'velocityKernel', 'fallingKernel'):
        assert trace[key] == old[key], key
    assert [f['physicalInput'] for f in trace['frames']] == [f['physicalInput'] for f in old['frames']]
    assert len(trace['frames']) == 60
prior_audit = load(E / 'character-floor-v1-integrity.json')
for name, digest in prior_audit['unchangedSources'].items():
    assert sha(ROOT / name) == digest, name
assert sha(ASSETS / 'character_floor_v1.json') == prior_audit['floorResourceSha256']
assert sha(ASSETS / 'character_motor_v2.json') == prior_audit['motorResourceSha256']

trajectory_reference = load(E / 'character-motor-trajectory-v1-reference.json')
reports, query_stats, trajectory_stats = [], [], []
for config, directory in [('debug', 'Debug'), ('optimize', 'ExportRelease')]:
    summaries = {kind: load(E / f'character-{kind}-{config}-{TAG}-verification.json')
                 for kind in ('motor', 'floor', 'ground', 'trajectory')}
    assemblies = summaries['motor']['assemblies']
    assert all(s['assemblies'] == assemblies for s in summaries.values())
    assert summaries['motor']['passed'] and len(summaries['motor']['runs']) == 9
    assert summaries['floor']['floorPhysicsPassed'] and summaries['floor']['diagnosticsCompleted']
    assert summaries['ground']['stepPhysicsPassed'] and summaries['ground']['diagnosticsCompleted']
    assert summaries['trajectory']['diagnosticsCompleted'] and not summaries['trajectory']['comparisonPassed']
    for name, digest in assemblies.items():
        assert sha(ROOT / f'.godot/mono/temp/bin/{directory}' / name) == digest.lower()
    for kind, summary in summaries.items():
        if config == 'optimize':
            assert summary['debugRestored']
            backup = E / f'character-{kind}-{config}-{TAG}-debug-backup'
            for name, digest in load(E / f'character-{kind}-debug-{TAG}-verification.json')['assemblies'].items():
                assert sha(backup / name) == digest.lower()
        for run in summary['runs']:
            assert sha(Path(run['log'])) == run['logSha256'].lower()
            text = log_text(Path(run['log']))
            assert not any(line.lstrip().startswith(('ERROR:', 'WARNING:')) for line in text.splitlines())
            if 'report' in run:
                assert sha(Path(run['report'])) == run['reportSha256'].lower()
            if 'passed' in run:
                assert run['passed'] and run['exitCode'] == 0
            else:
                assert run['exitCode'] == (0 if run['comparisonPassed'] else 1)
        reports.append(dict(configuration=config,kind=kind,sha256=sha(E / f'character-{kind}-{config}-{TAG}-verification.json')))

    floor_query = load(E / f'character-floor-{config}-{TAG}-queries.json')
    assert floor_query['queries'] == 144 and floor_query['mismatches'] == 3 and not floor_query['comparisonPassed']
    data = load(E / f'character-ground-{config}-{TAG}-queries.json')
    count = 0
    for row, ref in zip(data['rows'], reference['rows'], strict=True):
        assert row['native'] == ref
        actual = row['actual']; grounded = ref['kind'] == 'ground'
        p, v = distance(actual['after'], ref['after']), distance(actual['velocity'], ref['velocity'])
        t = 0 if grounded else abs(f32(actual['Time']) - ref['time'])
        n = 0 if grounded or not actual['Blocking'] else distance(actual['normal'], ref['normal'])
        flags = actual['grounded'] == ref['walkable'] if grounded else (
            actual['Blocking'] == ref['blocking'] and actual['Penetrating'] == ref['penetrating'])
        for key, value in [('p',p),('v',v),('time',t),('normal',n)]:
            assert abs(row[key] - value) < 1e-12
        match = p <= .01 and v <= .001 and t <= .0001 and n <= .0001 and row['gap'] <= .01 and flags
        assert row['match'] == match and row['flags'] == flags
        count += not match
        if grounded and ref['name'] == 'step30':
            assert actual['stepped'] and not actual['reverted']
        if grounded and ref['name'] == 'step55':
            assert not actual['stepped'] and actual['reverted']
    assert count == data['mismatches'] == 8 and not data['comparisonPassed']
    for key, field in [('p','maxPositionCm'),('v','maxVelocityCmps'),('time','maxTime'),('normal','maxNormal'),('gap','maxFloorCm')]:
        assert max(r[key] for r in data['rows']) == data[field]
    query_stats.append({k:v for k,v in data.items() if k != 'rows'} | dict(configuration=config))
    if config == 'optimize':
        assert data == load(E / f'character-ground-debug-{TAG}-queries.json')
        assert floor_query == load(E / f'character-floor-debug-{TAG}-queries.json')

    for hz in (30, 60, 120):
        steps = load(E / f'character-ground-{config}-{TAG}-steps-{hz}.json')
        assert steps['frames'] == hz*2 and steps['actors'] == 5 and steps['moves'] == steps['retries'] == hz*10
        assert all(steps[k] for k in ('actualJolt','sceneService','standingAndCrouchingLowStep','highStepRejected','roofBlocksFullClimb','roofRollback','disabledStepPolicyRespected'))
        assert steps['steps'][0] > 0 and steps['steps'][1] > 0 and steps['steps'][2] == steps['steps'][4] == 0
        assert all(r['Grounded'] for r in steps['rows'])
        assert len(steps['rows']) == steps['moves'] and not steps['terrainAcceptance']
        if config == 'optimize':
            for kind in ('ground','floor'):
                suffix = f'steps-{hz}' if kind == 'ground' else f'physics-{hz}'
                assert load(E / f'character-{kind}-{config}-{TAG}-{suffix}.json') == load(E / f'character-{kind}-debug-{TAG}-{suffix}.json')
            for name in (f'physics-{hz}',f'ordinary-{hz}'):
                assert load(E / f'character-motor-{config}-{TAG}-{name}.json') == load(E / f'character-motor-debug-{TAG}-{name}.json')
        trace = next(t for t in trajectory_reference['traces'] if t['hz'] == hz)
        trajectory = load(E / f'character-trajectory-{config}-{TAG}-{hz}.json')
        assert trajectory['frames'] == trajectory['moves'] == trajectory['retries'] == hz*8
        assert not trajectory['replayedPhysicalObservations']
        mismatches = ground_mismatches = stance_mismatches = 0
        maxima = dict(p=0,xy=0,z=0,v=0,a=0)
        for row, ref in zip(trajectory['rows'],trace['frames'],strict=True):
            assert row['native'] == ref['physical'] and row['control'] == ref['control']
            actual, expected = row['actual'], ref['physical']
            assert actual['floorPolicyApplied'] and actual['Grounded'] == actual['Ground']
            d = [x-y for x,y in zip(actual['location'],expected['location'],strict=True)]
            values = dict(p=distance(actual['location'],expected['location']),xy=math.hypot(*d[:2]),z=abs(d[2]),
                          v=distance(actual['velocity'],expected['velocity']),a=distance(actual['acceleration'],expected['acceleration']))
            for key, value in values.items():
                assert abs(row[key]-value) < 1e-9
                maxima[key] = max(maxima[key],value)
            ground, stance = actual['Ground'] == expected['ground'], actual['Crouching'] == expected['crouching']
            match = values['p'] <= .01 and values['v'] <= .001 and values['a'] <= .001 and ground and stance
            assert row['match'] == match and row['ground'] == ground and row['stance'] == stance
            mismatches += not match; ground_mismatches += not ground; stance_mismatches += not stance
        assert mismatches == trajectory['mismatchFrames'] and ground_mismatches == trajectory['groundMismatchFrames']
        assert stance_mismatches == trajectory['stanceMismatchFrames'] == 0 and maxima['a'] == trajectory['maxAccelerationCmps2'] == 0
        for key, name in [('p','maxPositionCm'),('xy','maxPlanarCm'),('z','maxVerticalCm'),('v','maxVelocityCmps')]:
            assert abs(maxima[key]-trajectory[name]) < 1e-9
        assert not trajectory['comparisonPassed'] and not trajectory['completeAcceptance']
        if config == 'optimize':
            assert trajectory == load(E / f'character-trajectory-debug-{TAG}-{hz}.json')
        trajectory_stats.append(dict(configuration=config,hz=hz,frames=trajectory['frames'],mismatches=mismatches,
                                     groundMismatches=ground_mismatches,maxima=maxima))
    if config == 'optimize':
        for name in ('warp-60','emote-60'):
            assert load(E / f'character-motor-{config}-{TAG}-{name}.json') == load(E / f'character-motor-debug-{TAG}-{name}.json')

for name in ('character-ground-sweep-v1-final3-debug-build.log','character-ground-sweep-v1-final3-optimize-build.log'):
    text = log_text(E/name)
    assert '已成功生成' in text and '0 个错误' in text and '0 个警告' in text
sources = [f'src/Als.Godot/Locomotion/{name}.cs' for name in (
    'LyraCharacterSweep','LyraCharacterGroundMovement','LyraCharacterGroundQuerySmoke','LyraCharacterStepPhysicsSmoke',
    'LyraCharacterFloorProbe','LyraSceneMovementService','LyraSceneCharacter','LyraRootMovementMotor',
    'LyraCharacterMovementPhysicsSmoke','LyraCharacterTrajectorySmoke')]
sources += ['src/Als.Godot/Animation/Lyra/LyraCharacterAnimation.cs',
            'scenes/tests/lyra_character_ground_query_smoke.tscn','scenes/tests/lyra_character_step_physics_smoke.tscn',
            'scripts/verify-lyra-character-ground.ps1','tools/export_lyra_character_ground.py','tools/verify_lyra_character_ground.py']
result = dict(auditPassed=True,sceneRegressionProcesses=18,floorPhysicsProcesses=6,stepPhysicsProcesses=6,
              groundQueryComparisonPassed=False,floorQueryComparisonPassed=False,nativeWorldTrajectoryParity=False,
              completeAcceptance=False,goalComplete=False,nativeQueries=32,nativeProviders=3,originalPhysicalPrefixUnchanged=True,
              protectedJson=len(closure['previousFixtureSha256']),protectedPackages=len(closure['assetSha256']),
              protectedConfig=len(closure['protectedProject']),protectedMovementSources=len(closure['movementSourceSha256']),
              queries=query_stats,trajectory=trajectory_stats,reports=reports,sources={s:sha(ROOT/s) for s in sources},
              nativeCaptureSha256=sha(native_path),closureSha256=sha(closure_path),referenceSha256=sha(reference_path),
              ownedProbeDllSha256=sha(package/'Binaries/Win64/UnrealEditor-LyraWholeMainOracle.dll'))
with OUTPUT.open('x',encoding='utf-8',newline='\n') as stream:
    json.dump(result,stream,indent=2,allow_nan=False)
print('CHARACTER_GROUND_AUDIT_OK ' + json.dumps({k:v for k,v in result.items() if k not in ('queries','trajectory','reports','sources')}))
