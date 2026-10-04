"""Original Main + ten roots + native Sync and pose on immutable ALS81 sources."""
import copy
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
repo = Path(__file__).resolve().parents[2]
sha = lambda b: hashlib.sha256(b).hexdigest()
prefix = 'main_als_locomotion_v1'
own = {prefix + s + '.json' for s in ('_requests', '_native')}
names = ('locomotion_resources.json', 'main_machine_runtime_v2_requests.json',
         'main_ground_scope_v2_requests.json', 'logical_controls/catalog.json',
         'locomotion_extras/catalog.json', 'logical_controls/calibration.json',
         'main_lean/catalog.json', 'main_lean/inventory.json', 'source_nodes.json',
         'runtime_graph.json', 'main_update_policy.json', 'root_motion_policy.json')
files = {n: (root / n).read_bytes() for n in names}
resources = json.loads(files['locomotion_resources.json'])
baseline = json.loads((repo / 'artifacts/lyra-analysis/main-idle-root-native.json').read_bytes())
packages = baseline['packages']
previous = {str(p.relative_to(root)).replace('\\', '/'): sha(p.read_bytes()) for p in root.rglob('*.json') if p.name not in own}
if (root / (prefix + '_native.json')).exists():
    previous = json.loads((root / (prefix + '_native.json')).read_bytes())['previousFixtureSha256']
content = Path(unreal.Paths.project_content_dir())

def protect():
    for p, digest in packages.items():
        assert sha((content / (p.split('.')[0].removeprefix('/Game/') + '.uasset')).read_bytes()) == digest, p
    for n, digest in previous.items():
        assert sha((root / n).read_bytes()) == digest, n

def save(name, value):
    p = root / (prefix + '_' + name + '.json')
    if p.exists():
        assert json.loads(p.read_bytes()) == value, 'Immutable full Main fixture differs: ' + name
    else:
        p.write_text(json.dumps(value, separators=(',', ':')), encoding='utf-8')
    return sha(p.read_bytes())

protect()
entries = json.loads(files['logical_controls/catalog.json'])['entries'] + json.loads(files['locomotion_extras/catalog.json'])['entries']
lookup = {e['target']: e for e in entries}
assets = resources['assets']
paths = [a['path'] for a in assets]
assert len(paths) == 194 and len(set(paths)) == 194
basis = json.loads(files['logical_controls/calibration.json'])['calibration']
rotation = unreal.Quat(*basis['handBasis']['rotation'])
skeleton = unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(
    unreal.load_asset(basis['sourceMesh']).get_editor_property('skeleton'),
    unreal.load_asset(basis['targetMesh']).get_editor_property('skeleton'), rotation)
sequences = [unreal.AlsLyraControlRigLibrary.create_weapon_sequence(
    unreal.load_asset(lookup[p]['source']), unreal.load_asset(p), skeleton, rotation, None) for p in paths]
assert all(sequences), 'Missing Main ALS transient clip'
def inventory():
    for p, asset, sequence in zip(paths, assets, sequences, strict=True):
        m = json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(sequence))
        assert m['length'] == asset['length'] and m['rateScale'] == asset['rateScale'], p
        assert m['markers'] == asset['markers'], ('markers', p)
        assert json.loads(unreal.AlsLyraGraphLibrary.read_root_compression_data(sequence)) == resources['compressedRoots']['assets'][p], ('root', p)
inventory()
lean = []
for e in json.loads(files['main_lean/catalog.json'])['entries']:
    s = unreal.AlsLyraControlRigLibrary.create_weapon_sequence(unreal.load_asset(e['source']),
        unreal.load_asset(e['target']), skeleton, rotation, lean[0] if lean else None)
    assert s, e['slot']
    lean.append(s)

requests = copy.deepcopy(json.loads(files['main_machine_runtime_v2_requests.json']))
requests['sequencePaths'] = paths
ground = json.loads(files['main_ground_scope_v2_requests.json'])['traces']
for trace in requests['traces']:
    profile = trace['profile']
    provider = resources['providers'][profile]
    bindings = copy.deepcopy(next(t['bindings'] for t in ground if t['profile'] == profile))
    for key in ('start', 'stop', 'pivot', 'air', 'idle'):
        for name, value in provider[key].items():
            if name in bindings:
                assert bindings[name] == value, (profile, name)
            bindings[name] = value
    trace.update(bindings=bindings, breaks=provider['idleBreaks'], case='movement')
    for i, frame in enumerate(trace['frames']):
        frame['providerClass'] = trace['class']
        frame['hipWeight'] = .65
        frame['evaluate'] = i % 7 != 0
        frame['observation'].pop('snapshot', None)
        frame['observation']['rotation'] = [0, 0, 0]
