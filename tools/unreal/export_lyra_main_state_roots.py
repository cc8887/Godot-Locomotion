"""Actual Start/Cycle/Stop StateResult roots, one Main update and native common Sync.

Weights/relevance/order remain explicit state-machine boundaries. This capture
does not replace the original source callbacks with hand-written calculations.
"""
import copy
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
sha = lambda b: hashlib.sha256(b).hexdigest()
names = ('main_start_lean_requests.json', 'main_start_lean_native.json',
         'main_cycle_lean_requests.json', 'main_cycle_lean_native.json',
         'start_runtime_distance.json', 'start_runtime_roots_v2.json',
         'start_layer_graph.json', 'cycle_layer_graph.json', 'cycle_source_definitions.json',
         'logical_controls/catalog.json', 'logical_controls/calibration.json',
         'linked_layer_inventory.json', 'source_nodes.json', 'cycle_layer_pose_policy.json',
         'root_motion_policy.json', 'orientation_policy.json', 'stride_policy.json',
         'main_update_policy.json', 'main_lean/catalog.json', 'main_lean/inventory.json',
         'main_lean/behavior.json', 'main_lean/runtime_policies.json', 'main_lean/composition_v3_policy.json',
         'main_source_scope_requests.json', 'main_source_scope_native.json', 'main_source_scope_roots.json',
         'main_stop_runtime_requests.json', 'main_stop_runtime_native.json', 'stop_runtime_distance.json',
         'stop_runtime_roots.json', 'stop_layer_graph.json', 'linked_layer_contracts.json', 'runtime_graph.json',
         'main_source_stop_requests.json', 'main_source_stop_native.json', 'main_source_stop_roots.json')
files = {n: (root/n).read_bytes() for n in names}
start = json.loads(files['main_start_lean_native.json'])
cycle = json.loads(files['main_cycle_lean_native.json'])
start_requests = json.loads(files['main_start_lean_requests.json'])
cycle_requests = json.loads(files['main_cycle_lean_requests.json'])
packages = start['assetSha256']
if cycle['assetSha256'] != packages:
    raise ValueError('Different original Main source packages')
content = Path(unreal.Paths.project_content_dir())

def protect():
    for p, h in packages.items():
        if sha((content/(p.split('.')[0].removeprefix('/Game/')+'.uasset')).read_bytes()) != h:
            raise ValueError('Changed Main scope package: '+p)

def save(n, value):
    p = root/n
    if p.exists():
        if json.loads(p.read_bytes()) != value:
            raise ValueError('Immutable Main scope fixture differs: '+n)
    else:
        p.write_text(json.dumps(value, separators=(',', ':')), encoding='utf-8')
    return sha(p.read_bytes())

protect()
prior = json.loads(files['main_source_scope_native.json'])
stop = json.loads(files['main_stop_runtime_native.json'])
if stop['assetSha256'] != packages: raise ValueError('Different Stop package provenance')
assets = list(prior['assets']); paths = [a['path'] for a in assets]
for asset in stop['assets']:
    if asset['path'] not in paths: paths.append(asset['path']); assets.append(asset)
requests=copy.deepcopy(json.loads(files['main_source_stop_requests.json']))
for trace in requests['traces']:
    for index,frame in enumerate(trace['frames']):
        frame['observation'].pop('snapshot'); frame.pop('componentInput')
        t=index/trace['hz']; segment=int(t/.2)
        current=1+int(t/.35)%3
        context={'current':current,'previousStartWeight':0.0 if segment%4==0 else .65,
            'previousCycleWeight':0.0 if segment%5==0 else .8,
            'previousStopWeight':0.0 if segment%3==0 else .45}
        frame['stateRoots']=context
        frame['stop']['machineCurrent']=current
        frame['stop']['previousStopWeight']=context['previousStopWeight']

catalog = json.loads(files['logical_controls/catalog.json'])
basis = json.loads(files['logical_controls/calibration.json'])['calibration']
rotation = unreal.Quat(*basis['handBasis']['rotation'])
skeleton = unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(
    unreal.load_asset(basis['sourceMesh']).get_editor_property('skeleton'),
    unreal.load_asset(basis['targetMesh']).get_editor_property('skeleton'), rotation)
entries = {e['target']: e for e in catalog['entries']}
sequences = [unreal.AlsLyraControlRigLibrary.create_weapon_sequence(unreal.load_asset(entries[p]['source']),
             unreal.load_asset(p), skeleton, rotation, None) for p in paths]
if any(s is None for s in sequences):
    raise ValueError('Missing shared Main transient source')
