"""Audit source floor policy, real scene runs and retained native differences."""
import hashlib
import json
import math
import re
import struct
from pathlib import Path

from locomotion_paths import project_path

ROOT = Path(__file__).resolve().parents[1]
E = ROOT / 'artifacts/lyra-analysis'
ASSETS = ROOT / 'assets/generated/lyra_als'
PROJECT = project_path()
TAG = 'cmc-floor-v1-final'
OUTPUT = E / 'character-floor-v1-integrity.json'
assert not OUTPUT.exists(), 'Preserve floor audit'


def load(path):
    return json.loads(path.read_bytes())


def log_text(path):
    raw = path.read_bytes()
    return raw.decode('utf-16' if raw.startswith((b'\xff\xfe',b'\xfe\xff')) else 'utf-8-sig')


def sha(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def distance(a, b):
    return math.sqrt(sum((x-y)**2 for x, y in zip(a, b, strict=True)))


def f32(value):
    return struct.unpack('<f', struct.pack('<f', value))[0]


closure_path = E / 'whole-main-cmc60-floor-v2-closure.json'
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
package = ROOT / 'artifacts/unreal/lyra-whole-main-oracle/package-cmc-floor-v2'
for rel, digest in closure['probeSourceSha256'].items():
    assert sha(probe / rel) == sha(package / rel) == digest, rel
assert not (PROJECT / 'Plugins/LyraWholeMainOracle').exists(), 'Owned probe stage was not removed'

native_path = E / 'whole-main-cmc60-floor-v2-native.json'
native = load(native_path)
old = load(E / 'whole-main-cmc60-motor-v2-native.json')
floor_resource_path = ASSETS / 'character_floor_v1.json'
floor_reference_path = E / 'character-floor-v1-reference.json'
resource, reference = load(floor_resource_path), load(floor_reference_path)
motor_path = ASSETS / 'character_motor_v2.json'
motor = load(motor_path)
assert resource['evidenceSha256'] == reference['evidenceSha256'] == sha(native_path)
assert resource['closureSha256'] == reference['closureSha256'] == sha(closure_path)
assert resource['baseMotorSha256'] == sha(motor_path)
assert len(reference['rows']) == 144
for trace, prior in zip(native['traces'], old['traces'], strict=True):
    assert trace['actualCharacterMovement'] and trace['motorProfile'] == resource['profile']
    assert trace['floorKernel'] == reference['rows']
    assert trace['velocityKernel'] == prior['velocityKernel'] and trace['fallingKernel'] == prior['fallingKernel']
    assert [f['physicalInput'] for f in trace['frames']] == [f['physicalInput'] for f in prior['frames']]
assert all(resource['profile'][k] == value for k, value in motor['profile'].items())
assert not resource['profile']['bUseFlatBaseForFloorChecks'] and resource['profile']['bAlwaysCheckFloor']
assert resource['profile']['PerchRadiusThreshold'] == 6 and resource['profile']['PerchAdditionalHeight'] == 40
prior = load(E / 'character-motor-v2-integrity.json')
unchanged = {s:d for s,d in prior['sourceSha256'].items() if s.startswith('src/Als.Core/') or s.startswith('tests/') or
             s.endswith('LyraCharacterMovementSettings.cs') or s.endswith('LyraMotionWarpingPhysicsSmoke.cs')}
for source, digest in unchanged.items():
    assert sha(ROOT / source) == digest, source
assert sha(motor_path) == prior['motorResourceSha256']
assert sha(ROOT / 'tests/Als.Core.Tests/Fixtures/Physics/lyra_character_falling_native.json') == prior['fixtureSha256']

reports, trajectory_stats = [], []
trajectory_reference_path = E / 'character-motor-trajectory-v1-reference.json'
trajectory_reference = load(trajectory_reference_path)
for config, directory in [('debug','Debug'), ('optimize','ExportRelease')]:
    summaries = {kind:load(E / f'character-{kind}-{config}-{TAG}-verification.json') for kind in ('motor','floor','trajectory')}
    assemblies = summaries['motor']['assemblies']
    assert all(s['assemblies'] == assemblies for s in summaries.values())
    assert summaries['motor']['passed'] and len(summaries['motor']['runs']) == 9
    assert summaries['floor']['floorPhysicsPassed'] and summaries['floor']['diagnosticsCompleted']
    assert not summaries['floor']['floorQueryComparisonPassed']
    assert summaries['trajectory']['diagnosticsCompleted'] and not summaries['trajectory']['comparisonPassed']
    for name, digest in assemblies.items():
        assert sha(ROOT / '.godot/mono/temp/bin' / directory / name).upper() == digest
        if config == 'optimize':
            for kind in summaries:
                assert summaries[kind]['debugRestored']
                assert sha(E / f'character-{kind}-{config}-{TAG}-debug-backup' / name) == sha(ROOT / '.godot/mono/temp/bin/Debug' / name)
    for kind, summary in summaries.items():
        assert not summary['goalComplete'] and not summary['nativeWorldTrajectoryParity']
        summary_path = E / f'character-{kind}-{config}-{TAG}-verification.json'
        reports.append(dict(path=summary_path.name, sha256=sha(summary_path)))
        for run in summary['runs']:
            log = Path(run['log'])
            assert sha(log).upper() == run['logSha256']
            assert not re.search(r'^\s*(ERROR|WARNING):', log_text(log), re.M)
            if kind == 'motor' or run.get('kind') == 'physics':
                assert run['passed'] and run['exitCode'] == 0
            else:
                assert run['exitCode'] == 1 and not run['comparisonPassed']
            if 'report' in run:
                assert sha(Path(run['report'])).upper() == run['reportSha256']
    for hz in (30,60,120):
        physics = load(E / f'character-motor-{config}-{TAG}-physics-{hz}.json')
        assert physics['moves'] == physics['retries'] == hz*24
        assert physics['wallTangentFrames'] == round(hz*.6) and physics['wallMinimumTangentFraction'] > 0
        floor = load(E / f'character-floor-{config}-{TAG}-physics-{hz}.json')
        assert floor['frames'] == hz*3//5 and floor['moves'] == floor['retries'] == floor['frames']*8
        assert floor['actualJolt'] and floor['sceneService'] and floor['nativeBand'] and floor['blockedHeight'] and floor['perchRejected']
        assert floor['floorSettingsSha256'].lower() == sha(floor_resource_path)
        for row in floor['rows']:
            frame, role = row['frame'], row['role']
            if role < 6:
                gaps = [0,.018,.020,.021,.030,.25]
                gap = gaps[role] if .019 <= gaps[role] <= .024 else .0215
                crouch = hz//5 <= frame < hz*2//5
                expected = (.65 if crouch else .9)+gap+(.00001 if frame >= hz*2//5 else 0)
                assert row['Grounded'] and row['crouch'] == crouch and abs(row['position'][1]-expected) < .0001
            elif role == 6:
                assert row['Grounded'] and .9 <= row['position'][1] < .9061
            else:
                assert not row['Grounded'] and not row['floorWalkable']
        assert floor['rows'][-1]['position'][1] < .92
        ordinary = load(E / f'character-motor-{config}-{TAG}-ordinary-{hz}.json')
        assert ordinary['characters'] == 10 and ordinary['player']['model']['capsuleMoves'] == hz*8
        if config == 'optimize':
            for kind, data in [('motor-physics',physics),('motor-ordinary',ordinary),('floor-physics',floor)]:
                prefix, case = kind.split('-')
                assert data == load(E / f'character-{prefix}-debug-{TAG}-{case}-{hz}.json')
    queries = load(E / f'character-floor-{config}-{TAG}-queries.json')
    assert queries['queries'] == len(reference['rows']) and not queries['queryMutatesActor']
    maxima = dict(gap=0,line=0,point=0,normal=0,time=0)
    mismatches = 0
    for row, ref in zip(queries['rows'], reference['rows'], strict=True):
        assert row['native'] == ref
        actual = row['actual']
        values = dict(gap=abs(f32(actual['FloorDistance'])*100-ref['floorDistance']),
                      line=abs(f32(actual['LineDistance'])*100-ref['lineDistanceResult']),
                      point=distance(actual['point'],ref['point']), normal=distance(actual['normal'],ref['normal']),
                      time=abs(f32(actual['Time'])-ref['time']))
        for key, value in values.items():
            assert abs(value-row[key]) < 1e-9, key
            if key in ('gap','line') or ref['blocking']:
                maxima[key] = max(maxima[key], value)
        flags = all(actual[k] == ref[v] for k,v in [('Blocking','blocking'),('Walkable','walkable'),('LineTrace','lineTrace'),('Penetrating','penetrating')])
        assert flags == row['flags']
        match = flags and values['gap'] <= .01 and values['line'] <= .01 and (not ref['blocking'] or
                   values['point'] <= .01 and values['normal'] <= .0001 and values['time'] <= .0001)
        assert match == row['match']
        mismatches += not match
    assert mismatches == queries['mismatches'] == 3
    for key, name in [('gap','maxDistanceCm'),('line','maxLineDistanceCm'),('point','maxValidPointCm'),('normal','maxValidNormal'),('time','maxValidTime')]:
        assert abs(maxima[key]-queries[name]) < 1e-9
    if config == 'optimize':
        assert queries == load(E / f'character-floor-debug-{TAG}-queries.json')
    assert summaries['trajectory']['referenceSha256'].lower() == sha(trajectory_reference_path)
    for trace in trajectory_reference['traces']:
        hz = trace['hz']
        data = load(E / f'character-trajectory-{config}-{TAG}-{hz}.json')
        assert data['frames'] == data['moves'] == data['retries'] == hz*8 and not data['replayedPhysicalObservations']
        mismatches = ground_mismatches = stance_mismatches = 0
        maxima = dict(p=0,xy=0,z=0,v=0,a=0)
        for row, ref in zip(data['rows'], trace['frames'], strict=True):
            assert row['native'] == ref['physical'] and row['control'] == ref['control']
            actual, native_row = row['actual'], ref['physical']
            assert actual['floorPolicyApplied'] and actual['Grounded'] == actual['Ground']
            d = [x-y for x,y in zip(actual['location'],native_row['location'],strict=True)]
            expected = dict(p=distance(actual['location'],native_row['location']),xy=math.hypot(*d[:2]),z=abs(d[2]),
                            v=distance(actual['velocity'],native_row['velocity']),a=distance(actual['acceleration'],native_row['acceleration']))
            for key, value in expected.items():
                assert abs(value-row[key]) < 1e-9
                maxima[key] = max(maxima[key],value)
            ground = actual['Ground'] == native_row['ground']
            stance = actual['Crouching'] == native_row['crouching']
            match = expected['p'] <= .01 and expected['v'] <= .001 and expected['a'] <= .001 and ground and stance
            assert row['match'] == match and row['ground'] == ground and row['stance'] == stance
            mismatches += not match
            ground_mismatches += not ground
            stance_mismatches += not stance
        assert mismatches == data['mismatchFrames'] and ground_mismatches == data['groundMismatchFrames']
        assert stance_mismatches == data['stanceMismatchFrames'] == 0
        assert maxima['a'] == data['maxAccelerationCmps2'] == 0
        for key, name in [('p','maxPositionCm'),('xy','maxPlanarCm'),('z','maxVerticalCm'),('v','maxVelocityCmps')]:
            assert abs(maxima[key]-data[name]) < 1e-9
        # The established 60Hz grounded prefix remains inside the original gates.
        if hz == 60:
            assert all(r['match'] for r in data['rows'][:hz*4])
        assert not data['comparisonPassed'] and not data['completeAcceptance']
        if config == 'optimize':
            assert data == load(E / f'character-trajectory-debug-{TAG}-{hz}.json')
        trajectory_stats.append(dict(configuration=config,hz=hz,frames=data['frames'],mismatches=mismatches,
                                     initialGroundedMatches=sum(r['match'] for r in data['rows'][:hz*4]),
                                     groundMismatches=ground_mismatches,maxPositionCm=maxima['p'],maxVerticalCm=maxima['z'],maxPlanarCm=maxima['xy']))

assert load(E / 'character-motor-debug-cmc-floor-v1-first-invalidated.json')['invalidated']
for name in ('character-floor-v1-ground-info-debug-build.log','character-floor-v1-final-optimize-build.log'):
    text = log_text(E/name)
    assert '已成功生成' in text and '0 个错误' in text and '0 个警告' in text
sources = [
    'src/Als.Godot/Locomotion/LyraCharacterFloorProbe.cs',
    'src/Als.Godot/Locomotion/LyraCharacterFloorSettings.cs',
    'src/Als.Godot/Locomotion/LyraCharacterFloorQuerySmoke.cs',
    'src/Als.Godot/Locomotion/LyraCharacterFloorPhysicsSmoke.cs',
    'src/Als.Godot/Locomotion/LyraSceneMovementService.cs',
    'src/Als.Godot/Locomotion/LyraSceneCharacter.cs',
    'src/Als.Godot/Locomotion/LyraLocomotionDemo.cs',
    'src/Als.Godot/Locomotion/LyraRootMovementMotor.cs',
    'src/Als.Godot/Locomotion/LyraCharacterMovementPhysicsSmoke.cs',
    'src/Als.Godot/Locomotion/LyraCharacterTrajectorySmoke.cs',
    'src/Als.Godot/Animation/Lyra/LyraCharacterAnimation.cs',
    'src/Als.Godot/Animation/Lyra/LyraGodotRigCollision.cs',
    'scenes/tests/lyra_character_floor_query_smoke.tscn',
    'scenes/tests/lyra_character_floor_physics_smoke.tscn',
    'scripts/verify-lyra-character-floor.ps1',
    'tools/export_lyra_character_floor.py',
    'tools/verify_lyra_character_floor.py',
]
result = dict(auditPassed=True,floorPhysicsPassed=True,floorQueryComparisonPassed=False,
              nativeWorldTrajectoryParity=False,completeAcceptance=False,goalComplete=False,
              nativeFloorQueries=144,queryMismatchCases=3,nativeProviders=3,
              originalPhysicalPrefixUnchanged=True,sceneRegressionProcesses=18,floorPhysicsProcesses=6,
              floorQueryDiagnostics=2,trajectoryDiagnostics=6,trajectory=trajectory_stats,
              protectedJson=len(closure['previousFixtureSha256']),protectedPackages=len(closure['assetSha256']),
              protectedConfig=len(closure['protectedProject']),protectedMovementSources=len(closure['movementSourceSha256']),
              floorResourceSha256=sha(floor_resource_path),floorReferenceSha256=sha(floor_reference_path),
              motorResourceSha256=sha(motor_path),nativeCaptureSha256=sha(native_path),closureSha256=sha(closure_path),
              ownedProbeDllSha256=sha(package/'Binaries/Win64/UnrealEditor-LyraWholeMainOracle.dll'),
              sources={s:sha(ROOT/s) for s in sources},unchangedSources=unchanged,reports=reports)
with OUTPUT.open('x',encoding='utf-8',newline='\n') as stream:
    json.dump(result,stream,indent=2,allow_nan=False)
print(json.dumps(result,separators=(',',':')))
