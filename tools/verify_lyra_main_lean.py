"""Check immutable Main Lean extension dependencies and ALS source provenance."""
import argparse
import hashlib
import json
from pathlib import Path

sha = lambda data: hashlib.sha256(data).hexdigest()


def verify(root, content):
    directory = root / 'main_lean'
    read = lambda path: json.loads(path.read_bytes())
    inventory = read(directory / 'inventory.json')
    catalog = read(directory / 'catalog.json')
    native = read(directory / 'native.json')
    behavior = read(directory / 'behavior.json')
    nodes = read(root / 'source_nodes.json')['classes']['main']['sources']
    base = read(root / 'logical_controls/catalog.json')
    expected_dependencies = {
        'baseCatalogSha256': root / 'logical_controls/catalog.json',
        'calibrationSha256': root / 'logical_controls/calibration.json',
        'inventorySha256': directory / 'inventory.json',
    }
    for name, path in expected_dependencies.items():
        if catalog[name] != sha(path.read_bytes()):
            raise ValueError('Stale Main Lean dependency: ' + name)
    catalog_sha = sha((directory / 'catalog.json').read_bytes())
    if (inventory['sourceNodesSha256'] != sha((root / 'source_nodes.json').read_bytes()) or
            native['catalogSha256'] != catalog_sha or behavior['catalogSha256'] != catalog_sha or
            behavior['inventorySha256'] != catalog['inventorySha256']):
        raise ValueError('Stale Main Lean fixture/configuration')
    if len(nodes) != 3 or any(node['asset'] != inventory['source'] for node in nodes):
        raise ValueError('Main Lean sources do not match the original compiled nodes')
    expected_names = ['MM_Rifle_Jog_Lean_Center', 'MM_Rifle_Jog_Leans_Left', 'MM_Rifle_Jog_Lean_Right']
    expected_slots = ['main_lean_center', 'main_lean_left', 'main_lean_right']
    if (len(catalog['entries']) != 3 or len(catalog['curves']) != 3 or len(native['rows']) != 24 or
            len(native['blendRows']) != 33 or catalog['skinPreservation'] != 0):
        raise ValueError('Incomplete Main Lean resource closure')
    for index, (sample, entry, curve) in enumerate(zip(inventory['samples'], catalog['entries'], catalog['curves'], strict=True)):
        if (sample['source'].split('.')[-1] != expected_names[index] or entry['slot'] != expected_slots[index] or
                entry['sampleIndex'] != index or entry['source'] != sample['source'] or entry['position'] != sample['position'] or
                entry['rateScale'] != sample['rateScale'] or not entry['additive']):
            raise ValueError('Changed original Main Lean sample identity')
        path = directory / entry['file']
        if not path.resolve().is_relative_to((directory / 'clips').resolve()) or sha(path.read_bytes()) != entry['sha256']:
            raise ValueError('Changed Main Lean raw clip')
        clip = read(path)
        metadata = clip['metadata']
        if (clip['source'] != entry['source'] or clip['target'] != entry['target'] or
                clip['calibrationSha256'] != catalog['calibrationSha256'] or
                metadata['additiveType'] != 'AAT_LocalSpaceBase' or metadata['basePoseType'] != 'ABPT_AnimFrame' or
                metadata['baseFrame'] != 0 or metadata['baseAsset'] != catalog['entries'][0]['target'] or
                clip['raw']['sampledKeyCount'] != entry['keyCount'] or clip['raw']['playLength'] != entry['playLength'] or
                curve['slot'] != entry['slot'] or curve['source'] != entry['source'] or curve['target'] != entry['target'] or
                curve['baseSlot'] != expected_slots[0] or curve['curves'] or curve['attributes'] or curve['transformCurves'] != 0):
            raise ValueError('Changed Main Lean local additive/metadata policy')
    if (inventory['weightSpeed'] != 3 or not behavior['weightEaseInOut'] or behavior['allowMeshSpaceBlending'] or
            any(axis['time'] != 0 for axis in behavior['axisFilters']) or behavior['manualPerBoneOverrideCount'] != 0 or
            not behavior['legacySampleLength'] or behavior['matchSyncPhases']):
        raise ValueError('Changed original Main Lean interpolation policy')
    for row in native['rows']:
        if row['slot'] not in expected_slots or len(row['raw']) != 81 or len(row['output']) != 81:
            raise ValueError('Incomplete Main Lean native pose')
    angles = set()
    for row in native['blendRows']:
        output = row['output']; angles.add(row['angle'])
        if len(output['pose']) != 81 or len(output['names']) != 81 or output['curves']:
            raise ValueError('Incomplete Main Lean native BlendSpace pose')
        if len(output['samples']) not in (1, 2) or abs(sum(sample['weight'] for sample in output['samples']) - 1) > 1e-7:
            raise ValueError('Invalid native Main Lean weights')
    if len(angles) != 11:
        raise ValueError('Missing Main Lean interpolation boundaries')
    old_assets = read(root / 'cycle_runtime_native.json')['assetSha256']
    packages = old_assets | inventory['assetSha256']
    packages.update({entry['target']: catalog['assetSha256'][entry['target']] for entry in catalog['entries']})
    if catalog['assetSha256'] != packages or len(old_assets) != 489 or len(packages) != 492:
        raise ValueError('Changed source/target package closure')
    for asset, digest in packages.items():
        path = content / (asset.split('.')[0].removeprefix('/Game/') + '.uasset')
        if sha(path.read_bytes()) != digest:
            raise ValueError('Changed source/target package: ' + asset)
    if len(base['entries']) != 234:
        raise ValueError('Changed original source closure')
    for entry in base['entries']:
        if sha((root / 'logical_controls' / entry['file']).read_bytes()) != entry['sha256']:
            raise ValueError('Changed existing logical source: ' + entry['slot'])
    return {'status': 'pass', 'baseSources': 234, 'mainLeanSamples': 3, 'fullMainSources': 237,
            'originalPackages': 489, 'packages': len(packages), 'logical': 81, 'skin': 68,
            'nativeSamples': 24, 'staticBlends': 33, 'skinPreservation': 0,
            'runtimeWeightSpeed': 3, 'runtimeEaseInOut': True, 'legacyLength': True,
            'scope': 'Resources and static sampling; player history/common Sync remain open',
            'files': {str(path.relative_to(directory)): {'bytes': path.stat().st_size, 'sha256': sha(path.read_bytes())}
                      for path in sorted(directory.rglob('*.json'))}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path, default=Path('assets/generated/lyra_als'))
    parser.add_argument('--content', type=Path, default=Path('../GASP58/Content'))
    args = parser.parse_args()
    print(json.dumps(verify(args.root, args.content), indent=2))
