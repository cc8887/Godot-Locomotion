"""Actual Main Pivot StateResult20, full Pivot provider and Lean22, one Sync."""
import copy
import hashlib
import json
import math
import os
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
sha = lambda data: hashlib.sha256(data).hexdigest()
names = ('pivot_layer_graph.json', 'pivot_runtime_requests.json', 'pivot_runtime_distance.json',
    'pivot_runtime_roots.json', 'logical_controls/catalog.json', 'logical_controls/calibration.json',
    'linked_layer_inventory.json', 'linked_layer_contracts.json', 'source_nodes.json',
    'locomotion_layer_closures.json', 'cycle_layer_pose_policy.json', 'root_motion_policy.json',
    'orientation_policy.json', 'stride_policy.json', 'main_update_requests.json', 'main_update_policy.json',
    'main_lean/catalog.json', 'main_lean/inventory.json', 'main_lean/behavior.json',
    'main_lean/runtime_policies.json', 'main_lean/composition_v3_policy.json')
files = {name: (root/name).read_bytes() for name in names}
prior = json.loads((root/'pivot_runtime_native.json').read_bytes())
requests = copy.deepcopy(json.loads(files['pivot_runtime_requests.json']))
catalog = json.loads(files['logical_controls/catalog.json'])
calibration = json.loads(files['logical_controls/calibration.json'])
nodes = json.loads(files['source_nodes.json'])
packages = prior['assetSha256']
content = Path(unreal.Paths.project_content_dir())
previous = {str(p.relative_to(root)).replace('\\', '/'): sha(p.read_bytes()) for p in root.rglob('*.json')
            if p.name not in ('main_pivot_requests.json', 'main_pivot_native.json')}
if (root/'main_pivot_native.json').exists():
    previous = json.loads((root/'main_pivot_native.json').read_bytes())['previousFixtureSha256']

def protect():
    for path, digest in packages.items():
        if sha((content/(path.split('.')[0].removeprefix('/Game/')+'.uasset')).read_bytes()) != digest:
            raise ValueError('Changed Main Pivot package: '+path)
    for name, digest in previous.items():
        if sha((root/name).read_bytes()) != digest:
            raise ValueError('Changed previous Main Pivot dependency: '+name)

def save(name, data):
    path = root/name
    if path.exists():
        if json.loads(path.read_bytes()) != data: raise ValueError('Immutable Main Pivot fixture differs: '+name)
    else: path.write_text(json.dumps(data, separators=(',', ':')), encoding='utf-8')
    return sha(path.read_bytes())

protect()
observations = {t['hz']: t['frames'] for t in json.loads(files['main_update_requests.json'])['traces']}
for trace in requests['traces']:
    for index, frame in enumerate(trace['frames']):
        explicit = frame.pop('main')
        observation = copy.deepcopy(observations[trace['hz']][index])
        observation.pop('snapshot')
        observation['rotation'] = [0, observation['rotation'][1], 0]
        observation['crouching'] = explicit['IsCrouching']
        observation['ads'] = explicit['GameplayTag_IsADS']
        # Supply actual character movement, not authored Main direction fields.
        # Preserve zero/tiny acceleration and the source fixture's reversing phases.
        acceleration = explicit['LocalAcceleration2D']
        magnitude = math.hypot(acceleration[0], acceleration[1])
        velocity = explicit['LocalVelocity2D']
        speed = math.hypot(velocity[0], velocity[1])
        direction = explicit['CardinalDirectionFromAcceleration']
        axes = ((1, .123), (-1, -.157), (.119, -1), (-.131, 1))
        x, y = axes[direction]
        scale = magnitude/math.hypot(x, y)
        local = (x*scale, y*scale)
        sign = -1 if sum(a*v for a,v in zip(acceleration, velocity, strict=True)) < 0 else 1
        yaw = math.radians(observation['rotation'][1])
        world = (math.cos(yaw)*local[0]-math.sin(yaw)*local[1],
                 math.sin(yaw)*local[0]+math.cos(yaw)*local[1])
        observation['acceleration'] = [world[0], world[1], 77.25]
        if magnitude > 0:
            observation['velocity'] = [sign*world[0]/magnitude*speed, sign*world[1]/magnitude*speed, 0]
        else:
            observation['velocity'] = [0, 0, 0]
        frame['observation'] = observation
        frame['delta'] = observation['delta']

basis = calibration['calibration']
rotation = unreal.Quat(*basis['handBasis']['rotation'])
skeleton = unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(
    unreal.load_asset(basis['sourceMesh']).get_editor_property('skeleton'),
    unreal.load_asset(basis['targetMesh']).get_editor_property('skeleton'), rotation)
paths = requests['sequencePaths']
entries = {e['target']: e for e in catalog['entries']}
sequences = [unreal.AlsLyraControlRigLibrary.create_weapon_sequence(unreal.load_asset(entries[p]['source']),
             unreal.load_asset(p), skeleton, rotation, None) for p in paths]
