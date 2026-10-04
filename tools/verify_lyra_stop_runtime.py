"""Verify Stop/Main Stop native resources, old source behavior and provenance."""
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
    source = load('stop_source_native.json')
    graphs = load('stop_layer_graph.json')
    roots = load('stop_runtime_roots.json')
    distance = load('stop_runtime_distance.json')
    if len(graphs['graphs']) != 9 or any(len(g['nodes']) != 4 for g in graphs['graphs'].values()):
        raise ValueError('Incomplete original Stop closure')
    if len(roots['assets']) != 42 or len(distance['assets']) != 36:
        raise ValueError('Incomplete Stop source codecs')
    probes = 0
    for path, data in roots['assets'].items():
        if not base64.b64decode(data['payload'], validate=True) or data['rootTrack'] != 0 or len(data['channels']) != 3:
            raise ValueError('Invalid compressed root: ' + path)
        if len(data['probes']) != 9: raise ValueError('Incomplete compressed root probes')
        probes += len(data['probes'])
        for index, channel in enumerate(data['channels']):
            if not channel['keys'] or any(len(key) != (4 if index == 1 else 3) or
                                         any(not math.isfinite(v) for v in key) for key in channel['keys']):
                raise ValueError('Invalid decoded compressed root channel')
    reports = {}
    for stage in ('stop_runtime', 'main_stop_runtime'):
        native = load(stage + '_native.json'); requests = load(stage + '_requests.json')
        for field, name in (('requestSha256', stage + '_requests.json'), ('rootSha256', 'stop_runtime_roots.json'),
                            ('distanceSha256', 'stop_runtime_distance.json'), ('contractSha256', 'stop_layer_graph.json')):
            if native[field] != sha((root / name).read_bytes()): raise ValueError('Stale dependency: ' + name)
        for name, digest in native['dependencies'].items():
            if sha((root / name).read_bytes()) != digest: raise ValueError('Changed dependency: ' + name)
        if roots['requestSha256'] != sha((root / 'stop_runtime_requests.json').read_bytes()) or \
           distance['requestSha256'] != roots['requestSha256']:
            raise ValueError('Stale codec request identity')
        for path, digest in native['assetSha256'].items():
            if sha((content / (path.split('.')[0].removeprefix('/Game/') + '.uasset')).read_bytes()) != digest:
                raise ValueError('Changed protected package: ' + path)
        if [a['path'] for a in native['assets']] != requests['sequencePaths'] or \
           set(requests['sequencePaths']) != set(roots['assets']):
            raise ValueError('Changed source inventory order')
        counts = dict.fromkeys(('frames', 'poses', 'hidden', 'hipFire', 'setups', 'attributes', 'roots',
                              'movingRoots', 'clockDiverged', 'retainedPreviousOutside', 'inertia', 'accumulating',
                              'blendingOut', 'zeroPrevious'), 0)
        identities = set(); seen = set()
        lengths = {a['path']: a['length'] for a in native['assets']}
        for ti, (trace, request) in enumerate(zip(native['traces'], requests['traces'], strict=True)):
            identity = trace['profile'], trace['hz']
            if identity in identities or identity != (request['profile'], request['hz']):
                raise ValueError('Reordered/duplicated native trace')
            identities.add(identity)
            for index, (row, frame) in enumerate(zip(trace['frames'], request['frames'], strict=True)):
                counts['frames'] += 1
                if stage == 'stop_runtime':
                    old = source['traces'][ti]['frames'][index]
                    if any(row[k] != value for k, value in old.items()):
                        raise ValueError('Full Stop changed original scalar/source behavior')
                else:
                    if row['machineCurrent'] != frame['machineCurrent'] or row['previousStopWeight'] != single(frame['previousStopWeight']):
                        raise ValueError('Changed explicit state-machine observation')
                    if row['observation']['tailAfter']['mode'] != 0 or row['observation']['after']['IsFirstUpdate']:
                        raise ValueError('Incomplete actual Main update')
                    if frame['observation']['snapshot'] != row['observation']['input']:
                        raise ValueError('Stale Main gather snapshot')
                    counts['inertia'] += len(row['inertia'])
                    if frame['active']:
                        counts['accumulating'] += row['rootYawModeAfterGraph'] == 2
                        counts['blendingOut'] += row['rootYawModeAfterGraph'] == 0
                        counts['zeroPrevious'] += row['previousStopWeight'] == 0
                if not frame['active']:
                    if 'output' in row or row['hipFireActive']: raise ValueError('Hidden root evaluated or ticked')
                    counts['hidden'] += 1
                    continue
                output = row['output']; root_motion = output.get('rootMotion')
                if len(output['pose']) != 81 or len(output['attributes']) != 4 or not root_motion:
                    raise ValueError('Incomplete original Stop output')
                if (root_motion['name'], root_motion['bone'], root_motion['namespace'], root_motion['type']) != \
                   ('RootMotionDelta', 'root', 'bone', '/Script/Engine.TransformAnimationAttribute'):
                    raise ValueError('Wrong typed RootMotion identity')
                if any(key in row for key in ('orientationAngle', 'strideSpeed', 'strideNodeAlpha')):
                    raise ValueError('Stop erroneously contains Warp nodes')
                counts['poses'] += 1; counts['roots'] += 1; counts['attributes'] += len(output['attributes'])
                counts['hipFire'] += row['hipFireActive']; counts['setups'] += row['becameRelevant']
                counts['movingRoots'] += any(v != 0 for v in root_motion['position'])
                counts['clockDiverged'] += row['timeBits'] != row['explicitBits']
                counts['retainedPreviousOutside'] += row['previous'] > lengths[row['asset']]
                seen.add(row['asset'])
        if identities != {(p, hz) for p in ('unarmed', 'pistol', 'rifle') for hz in (30, 60, 120)} or \
           counts['frames'] != 3780 or counts['poses'] != 3672 or counts['hidden'] != 108 or counts['hipFire'] != 2268:
            raise ValueError('Incomplete native Stop coverage: ' + str(counts))
        reports[stage] = {'counts': counts, 'selectedAssets': len(seen), 'packages': len(native['assetSha256'])}
    names = ('stop_layer_graph.json', 'stop_runtime_requests.json', 'stop_runtime_distance.json',
             'stop_runtime_roots.json', 'stop_runtime_native.json', 'main_stop_runtime_requests.json', 'main_stop_runtime_native.json')
    return {'status': 'pass_component', 'rootProbes': probes, 'stages': reports,
            'files': {name: {'sha256': sha((root / name).read_bytes()), 'bytes': (root / name).stat().st_size} for name in names},
            'production': False, 'wholeMachine': False, 'scope': 'Stop provider and Main StateResult/Linked component; machine observations are explicit, no transition selection or common three-root/production acceptance.'}

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path, default=Path('assets/generated/lyra_als'))
    parser.add_argument('--content', type=Path, default=Path('../GASP58/Content'))
    parser.add_argument('--output', type=Path, default=Path('artifacts/lyra-analysis/stop-runtime-resource-verification.json'))
    args = parser.parse_args(); report = verify(args.root, args.content)
    args.output.write_text(json.dumps(report, indent=2), encoding='utf-8')
    print('LYRA_STOP_RUNTIME_VERIFY_OK ' + json.dumps(report['stages']))
