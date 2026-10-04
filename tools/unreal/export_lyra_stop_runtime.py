"""Actual original Stop provider root Update/Sync/Evaluate on transient ALS81."""
import copy
import hashlib
import json
import math
import os
import struct
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
sha = lambda data: hashlib.sha256(data).hexdigest()
names = ('stop_source_requests.json', 'stop_source_definitions.json', 'source_nodes.json',
         'linked_layer_inventory.json', 'logical_controls/catalog.json', 'logical_controls/calibration.json',
         'cycle_layer_graph.json', 'cycle_layer_pose_policy.json', 'root_motion_policy.json', 'main_source_scope_native.json')
files = {name: (root / name).read_bytes() for name in names}
requests, distance, nodes, inventory, catalog, calibration = map(json.loads, list(files.values())[:6])
packages = json.loads(files['main_source_scope_native.json'])['assetSha256']
content = Path(unreal.Paths.project_content_dir())

def check_packages():
    for path, digest in packages.items():
        if sha((content / (path.split('.')[0].removeprefix('/Game/') + '.uasset')).read_bytes()) != digest:
            raise ValueError('Changed Stop runtime provenance: ' + path)

def save(name, data):
    path = root / name
    if path.exists():
        if json.loads(path.read_bytes()) != data: raise ValueError('Existing Stop runtime resource differs: ' + name)
    else: path.write_text(json.dumps(data, separators=(',', ':')), encoding='utf-8')
    return sha(path.read_bytes())

check_packages()
graphs = {}
for profile, row in inventory['classes'].items():
    graph = json.loads(unreal.AlsLyraGraphLibrary.read_animation_layer_graph(
        unreal.load_class(None, row['class']), 'FullBody_StopState'))
    if len(graph['nodes']) != 4: raise ValueError('Incomplete original Stop closure')
    graphs[profile] = graph
graph_sha = save('stop_layer_graph.json', {'schemaVersion': 1, 'sourceNodesSha256': sha(files['source_nodes.json']),
                                         'assetSha256': nodes['assetSha256'], 'graphs': graphs})
cycle_graphs = json.loads(files['cycle_layer_graph.json'])['graphs']
for profile, graph in graphs.items():
    blend = next(n['settings'] for n in graph['nodes'] if n['type'].endswith('.AnimNode_LayeredBoneBlend'))
    cycle = next(n['settings'] for n in cycle_graphs[profile]['nodes'] if n['type'].endswith('.AnimNode_LayeredBoneBlend'))
    # The mask/data policy is shared only after checking actual authored options.
    keys = ('blendMode', 'blendMasks', 'bMeshSpaceRotationBlend', 'bMeshSpaceScaleBlend',
            'bRootSpaceRotationBlend', 'curveBlendOption', 'bBlendRootMotionBasedOnRootBone', 'bUpdateBasePoseFirst', 'lODThreshold')
    if any(blend[k] != cycle[k] for k in keys): raise ValueError('Stop blend differs from verified mask policy')

entries = {e['target']: e for e in catalog['entries']}
targets = {}
for e in catalog['entries']: targets.setdefault(e['source'], []).append(e)
paths = list(distance['assets'])
assets = []
for path in paths:
    d = distance['assets'][path]
    assets.append({'path': path, 'length': d['length'], 'rateScale': d['rateScale'], 'markers': d['markers']})
