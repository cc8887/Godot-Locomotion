"""Original five Slot nodes and ALS81 complete-data pose blending oracle."""
import hashlib
import json
import os
from pathlib import Path
import unreal

assert unreal.AlsLyraControlRigLibrary.prefer_raw_track_data_model(True)
root = Path(os.environ['LYRA_OUTPUT_ROOT'])
repo = Path(__file__).resolve().parents[2]
prefix = 'main_slots_v1'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
load = lambda p: json.loads((root / p).read_bytes())
catalog = load('montage_catalog_v2.json')
calibration = load('logical_controls/calibration.json')
inventory = load('montage_actions/inventory.json')
requests = load('montage_sampling_v1_requests.json')
requests.pop('mathCases')
requests.pop('boneNames')
requests['sequenceIndices'] = {row['source']: i for i,row in enumerate(inventory['rows'])}
graph = load('main_layer_graph_v1.json')
original = requests['traces']
requests['traces'] = []
for profile in ('unarmed','pistol','rifle'):
    for trace in original:
        t = json.loads(json.dumps(trace)); t['profile'] = profile
        t['class'] = graph['classes'][profile]['classPath']
        for fi,f in enumerate(t['frames']):
            k = fi // max(1,t['hz']//3)
            f.update(initialize=fi==0 or fi==t['hz']*2,visited=fi%127 not in (63,64,65),
                active=fi%89 not in (30,31),weight=(1,.5,.125)[k%3],rootModifier=(1,.25,0)[(k//3)%3],
                aimBlend=(0,.5,1)[(k//2)%3],dynamicWeight=(0,.25,1)[(k//4)%3])
        requests['traces'].append(t)
previous = {str(p.relative_to(root)).replace('\\','/'): sha(p) for p in root.rglob('*.json') if not p.name.startswith(prefix)}
if (root / (prefix + '_native.json')).exists():
    previous = load(prefix + '_native.json')['previousFixtureSha256']
packages = dict(load('montage_actions/catalog.json')['assetSha256'])
content = Path(unreal.Paths.project_content_dir())
def protect():
    for p,digest in previous.items(): assert sha(root / p) == digest,p
    for p,digest in packages.items():
        assert sha(content / (p.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest,p

def save(kind,value):
    path = root / (prefix + '_' + kind + '.json')
    if path.exists(): assert json.loads(path.read_bytes()) == value,'Immutable Slot capture differs: ' + kind
    else: path.write_text(json.dumps(value,separators=(',',':'),allow_nan=False),encoding='utf-8')
    return sha(path)
protect()
basis = calibration['calibration']
montages = [unreal.load_asset(a['path']) for a in catalog['assets']]
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

text = unreal.AlsLyraMainSlotsLibrary.read_trace(
    unreal.load_class(None,load('main_layer_graph_v1.json')['classes']['main']['classPath']),
    source_mesh,skeleton,montages,[sequences[r['source']] for r in rows],json.dumps(requests,separators=(',',':')))
assert text,'No original Slot output'
native = json.loads(text)
protect()
dependencies = {p: sha(root / p) for p in ('montage_catalog_v2.json','montage_blend_v1_policy.json',
    'montage_sampling_v1_policy.json','montage_sampling_v1_requests.json','logical_controls/calibration.json','montage_actions/catalog.json')}
source = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
probe = {p: sha(source / p) for p in ('Private/AlsLyraMainSlotsLibrary.cpp','Public/AlsLyraMainSlotsLibrary.h')}
for tree in ('source','package'):
    mirror = repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter'
    for p,digest in probe.items(): assert sha(mirror / p) == digest,(tree,p)
native.update(schemaVersion=1,requestSha256=save('requests',requests),dependencies=dependencies,
    assetSha256=packages,previousFixtureSha256=previous,probeSourceSha256=probe,
    scope='Actual original five Slots, two ApplyAdditive nodes, LayeredBoneBlend and three owner-scoped caches. Controlled Aiming branch weights and linked-layer leaves; Update-only oracle, not complete Main pose or production.')
save('native',native)
frame_count = sum(len(t['frames']) for t in native['traces'])
event_count = sum(len(f['events']) for t in native['traces'] for f in t['frames'])
unreal.log(f'LYRA_MAIN_SLOTS_NATIVE_OK frames={frame_count} events={event_count} assets_saved=0')
