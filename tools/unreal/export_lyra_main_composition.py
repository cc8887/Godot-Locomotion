"""Immutable native capture of Main composition operators on ALS81."""
import hashlib
import json
import os
import struct
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
repo = Path(__file__).resolve().parents[2]
prefix = 'main_composition_v2'
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
        assert json.loads(p.read_bytes()) == data, 'Immutable Main composition fixture differs: ' + kind
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
weights = [-.5, 0, 1e-6, 1e-5, 1.000001e-5, .1, .25, .65, .99999, 1, 1.25]
yaws = [0, .00009999, .0001, -.0001, 15.123456789, -63.33333333, 179.99999, -180, 359, 721.3]
for profile in ('unarmed', 'pistol', 'rifle'):
    for hz in (30, 60, 120):
        provider = resources['providers'][profile]
        jog = paths.index(provider['start']['Jog_Start_Cardinals']['forward'])
        ads = paths.index(provider['start']['ADS_Start_Cardinals']['forward'])
        additive = paths.index(recoveries[profile])
        frames = []
        for i in range(hz * 6):
            leaves = []
            for leaf, asset in enumerate((jog, ads, additive, additive, ads if i % 2 else jog)):
                # Both interval ends must stay inside the recovery clip. v1
                # retained a beyond-end previous time and hit UE's forward
                # root-extraction ensure; preserve those fixtures as evidence.
                time = f32(min(((i * (leaf + 1)) % 31) / hz, sequences[asset].get_play_length() * .8))
                leaves.append(dict(asset=asset, time=time, previous=f32(max(0, time - 1 / hz)),
                    sourceDelta=f32(min(time, 1 / hz)), distance=f32((leaf - 2) * 7.25 + i % 13 * .125), flags=(i + leaf) % 4))
            frames.append(dict(delta=f32(0 if i % 101 == 100 else 1 / hz),
                dynamicWeight=weights[(i // 7) % len(weights)], rootYaw=yaws[(i // 11) % len(yaws)],
                visited=not (.8 <= i / hz < 1.1), evaluate=i % 7 != 3,
                initialize=i == 0 or i % 83 == 0, weight=f32([1, .5, 1e-5, .001, .8][(i // 19) % 5]), leaves=leaves))
        requests['traces'].append(dict(profile=profile, hz=hz, frames=frames))
native = json.loads(unreal.AlsLyraMainCompositionLibrary.read_trace(unreal.load_class(None, graph['classes']['main']['classPath']),
    unreal.load_asset(basis['sourceMesh']), skeleton, sequences, json.dumps(requests, separators=(',', ':'))))
diagnostic = repo / 'artifacts/lyra-analysis/main-composition-native-diagnostic.json'
diagnostic.write_text(json.dumps(native, separators=(',', ':')), encoding='utf-8')
counts = dict(frames=0, poses=0, hidden=0, updateOnly=0)
policies = {}
for actual, trace in zip(native['traces'], requests['traces'], strict=True):
    policy = {k: actual[k] for k in ('mask', 'sourceMask', 'curveBindings')}
    assert len(policy['mask']) == 81 and policy['mask'][0] == 0
    if trace['profile'] in policies:
        assert policies[trace['profile']] == policy
    policies[trace['profile']] = policy
    for row, frame in zip(actual['frames'], trace['frames'], strict=True):
        counts['frames'] += 1
        counts['poses'] += 'upper' in row
        counts['hidden'] += not frame['visited']
        counts['updateOnly'] += frame['visited'] and not frame['evaluate']
        assert ('upper' in row) == ('final' in row) == (frame['visited'] and frame['evaluate'])
        if frame['visited']:
            assert row['recoveryAlpha'] == f32(.65)
            # Pins are authoritative; the Godot verifier independently checks
            # the expressions rather than baking native per-frame values.
            assert row['pitch'] == 0
protect()
probe_names = ('Private/AlsLyraMainCompositionLibrary.cpp', 'Public/AlsLyraMainCompositionLibrary.h',
               'Private/AlsLyraPoseProbe.h', 'Private/AlsLyraCyclePoseLibrary.cpp')
source = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
source_sha = {p: sha(source / p) for p in probe_names}
for tree in ('source', 'package'):
    mirror = repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter'
    for p, digest in source_sha.items():
        assert sha(mirror / p) == digest, (tree, p)
request_sha = save('requests', requests)
policy_sha = save('policy', dict(schemaVersion=1, dependencies=dependencies, policies=policies,
    stage='OriginalMainCompositionOperators', skeleton='ALS81', nodes=[0, 3, 76, 72]))
native.update(schemaVersion=1, dependencies=dependencies, requestSha256=request_sha, policySha256=policy_sha,
    assetSha256=packages, previousFixtureSha256=previous, probeSourceSha256=source_sha, counts=counts,
    scope='Original Main composition nodes 0/3 and 76/72 with real compiled handlers and name-mapped ALS81 mask. Controlled Slot/Aiming boundaries; no cache traversal, inertia, SkeletalControls, ControlRig or complete Main oracle.')
save('native', native)
unreal.log('LYRA_MAIN_COMPOSITION_NATIVE_OK frames=' + str(counts['frames']) + ' poses=' + str(counts['poses']) + ' previous=' + str(len(previous)) + ' assets_saved=0')
