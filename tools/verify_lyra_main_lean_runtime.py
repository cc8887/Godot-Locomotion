"""Verify original compiled Main Lean traces and immutable runtime policy/provenance."""
import argparse
import hashlib
import json
import struct
from pathlib import Path
from verify_lyra_main_lean import verify as verify_resources

sha = lambda data: hashlib.sha256(data).hexdigest()
bits = lambda value: struct.unpack('<I', struct.pack('<f', value))[0]


def verify(root, content):
    resources = verify_resources(root, content)
    directory = root / 'main_lean'
    read = lambda file: json.loads((directory / file).read_bytes())
    native, requests, policies, catalog = (read(file) for file in ('runtime_native.json', 'runtime_requests.json', 'runtime_policies.json', 'catalog.json'))
    if (native['requestSha256'] != sha((directory / 'runtime_requests.json').read_bytes()) or
            native['catalogSha256'] != sha((directory / 'catalog.json').read_bytes()) or
            native['behaviorSha256'] != sha((directory / 'behavior.json').read_bytes()) or
            native['sourceNodesSha256'] != sha((root / 'source_nodes.json').read_bytes()) or
            policies['nativeSha256'] != sha((directory / 'runtime_native.json').read_bytes()) or
            policies['sourceNodesSha256'] != native['sourceNodesSha256'] or policies['catalogSha256'] != native['catalogSha256'] or
            native['assetSha256'] != catalog['assetSha256']):
        raise ValueError('Stale Main Lean runtime/provenance')
    if native['angleType'] != 'double' or requests['nodeIndices'] != [22, 16, 12]:
        raise ValueError('Changed original Main Lean nodes/pin type')
    if len(policies['sequences']) != 3:
        raise ValueError('Incomplete Main Lean playback configuration')
    for policy, source in zip(policies['sequences'], catalog['entries'], strict=True):
        if (policy['source'] != source['source'] or policy['target'] != source['target'] or
                bits(policy['playLength']) != bits(source['playLength']) or policy['sourceRate'] != 1 or policy['targetRate'] != 1 or
                policy['sourceMarkerCount'] != 0 or policy['targetMarkerCount'] != 0):
            raise ValueError('Changed Main Lean playback rates/marker policy')
    counts = dict.fromkeys(('frames', 'ticks', 'sampleTicks', 'tripleSamples', 'hiddenWithSamples', 'tinyWeightTicks', 'hiddenInitialize', 'overlapFrames'), 0)
    if [trace['hz'] for trace in native['traces']] != [30, 60, 120] or len(requests['traces']) != 3:
        raise ValueError('Missing Main Lean rates')
    for trace, request in zip(native['traces'], requests['traces'], strict=True):
        if trace['hz'] != request['hz'] or len(trace['frames']) != len(request['frames']) or len(trace['frames']) != trace['hz'] * 10:
            raise ValueError('Incomplete Main Lean physical frames')
        for index, (frame, expected) in enumerate(zip(trace['frames'], request['frames'], strict=True)):
            counts['frames'] += 1; counts['overlapFrames'] += sum(expected['active']) > 1
            if len(frame['nodes']) != 3 or sorted(expected['order']) != [0, 1, 2]:
                raise ValueError('Invalid Main Lean source traversal')
            for node_index, node in enumerate(frame['nodes']):
                active = expected['active'][node_index]
                if node['active'] != active or ('output' in node) != active or node['cache'] != -1:
                    raise ValueError('Hidden Main Lean evaluation or invented persistent cache')
                for key, value in node.items():
                    if key.endswith('Bits') and bits(node[key[:-4]]) != value:
                        raise ValueError('Incomplete Main Lean exact scalar export')
                if active or expected['initialize'][node_index]:
                    if node['pinBits'] != bits(expected['angle']):
                        raise ValueError('Incorrect original double-to-float Lean pin')
                if not active:
                    counts['hiddenWithSamples'] += bool(node['samples'])
                    counts['hiddenInitialize'] += expected['initialize'][node_index] and index != 0
                    continue
                counts['ticks'] += 1
                counts['tinyWeightTicks'] += expected['weights'][node_index] < 1e-5
                counts['tripleSamples'] += len(node['samples']) == 3
                counts['sampleTicks'] += len(node['samples'])
                if node['cachedWeightBits'] != bits(expected['weights'][node_index]) or node['previousBits'] != node['beforeBits']:
                    raise ValueError('Main Lean source context/previous clock changed')
                output = node['output']
                if len(output['pose']) != 81 or output['curves'] or output['attributes']:
                    raise ValueError('Incomplete Main Lean pose/metadata')
                if len(node['samples']) not in (1, 2, 3) or abs(sum(sample['weight'] for sample in node['samples']) - 1) > 1e-5:
                    raise ValueError('Invalid native smoothed sample weights')
                if len({sample['index'] for sample in node['samples']}) != len(node['samples']):
                    raise ValueError('Duplicate Main Lean sample')
                for sample in node['samples']:
                    if sample['index'] not in (0, 1, 2) or sample['rate'] != 1:
                        raise ValueError('Foreign Main Lean sample/rate')
                    for key, value in sample.items():
                        if key.endswith('Bits') and bits(sample[key[:-4]]) != value:
                            raise ValueError('Incomplete smoothed sample scalar export')
    expected_counts = {'frames': 2100, 'ticks': 2121, 'sampleTicks': 6155, 'tripleSamples': 1944,
                       'hiddenWithSamples': 3255, 'tinyWeightTicks': 21, 'hiddenInitialize': 6, 'overlapFrames': 189}
    if counts != expected_counts:
        raise ValueError('Incomplete Main Lean runtime coverage: ' + str(counts))
    return {'status': 'pass', 'scope': 'Original Main Lean source occurrences/common Sync/pose; complete Main and production remain open',
            **counts, 'logical': 81, 'originalPackages': 492, 'resources': resources,
            'files': {file: {'bytes': (directory / file).stat().st_size, 'sha256': sha((directory / file).read_bytes())}
                      for file in ('runtime_requests.json', 'runtime_native.json', 'runtime_policies.json')}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path, default=Path('assets/generated/lyra_als'))
    parser.add_argument('--content', type=Path, default=Path('../GASP58/Content'))
    args = parser.parse_args()
    print(json.dumps(verify(args.root, args.content), indent=2))
