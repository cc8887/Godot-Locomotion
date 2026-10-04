"""Original Main RotationData and ApplyAdditive roots on a frozen ALS source-pose boundary."""
import copy
import json
import math
import runpy
from pathlib import Path
import unreal

resources = runpy.run_path(str(Path(__file__).with_name('export_lyra_main_lean.py')))
root, save, sha = (resources[key] for key in ('root', 'save', 'sha'))
catalog = json.loads((root / 'logical_controls/catalog.json').read_bytes())
base_slots = ['jog_fwd_pivot', 'jog_fwd_cycle', 'jog_fwd_start']
base_rows = [next(row for row in catalog['entries'] if row['slot'] == slot) for slot in base_slots]
base_sequences = [unreal.AlsLyraControlRigLibrary.create_weapon_sequence(
    unreal.load_asset(row['source']), unreal.load_asset(row['target']), resources['extended'], resources['hand'], None)
    for row in base_rows]
if any(sequence is None for sequence in base_sequences):
    raise ValueError('Missing transient composition source')
requests = copy.deepcopy(json.loads((root / 'main_lean/runtime_requests.json').read_bytes()))
requests['baseSlots'] = base_slots
requests['schemaVersion'] = 2
for trace in requests['traces']:
    hz = trace['hz']
    for index, frame in enumerate(trace['frames']):
        frame.pop('angle')
        t = index / hz
        yaw = 173 + math.sin(t * 1.17) * 175 + t * 21.123456789
        if 2 <= t < 3: yaw = 179.9 if (index // 2) % 2 else -179.9
        frame['actorRotation'] = [7.25 * math.sin(t * .81), yaw, -2.5 * math.cos(t * .33)]
        frame['first'] = index < 2 or index == hz * 7
        # RotationData precedes UpdateCharacterStateData: these are its input fields.
        frame['crouchingAtRotation'] = 2.5 <= t < 5.2
        frame['adsAtRotation'] = 4.2 <= t < 7.8
        frame['baseTimes'] = [math.fmod(t * rate, row['playLength']) for rate, row in zip((.71, 1.123, 1.31), base_rows, strict=True)]
        if index in (hz * 4, hz * 8): frame['delta'] = 0
        if index == hz * 6: frame['delta'] = 1e-6
        frame['generateRootMotion'] = [1 <= t < 9, t < 7.7, t >= 3]
        frame['baseRootPrevious'] = frame['baseTimes'].copy()
        frame['baseRootDelta'] = [frame['delta'], -frame['delta'], frame['delta'] * 2.71]
        if index % 97 == 0: frame['baseRootDelta'] = [row['playLength'] * 2.71 for row in base_rows]
native = json.loads(unreal.AlsLyraGraphLibrary.read_main_lean_composition_trace(
    unreal.load_class(None, '/Game/Characters/Heroes/Mannequin/Animations/ABP_Mannequin_Base.ABP_Mannequin_Base_C'),
    resources['source_mesh'], resources['extended'], resources['space'], resources['extended_sequences'], base_sequences,
    json.dumps(requests, separators=(',', ':'))))
if native['angleType'] != 'double' or native['yawDeltaType'] != 'double' or native['yawSpeedType'] != 'double' or native['rotationType'] != 'FRotator':
    raise ValueError('Changed original RotationData property types')
# Snapshot actor rotation at the real engine boundary; these observations are
# inputs, distinct from RotationData output and from expected animation poses.
for trace, expected in zip(requests['traces'], native['traces'], strict=True):
    for frame, row in zip(trace['frames'], expected['frames'], strict=True):
        actor = row['actorRotation']; frame['actorSnapshot'] = [actor[field] for field in ('pitch', 'yaw', 'roll')]
        rotation = row['rotation']
        if any(rotation[field] != actor[field] for field in ('pitch', 'yaw', 'roll')):
            raise ValueError('Original PropertyAccess did not read the current owning actor rotation')
request_sha = save('composition_v2_requests.json', requests)
dependencies = {name: sha((root / name).read_bytes()) for name in (
    'main_lean/catalog.json', 'main_lean/behavior.json', 'main_lean/runtime_policies.json',
    'logical_controls/catalog.json', 'logical_controls/calibration.json', 'logical_controls/curve_bank.json',
    'source_nodes.json', 'runtime_graph.json')}
native.update({'schemaVersion': 2, 'requestSha256': request_sha, 'dependencies': dependencies,
               'assetSha256': resources['packages']})
save('composition_v2_native.json', native)
break_source_path = Path(unreal.Paths.engine_dir()) / 'Source/Runtime/Engine/Classes/Kismet/KismetMathLibrary.inl'
break_source = break_source_path.read_bytes()
if b'void UKismetMathLibrary::BreakRotator(FRotator InRot, float& Roll, float& Pitch, float& Yaw)' not in break_source:
    raise ValueError('Changed original Kismet BreakRotator pin types')
save('composition_v3_policy.json', {'schemaVersion': 3, 'dependencies': dependencies,
    'graph': native['compositionGraph'], 'baseSlots': base_slots,
    'rotationFunction': 'UpdateRotationData', 'initial': native['traces'][0]['frames'][0]['rotationBefore'],
    'standingCoefficient': .0375, 'crouchOrAdsCoefficient': .025,
    'yawDifference': 'BreakRotatorFloatPromotedDoubleSubtract', 'breakRotatorYawType': 'float',
    'breakRotatorSourceSha256': sha(break_source), 'delta': 'GetDeltaSecondsFloatPromotedDouble',
    'firstReset': ['YawDeltaSinceLastUpdate', 'AdditiveLeanAngle'],
    'rotationOrder': 'beforeUpdateCharacterStateData',
    'scope': 'Original Main RotationData and ApplyAdditive roots; explicit source pose boundary, not complete linked provider/Main execution'})
resources['check_packages']()
unreal.log('LYRA_MAIN_LEAN_COMPOSITION_NATIVE_OK traces=3 frames=2100 roots=3 assets_saved=0')
