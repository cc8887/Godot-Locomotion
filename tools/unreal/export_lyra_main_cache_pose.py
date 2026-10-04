"""Immutable original cache pose capture through Engine evaluation on ALS81."""
import hashlib
import json
import os
import struct
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
repo = Path(__file__).resolve().parents[2]
prefix = 'main_cache_pose_v1'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
f32 = lambda v: struct.unpack('<f', struct.pack('<f', v))[0]
graph = json.loads((root / 'main_layer_graph_v1.json').read_bytes())
packages = graph['assetSha256']
previous = {str(p.relative_to(root)).replace('\\', '/'): sha(p) for p in root.rglob('*.json') if not p.name.startswith(prefix)}
if (root / (prefix + '_native.json')).exists():
    previous = json.loads((root / (prefix + '_native.json')).read_bytes())['previousFixtureSha256']
content = Path(unreal.Paths.project_content_dir())

def protect():
    for name, digest in packages.items():
        assert sha(content / (name.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, name
    for name, digest in previous.items():
        assert sha(root / name) == digest, name

def save(kind, data):
    p = root / (prefix + '_' + kind + '.json')
    if p.exists():
        assert json.loads(p.read_bytes()) == data, 'Immutable Main cache pose fixture differs: ' + kind
    else:
        p.write_text(json.dumps(data, separators=(',', ':')), encoding='utf-8')
    return sha(p)

protect()
names = ('main_layer_graph_v1.json', 'logical_controls/calibration.json', 'logical_controls/catalog.json',
         'locomotion_extras/catalog.json', 'locomotion_resources.json', 'cycle_layer_pose_policy.json')
dependencies = {p: sha(root / p) for p in names}
resources = json.loads((root / 'locomotion_resources.json').read_bytes())
entries = json.loads((root / 'logical_controls/catalog.json').read_bytes())['entries'] + json.loads((root / 'locomotion_extras/catalog.json').read_bytes())['entries']
lookup = {e['target']: e for e in entries}
paths = list(dict.fromkeys(resources['providers'][p]['start'][k]['forward']
    for p in ('unarmed', 'pistol', 'rifle') for k in ('Jog_Start_Cardinals', 'ADS_Start_Cardinals')))
recoveries = {e['profile']: e['target'] for e in entries if e.get('category') == 'locomotion_extras' and e['additive']}
paths += list(recoveries.values())
basis = json.loads((root / 'logical_controls/calibration.json').read_bytes())['calibration']
q = unreal.Quat(*basis['handBasis']['rotation'])
skeleton = unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(unreal.load_asset(basis['sourceMesh']).get_editor_property('skeleton'),
    unreal.load_asset(basis['targetMesh']).get_editor_property('skeleton'), q)
sequences = [unreal.AlsLyraControlRigLibrary.create_weapon_sequence(unreal.load_asset(lookup[p]['source']),
    unreal.load_asset(p), skeleton, q, None) for p in paths]
for p, sequence in zip(paths, sequences, strict=True):
    assert sequence is not None, p
    unreal.AlsSourceAnimationLibrary.finish_source_compression(sequence)


requests = dict(sequencePaths=paths, traces=[])
for profile in ('unarmed', 'pistol', 'rifle'):
    provider = resources['providers'][profile]
    assets = [paths.index(provider['start'][kind]['forward']) for kind in ('Jog_Start_Cardinals', 'ADS_Start_Cardinals')]
    assets.append(paths.index(recoveries[profile]))
    for hz in (30, 60, 120):
        frames = []
        for i in range(hz * 2):
            asset = assets[(i // 13) % 3]
            inputs = []
            for pass_index in range(2):
                time = f32(min(((i * 3 + pass_index * 11) % 31) / hz, sequences[asset].get_play_length() * .8))
                inputs.append(dict(time=time, previous=f32(max(0, time - 1 / hz)), sourceDelta=f32(min(time, 1 / hz)),
                                   distance=f32(i % 13 * .125 + pass_index * 7.25), flags=(i + pass_index) % 4))
            frames.append(dict(asset=asset, evaluate=i % 7 != 3, inputs=inputs))
        requests['traces'].append(dict(profile=profile, hz=hz, **{'class': graph['classes'][profile]['classPath']}, frames=frames))
text = unreal.AlsLyraMainCachePoseLibrary.read_trace(unreal.load_class(None, graph['classes']['main']['classPath']),
    unreal.load_asset(basis['sourceMesh']), skeleton, sequences, json.dumps(requests, separators=(',', ':')))
assert text, 'Main cache pose probe returned no data'
native = json.loads(text)
counts = dict(frames=0, poses=0, evaluations=0)
for trace, request in zip(native['traces'], requests['traces'], strict=True):
    for row, frame in zip(trace['frames'], request['frames'], strict=True):
        counts['frames'] += 1
        assert row['evaluations'] == (2 if frame['evaluate'] else 0)
        assert len(row['outputs']) == (4 if frame['evaluate'] else 0)
        if frame['evaluate']:
            assert row['outputs'][0] == row['outputs'][1]
            assert row['outputs'][2] == row['outputs'][3]
            assert row['outputs'][0] != row['outputs'][2]
        counts['poses'] += len(row['outputs'])
        counts['evaluations'] += row['evaluations']
assert counts['frames'] == 1260
protect()
source = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
source_sha = {p: sha(source / p) for p in ('Private/AlsLyraMainCachePoseLibrary.cpp', 'Public/AlsLyraMainCachePoseLibrary.h',
                                         'Private/AlsLyraPoseProbe.h', 'Private/AlsLyraCyclePoseLibrary.cpp')}
for tree in ('source', 'package'):
    mirror = repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter'
    for p, digest in source_sha.items():
        assert sha(mirror / p) == digest, (tree, p)
native.update(schemaVersion=1, dependencies=dependencies, requestSha256=save('requests', requests),
              assetSha256=packages, previousFixtureSha256=previous, probeSourceSha256=source_sha, counts=counts,
              scope='Actual Main Locomotion83/Split78 and provider Aiming BasePose78 cache evaluation on ALS81. Controlled sequence and passthrough input boundaries; no full Main, initialization history, active Montage, inertia or final ControlRig.')
save('native', native)
unreal.log('LYRA_MAIN_CACHE_POSE_NATIVE_OK frames=' + str(counts['frames']) + ' poses=' + str(counts['poses']) + ' previous=' + str(len(previous)) + ' assets_saved=0')
