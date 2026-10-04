"""Check real Main stage history, native input boundaries and observed Cycle provenance."""
import argparse
import hashlib
import json
import math
import struct
from pathlib import Path

sha = lambda data: hashlib.sha256(data).hexdigest()
single = lambda v: struct.unpack('<f', struct.pack('<f', v))[0]
double_bits = lambda v: struct.pack('>d', v).hex()
vector = lambda row: [row[key] for key in ('x', 'y', 'z')]
ORDER = ['UpdateLocationData', 'UpdateRotationData', 'UpdateVelocityData', 'UpdateAccelerationData',
         'UpdateWallDetectionHeuristic', 'UpdateCharacterStateData']
EXTERNAL = {'IsFirstUpdate': 'first', 'RootYawOffset': 'rootYaw', 'GameplayTag_IsADS': 'ads', 'GameplayTag_IsFiring': 'firing'}

def verify(root, content):
    load = lambda name: json.loads((root / name).read_bytes())
    policy = load('main_observation_policy.json')
    native = load('main_observation_native.json')
    requests = load('main_observation_requests.json')
    joint = load('main_observation_cycle_native.json')
    joint_requests = load('main_observation_cycle_requests.json')
    if policy['schemaVersion'] != 1 or policy['order'] != ORDER or native['order'] != ORDER or policy['deadZone'] != 10:
        raise ValueError('Changed original Main observation policy')
    for contract, request_name in ((native, 'main_observation_requests.json'), (joint, 'main_observation_cycle_requests.json')):
        if contract['schemaVersion'] != 1 or contract['requestSha256'] != sha((root / request_name).read_bytes()):
            raise ValueError('Changed observation request dependency')
        for name, value in contract['dependencies'].items():
            if sha((root / name).read_bytes()) != value:
                raise ValueError('Changed observation resource: ' + name)
    packages = native['assetSha256']
    if len(packages) != 492 or joint['assetSha256'] != packages:
        raise ValueError('Incomplete protected package set')
    for path, value in packages.items():
        if sha((content / (path.split('.')[0].removeprefix('/Game/') + '.uasset')).read_bytes()) != value:
            raise ValueError('Changed protected package: ' + path)
    counts = dict.fromkeys(('frames', 'stages', 'zeroDelta', 'tinyDelta', 'first', 'wall', 'jumping', 'falling', 'crouchChanged', 'adsChanged'), 0)
    joint_counts = dict.fromkeys(('frames', 'stages', 'poseFrames', 'hidden', 'rootPresent', 'rootAbsent', 'rootMoving', 'attributes', 'curves', 'hipFireTicks', 'inertia'), 0)
    directions = set()
    def bit(row, field, value):
        if row[field + 'Bits'] != double_bits(float(value)):
            raise ValueError('Incorrect independent Main arithmetic: ' + field)
    def stage_history(frame, row, previous):
        delta = single(frame['delta']); observed = row['input']
        if frame['snapshot'] != observed:
            raise ValueError('Snapshot is not the real actor/Movement input')
        before = row['before']
        for name, value in previous.items():
            if name.endswith('Bits') or name in EXTERNAL:
                continue
            if value != before[name]:
                raise ValueError('Main committed history was lost: ' + name)
        for name, key in EXTERNAL.items():
            if before[name] != frame[key]:
                raise ValueError('Wrong external observation input')
        stages = row['stages']
        if len(stages) != 6:
            raise ValueError('Incomplete Main update order')
        location = stages[0]; old = vector(before['WorldLocation']); new = vector(observed['location'])
        distance = math.sqrt((new[0]-old[0])**2+(new[1]-old[1])**2)
        bit(location, 'DisplacementSinceLastUpdate', 0 if frame['first'] else distance)
        bit(location, 'DisplacementSpeed', 0 if frame['first'] or delta == 0 else distance/delta)
        if location['WorldLocation'] != observed['location']:
            raise ValueError('Location is not the actual actor snapshot')
        rotation = stages[1]; yaw_delta = single(observed['rotation']['yaw']) - single(before['WorldRotation']['yaw'])
        speed = yaw_delta/delta if delta != 0 else 0
        angle = speed*(.025 if before['IsCrouching'] or frame['ads'] else .0375)
        bit(rotation, 'YawDeltaSinceLastUpdate', 0 if frame['first'] else yaw_delta)
        bit(rotation, 'YawDeltaSpeed', speed); bit(rotation, 'AdditiveLeanAngle', 0 if frame['first'] else angle)
        if rotation['WorldRotation'] != observed['rotation']:
            raise ValueError('Rotation is not actual actor snapshot')
        state = stages[5]
        expected_flags = {'IsOnGround': observed['ground'], 'IsCrouching': observed['crouching'],
            'CrouchStateChange': observed['crouching'] != before['IsCrouching'],
            'ADSStateChanged': frame['ads'] != before['WasADSLastUpdate'], 'WasADSLastUpdate': frame['ads'],
            'IsJumping': observed['movementMode'] == 3 and observed['velocity']['z'] > 0,
            'IsFalling': observed['movementMode'] == 3 and observed['velocity']['z'] <= 0,
            'IsFirstUpdate': frame['first']}
        if any(state[key] != value for key, value in expected_flags.items()):
            raise ValueError('Incorrect ordered Main state flags')
        bit(state, 'TimeSinceFiredWeapon', 0 if frame['firing'] else before['TimeSinceFiredWeapon']+delta)
        return state
    if len(native['traces']) != 3:
        raise ValueError('Missing Main traces')
    for trace, authored in zip(native['traces'], requests['traces'], strict=True):
        if trace['initial'] != policy['initial'] or trace['hz'] != authored['hz'] or len(trace['frames']) != trace['hz']*12:
            raise ValueError('Changed observation trace identity')
        previous = trace['initial']
        for frame, row in zip(authored['frames'], trace['frames'], strict=True):
            state = stage_history(frame, row, previous); previous = state
            counts['frames'] += 1; counts['stages'] += 6
            counts['zeroDelta'] += frame['delta'] == 0; counts['tinyDelta'] += frame['delta'] == 1e-6
            counts['first'] += frame['first']; counts['wall'] += state['IsRunningIntoWall']; counts['jumping'] += state['IsJumping']
            counts['falling'] += state['IsFalling']; counts['crouchChanged'] += state['CrouchStateChange']; counts['adsChanged'] += state['ADSStateChanged']
            directions.add(state['LocalVelocityDirectionNoOffset'])
    expected = (2520, 15120, 6, 3, 9, 312, 105, 210, 9, 6)
    if tuple(counts.values()) != expected or directions != {0, 1, 2, 3}:
        raise ValueError('Missing observation coverage: ' + repr(counts))
    identities = set()
    if joint_requests.get('observeMain') is not True:
        raise ValueError('Cycle still uses manually assigned Main fields')
    for trace, authored in zip(joint['traces'], joint_requests['traces'], strict=True):
        identity = (trace['profile'], trace['hz']); identities.add(identity)
        previous = policy['initial']
        for frame, row in zip(authored['frames'], trace['frames'], strict=True):
            if 'main' in frame:
                raise ValueError('Joint trace contains manual Main pose inputs')
            state = stage_history(frame['observation'], row['observation'], previous); previous = state
            if frame['componentInput'] != row['observation']['componentInput']:
                raise ValueError('Changed component gather snapshot')
            joint_counts['frames'] += 1; joint_counts['stages'] += 6
            joint_counts['hipFireTicks'] += row['hipFireActive']; joint_counts['inertia'] += len(row['inertia'])
            if bool(frame['active']) != ('output' in row):
                raise ValueError('Missing/hidden Cycle root pose')
            if not frame['active']:
                joint_counts['hidden'] += 1; continue
            if row['orientationAngle'] != single(state['LocalVelocityDirectionAngle']) or row['strideSpeed'] != single(state['DisplacementSpeed']):
                raise ValueError('Original Cycle handler did not read freshly updated Main fields')
            output = row['output']; root_motion = output.get('rootMotion')
            if len(output['pose']) != 81 or len(output['attributes']) != 4:
                raise ValueError('Incomplete Cycle pose data')
            joint_counts['poseFrames'] += 1; joint_counts['rootPresent'] += root_motion is not None; joint_counts['rootAbsent'] += root_motion is None
            joint_counts['rootMoving'] += root_motion is not None and root_motion['position'] != [0, 0, 0]
            joint_counts['attributes'] += len(output['attributes']); joint_counts['curves'] += len(output['curves'])
    if identities != {(p,hz) for p in ('unarmed','pistol','rifle') for hz in (30,60,120)} or any(
            joint_counts[key] != value for key,value in {'frames':3780,'stages':22680,'poseFrames':3528,'hidden':252,
                'rootPresent':3406,'rootAbsent':122,'attributes':14112,'curves':0,'hipFireTicks':2205,'inertia':99}.items()):
        raise ValueError('Missing observed Cycle coverage: '+repr(joint_counts))
    names = ['main_observation_requests.json','main_observation_native.json','main_observation_policy.json',
             'main_observation_cycle_requests.json','main_observation_cycle_native.json']
    return {'status':'pass','observations':counts,'jointCycle':joint_counts,'packages':492,'logical':81,'skin':68,
        'production':False,'wholeMain':False,'remainingMainUpdates':['BlendWeight','RootYaw','Aiming','JumpFall','clearFirst'],
        'files':{name:{'sha256':sha((root/name).read_bytes()),'bytes':(root/name).stat().st_size} for name in names}}

if __name__ == '__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--root',type=Path,default=Path('assets/generated/lyra_als'))
    parser.add_argument('--content',type=Path,default=Path('../GASP58/Content'))
    parser.add_argument('--out',type=Path,default=Path('artifacts/lyra-analysis/main-observation-resource-verification.json'))
    args=parser.parse_args(); result=verify(args.root,args.content)
    args.out.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print('LYRA_MAIN_OBSERVATION_VERIFIED frames=2520 stages=15120 jointFrames=3780 poseFrames=3528 packages=492 production=false wholeMain=false')
