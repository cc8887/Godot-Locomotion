"""Actual Start/Cycle Main roots, one Main update and native common Sync.

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
         'main_lean/behavior.json', 'main_lean/runtime_policies.json', 'main_lean/composition_v3_policy.json')
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
assets = list(start['assets'])
paths = [a['path'] for a in assets]
for asset in cycle['assets']:
    if asset['path'] not in paths:
        paths.append(asset['path']); assets.append(asset)
requests = copy.deepcopy(start_requests)
requests['sequencePaths'] = paths
for trace, c in zip(requests['traces'], cycle_requests['traces'], strict=True):
    if (trace['profile'], trace['hz'], trace['class']) != (c['profile'], c['hz'], c['class']):
        raise ValueError('Reordered original provider identity')
    trace['cycleNodeIndex'] = c['nodeIndex']; trace['cycleHipFireIndex'] = c['hipFireIndex']
    for key, value in c['bindings'].items():
        if key in trace['bindings'] and trace['bindings'][key] != value:
            raise ValueError('Conflicting original shared Linked binding: '+key)
        trace['bindings'][key] = value
    for i, frame in enumerate(trace['frames']):
        frame['observation'].pop('snapshot'); frame.pop('componentInput')
        t = i/trace['hz']
        # Two roots, asymmetric weights, reversed traversal, true empty scope,
        # separate reset and persistent source history are all intentional.
        active_start = t < 1.4 or 2.1 <= t < 2.8 or t >= 3.1
        active_cycle = .7 <= t < 2.8 or t >= 3.1
        frame['active'] = active_start
        frame['weight'] = 1.1 if t >= 5 else .75 if t < 2.1 else .25
        frame['reinitialize'] = i in (0, round(3.1*trace['hz']), round(4.2*trace['hz']))
        frame['cycle'] = {'active': active_cycle, 'weight': .35 if t < 2.1 else .85,
                          'reinitialize': i in (0, round(3.1*trace['hz']), round(3.8*trace['hz']))}
        frame['order'] = [2, 1] if int(t/.35) % 2 == 0 else [1, 2]

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
roots = json.loads(files['start_runtime_roots_v2.json'])
def codec_check():
    for p, seq in zip(paths, sequences, strict=True):
        if p in roots['assets'] and json.loads(unreal.AlsLyraGraphLibrary.read_root_compression_data(seq)) != roots['assets'][p]:
            raise ValueError('Changed original compressed Start root: '+p)
        if p in distance['assets']:
            data = json.loads(unreal.AlsLyraGraphLibrary.read_distance_sequence_data(seq, 'Distance'))
            data['path'] = p; data['markers'] = distance['assets'][p]['markers']
            if data != distance['assets'][p]: raise ValueError('Changed Start distance codec: '+p)
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
text = unreal.AlsLyraGraphLibrary.read_main_source_scope_trace(
    unreal.load_class(None, nodes['classes']['main']['class']), unreal.load_asset(basis['sourceMesh']), skeleton,
    sequences, unreal.load_asset(json.loads(files['main_lean/inventory.json'])['source']), lean,
    json.dumps(requests, separators=(',', ':')))
if not text: raise ValueError('Empty actual shared Main trace')
native = json.loads(text)
counts = {'frames': 0, 'startPoses': 0, 'cyclePoses': 0, 'both': 0, 'hidden': 0}
for trace, authored in zip(native['traces'], requests['traces'], strict=True):
    for row, frame in zip(trace['frames'], authored['frames'], strict=True):
        frame['observation']['snapshot'] = row['observation']['input']
        frame['componentInput'] = row['observation']['componentInput']
        a = 'output' in row; b = 'output' in row['cycle']
        if a != frame['active'] or b != frame['cycle']['active']:
            raise ValueError('Wrong native traversal relevance')
        counts['frames'] += 1; counts['startPoses'] += a; counts['cyclePoses'] += b
        counts['both'] += a and b; counts['hidden'] += not a and not b
if counts['frames'] != 3780 or not counts['both'] or not counts['hidden']:
    raise ValueError('Incomplete joint Main coverage: '+str(counts))
codec_check(); protect()
for p, seq in zip(paths, sequences, strict=True):
    if json.loads(unreal.AlsLyraGraphLibrary.read_root_compression_data(seq)) != root_definitions[p]:
        raise ValueError('Actual common root codec changed during traversal: '+p)
request_sha = save('main_source_scope_requests.json', requests)
save('main_source_scope_roots.json', {'schemaVersion':1, 'requestSha256':request_sha,
    'catalogSha256':sha(files['logical_controls/catalog.json']), 'assetSha256':packages, 'assets':root_definitions})
save('main_source_scope_native.json', {'schemaVersion': 1, 'requestSha256': request_sha,
    'dependencies': {n: sha(v) for n, v in files.items()}, 'assetSha256': packages,
    'assets': assets, 'traces': native['traces'], 'counts': counts,
    'scope': 'Actual Main Start13/Linked11/Lean12 and Cycle17/Linked15/Lean16 share one original complete Main update, registered Linked owner and Sync. Explicit root traversal order/weights/relevance; not LocomotionSM selection/blended output or production.'})
unreal.log('LYRA_MAIN_SOURCE_SCOPE_NATIVE_OK '+json.dumps(counts, separators=(',', ':'))+' packages=492 assets_saved=0')