if any(s is None for s in sequences): raise ValueError('Missing original Pivot transient source')
distance = json.loads(files['pivot_runtime_distance.json'])
roots = json.loads(files['pivot_runtime_roots.json'])

def codec_check():
    for path, sequence in zip(paths, sequences, strict=True):
        if json.loads(unreal.AlsLyraGraphLibrary.read_root_compression_data(sequence)) != roots['assets'][path]:
            raise ValueError('Changed original Pivot compressed root: '+path)
        if path in distance['assets']:
            data = json.loads(unreal.AlsLyraGraphLibrary.read_distance_sequence_data(sequence, 'Distance'))
            data['path'] = path
            data['markers'] = distance['assets'][path]['markers']
            if data != distance['assets'][path]: raise ValueError('Changed original Pivot distance codec: '+path)

codec_check()
# Controller brackets can reorder equal-time markers on a transient copy.
# The oracle must describe the actual sequence that participates in Sync.
assets = copy.deepcopy(prior['assets'])
for asset, sequence in zip(assets, sequences, strict=True):
    metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(sequence))
    if metadata['length'] != asset['length'] or metadata['rateScale'] != asset['rateScale']:
        raise ValueError('Changed actual Pivot sequence timing: ' + asset['path'])
    asset['markers'] = metadata['markers']
lean_sequences = []
for entry in json.loads(files['main_lean/catalog.json'])['entries']:
    sequence = unreal.AlsLyraControlRigLibrary.create_weapon_sequence(unreal.load_asset(entry['source']),
        unreal.load_asset(entry['target']), skeleton, rotation, lean_sequences[0] if lean_sequences else None)
    if sequence is None: raise ValueError('Missing Main Lean transient')
    lean_sequences.append(sequence)
text = unreal.AlsLyraGraphLibrary.read_main_pivot_trace(
    unreal.load_class(None, nodes['classes']['main']['class']), unreal.load_asset(basis['sourceMesh']), skeleton,
    sequences, unreal.load_asset(json.loads(files['main_lean/inventory.json'])['source']), lean_sequences,
    json.dumps(requests, separators=(',', ':')))
if not text: raise ValueError('Empty actual Main Pivot trace')
native = json.loads(text)
for asset, sequence in zip(assets, sequences, strict=True):
    metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(sequence))
    if metadata['markers'] != asset['markers']:
        raise ValueError('Pivot markers changed during original execution: ' + asset['path'])
if len(native['traces']) != 9 or sum('output' in row for t in native['traces'] for row in t['frames']) != 3528:
    raise ValueError('Incomplete actual Main Pivot trace')
for trace, authored in zip(native['traces'], requests['traces'], strict=True):
    if trace['mainPivotBinding'] != {'stateRoot': 20, 'applyAdditive': 23, 'linked': 21, 'lean': 22,
        'state': 4, 'becomeRelevant': 'SetUpPivotState', 'update': 'UpdatePivotState'}:
        raise ValueError('Changed actual Main Pivot compiled binding')
    for row, frame in zip(trace['frames'], authored['frames'], strict=True):
        frame['observation']['snapshot'] = row['observation']['input']
        frame['observation']['mode'] = row['observation']['tailBefore']['mode']
        frame['componentInput'] = row['observation']['componentInput']
        frame['movement']['acceleration'] = [row['observation']['input']['acceleration'][k] for k in ('x', 'y', 'z')]
        if row['observation']['after']['IsFirstUpdate'] or row['observation']['tailAfter']['mode'] != 0:
            raise ValueError('Incomplete actual Main update macro')
codec_check()
protect()
request_sha = save('main_pivot_requests.json', requests)
save('main_pivot_native.json', {'schemaVersion': 1, 'requestSha256': request_sha,
    'contractSha256': sha(files['pivot_layer_graph.json']), 'distanceSha256': sha(files['pivot_runtime_distance.json']),
    'rootSha256': sha(files['pivot_runtime_roots.json']), 'dependencies': {n: sha(v) for n,v in files.items()},
    'assets': assets, 'assetSha256': packages, 'previousFixtureSha256': previous, 'traces': native['traces'],
    'scope': 'Actual Main StateResult20 callbacks -> ApplyAdditive23 -> original Linked21 full Pivot provider and Lean22, complete Main macro, one registered provider and shared Sync on transient ALS81. Root traversal/weights/relevance remain explicit state-machine observations; no full LocomotionSM, unified four-root scope, Notify/Montage or production acceptance.'})
unreal.log(f'LYRA_MAIN_PIVOT_NATIVE_OK traces=9 frames=3780 poseFrames=3528 functions=10 packages={len(packages)} previous={len(previous)} assets_saved=0')