turns = []
for seed in requests['traces']:
    trace = {k: copy.deepcopy(v) for k, v in seed.items() if k != 'frames'}
    hz = trace['hz']
    trace['case'] = 'turn'
    trace['frames'] = []
    for i in range(hz * 6):
        t = i / hz
        moving = 1.2 <= t < 2
        x = 0 if t < 1.2 else (t - 1.2) * 180 if t < 2 else 144
        yaw = min(t, 1.2) * 80 if t < 2 else 96 - (t - 2) * 90
        observation = copy.deepcopy(seed['frames'][0]['observation'])
        observation.update(location=[x, 0, 0], rotation=[0, yaw, 0], velocity=[180 if moving else 0, 0, 0],
            acceleration=[800 if moving else 0, 0, 0], movementMode=1, crouching=False, ads=False,
            firing=False, montage=False, delta=1 / hz, aimPitch=0, dashing=False, enabled=True)
        trace['frames'].append(dict(delta=1 / hz, observation=observation, providerClass=trace['class'],
            groundDistance=0, active=True, contextActive=True, weight=1, reinitialize=i == 0, hipWeight=.65, evaluate=i % 11 != 0))
    turns.append(trace)
requests['traces'].extend(turns)
nodes = json.loads(files['source_nodes.json'])
text = unreal.AlsLyraMainLocomotionLibrary.read_trace(
    unreal.load_class(None, nodes['classes']['main']['class']), unreal.load_asset(basis['sourceMesh']),
    skeleton, sequences, unreal.load_asset(json.loads(files['main_lean/inventory.json'])['source']), lean,
    json.dumps(requests, separators=(',', ':')))
assert text, 'Empty original Main ALS81 trace'
native = json.loads(text)
counts = dict(frames=0, poses=0, rootPoses=0, hidden=0, updateOnly=0)
states = set()
for trace, authored in zip(native['traces'], requests['traces'], strict=True):
    trace['case'] = authored['case']
    assert len(trace['frames']) == len(authored['frames'])
    for row, frame in zip(trace['frames'], authored['frames'], strict=True):
        frame['observation']['snapshot'] = row['observation']['input']
        frame['observation']['mode'] = row['observation']['tailBefore']['mode']
        frame['componentInput'] = row['observation']['componentInput']
        frame['movement'] = row['observation']['movement']
        frame['relativeRotation'] = [0, 0, 0, 1]
        counts['frames'] += 1
        counts['poses'] += 'output' in row
        counts['rootPoses'] += len(row['evaluatedRoots'])
        counts['hidden'] += not frame['active']
        counts['updateOnly'] += not frame['active'] or not frame['evaluate']
        states.add(row['state'])
assert counts['frames'] == 11340 and counts['poses'] == 9762 and len(states) == 10, counts
inventory()
protect()
probe_root = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
probe_names = ('Private/AlsLyraMainLocomotionLibrary.cpp', 'Public/AlsLyraMainLocomotionLibrary.h',
               'Private/AlsLyraMainObservationLibrary.cpp', 'Private/AlsLyraMainObservationProbe.h', 'Private/AlsLyraPoseProbe.h')
source_sha = {p: sha((probe_root / p).read_bytes()) for p in probe_names}
for tree in ('source', 'package'):
    compiled = repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter'
    for p, digest in source_sha.items():
        assert sha((compiled / p).read_bytes()) == digest, (tree, p)
request_sha = save('requests', requests)
native.update(schemaVersion=1, requestSha256=request_sha, dependencies={n: sha(b) for n, b in files.items()},
    assetSha256=packages, previousFixtureSha256=previous, probeSourceSha256=source_sha, counts=counts,
    states=sorted(states), skeleton='ALS81', outputBoundary='LocomotionSM', curveCopy='Native CopyCurveValues Main to Linked',
    fixedProvider=True, wholeMain=False, production=False)
save('native', native)
print('LYRA_MAIN_ALS_NATIVE_OK frames=11340 poses=9762 rootPoses=' + str(counts['rootPoses']) +
    ' states=10 packages=508 previous=' + str(len(previous)) + ' assets_saved=0')
