"""Full original Main macro history, actual input boundaries, joint Cycle and protected bytes."""
import argparse
import hashlib
import json
import math
import struct
from pathlib import Path

sha = lambda data: hashlib.sha256(data).hexdigest()
single = lambda value: struct.unpack('<f', struct.pack('<f', value))[0]
bits = lambda value: struct.pack('>d', float(value)).hex()
ORDER = ['UpdateLocationData', 'UpdateRotationData', 'UpdateVelocityData', 'UpdateAccelerationData',
    'UpdateWallDetectionHeuristic', 'UpdateCharacterStateData', 'UpdateBlendWeightData',
    'UpdateRootYawOffset', 'UpdateAimingData', 'UpdateJumpFallData', 'ClearFirstUpdate']

def verify(root, content):
    load = lambda name: json.loads((root/name).read_bytes())
    policy = load('main_update_policy.json')
    native = load('main_update_native.json'); requests = load('main_update_requests.json')
    joint = load('main_update_cycle_native.json'); joint_requests = load('main_update_cycle_requests.json')
    if policy['schemaVersion'] != 1 or policy['order'] != ORDER or native['order'] != ORDER:
        raise ValueError('Changed original full Main update order')
    for contract, request_name in ((native, 'main_update_requests.json'), (joint, 'main_update_cycle_requests.json')):
        if contract['requestSha256'] != sha((root/request_name).read_bytes()):
            raise ValueError('Changed full Main requests')
        for name, expected in contract['dependencies'].items():
            if sha((root/name).read_bytes()) != expected:
                raise ValueError('Changed full Main dependency: '+name)
    if native['assetSha256'] != joint['assetSha256'] or len(native['assetSha256']) != 492:
        raise ValueError('Incomplete source package provenance')
    for path, expected in native['assetSha256'].items():
        if sha((content/(path.split('.')[0].removeprefix('/Game/')+'.uasset')).read_bytes()) != expected:
            raise ValueError('Changed protected source asset: '+path)
    counts = dict.fromkeys(('frames', 'first', 'hold', 'disabledHold', 'dashAccumulate', 'groundMontage',
        'airMontage', 'upperbodyDecay', 'jumping', 'falling', 'zeroDelta', 'tinyDelta', 'rootChanges'), 0)
    joint_counts = dict.fromkeys(('frames','poseFrames','hidden','rootPresent','rootAbsent','rootMoving','curves','attributes','inertia'), 0)
    def frame_history(frame, row, previous, previous_tail):
        if 'first' in frame or 'rootYaw' in frame or not frame['completeMain'] or frame['snapshot'] != row['input']:
            raise ValueError('Full Main is still externally assigned output/history')
        before = row['before']; tail_before = row['tailBefore']; after = row['after']; tail_after = row['tailAfter']
        for name, value in previous.items():
            expected = frame['ads'] if name == 'GameplayTag_IsADS' else frame['firing'] if name == 'GameplayTag_IsFiring' else value
            if before[name] != expected:
                raise ValueError('Lost complete Main history: '+name)
        external = {'mode':'mode','dashing':'dashing','enabled':'enabled'}
        for name, value in previous_tail.items():
            if tail_before[name] != (frame[external[name]] if name in external else value):
                raise ValueError('Lost full Main tail history: '+name)
        if after['IsFirstUpdate'] or tail_after['mode'] != 0 or row['input']['montage'] != frame['montage']:
            raise ValueError('Macro clear/reset or actual montage activity is incomplete')
        delta = single(frame['delta']); ground = after['IsOnGround']; montage = row['input']['montage']
        current = tail_before['UpperbodyDynamicAdditiveWeight']; distance = 0-current
        upper = 1 if montage and ground else 0 if distance*distance < single(1e-8) else current+distance*min(max(delta*6,0),1)
        apex = (0-after['WorldVelocity']['z'])/row['input']['gravity'] if after['IsJumping'] else 0
        if tail_after['UpperbodyDynamicAdditiveWeightBits'] != bits(upper) or tail_after['TimeToJumpApexBits'] != bits(apex):
            raise ValueError('Original double weight/apex boundary differs')
        pitch = math.fmod(single(row['input']['aimPitch']),360)
        if pitch < 0: pitch += 360
        if pitch > 180: pitch -= 360
        if tail_after['AimPitchBits'] != bits(single(pitch)):
            raise ValueError('Original float NormalizeAxis boundary differs')
        hold = frame['mode'] == 1 and not frame['dashing']
        if hold and (after['RootYawOffsetBits'] != before['RootYawOffsetBits'] or tail_after['AimYawBits'] != tail_before['AimYawBits']):
            raise ValueError('Hold modified root/aim history')
        if hold or delta <= single(1e-8) or frame['mode'] == 2 and not frame['dashing']:
            for field in ('springVelocityBits','springPreviousBits','springValid'):
                if tail_after[field] != tail_before[field]:
                    raise ValueError('Inactive/zero-time Kismet spring modified history')
        return dict(frames=1, first=int(before['IsFirstUpdate']), hold=int(hold),
            disabledHold=int(hold and not frame['enabled'] and before['RootYawOffset'] != 0),
            dashAccumulate=int(frame['dashing'] and frame['mode']==2), groundMontage=int(montage and ground),
            airMontage=int(montage and not ground), upperbodyDecay=int(tail_after['UpperbodyDynamicAdditiveWeight'] < current),
            jumping=int(after['IsJumping']), falling=int(after['IsFalling']), zeroDelta=int(delta==0), tinyDelta=int(0<delta<=1e-6),
            rootChanges=int(after['RootYawOffset']!=before['RootYawOffset']))
    if not requests['completeMain'] or len(native['traces']) != 3:
        raise ValueError('Missing complete Main macro traces')
    for trace, authored in zip(native['traces'], requests['traces'], strict=True):
        if trace['initial'] != policy['initial'] or trace['tailInitial'] != policy['tailInitial'] or trace['hz'] != authored['hz']:
            raise ValueError('Changed Main initial state')
        previous = trace['initial']; tail = trace['tailInitial']
        for row, frame in zip(trace['frames'],authored['frames'],strict=True):
            values = frame_history(frame,row,previous,tail)
            for name,value in values.items(): counts[name] += value
            previous = row['after']; tail = row['tailAfter']
    if counts['frames'] != 2520 or counts['first'] != 3 or any(counts[k] == 0 for k in counts):
        raise ValueError('Missing native whole Main coverage: '+repr(counts))
    identities = set()
    if not joint_requests['completeMain'] or not joint_requests['observeMain']:
        raise ValueError('Joint Cycle still uses controlled Main outputs')
    for trace, authored in zip(joint['traces'],joint_requests['traces'],strict=True):
        identities.add((trace['profile'],trace['hz'])); previous=policy['initial']; tail=policy['tailInitial']
        for row,frame in zip(trace['frames'],authored['frames'],strict=True):
            if 'main' in frame: raise ValueError('Manually assigned joint Main output')
            obs=row['observation']; frame_history(frame['observation'],obs,previous,tail)
            previous=obs['after']; tail=obs['tailAfter']; joint_counts['frames']+=1
            joint_counts['inertia']+=len(row['inertia'])
            if frame['componentInput'] != obs['componentInput']:
                raise ValueError('Joint actual component boundary changed')
            if not frame['active']:
                if 'output' in row: raise ValueError('Hidden Cycle evaluated a pose')
                joint_counts['hidden']+=1; continue
            if row['orientationAngle'] != single(previous['LocalVelocityDirectionAngle']) or row['strideSpeed'] != single(previous['DisplacementSpeed']):
                raise ValueError('Original Cycle handler did not consume fresh Main')
            output=row['output']; motion=output.get('rootMotion')
            if len(output['pose']) != 81 or len(output['attributes']) != 4:
                raise ValueError('Incomplete full Main/Cycle output')
            joint_counts['poseFrames']+=1; joint_counts['rootPresent']+=motion is not None; joint_counts['rootAbsent']+=motion is None
            joint_counts['rootMoving']+=motion is not None and motion['position'] != [0,0,0]
            joint_counts['curves']+=len(output['curves']); joint_counts['attributes']+=len(output['attributes'])
    if identities != {(p,hz) for p in ('unarmed','pistol','rifle') for hz in (30,60,120)} or joint_counts != dict(
        frames=3780,poseFrames=3528,hidden=252,rootPresent=3406,rootAbsent=122,rootMoving=3238,curves=0,attributes=14112,inertia=99):
        raise ValueError('Missing full Main/Cycle coverage: '+repr(joint_counts))
    files=['main_update_requests.json','main_update_native.json','main_update_policy.json','main_update_cycle_requests.json','main_update_cycle_native.json']
    return dict(status='pass',main=counts,jointCycle=joint_counts,packages=492,logical=81,skin=68,production=False,mainGraph=False,
        files={f:dict(sha256=sha((root/f).read_bytes()),bytes=(root/f).stat().st_size) for f in files})

if __name__ == '__main__':
    parser=argparse.ArgumentParser(); parser.add_argument('--root',type=Path,default=Path('assets/generated/lyra_als'))
    parser.add_argument('--content',type=Path,default=Path('../GASP58/Content'))
    parser.add_argument('--out',type=Path,default=Path('artifacts/lyra-analysis/main-update-resource-verification.json'))
    args=parser.parse_args(); result=verify(args.root,args.content)
    args.out.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print('LYRA_MAIN_UPDATE_VERIFIED '+json.dumps(result['main'])+' jointFrames=3780 poseFrames=3528 packages=492 production=false mainGraph=false')
