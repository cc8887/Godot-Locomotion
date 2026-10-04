"""Verify original Main rotation/composition provenance and continuous native coverage."""
import argparse
import hashlib
import json
import struct
from pathlib import Path
from locomotion_paths import engine_path, project_path

from verify_lyra_main_lean_runtime import verify as verify_runtime


def sha(data):
    return hashlib.sha256(data).hexdigest()


def single(value):
    return struct.unpack('f', struct.pack('f', value))[0]


def double_bits(value):
    return struct.pack('>d', value).hex()


def verify(root, content, engine):
    resources = verify_runtime(root, content)
    directory = root / 'main_lean'
    requests_bytes = (directory / 'composition_v2_requests.json').read_bytes()
    requests = json.loads(requests_bytes)
    native = json.loads((directory / 'composition_v2_native.json').read_bytes())
    policy = json.loads((directory / 'composition_v3_policy.json').read_bytes())
    source = engine / 'Engine/Source/Runtime/Engine/Classes/Kismet/KismetMathLibrary.inl'
    if policy['schemaVersion'] != 3 or policy['breakRotatorSourceSha256'] != sha(source.read_bytes()):
        raise ValueError('Changed Kismet BreakRotator source/type policy')
    if (native['schemaVersion'] != 2 or native['requestSha256'] != sha(requests_bytes) or
            policy['graph'] != native['compositionGraph'] or native['angleType'] != 'double' or
            native['yawDeltaType'] != 'double' or native['yawSpeedType'] != 'double' or native['rotationType'] != 'FRotator'):
        raise ValueError('Changed Main rotation/composition contract')
    catalog = json.loads((directory / 'catalog.json').read_bytes())
    if native['assetSha256'] != catalog['assetSha256']:
        raise ValueError('Changed Main composition package closure')
    if native['dependencies'] != policy['dependencies']:
        raise ValueError('Changed Main composition resource dependencies')
    for name, digest in native['dependencies'].items():
        if sha((root / name).read_bytes()) != digest:
            raise ValueError('Stale Main composition dependency: ' + name)
    expected_layers = {22: (23, 21, 'FullBody_PivotState'), 16: (17, 15, 'FullBody_CycleState'), 12: (13, 11, 'FullBody_StartState')}
    for node in policy['graph']:
        if (node['index'], node['baseIndex'], node['layer']) != expected_layers[node['additiveIndex']]:
            raise ValueError('Changed actual Main phase/source linkage')
        settings = node['settings']; clamp = settings['alphaScaleBiasClamp']
        if (settings['alpha'] != 1 or settings['alphaInputType'] != 'Float' or settings['lODThreshold'] != -1 or
                settings['alphaScaleBias'] != {'scale': 1, 'bias': 0} or
                any(clamp[key] for key in ('bMapRange', 'bClampResult', 'bInterpResult'))):
            raise ValueError('Changed Main ApplyAdditive configuration')
    counts = dict.fromkeys(('frames', 'ticks', 'curves', 'attributes', 'rootPresent', 'rootIdentity', 'zeroDelta', 'tinyDelta', 'first', 'unwoundBoundary'), 0)
    if requests['baseSlots'] != ['jog_fwd_pivot', 'jog_fwd_cycle', 'jog_fwd_start'] or len(native['traces']) != 3:
        raise ValueError('Missing actual phase/base ordering')
    for trace, authored in zip(native['traces'], requests['traces'], strict=True):
        if trace['hz'] != authored['hz'] or len(trace['frames']) != trace['hz'] * 10:
            raise ValueError('Incomplete Main composition physical trace')
        for row, frame in zip(trace['frames'], authored['frames'], strict=True):
            counts['frames'] += 1
            delta = single(frame['delta']); actor = row['actorRotation']; previous = row['rotationBefore']; actual = row['rotation']
            if frame['actorSnapshot'] != [actor[field] for field in ('pitch', 'yaw', 'roll')]:
                raise ValueError('Changed actual owning actor observation boundary')
            difference = float(single(actor['yaw'])) - float(single(previous['yaw']))
            speed = difference / delta if delta else 0
            lean = speed * (.025 if frame['crouchingAtRotation'] or frame['adsAtRotation'] else .0375)
            expected = {'pitch': actor['pitch'], 'yaw': actor['yaw'], 'roll': actor['roll'],
                        'yawDelta': 0 if frame['first'] else difference, 'yawSpeed': speed, 'angle': 0 if frame['first'] else lean}
            for name, value in expected.items():
                if double_bits(value) != actual[name + 'Bits'] or double_bits(actual[name]) != actual[name + 'Bits']:
                    raise ValueError('Original RotationData float/double operation order differs: ' + name)
            counts['zeroDelta'] += delta == 0; counts['tinyDelta'] += 0 < delta < 1e-5
            counts['first'] += frame['first']; counts['unwoundBoundary'] += abs(difference) > 180
            for index, node in enumerate(row['nodes']):
                if node['active'] != frame['active'][index]:
                    raise ValueError('Changed original ApplyAdditive activity')
                if not node['active']:
                    continue
                counts['ticks'] += 1
                if len(node['output']['pose']) != 81 or len(node['base']['pose']) != 81 or node['alpha'] != 1 or node['baseWeight'] != single(frame['weights'][index]):
                    raise ValueError('Incomplete ApplyAdditive context/output')
                for metadata in ('curves', 'attributes', 'rootMotion'):
                    if node['output'].get(metadata) != node['base'].get(metadata):
                        raise ValueError('Main changed provider metadata during additive composition')
                counts['curves'] += len(node['output']['curves']); counts['attributes'] += len(node['output']['attributes'])
                if 'rootMotion' in node['output']:
                    counts['rootPresent'] += 1; root_pose = node['output']['rootMotion']
                    counts['rootIdentity'] += root_pose['position'] == [0, 0, 0] and root_pose['rotation'] == [0, 0, 0, 1] and root_pose['scale'] == [1, 1, 1]
    if (counts['frames'], counts['ticks'], counts['curves'], counts['attributes'], counts['rootPresent'], counts['rootIdentity'], counts['zeroDelta'], counts['tinyDelta'], counts['first']) != (2100, 2121, 966, 8484, 2121, 576, 6, 3, 9):
        raise ValueError('Incomplete Main composition coverage: ' + str(counts))
    if counts['unwoundBoundary'] == 0:
        raise ValueError('Missing raw yaw crossing boundary')
    return {'status': 'pass', 'scope': 'Original RotationData and compiled Main ApplyAdditive with explicit source-pose boundary; complete provider/Main and production remain open',
            **counts, 'logical': 81, 'skin': 68, 'packages': 492, 'baseResources': resources,
            'files': {name: {'sha256': sha((directory / name).read_bytes()), 'bytes': (directory / name).stat().st_size}
                      for name in ('composition_v2_requests.json', 'composition_v2_native.json', 'composition_v3_policy.json')}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path, default=Path('assets/generated/lyra_als'))
    parser.add_argument('--content', type=Path, default=project_path('Content'))
    parser.add_argument('--engine', type=Path, default=engine_path())
    args = parser.parse_args()
    print(json.dumps(verify(args.root, args.content, args.engine), indent=2))