distance = json.loads(files['start_runtime_distance.json'])
stop_distance = json.loads(files['stop_runtime_distance.json'])
root_inventory = json.loads(files['main_source_scope_roots.json'])['assets']
for path, data in json.loads(files['stop_runtime_roots.json'])['assets'].items():
    if path in root_inventory and root_inventory[path] != data: raise ValueError('Different shared HipFire root codec')
    root_inventory[path] = data

def codec_check():
    for p, seq in zip(paths, sequences, strict=True):
        if json.loads(unreal.AlsLyraGraphLibrary.read_root_compression_data(seq)) != root_inventory[p]:
            raise ValueError('Changed actual compressed common root: '+p)
        original = distance['assets'].get(p, stop_distance['assets'].get(p))
        if original is not None:
            data = json.loads(unreal.AlsLyraGraphLibrary.read_distance_sequence_data(seq, 'Distance'))
            data['path'] = p; data['markers'] = original['markers']
            if data != original: raise ValueError('Changed Start/Stop distance codec: '+p)
codec_check()
root_definitions = {}
for p, seq in zip(paths, sequences, strict=True):
    text = unreal.AlsLyraGraphLibrary.read_root_compression_data(seq)
    if not text: raise ValueError('Missing actual common root codec: '+p)
    root_definitions[p] = json.loads(text)
lean = []
for e in json.loads(files['main_lean/catalog.json'])['entries']:
    seq = unreal.AlsLyraControlRigLibrary.create_weapon_sequence(unreal.load_asset(e['source']),
        unreal.load_asset(e['target']), skeleton, rotation, lean[0] if lean else None)
    if seq is None: raise ValueError('Missing shared Main Lean source')
    lean.append(seq)
nodes = json.loads(files['source_nodes.json'])
text = unreal.AlsLyraGraphLibrary.read_main_state_roots_trace(
    unreal.load_class(None, nodes['classes']['main']['class']), unreal.load_asset(basis['sourceMesh']), skeleton,
    sequences, unreal.load_asset(json.loads(files['main_lean/inventory.json'])['source']), lean,
    json.dumps(requests, separators=(',', ':')))
if not text: raise ValueError('Empty actual shared Main trace')
native = json.loads(text)
counts = {'frames': 0, 'startPoses': 0, 'cyclePoses': 0, 'stopPoses': 0, 'both': 0, 'allThree': 0, 'stopOnly': 0, 'hidden': 0}
for trace, authored in zip(native['traces'], requests['traces'], strict=True):
    for row, frame in zip(trace['frames'], authored['frames'], strict=True):
        frame['observation']['snapshot'] = row['observation']['input']
        frame['observation']['mode'] = row['observation']['tailBefore']['mode']
        frame['componentInput'] = row['observation']['componentInput']
        a = 'output' in row; b = 'output' in row['cycle']; c = 'output' in row['stop']
        if a != frame['active'] or b != frame['cycle']['active'] or c != frame['stop']['active']:
            raise ValueError('Wrong native traversal relevance')
        counts['frames'] += 1; counts['startPoses'] += a; counts['cyclePoses'] += b
        counts['stopPoses'] += c; counts['both'] += a and b; counts['allThree'] += a and b and c
        counts['stopOnly'] += c and not a and not b; counts['hidden'] += not a and not b and not c
if counts['frames'] != 3780 or not counts['allThree'] or not counts['stopOnly'] or not counts['hidden']:
    raise ValueError('Incomplete joint Main coverage: '+str(counts))
codec_check(); protect()
for p, seq in zip(paths, sequences, strict=True):
    if json.loads(unreal.AlsLyraGraphLibrary.read_root_compression_data(seq)) != root_definitions[p]:
        raise ValueError('Actual common root codec changed during traversal: '+p)
request_sha = save('main_state_roots_requests.json', requests)
save('main_state_roots_roots.json', {'schemaVersion':1, 'requestSha256':request_sha,
    'catalogSha256':sha(files['logical_controls/catalog.json']), 'assetSha256':packages, 'assets':root_definitions})
save('main_state_roots_native.json', {'schemaVersion': 1, 'requestSha256': request_sha,
    'dependencies': {n: sha(v) for n, v in files.items()}, 'assetSha256': packages,
    'assets': assets, 'traces': native['traces'], 'counts': counts,
    'scope': 'Actual Main StateResult10 Start/Hold, StateResult14 Cycle and StateResult18 Stop/Accumulate with their unchanged children share one Main update, Linked owner and Sync. Previous weights, current state and traversal are explicit observations; not full machine selection/final blend or production.'})
unreal.log('LYRA_MAIN_STATE_ROOTS_NATIVE_OK '+json.dumps(counts, separators=(',', ':'))+' packages=492 assets_saved=0')
