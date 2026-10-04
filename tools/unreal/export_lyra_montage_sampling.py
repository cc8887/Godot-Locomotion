"""Read-only physical Montage intervals and ALS81 real track extraction."""
import hashlib
import json
import os
from pathlib import Path
import unreal

assert unreal.AlsLyraControlRigLibrary.prefer_raw_track_data_model(True)
root = Path(os.environ['LYRA_OUTPUT_ROOT'])
repo = Path(__file__).resolve().parents[2]
prefix = 'montage_sampling_v1'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
load = lambda p: json.loads((root / p).read_bytes())
catalog = load('montage_catalog_v2.json')
calibration = load('logical_controls/calibration.json')
inventory = load('montage_actions/inventory.json')
requests = load('montage_blend_v1_requests.json')
requests['mathCases'] = []
for hz in (30, 60, 120):
    frames = []
    for i, asset in enumerate(catalog['assets']):
        for rate in (1, -1, .01):
            start = asset['duration'] - .001 if rate < 0 else 0
            play = dict(asset=i, stop=False, rate=rate, start=start, stopGroup=True)
            frames.extend([dict(delta=1/hz, commands=[play]), dict(delta=.037, commands=[]),
                dict(delta=0, commands=[]), dict(delta=asset['duration'] / abs(rate) * 2, commands=[]),
                dict(delta=0, commands=[]), dict(delta=1/hz, commands=[])])
    requests['traces'].append(dict(hz=hz, frames=frames))
previous = {str(p.relative_to(root)).replace('\\', '/'): sha(p) for p in root.rglob('*.json') if not p.name.startswith(prefix)}
if (root / (prefix + '_native.json')).exists():
    previous = load(prefix + '_native.json')['previousFixtureSha256']
packages = dict(load('montage_actions/catalog.json')['assetSha256'])
content = Path(unreal.Paths.project_content_dir())
def protect():
    for p, digest in previous.items():
        assert sha(root / p) == digest, p
    for p, digest in packages.items():
        assert sha(content / (p.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, p

def save(kind, value):
    path = root / (prefix + '_' + kind + '.json')
    if path.exists():
        assert json.loads(path.read_bytes()) == value, 'Immutable sampling capture differs: ' + kind
    else:
        path.write_text(json.dumps(value, separators=(',', ':'), allow_nan=False), encoding='utf-8')
    return sha(path)

protect()
basis = calibration['calibration']
montages = [unreal.load_asset(a['path']) for a in catalog['assets']]
text = unreal.AlsLyraMontageSamplingLibrary.read_time_trace(
    unreal.load_class(None, load('main_layer_graph_v1.json')['classes']['main']['classPath']),
    unreal.load_asset(basis['sourceMesh']), montages, json.dumps(requests, separators=(',', ':')))
assert text, 'No original time trace'
native = json.loads(text)
hand = unreal.Quat(*basis['handBasis']['rotation'])
source_mesh = unreal.load_asset(basis['sourceMesh'])
target_mesh = unreal.load_asset(basis['targetMesh'])
skeleton = unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(source_mesh.get_editor_property('skeleton'),
    target_mesh.get_editor_property('skeleton'), hand)
sequences = {}
for p, row in inventory['externalBases'].items():
    sequences[p] = unreal.AlsLyraControlRigLibrary.create_weapon_sequence(unreal.load_asset(p),
        unreal.load_asset(row['target']), skeleton, hand, None)
world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
unreal.SystemLibrary.execute_console_command(world, 'Editor.AsyncAssetCompilation 2')
roots = {}
rows = inventory['rows']
for row in rows:
    p = row['source']; base = catalog['sequences'][p]['baseAsset']
    assert not base or base == p or base in sequences
    sequences[p] = unreal.AlsLyraControlRigLibrary.create_weapon_sequence(unreal.load_asset(p),
        unreal.load_asset(row['target']), skeleton, hand, sequences.get(base) if base != p else None)
    assert sequences[p] is not None
    unreal.AlsSourceAnimationLibrary.finish_source_compression(sequences[p])
    data = json.loads(unreal.AlsLyraGraphLibrary.read_root_compression_data(sequences[p]))
    assert data == json.loads(unreal.AlsLyraGraphLibrary.read_root_compression_data(sequences[p]))
    roots[row['target']] = data
    unreal.log('LYRA_MONTAGE_SAMPLING_ROOT_OK ' + row['slot'])
indices = {row['source']: i for i, row in enumerate(rows)}
samples = []
for ti, trace in enumerate(native['traces']):
    hz = trace['hz']
    for fi, frame in enumerate(trace['frames']):
        for frozen in frame['frozen']:
            if fi % max(1, hz//5) and not requests['traces'][ti]['frames'][fi]['commands'] and frozen['delta'] != 0:
                continue
            asset = catalog['assets'][frozen['asset']]
            for track in range(len(asset['slots'])):
                samples.append(dict(asset=frozen['asset'], track=track, position=frozen['position'],
                    previous=frozen['previous'], delta=frozen['delta'], extract=asset['rootMotion'], trace=ti, frame=fi))
# Each original segment plus both extraction flags, including zero, partial and full ranges.
for ai, asset in enumerate(catalog['assets']):
    for track in range(len(asset['slots'])):
        for fraction in (0, .123456789, .5, 1):
            for extract in (False, True):
                position = asset['duration'] * fraction
                samples.append(dict(asset=ai, track=track, position=position,
                    previous=max(position - .037, 0), delta=min(position, .037), extract=extract, trace=-1, frame=-1))
track_requests = dict(sequenceIndices=indices, samples=samples)
text = unreal.AlsLyraMontageSamplingLibrary.read_track_samples(skeleton, montages,
    [sequences[r['source']] for r in rows], json.dumps(track_requests, separators=(',', ':')))
assert text, 'No original track extraction'
tracks = json.loads(text)
assert len(tracks['rows']) == len(samples)
protect()
dependencies = {p: sha(root / p) for p in ('montage_catalog_v2.json', 'montage_blend_v1_requests.json',
    'logical_controls/calibration.json', 'montage_actions/catalog.json', 'montage_actions/inventory.json')}
source = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
probe = {p: sha(source / p) for p in ('Private/AlsLyraMontageSamplingLibrary.cpp', 'Public/AlsLyraMontageSamplingLibrary.h')}
for tree in ('source', 'package'):
    mirror = repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter'
    for p, digest in probe.items():
        assert sha(mirror / p) == digest, (tree, p)
root_sha = save('roots', dict(schemaVersion=1, assets=roots))
save('policy', dict(schemaVersion=1, dependencies=dependencies, rootSha256=root_sha,
    montagePaths=[a['path'] for a in catalog['assets']], sequencePaths=[r['source'] for r in rows],
    sourceLayout='ALS81', curveCombine='MontageOverridesSequence', rootExtraction='CompressedSequenceTrack'))
native.update(schemaVersion=1, requestSha256=save('requests', requests), dependencies=dependencies,
    assetSha256=packages, previousFixtureSha256=previous, probeSourceSha256=probe,
    trackRequestSha256=save('track_requests', track_requests), trackSha256=save('tracks', tracks),
    rootSha256=save('roots', dict(schemaVersion=1, assets=roots)),
    scope='Physical Montage DeltaTimeRecord and ALS81 original track pose/curves/attributes/root extraction. Full Slot mixing remains open.')
save('native', native)
unreal.log(f'LYRA_MONTAGE_SAMPLING_NATIVE_OK frames={sum(len(t["frames"]) for t in native["traces"])} samples={len(samples)} roots={len(roots)} assets_saved=0')
