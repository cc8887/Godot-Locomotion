"""Run original Main Lean nodes with real exposed inputs and one native Sync."""
import json
import runpy
from pathlib import Path
import unreal

# Reuse the exact immutable resource reconstruction and provenance checks.
resources = runpy.run_path(str(Path(__file__).with_name('export_lyra_main_lean.py')))
root, save, sha = (resources[key] for key in ('root', 'save', 'sha'))
requests = {'schemaVersion': 1, 'nodeIndices': [22, 16, 12], 'traces': []}
angles = [19.123456789, -30, 0, 8.9, 20, 13.75, -1.25, -20, .0123456789, 30, -13.37]
for hz in (30, 60, 120):
    frames = []
    for index in range(hz * 10):
        t = index / hz
        active = [t < 1 or 8 <= t < 8.4 or t >= 9,
                  .8 <= t < 3.5 or 5.6 <= t < 8.4,
                  3.2 <= t < 5 or 8 <= t < 8.4]
        initialize = [index == 0 or index == hz * 9,
                      index == 0 or index == round(hz * 5.2),
                      index == 0 or index == round(hz * 3.1)]
        weights = [.75, .25, .125] if 8 <= t < 8.4 else [1, 1, 1]
        if 8.2 <= t < 8.3:
            weights[1] = 1e-6
        frames.append({'delta': 1 / hz, 'angle': angles[(index // max(1, round(hz * .11))) % len(angles)],
                       'active': active, 'initialize': initialize, 'weights': weights,
                       'order': [0, 1, 2] if (index // 11) % 2 == 0 else [2, 1, 0]})
    requests['traces'].append({'hz': hz, 'frames': frames})
request_sha = save('runtime_requests.json', requests)
main_class = unreal.load_class(None, '/Game/Characters/Heroes/Mannequin/Animations/ABP_Mannequin_Base.ABP_Mannequin_Base_C')
if main_class is None:
    raise ValueError('Missing original Main class')
native = json.loads(unreal.AlsLyraGraphLibrary.read_main_lean_trace(main_class, resources['source_mesh'],
    resources['extended'], resources['space'], resources['extended_sequences'], json.dumps(requests, separators=(',', ':'))))
native.update({'schemaVersion': 1, 'requestSha256': request_sha,
    'catalogSha256': sha((root / 'main_lean/catalog.json').read_bytes()),
    'behaviorSha256': sha((root / 'main_lean/behavior.json').read_bytes()),
    'sourceNodesSha256': sha((root / 'source_nodes.json').read_bytes()), 'assetSha256': resources['packages']})
save('runtime_native.json', native)
policies = []
for sample, target in zip(resources['samples'], resources['target_sequences'], strict=True):
    source = unreal.load_asset(sample['source'])
    source_sync = json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(source))
    target_sync = json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(target))
    policies.append({'source': source.get_path_name(), 'target': target.get_path_name(),
        'sourceRate': source_sync['rateScale'], 'targetRate': target_sync['rateScale'],
        'sourceMarkerCount': len(source_sync['markers']), 'targetMarkerCount': len(target_sync['markers']),
        'playLength': sample['metadata']['sequencePlayLength']})
save('runtime_policies.json', {'schemaVersion': 1, 'catalogSha256': native['catalogSha256'],
    'sourceNodesSha256': native['sourceNodesSha256'],
    'nativeSha256': sha((root / 'main_lean/runtime_native.json').read_bytes()), 'sequences': policies})
resources['check_packages']()
unreal.log('LYRA_MAIN_LEAN_RUNTIME_NATIVE_OK traces=3 frames=2100 nodes=3 assets_saved=0')
