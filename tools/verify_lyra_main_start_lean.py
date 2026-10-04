"""Actual Main Start Linked/Lean traversal and immutable ALS source resources."""
import argparse
import hashlib
import json
import math
import struct
from pathlib import Path

from locomotion_paths import project_path

sha = lambda data: hashlib.sha256(data).hexdigest()
single = lambda value: struct.unpack('f', struct.pack('f', value))[0]


def verify(root, content):
    load = lambda name: json.loads((root/name).read_bytes())
    native = load('main_start_lean_native.json'); requests = load('main_start_lean_requests.json')
    original = load('start_runtime_native_v3.json'); start_requests = load('start_runtime_requests.json')
    if native['requestSha256'] != sha((root/'main_start_lean_requests.json').read_bytes()) or native['assets'] != original['assets'] or native['assetSha256'] != original['assetSha256']:
        raise ValueError('Changed original Main Start inventory')
    for name, expected in native['dependencies'].items():
        if sha((root/name).read_bytes()) != expected: raise ValueError('Changed Main Start dependency: '+name)
    for path, expected in native['assetSha256'].items():
        if sha((content/(path.split('.')[0].removeprefix('/Game/')+'.uasset')).read_bytes()) != expected:
            raise ValueError('Changed protected package: '+path)
    start = next(n for n in load('main_lean/composition_v3_policy.json')['graph'] if n['layer'] == 'FullBody_StartState')
    if (start['index'],start['baseIndex'],start['additiveIndex']) != (13,11,12):
        raise ValueError('Wrong original Main Start topology')
    counts = dict.fromkeys(('frames','poses','hidden','setups','retained','clockDiverged','roots','movingRoots',
        'curves','attributes','hipTicks','sampleTicks','threeSamples','inertia','leanAngleChanges','offsetDirections'),0)
    identities, seen, pins = set(),set(),set()
    for trace, authored, prior in zip(native['traces'],requests['traces'],start_requests['traces'],strict=True):
        identity = trace['profile'],trace['hz']
        if identity in identities or identity != (authored['profile'],authored['hz']) or identity != (prior['profile'],prior['hz']):
            raise ValueError('Reordered Main Start trace')
        identities.add(identity); old_pin = None
        for index, (row, frame, prior_frame) in enumerate(zip(trace['frames'],authored['frames'],prior['frames'],strict=True)):
            if 'main' in frame or any(frame[k] != prior_frame[k] for k in ('active','weight','reinitialize','relativeRotation','layer')):
                raise ValueError('Main Start bypassed observation or changed graph boundary')
            observation = row['observation']; state = observation['after']; lean = row['lean']
            if state['IsFirstUpdate'] or observation['tailAfter']['mode'] != 0 or frame['observation']['snapshot'] != observation['input'] or frame['componentInput'] != observation['componentInput']:
                raise ValueError('Main Start did not consume the complete Main update/gather')
            if old_pin is not None and lean['pinBits'] != old_pin: counts['leanAngleChanges'] += 1
            old_pin = lean['pinBits']; pins.add(lean['pinBits']); counts['inertia'] += len(row['inertia'])
            if index == 0:
                if len(row['inertia']) != 1 or row['inertia'][0]['durationBits'] != 1041865114:
                    raise ValueError('Lost original initial Linked BlendIn')
            elif row['inertia']: raise ValueError('Repeated initial Linked BlendIn')
            if not frame['active']:
                if 'output' in row or row['hipFireActive']: raise ValueError('Hidden Main Start evaluated/ticked')
                counts['hidden'] += 1; counts['frames'] += 1; continue
            if row['orientationAngle'] != single(state['LocalVelocityDirectionAngleWithOffset']) or row['strideSpeed'] != single(state['DisplacementSpeed']) or row['strideNodeAlpha'] != single(row['StrideWarpingStartAlpha']):
                raise ValueError('Main Start used stale bound Warp pins')
            if lean['pin'] != single(state['AdditiveLeanAngle']) or lean['cachedWeightBits'] != row['cachedWeightBits'] or not lean['samples']:
                raise ValueError('Lean did not traverse with original current Main pins/weight')
            output = row['output']
            if len(output['pose']) != 81 or len(output['attributes']) != 4:
                raise ValueError('Incomplete original ALS logical output')
            root_motion = output['rootMotion']
            if (root_motion['name'],root_motion['bone'],root_motion['namespace'],root_motion['type']) != ('RootMotionDelta','root','bone','/Script/Engine.TransformAnimationAttribute'):
                raise ValueError('Changed generated root attribute identity')
            group = 'Crouch_Start_Cardinals' if state['IsCrouching'] else 'ADS_Start_Cardinals' if state['GameplayTag_IsADS'] else 'Jog_Start_Cardinals'
            desired = authored['bindings'][group][('forward','backward','left','right')[state['LocalVelocityDirection']]]
            if row['becameRelevant'] and row['asset'] != desired or not row['becameRelevant'] and row['asset'] != row['beforeAsset']:
                raise ValueError('Main Start violated original Setup-only asset selection')
            seen.add(row['asset']); counts['poses'] += 1; counts['frames'] += 1; counts['setups'] += row['becameRelevant']
            counts['retained'] += row['asset'] != desired; counts['clockDiverged'] += row['timeBits'] != row['explicitBits']
            counts['roots'] += 1; counts['movingRoots'] += any(v != 0 for v in root_motion['position'])
            counts['curves'] += len(output['curves']); counts['attributes'] += len(output['attributes']); counts['hipTicks'] += row['hipFireActive']
            counts['sampleTicks'] += len(lean['samples']); counts['threeSamples'] += len(lean['samples']) == 3
            counts['offsetDirections'] += state['LocalVelocityDirection'] != state['LocalVelocityDirectionNoOffset']
    if identities != {(p,hz) for p in ('unarmed','pistol','rifle') for hz in (30,60,120)} or counts['frames'] != 3780 or counts['poses'] != 3672 or counts['hidden'] != 108 or counts['inertia'] != 9 or not counts['threeSamples'] or not counts['leanAngleChanges']:
        raise ValueError('Incomplete original Main Start coverage: '+str(counts))
    return {'status':'pass','packages':len(native['assetSha256']),'assets':len(native['assets']),'selectedStartAssets':len(seen),
        'leanPins':len(pins),'counts':counts,'files':{n:{'sha256':sha((root/n).read_bytes()),'bytes':(root/n).stat().st_size} for n in ('main_start_lean_requests.json','main_start_lean_native.json')},
        'scope':'Actual original Main Start ApplyAdditive13/Linked11/Lean12, complete Main update and provider root/common Sync. Graph weights/relevance and Layer HipFire weight controlled; not full LocomotionSM/production.'}


if __name__ == '__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--root',type=Path,default=Path('assets/generated/lyra_als'))
    parser.add_argument('--content',type=Path,default=project_path('Content'))
    parser.add_argument('--output',type=Path,default=Path('artifacts/lyra-analysis/main-start-lean-resource-verification.json'))
    args=parser.parse_args();report=verify(args.root,args.content)
    args.output.write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('LYRA_MAIN_START_LEAN_VERIFY_OK '+json.dumps(report['counts']))
