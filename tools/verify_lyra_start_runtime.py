"""Original Start provider closure, compressed root resources and package safety."""
import argparse
import base64
import hashlib
import json
import math
import struct
from pathlib import Path

sha = lambda data: hashlib.sha256(data).hexdigest()
single = lambda value: struct.unpack('f', struct.pack('f', value))[0]


def verify(root, content):
    load = lambda name: json.loads((root / name).read_bytes())
    native = load('start_runtime_native_v3.json')
    requests = load('start_runtime_requests.json')
    roots = load('start_runtime_roots_v2.json')
    distance = load('start_runtime_distance.json')
    graphs = load('start_layer_graph.json')
    source = load('start_source_native_bits.json')
    prior = load('start_runtime_native_v2.json')
    for field, name in (('requestSha256', 'start_runtime_requests.json'), ('rootSha256', 'start_runtime_roots_v2.json'),
                        ('distanceSha256', 'start_runtime_distance.json'), ('contractSha256', 'start_layer_graph.json')):
        if native[field] != sha((root / name).read_bytes()):
            raise ValueError('Stale Start root dependency: ' + name)
    if roots['requestSha256'] != native['requestSha256'] or distance['requestSha256'] != native['requestSha256']:
        raise ValueError('Stale Start codec resource')
    for name, expected in native['dependencies'].items():
        if sha((root / name).read_bytes()) != expected:
            raise ValueError('Changed Start dependency: ' + name)
    if native['assetSha256'] != prior['assetSha256'] or native['traces'] != prior['traces']:
        raise ValueError('Root metadata export changed the captured native behavior')
    for path, expected in native['assetSha256'].items():
        if sha((content / (path.split('.')[0].removeprefix('/Game/') + '.uasset')).read_bytes()) != expected:
            raise ValueError('Changed protected package: ' + path)
    assets = [a['path'] for a in native['assets']]
    if len(assets) != 42 or set(assets) != set(roots['assets']) or len(distance['assets']) != 36:
        raise ValueError('Incomplete compressed source inventory')
    codecs, encodings, probes, channel_keys, variable_channels = set(), set(), 0, 0, 0
    old_roots = load('start_runtime_roots.json')['assets']
    for path, data in roots['assets'].items():
        if any(data[k] != v for k, v in old_roots[path].items()):
            raise ValueError('Changed settled compressed payload: ' + path)
        payload = base64.b64decode(data['payload'], validate=True)
        if not payload or data['rootTrack'] != 0 or data['frameRateNumerator'] <= 0 or data['frameRateDenominator'] <= 0:
            raise ValueError('Missing actual compressed root track')
        if len(data['channels']) != 3 or len(data['probes']) != 9:
            raise ValueError('Incomplete compressed root channels/probes')
        codecs.add(data['codec']); encodings.add(data['keyEncoding']); probes += len(data['probes'])
        for index, channel in enumerate(data['channels']):
            if not channel['keys'] or any(len(k) != (4 if index == 1 else 3) or any(not math.isfinite(v) for v in k)
                                           for k in channel['keys']):
                raise ValueError('Invalid decoded compressed keys')
            channel_keys += len(channel['keys'])
            if channel['frames']:
                if len(channel['frames']) != len(channel['keys']) or any(a >= b for a, b in zip(channel['frames'], channel['frames'][1:])):
                    raise ValueError('Invalid compressed key frame table')
                variable_channels += 1
    if len(graphs['graphs']) != 9 or any(len(g['nodes']) != 8 for g in graphs['graphs'].values()):
        raise ValueError('Incomplete original Start graph')
    counts = dict.fromkeys(('frames', 'poses', 'hidden', 'hipTicks', 'setups', 'roots', 'movingRoots', 'attributes', 'tinyHip', 'clockDiverged'), 0)
    identities, seen = set(), set()
    for trace, request, old_source in zip(native['traces'], requests['traces'], source['traces'], strict=True):
        identity = trace['profile'], trace['hz']
        if identity in identities or identity != (request['profile'], request['hz']) or identity != (old_source['profile'], old_source['hz']):
            raise ValueError('Reordered Start trace')
        identities.add(identity)
        for row, frame, old in zip(trace['frames'], request['frames'], old_source['frames'], strict=True):
            if any(row[k] != v for k, v in old.items()):
                raise ValueError('Start provider changed the original source callbacks/Sync')
            counts['frames'] += 1
            if not frame['active']:
                if 'output' in row or row['hipFireActive']:
                    raise ValueError('Hidden Start root evaluated/ticked')
                counts['hidden'] += 1
                continue
            main = frame['main']; output = row['output']; root_motion = output['rootMotion']
            if row['orientationAngle'] != single(main['LocalVelocityDirectionAngleWithOffset']) or row['strideSpeed'] != single(main['DisplacementSpeed']) or row['strideNodeAlpha'] != single(row['StrideWarpingStartAlpha']):
                raise ValueError('Start root consumed stale/incorrect bound pins')
            if len(output['pose']) != 81 or len(output['attributes']) != 4 or root_motion['name'] != 'RootMotionDelta' or root_motion['bone'] != 'root' or root_motion['namespace'] != 'bone':
                raise ValueError('Start logical pose/typed metadata changed')
            counts['poses'] += 1; counts['roots'] += 1; counts['attributes'] += len(output['attributes'])
            counts['movingRoots'] += any(v != 0 for v in root_motion['position'])
            counts['hipTicks'] += row['hipFireActive']; counts['tinyHip'] += 0 < row['blendWeight'] <= 1e-5
            counts['setups'] += row['becameRelevant']; counts['clockDiverged'] += row['timeBits'] != row['explicitBits']
            seen.add(row['asset'])
    if identities != {(p, hz) for p in ('unarmed', 'pistol', 'rifle') for hz in (30, 60, 120)} or len(seen) != 36 or counts['frames'] != 3780 or counts['poses'] != 3672 or counts['hidden'] != 108 or probes != 378:
        raise ValueError('Incomplete original Start runtime coverage: ' + str(counts))
    names = ('start_layer_graph.json', 'start_runtime_requests.json', 'start_runtime_distance.json', 'start_runtime_roots_v2.json', 'start_runtime_native_v3.json')
    return {'status': 'pass', 'packages': len(native['assetSha256']), 'assets': len(assets), 'counts': counts,
        'rootCodecs': sorted(codecs), 'rootEncodings': sorted(encodings), 'rootProbes': probes, 'compressedChannelKeys': channel_keys,
        'variableChannels': variable_channels, 'files': {name: {'sha256': sha((root / name).read_bytes()), 'bytes': (root / name).stat().st_size} for name in names},
        'scope': 'Actual original Start provider closure with controlled Main pins; not Main Start/LocomotionSM/Notify/Montage/production acceptance.'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path, default=Path('assets/generated/lyra_als'))
    parser.add_argument('--content', type=Path, default=Path('../GASP58/Content'))
    parser.add_argument('--output', type=Path, default=Path('artifacts/lyra-analysis/start-runtime-resource-verification.json'))
    args = parser.parse_args(); report = verify(args.root, args.content)
    args.output.write_text(json.dumps(report, indent=2), encoding='utf-8')
    print('LYRA_START_RUNTIME_VERIFY_OK '+json.dumps(report['counts']))
