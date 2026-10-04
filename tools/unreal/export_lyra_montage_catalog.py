"""Read original Lyra Main-slot Montages without modifying any asset."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
repo = Path(__file__).resolve().parents[2]
content = Path(unreal.Paths.project_content_dir())
name = 'montage_catalog_v2.json'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
graph = json.loads((root / 'main_layer_graph_v1.json').read_bytes())
packages = dict(graph['assetSha256'])
previous = {str(p.relative_to(root)).replace('\\', '/'): sha(p) for p in root.rglob('*.json') if p.name != name}
if (root / name).exists():
    previous = json.loads((root / name).read_bytes())['previousFixtureSha256']

def package_file(path):
    return content / (path.split('.')[0].removeprefix('/Game/') + '.uasset')

def protect():
    for path, digest in packages.items():
        assert sha(package_file(path)) == digest, path
    for path, digest in previous.items():
        assert sha(root / path) == digest, path

protect()
montage_paths = sorted('/Game/' + str(p.relative_to(content).with_suffix('')).replace('\\', '/') + '.' + p.stem
    for directory in (content / 'Characters/Heroes/Mannequin/Animations', content / 'Weapons')
    for p in directory.rglob('AM_*.uasset'))
loaded = [unreal.load_asset(p) for p in montage_paths]
assert all(isinstance(m, unreal.AnimMontage) for m in loaded)
catalog = json.loads(unreal.AlsLyraMontageLibrary.read_catalog(loaded))
nodes = graph['classes']['main']['graphs']['AnimGraph']['nodes']
slot_nodes = [dict(node=n['index'], name=n['settings']['slotName'], alwaysUpdate=n['settings']['bAlwaysUpdateSourcePose'])
              for n in nodes if n['type'] == '/Script/AnimGraphRuntime.AnimNode_Slot']
slot_names = {s['name'] for s in slot_nodes}
assert slot_names == {'UpperBody', 'UpperBodyAdditive', 'FullBodyAdditivePreAim', 'AdditiveHitReact', 'FullBody'}
assets = [a for a in catalog['assets'] if any(s['name'] in slot_names for s in a['slots'])]
assert assets and all(a['slots'] for a in assets)
sequences = {}
for asset in assets:
    packages.setdefault(asset['path'], sha(package_file(asset['path'])))
    packages.setdefault(asset['skeleton'], sha(package_file(asset['skeleton'])))
    asset['notifies'] = json.loads(unreal.AlsSourceAnimationLibrary.read_source_notify_metadata(unreal.load_asset(asset['path'])))
    asset['curves'] = json.loads(unreal.AlsSourceAnimationLibrary.read_source_float_curves(unreal.load_asset(asset['path'])))
    for slot in asset['slots']:
        for segment in slot['segments']:
            path = segment['animation']
            assert path, (asset['path'], slot['name'])
            sequence = unreal.load_asset(path)
            assert isinstance(sequence, unreal.AnimSequence), ('Composite segment requires explicit support', path)
            if path not in sequences:
                metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(sequence))
                metadata['notifies'] = json.loads(unreal.AlsSourceAnimationLibrary.read_source_notify_metadata(sequence))
                metadata['curves'] = json.loads(unreal.AlsSourceAnimationLibrary.read_source_float_curves(sequence))
                sequences[path] = metadata
                packages.setdefault(path, sha(package_file(path)))
    for key in ('blendInCurve', 'blendOutCurve', 'blendInProfile', 'blendOutProfile'):
        if asset[key]:
            packages.setdefault(asset[key], sha(package_file(asset[key])))
protect()
source = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
probe = {p: sha(source / p) for p in ('Private/AlsLyraMontageLibrary.cpp', 'Public/AlsLyraMontageLibrary.h')}
for tree in ('source', 'package'):
    mirror = repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter'
    for p, digest in probe.items():
        assert sha(mirror / p) == digest, (tree, p)
result = dict(schemaVersion=1, dependencies={p: sha(root / p) for p in ('main_layer_graph_v1.json', 'linked_layer_contracts.json')},
              slots=slot_nodes, assets=assets, sequences=sequences, assetSha256=packages,
              previousFixtureSha256=previous, probeSourceSha256=probe,
              scope='Original Main-slot Montage metadata, source references, blend policies, sections, markers, curves and Notify events. No retarget, playback, effects dispatch or production claim.')
out = root / name
if out.exists():
    assert json.loads(out.read_bytes()) == result, 'Immutable Montage catalog differs'
else:
    out.write_text(json.dumps(result, separators=(',', ':')), encoding='utf-8')
unreal.log(f'LYRA_MONTAGE_CATALOG_OK montages={len(assets)} sequences={len(sequences)} slots={len(slot_nodes)} previous={len(previous)} assets_saved=0')
