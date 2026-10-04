"""Verify original compiled Cycle bindings, root output and immutable provenance."""
import argparse
import copy
import hashlib
import json
import math
import struct
from pathlib import Path
from locomotion_paths import project_path

from verify_lyra_stride import verify as verify_stride

sha = lambda data: hashlib.sha256(data).hexdigest()
single = lambda value: struct.unpack('<f', struct.pack('<f', value))[0]
bits = lambda value: struct.unpack('<I', struct.pack('<f', value))[0]


def verify(root, content):
    previous = verify_stride(root, content)
    native = json.loads((root / 'cycle_runtime_native.json').read_bytes())
    requests = json.loads((root / 'cycle_runtime_requests.json').read_bytes())
    bindings = json.loads((root / 'cycle_runtime_bindings.json').read_bytes())
    source_requests = json.loads((root / 'cycle_layer_requests.json').read_bytes())
    source = json.loads((root / 'cycle_layer_native_bits.json').read_bytes())
    definitions = json.loads((root / 'cycle_source_definitions.json').read_bytes())
    if native['requestSha256'] != sha((root / 'cycle_runtime_requests.json').read_bytes()) or native['contractSha256'] != sha((root / 'cycle_layer_graph.json').read_bytes()):
        raise ValueError('Stale original Cycle runtime fixture')
    expected_dependencies = ('cycle_layer_graph.json', 'cycle_layer_requests.json', 'cycle_layer_native_bits.json',
                             'logical_controls/catalog.json', 'logical_controls/calibration.json', 'linked_layer_inventory.json',
                             'source_nodes.json', 'cycle_source_definitions.json', 'cycle_layer_pose_policy.json',
                             'root_motion_policy.json', 'orientation_policy.json', 'stride_policy.json')
    for contract in (native, bindings):
        if contract['schemaVersion'] != 1 or set(contract['dependencies']) != set(expected_dependencies):
            raise ValueError('Incomplete original Cycle runtime provenance')
        for name, digest in contract['dependencies'].items():
            if sha((root / name).read_bytes()) != digest:
                raise ValueError('Changed original Cycle dependency: ' + name)
    if bindings['dependencies'] != native['dependencies'] or bindings['orientationAlpha'] != 1 or bindings['orientationDirection'] != [0, 0, 0]:
        raise ValueError('Changed original Cycle constants')
    expected_bindings = {'orientationAngle': ['GetMainAnimBPThreadSafe', 'LocalVelocityDirectionAngle'],
                         'strideSpeed': ['GetMainAnimBPThreadSafe', 'DisplacementSpeed'], 'strideAlpha': 'StrideWarpingCycleAlpha'}
    properties = dict.fromkeys(('LocalVelocityDirectionAngle', 'LocalVelocityDirectionAngleWithOffset',
                               'DisplacementSpeed', 'StrideWarpingCycleAlpha'), 'double')
    if bindings['bindings'] != expected_bindings or bindings['profiles'] != dict.fromkeys(('unarmed', 'pistol', 'rifle'), properties):
        raise ValueError('Changed original Cycle signatures or binding paths')
    expected_requests = copy.deepcopy(source_requests)
    expected_requests['sequencePaths'] = [asset['path'] for asset in source['assets']]
    angles = [0, 35.123456789, 88.9, -45.001234, 110, -170, 179.999999, -179.999999, 15, -80, 360, 720]
    for trace in expected_requests['traces']:
        for index, frame in enumerate(trace['frames']):
            frame['main']['LocalVelocityDirectionAngle'] = angles[(index // 7) % len(angles)]
            frame['main']['LocalVelocityDirectionAngleWithOffset'] = frame['main']['LocalVelocityDirectionAngle'] + 123.56789
            frame['main']['DisplacementSpeed'] += .0123456789
            half = math.radians([0, 45, -90, 170][(index // 23) % 4]) * .5
            frame['relativeRotation'] = [0, 0, math.sin(half), math.cos(half)]
    if requests != expected_requests or native['assets'] != source['assets'] or native['assetSha256'] != source['assetSha256']:
        raise ValueError('Changed original source requests or asset metadata')
    if any(len(contract['traces']) != 9 for contract in (native, requests)):
        raise ValueError('Missing original Cycle traces')
    counts = dict.fromkeys(('frames', 'hidden', 'poseFrames', 'rootPresent', 'rootAbsent', 'rootMoving', 'curves',
                           'attributes', 'partialStrideAlpha', 'zeroStrideAlpha', 'changedAlpha', 'angleQuantized',
                           'speedQuantized', 'distinctOffset', 'hipFireTicks', 'inertia'), 0)
    identities = set()
    for trace, request in zip(native['traces'], requests['traces'], strict=True):
        identity = (trace['profile'], trace['hz'])
        if identity != (request['profile'], request['hz']) or identity in identities or trace['propertyTypes'] != properties:
            raise ValueError('Reordered, duplicate or mistyped original Cycle trace')
        identities.add(identity)
        last_alpha = None
        for row, frame in zip(trace['frames'], request['frames'], strict=True):
            counts['frames'] += 1
            counts['hipFireTicks'] += row['hipFireActive']
            counts['inertia'] += len(row['inertia'])
            if row['active'] != frame['active'] or ('output' in row) != frame['active']:
                raise ValueError('Hidden Cycle pose or missing active pose')
            if not frame['active']:
                counts['hidden'] += 1
                continue
            main = frame['main']
            for key, value in (('orientationAngle', main['LocalVelocityDirectionAngle']), ('strideSpeed', main['DisplacementSpeed']),
                               ('strideNodeAlpha', row['strideAlpha']), ('orientationAlpha', 1)):
                if row[key] != single(value) or row[key + 'Bits'] != bits(value):
                    raise ValueError('Wrong original exposed-handler readback: ' + key)
            if row['orientationDirection'] != [0, 0, 0] or row['strideType'] != 'double':
                raise ValueError('Changed original Cycle constant or callback field')
            if any(row[key] != definitions['assets'][row['asset']][key] for key in ('lengthBits', 'rootDistanceBits')):
                raise ValueError('Transient ALS81 changed the source MatchSpeed metadata')
            counts['partialStrideAlpha'] += 0 < row['strideNodeAlpha'] < 1
            counts['zeroStrideAlpha'] += row['strideNodeAlpha'] == 0
            counts['changedAlpha'] += last_alpha is not None and row['strideNodeAlpha'] != last_alpha
            last_alpha = row['strideNodeAlpha']
            counts['angleQuantized'] += row['orientationAngle'] != main['LocalVelocityDirectionAngle']
            counts['speedQuantized'] += row['strideSpeed'] != main['DisplacementSpeed']
            counts['distinctOffset'] += row['orientationAngle'] != single(main['LocalVelocityDirectionAngleWithOffset'])
            output = row['output']
            if len(output['pose']) != 81 or len(output['attributes']) != 4:
                raise ValueError('Incomplete logical pose/attributes')
            root_motion = output.get('rootMotion')
            atoms = output['pose'] + ([root_motion] if root_motion else [])
            if any(not math.isfinite(value) for atom in atoms for key in ('position', 'rotation', 'scale') for value in atom[key]):
                raise ValueError('Nonfinite original Cycle output')
            if root_motion and (root_motion['name'], root_motion['bone'], root_motion['namespace'], root_motion['type']) != (
                    'RootMotionDelta', 'root', 'bone', '/Script/Engine.TransformAnimationAttribute'):
                raise ValueError('Changed original root attribute identity')
            counts['rootPresent'] += root_motion is not None
            counts['rootAbsent'] += root_motion is None
            counts['rootMoving'] += root_motion is not None and root_motion['position'] != [0, 0, 0]
            counts['poseFrames'] += 1
            counts['curves'] += len(output['curves'])
            counts['attributes'] += len(output['attributes'])
    expected = (3780, 252, 3528, 3255, 273, 2956, 105, 14112, 3519, 9, 3204, 1479, 3528, 3528, 2205, 108)
    if identities != {(p, hz) for p in ('unarmed', 'pistol', 'rifle') for hz in (30, 60, 120)} or tuple(counts.values()) != expected:
        raise ValueError('Missing original Cycle coverage: ' + repr(counts))
    return {**counts, 'packages': previous['packages'], 'stage': 'OriginalCycleRoot', 'originalExposedHandlers': True,
            'mainInputs': 'controlledSnapshots', 'production': False, 'wholeMain': False,
            'files': {name: {'sha256': sha((root / name).read_bytes()), 'bytes': (root / name).stat().st_size}
                      for name in ('cycle_runtime_bindings.json', 'cycle_runtime_requests.json', 'cycle_runtime_native.json')}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path, default=Path('assets/generated/lyra_als'))
    parser.add_argument('--content', type=Path, default=project_path('Content'))
    parser.add_argument('--out', type=Path, default=Path('artifacts/lyra-analysis/cycle-runtime-verification.json'))
    args = parser.parse_args()
    result = verify(args.root, args.content)
    args.out.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print('LYRA_CYCLE_RUNTIME_VERIFIED ' + ' '.join(f'{key}={result[key]}' for key in ('frames', 'poseFrames', 'rootPresent', 'changedAlpha', 'distinctOffset', 'packages')))
