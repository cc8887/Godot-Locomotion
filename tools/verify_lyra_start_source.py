"""Start fixture provenance and native boundary coverage, independent of Godot."""
import argparse
import hashlib
import json
from pathlib import Path

from locomotion_paths import project_path

sha = lambda data: hashlib.sha256(data).hexdigest()

def verify(root, content):
    load = lambda name: json.loads((root / name).read_bytes())
    native = load('start_source_native_bits.json')
    requests = load('start_source_requests.json')
    definitions = load('start_source_definitions.json')
    prior = load('main_cycle_lean_native.json')
    digest = sha((root / 'start_source_requests.json').read_bytes())
    if native['requestSha256'] != digest or definitions['requestSha256'] != digest:
        raise ValueError('Changed Start requests')
    for name, expected in native['dependencies'].items():
        if sha((root / name).read_bytes()) != expected:
            raise ValueError('Changed Start dependency: ' + name)
    packages = dict(prior['assetSha256'])
    for path, expected in native['assetSha256'].items():
        if path not in packages or packages[path] != expected:
            raise ValueError('Changed protected package inventory: ' + path)
    for path, expected in packages.items():
        if sha((content / (path.split('.')[0].removeprefix('/Game/') + '.uasset')).read_bytes()) != expected:
            raise ValueError('Changed protected package: ' + path)
    counts = dict.fromkeys(('frames', 'active', 'hidden', 'setups', 'changes', 'retained',
        'clockDiverged', 'zeroDelta', 'tinyDelta', 'negativeDistance', 'continuousInitialize', 'hiddenInitialize'), 0)
    identities, seen = set(), set()
    for trace, request in zip(native['traces'], requests['traces'], strict=True):
        identity = trace['profile'], trace['hz']
        if identity in identities or identity != (request['profile'], request['hz']) or trace['policy'] != definitions['policies'][trace['profile']]:
            raise ValueError('Wrong Start trace/policy')
        identities.add(identity)
        for row, frame in zip(trace['frames'], request['frames'], strict=True):
            if row['active'] != frame['active'] or row['asset'] not in definitions['assets']:
                raise ValueError('Wrong Start source identity')
            if frame['active']:
                seen.add(row['asset']); counts['active'] += 1
                counts['setups'] += row['becameRelevant']
                counts['changes'] += row['asset'] != row['beforeAsset']
                counts['clockDiverged'] += row['explicitBits'] != row['timeBits']
                main = frame['main']
                group = 'Crouch_Start_Cardinals' if main['IsCrouching'] else 'ADS_Start_Cardinals' if main['GameplayTag_IsADS'] else 'Jog_Start_Cardinals'
                direction = ('forward', 'backward', 'left', 'right')[main['LocalVelocityDirection']]
                desired = request['bindings'][group][direction]
                counts['retained'] += row['asset'] != desired
                if not row['becameRelevant'] and row['asset'] != row['beforeAsset']:
                    raise ValueError('Start Update incorrectly reselected sequence')
                if row['becameRelevant'] and row['asset'] != desired:
                    raise ValueError('Start Setup chose wrong sequence')
                counts['continuousInitialize'] += frame['reinitialize'] and not row['becameRelevant']
                counts['zeroDelta'] += frame['delta'] == 0
                counts['tinyDelta'] += 0 < frame['delta'] <= 1e-8
                counts['negativeDistance'] += main['DisplacementSinceLastUpdate'] < 0
            else:
                counts['hidden'] += 1
                counts['hiddenInitialize'] += frame['reinitialize']
                if row['becameRelevant'] or 'rate' in row:
                    raise ValueError('Hidden Start source ticked')
            counts['frames'] += 1
    if identities != {(p, hz) for p in ('unarmed', 'pistol', 'rifle') for hz in (30, 60, 120)} or len(seen) != 36 or counts['frames'] != 3780 or counts['hidden'] != 108 or not all(counts[k] for k in ('clockDiverged', 'retained', 'continuousInitialize', 'hiddenInitialize')):
        raise ValueError('Incomplete native Start coverage: ' + str(counts))
    return {'status': 'pass', 'packages': len(packages), 'assets': len(seen), 'counts': counts,
        'files': {name: {'sha256': sha((root / name).read_bytes()), 'bytes': (root / name).stat().st_size}
            for name in ('start_source_requests.json', 'start_source_native_bits.json', 'start_source_definitions.json')},
        'scope': 'Original Start evaluator callbacks and common native Sync on ALS target assets; controlled Main inputs. No provider root/state-machine/production/whole-chain acceptance.'}

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path, default=Path('assets/generated/lyra_als'))
    parser.add_argument('--content', type=Path, default=project_path('Content'))
    parser.add_argument('--output', type=Path, default=Path('artifacts/lyra-analysis/start-source-resource-verification.json'))
    args = parser.parse_args()
    report = verify(args.root, args.content)
    args.output.write_text(json.dumps(report, indent=2), encoding='utf-8')
    print('LYRA_START_SOURCE_VERIFY_OK ' + json.dumps(report['counts']))
