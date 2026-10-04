"""Original Main Start ApplyAdditive -> real Linked Start and Lean, one Sync."""
import copy
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
sha = lambda data: hashlib.sha256(data).hexdigest()
names = ('start_layer_graph.json', 'start_runtime_requests.json', 'start_runtime_distance.json',
    'start_runtime_roots_v2.json', 'start_runtime_native_v3.json', 'logical_controls/catalog.json',
    'logical_controls/calibration.json', 'linked_layer_inventory.json', 'source_nodes.json',
    'cycle_layer_pose_policy.json', 'root_motion_policy.json', 'orientation_policy.json', 'stride_policy.json',
    'main_update_requests.json', 'main_update_policy.json', 'main_lean/catalog.json', 'main_lean/inventory.json',
    'main_lean/behavior.json', 'main_lean/runtime_policies.json', 'main_lean/composition_v3_policy.json')
files = {name: (root/name).read_bytes() for name in names}
prior = json.loads(files['start_runtime_native_v3.json'])
requests = copy.deepcopy(json.loads(files['start_runtime_requests.json']))
catalog = json.loads(files['logical_controls/catalog.json'])
calibration = json.loads(files['logical_controls/calibration.json'])
nodes = json.loads(files['source_nodes.json'])
packages = prior['assetSha256']; content = Path(unreal.Paths.project_content_dir())

def protect():
    for path, digest in packages.items():
        if sha((content/(path.split('.')[0].removeprefix('/Game/')+'.uasset')).read_bytes()) != digest:
            raise ValueError('Changed Main Start package: '+path)

def save(name, data):
    path = root/name
    if path.exists():
        if json.loads(path.read_bytes()) != data: raise ValueError('Immutable Main Start fixture differs: '+name)
    else: path.write_text(json.dumps(data, separators=(',', ':')), encoding='utf-8')
    return sha(path.read_bytes())

protect()
observations = {t['hz']: t['frames'] for t in json.loads(files['main_update_requests.json'])['traces']}
for trace in requests['traces']:
    for index, frame in enumerate(trace['frames']):
        frame.pop('main')
        frame['observation'] = copy.deepcopy(observations[trace['hz']][index])
        frame['observation'].pop('snapshot')
        frame['delta'] = frame['observation']['delta']
basis = calibration['calibration']; rotation = unreal.Quat(*basis['handBasis']['rotation'])
skeleton = unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(
    unreal.load_asset(basis['sourceMesh']).get_editor_property('skeleton'),
    unreal.load_asset(basis['targetMesh']).get_editor_property('skeleton'), rotation)
paths = requests['sequencePaths']; entries = {e['target']: e for e in catalog['entries']}
sequences = [unreal.AlsLyraControlRigLibrary.create_weapon_sequence(unreal.load_asset(entries[p]['source']),
             unreal.load_asset(p), skeleton, rotation, None) for p in paths]
if any(s is None for s in sequences): raise ValueError('Missing original Start transient source')
distance = json.loads(files['start_runtime_distance.json'])
roots = json.loads(files['start_runtime_roots_v2.json'])
def codec_check():
    for path, sequence in zip(paths, sequences, strict=True):
        if json.loads(unreal.AlsLyraGraphLibrary.read_root_compression_data(sequence)) != roots['assets'][path]:
            raise ValueError('Changed original Start compressed root: '+path)
        if path in distance['assets']:
            data = json.loads(unreal.AlsLyraGraphLibrary.read_distance_sequence_data(sequence, 'Distance'))
            data['path'] = path; data['markers'] = distance['assets'][path]['markers']
            if data != distance['assets'][path]: raise ValueError('Changed original Start distance codec: '+path)
codec_check()
lean_sequences = []
for entry in json.loads(files['main_lean/catalog.json'])['entries']:
    sequence = unreal.AlsLyraControlRigLibrary.create_weapon_sequence(unreal.load_asset(entry['source']),
        unreal.load_asset(entry['target']), skeleton, rotation, lean_sequences[0] if lean_sequences else None)
    if sequence is None: raise ValueError('Missing Main Lean transient')
    lean_sequences.append(sequence)
text = unreal.AlsLyraGraphLibrary.read_main_start_lean_trace(
    unreal.load_class(None, nodes['classes']['main']['class']), unreal.load_asset(basis['sourceMesh']), skeleton,
    sequences, unreal.load_asset(json.loads(files['main_lean/inventory.json'])['source']), lean_sequences,
    json.dumps(requests, separators=(',', ':')))
if not text: raise ValueError('Empty original Main Start root trace')
native = json.loads(text)
if len(native['traces']) != 9 or sum('output' in r for t in native['traces'] for r in t['frames']) != 3672:
    raise ValueError('Incomplete original Main Start root trace')
for trace, authored in zip(native['traces'], requests['traces'], strict=True):
    for row, frame in zip(trace['frames'], authored['frames'], strict=True):
        frame['observation']['snapshot'] = row['observation']['input']
        frame['componentInput'] = row['observation']['componentInput']
        if row['observation']['after']['IsFirstUpdate'] or row['observation']['tailAfter']['mode'] != 0:
            raise ValueError('Incomplete original Main update macro')
codec_check(); protect()
request_sha = save('main_start_lean_requests.json', requests)
save('main_start_lean_native.json', {'schemaVersion': 1, 'requestSha256': request_sha,
    'contractSha256': sha(files['start_layer_graph.json']), 'distanceSha256': sha(files['start_runtime_distance.json']),
    'rootSha256': sha(files['start_runtime_roots_v2.json']), 'dependencies': {n: sha(v) for n,v in files.items()},
    'assets': prior['assets'], 'assetSha256': packages, 'traces': native['traces'],
    'scope': 'Original Main ApplyAdditive13/Linked11/Lean12, actual registered Start provider and complete Main update, one shared Sync on transient ALS81. Graph relevance/weights and Layer HipFire weight remain controlled; no full LocomotionSM/Notify/Montage/production acceptance.'})
unreal.log('LYRA_MAIN_START_LEAN_NATIVE_OK traces=9 frames=3780 poseFrames=3672 functions=10 packages=492 assets_saved=0')
