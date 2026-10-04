"""Original Main Cycle linked root, immutable packages and effective Lean output."""
import argparse
import hashlib
import json
import math
from pathlib import Path

from locomotion_paths import project_path

sha = lambda data: hashlib.sha256(data).hexdigest()


def verify(root, content):
    load = lambda name: json.loads((root / name).read_bytes())
    native = load('main_cycle_lean_native.json')
    requests = load('main_cycle_lean_requests.json')
    prior = load('main_update_cycle_native.json')
    prior_requests = load('main_update_cycle_requests.json')
    if requests != prior_requests or native['assets'] != prior['assets'] or native['assetSha256'] != prior['assetSha256']:
        raise ValueError('Changed original Main/Cycle inputs or protected asset inventory')
    if native['requestSha256'] != sha((root / 'main_cycle_lean_requests.json').read_bytes()):
        raise ValueError('Stale Main Cycle requests')
    for name, expected in native['dependencies'].items():
        if sha((root / name).read_bytes()) != expected:
            raise ValueError('Changed Main Cycle dependency: ' + name)
    for path, expected in native['assetSha256'].items():
        if sha((content / (path.split('.')[0].removeprefix('/Game/') + '.uasset')).read_bytes()) != expected:
            raise ValueError('Changed protected package: ' + path)
    counts = dict.fromkeys(('frames', 'poseFrames', 'changed', 'hidden', 'rootPresent', 'rootMoving',
        'attributes', 'curves', 'inertia', 'linkedInertia', 'sampleTicks', 'threeSamples', 'leanAngleChanges'), 0)
    identities = set()
    for trace, previous, request in zip(native['traces'], prior['traces'], requests['traces'], strict=True):
        identity = trace['profile'], trace['hz']
        if identity in identities or identity != (previous['profile'], previous['hz']) or identity != (request['profile'], request['hz']):
            raise ValueError('Reordered original Main Cycle traces')
        identities.add(identity)
        old_pin = None
        for index, (row, old, frame) in enumerate(zip(trace['frames'], previous['frames'], request['frames'], strict=True)):
            for key, value in old.items():
                if key not in ('output', 'inertia') and row[key] != value:
                    raise ValueError('Changed registered Cycle or original Main update: ' + key)
            inertia = row['inertia']
            if index == 0:
                if inertia[:-1] != old['inertia'] or inertia[-1]['durationBits'] != 1041865114:
                    raise ValueError('Lost original pending Linked BlendIn request or child-first order')
                counts['linkedInertia'] += 1
            elif inertia != old['inertia']:
                raise ValueError('Repeated/lost pending Linked BlendIn after initialization')
            counts['inertia'] += len(inertia)
            lean = row['lean']
            if old_pin is not None and lean['pinBits'] != old_pin:
                counts['leanAngleChanges'] += 1
            old_pin = lean['pinBits']
            if row['active']:
                if not math.isclose(lean['pin'], row['observation']['after']['AdditiveLeanAngle'], rel_tol=1e-7, abs_tol=1e-6):
                    raise ValueError('Lean did not consume this frame original Main RotationData')
                if lean['cachedWeightBits'] != row['cycleWeightBits'] or not lean['samples']:
                    raise ValueError('Lean source not visited with original ApplyAdditive weight')
                output = row['output']; base = old['output']
                if len(output['pose']) != 81 or any(output.get(key) != base.get(key) for key in ('curves', 'attributes', 'rootMotion')):
                    raise ValueError('Main additive changed provider metadata or logical layout')
                if output['pose'] != base['pose']:
                    counts['changed'] += 1
                counts['sampleTicks'] += len(lean['samples'])
                counts['threeSamples'] += len(lean['samples']) == 3
                counts['poseFrames'] += 1
                counts['attributes'] += len(output['attributes'])
                counts['curves'] += len(output['curves'])
                if 'rootMotion' in output:
                    counts['rootPresent'] += 1
                    counts['rootMoving'] += any(v != 0 for v in output['rootMotion']['position'])
            else:
                if 'output' in row:
                    raise ValueError('Hidden root evaluated')
                counts['hidden'] += 1
            counts['frames'] += 1
    expected = {(profile, hz) for profile in ('unarmed', 'pistol', 'rifle') for hz in (30, 60, 120)}
    if identities != expected or counts['frames'] != 3780 or counts['poseFrames'] != 3528 or counts['hidden'] != 252 or counts['linkedInertia'] != 9 or counts['changed'] < 3000 or counts['threeSamples'] == 0:
        raise ValueError('Incomplete original Main Cycle Lean coverage: ' + str(counts))
    return {'status': 'pass', 'packages': len(native['assetSha256']), 'counts': counts,
        'requestSha256': native['requestSha256'], 'nativeSha256': sha((root / 'main_cycle_lean_native.json').read_bytes()),
        'scope': 'Main Cycle ApplyAdditive/real Linked Cycle/Lean/common Sync. Explicit root relevance and graph weights; not whole LocomotionSM or production.'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path, default=Path('assets/generated/lyra_als'))
    parser.add_argument('--content', type=Path, default=project_path('Content'))
    parser.add_argument('--output', type=Path, default=Path('artifacts/lyra-analysis/main-cycle-lean-resource-verification.json'))
    args = parser.parse_args()
    report = verify(args.root, args.content)
    args.output.write_text(json.dumps(report, indent=2), encoding='utf-8')
    print('LYRA_MAIN_CYCLE_LEAN_VERIFY_OK ' + json.dumps(report['counts']))