requests = copy.deepcopy(requests)
for trace in requests['traces']:
    profile = trace['profile']; owner = inventory['classes'][profile]
    players = next(g['players'] for g in nodes['classes'][profile]['graphs'] if g['name'] == 'FullBody_StopState')
    trace['hipFireIndex'] = next(s['nodeIndex'] for s in nodes['classes'][profile]['sources']
                               if s['nodeIndex'] in players and s['functions']['update'] == 'UpdateHipFireRaiseWeaponPose')
    for key in ('Aim_HipFirePose', 'Aim_HipFirePose_Crouch'):
        choices = targets[owner['assets'][key]]
        if len(choices) != 1:
            slot = 'hipfire_crouch' if profile == 'unarmed' else 'pistol_crouch_idle'
            choices = [e for e in choices if e['slot'] == slot]
        if len(choices) != 1: raise ValueError('Ambiguous HipFire binding')
        path = choices[0]['target']; trace['bindings'][key] = path
        if path not in paths:
            sequence = unreal.load_asset(path)
            sync = json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(sequence))
            metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(sequence))
            paths.append(path); assets.append({'path': path, 'length': metadata['sequencePlayLength'],
                                              'rateScale': sync['rateScale'], 'markers': sync['markers']})
    for index, frame in enumerate(trace['frames']):
        angle = (0, 35.123456789, 88.9, -45.001234, 110, -170, 179.999999, -179.999999, 15, -80, 360, 720)[(index // 7) % 12]
        frame['main']['LocalVelocityDirectionAngleWithOffset'] = angle
        frame['main']['LocalVelocityDirectionAngle'] = angle + 123.56789
        frame['main']['DisplacementSpeed'] = (0, 79.99999, 80, 150.123456789, 300, 600, 1200)[(index // 11) % 7]
        frame['layer'] = {'HipFireUpperBodyOverrideWeight': (0, 1e-5, 1.00001e-5, .15, .8, 1, -.1, 1.2)[(index // 5) % 8]}
        half = math.radians((0, 45, -90, 170)[(index // 23) % 4]) * .5
        frame['relativeRotation'] = [0, 0, math.sin(half), math.cos(half)]
requests['sequencePaths'] = paths
request_sha = save('stop_runtime_requests.json', requests)
basis = calibration['calibration']; rotation = unreal.Quat(*basis['handBasis']['rotation'])
skeleton = unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(
    unreal.load_asset(basis['sourceMesh']).get_editor_property('skeleton'),
    unreal.load_asset(basis['targetMesh']).get_editor_property('skeleton'), rotation)
sequences = [unreal.AlsLyraControlRigLibrary.create_weapon_sequence(unreal.load_asset(entries[p]['source']),
             unreal.load_asset(p), skeleton, rotation, None) for p in paths]
if any(s is None for s in sequences): raise ValueError('Missing transient Stop source')
# Export the actual transient codec too; controller edits may invalidate the
# duplicated compressed data. Runtime callbacks must consume this exact resource.
settled = {}
for path, sequence in zip(paths[:len(distance['assets'])], sequences, strict=False):
    d = json.loads(unreal.AlsLyraGraphLibrary.read_distance_sequence_data(sequence, 'Distance'))
    d['path'] = path; d['markers'] = distance['assets'][path]['markers']; settled[path] = d
definition_sha = save('stop_runtime_distance.json', {'schemaVersion': 1, 'requestSha256': request_sha,
                          'assets': settled})
root_definitions = {}
for path, sequence in zip(paths, sequences, strict=True):
    text = unreal.AlsLyraGraphLibrary.read_root_compression_data(sequence)
    if not text: raise ValueError('Missing compressed root payload: ' + path)
    root_definitions[path] = json.loads(text)
root_sha = save('stop_runtime_roots.json', {'schemaVersion': 1, 'requestSha256': request_sha,
                                          'assets': root_definitions})
text = unreal.AlsLyraGraphLibrary.read_stop_runtime_pose_trace(
    unreal.load_class(None, nodes['classes']['main']['class']), unreal.load_asset(basis['sourceMesh']),
    skeleton, sequences, json.dumps(requests, separators=(',', ':')))
if not text: raise ValueError('Empty original Stop root trace')
native = json.loads(text)
for path, sequence in zip(paths[:len(distance['assets'])], sequences, strict=False):
    d = json.loads(unreal.AlsLyraGraphLibrary.read_distance_sequence_data(sequence, 'Distance'))
    d['path'] = path; d['markers'] = distance['assets'][path]['markers']
    if settled[path] != d: raise ValueError('Transient distance codec changed during traversal')
if len(native['traces']) != 9: raise ValueError('Incomplete Stop root')
check_packages()
save('stop_runtime_native.json', {'schemaVersion': 1, 'requestSha256': request_sha, 'contractSha256': graph_sha,
     'distanceSha256': definition_sha, 'rootSha256': root_sha, 'dependencies': {n: sha(v) for n,v in files.items()},
     'assets': assets, 'assetSha256': packages, 'traces': native['traces'],
     'scope': 'Real registered linked instance; actual original Stop four-node root Initialize/CacheBones/Update, one common Sync and Evaluate on transient ALS81. Controlled Main inputs; no full Main/LocomotionSM, Notify/Montage consumers or production Demo acceptance.'})
unreal.log(f"LYRA_STOP_RUNTIME_NATIVE_OK traces=9 frames=3780 poseFrames=3672 logical=81 sequences={len(sequences)} packages={len(packages)} assets_saved=0")
