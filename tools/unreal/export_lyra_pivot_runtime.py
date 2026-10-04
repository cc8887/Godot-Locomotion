"""Actual original Pivot provider Update/Sync/Evaluate on transient ALS81."""
import copy
import hashlib
import json
import math
import os
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
sha = lambda data: hashlib.sha256(data).hexdigest()
names = ('pivot_machine_reentry_requests.json', 'pivot_machine_reentry_definitions.json', 'source_nodes.json',
         'linked_layer_inventory.json', 'logical_controls/catalog.json', 'logical_controls/calibration.json',
         'cycle_layer_graph.json', 'cycle_layer_pose_policy.json', 'orientation_policy.json', 'stride_policy.json',
         'root_motion_policy.json', 'locomotion_layer_closures.json')
files = {name: (root/name).read_bytes() for name in names}
requests, distance, nodes, inventory, catalog, calibration = map(json.loads, list(files.values())[:6])
packages = json.loads((root/'pivot_machine_reentry_native.json').read_bytes())['assetSha256']
content = Path(unreal.Paths.project_content_dir())
previous = {str(p.relative_to(root)).replace('\\','/'): sha(p.read_bytes()) for p in root.rglob('*.json')
            if not p.name.startswith('pivot_runtime_') and p.name!='pivot_layer_graph.json'}
if (root/'pivot_runtime_native.json').exists():
    previous = json.loads((root/'pivot_runtime_native.json').read_bytes())['previousFixtureSha256']

def protect():
    for path, expected in packages.items():
        if sha((content/(path.split('.')[0].removeprefix('/Game/')+'.uasset')).read_bytes())!=expected:
            raise ValueError('Changed Pivot runtime package: '+path)
    for name, expected in previous.items():
        if sha((root/name).read_bytes())!=expected:
            raise ValueError('Changed previous resource: '+name)

def save(name, data):
    path = root/name
    if path.exists():
        if json.loads(path.read_bytes())!=data: raise ValueError('Immutable Pivot runtime resource differs: '+name)
    else: path.write_text(json.dumps(data,separators=(',',':')),encoding='utf-8')
    return sha(path.read_bytes())

protect()
graphs = {}
for profile, row in inventory['classes'].items():
    graph = json.loads(unreal.AlsLyraGraphLibrary.read_animation_layer_graph(
        unreal.load_class(None,row['class']),'FullBody_PivotState',True))
    if len(graph['nodes'])!=16: raise ValueError('Incomplete actual Pivot closure')
    graphs[profile] = graph
graph_sha = save('pivot_layer_graph.json',{'schemaVersion':1,'sourceNodesSha256':sha(files['source_nodes.json']),
                                        'assetSha256':nodes['assetSha256'],'graphs':graphs})
cycles = json.loads(files['cycle_layer_graph.json'])['graphs']
for profile, graph in graphs.items():
    blend = next(n['settings'] for n in graph['nodes'] if n['type'].endswith('.AnimNode_LayeredBoneBlend'))
    cycle = next(n['settings'] for n in cycles[profile]['nodes'] if n['type'].endswith('.AnimNode_LayeredBoneBlend'))
    for key in ('blendMode','blendMasks','bMeshSpaceRotationBlend','bMeshSpaceScaleBlend',
                'bRootSpaceRotationBlend','curveBlendOption','bBlendRootMotionBasedOnRootBone','bUpdateBasePoseFirst','lODThreshold'):
        if blend[key]!=cycle[key]: raise ValueError('Changed Pivot outer blend policy: '+key)

entries = {e['target']:e for e in catalog['entries']}
targets = {}
for e in catalog['entries']: targets.setdefault(e['source'],[]).append(e)
paths = list(distance['assets'])
assets = [{'path':p,'length':distance['assets'][p]['length'],'rateScale':distance['assets'][p]['rateScale'],
           'markers':distance['assets'][p]['markers']} for p in paths]
requests = copy.deepcopy(requests)
for trace in requests['traces']:
    profile = trace['profile']; owner = inventory['classes'][profile]; graph = graphs[profile]
    trace['hipFireIndex'] = 57
    trace['blendNode'] = next(n['index'] for n in graph['nodes'] if n['type'].endswith('.AnimNode_LayeredBoneBlend'))
    trace['orientationNodes'] = sorted(n['index'] for n in graph['nodes'] if n['type'].endswith('.AnimNode_OrientationWarping'))
    trace['strideNodes'] = sorted(n['index'] for n in graph['nodes'] if n['type'].endswith('.AnimNode_StrideWarping'))
    if trace['orientationNodes']!=[62,68] or trace['strideNodes']!=[65,71]: raise ValueError('Changed Pivot warps')
    for key in ('Aim_HipFirePose','Aim_HipFirePose_Crouch'):
        choices = targets[owner['assets'][key]]
        if len(choices)!=1:
            slot = 'hipfire_crouch' if profile=='unarmed' else 'pistol_crouch_idle'
            choices = [e for e in choices if e['slot']==slot]
        if len(choices)!=1: raise ValueError('Ambiguous Pivot HipFire binding')
        path = choices[0]['target']; trace['bindings'][key] = path
        if path not in paths:
            sequence = unreal.load_asset(path)
            sync = json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(sequence))
            metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(sequence))
            paths.append(path); assets.append({'path':path,'length':metadata['sequencePlayLength'],
                                              'rateScale':sync['rateScale'],'markers':sync['markers']})
    for index, frame in enumerate(trace['frames']):
        angle = (0,35.123456789,88.9,-45.001234,110,-170,179.999999,-179.999999,15,-80,360,720)[(index//7)%12]
        frame['main']['LocalVelocityDirectionAngleWithOffset'] = angle
        frame['main']['LocalVelocityDirectionAngle'] = angle+123.56789
        frame['main']['DisplacementSpeed'] = (0,79.99999,80,150.123456789,300,600,1200)[(index//11)%7]
        frame['layer'] = {'HipFireUpperBodyOverrideWeight':(0,1e-5,1.00001e-5,.15,.8,1,-.1,1.2)[(index//5)%8]}
        half = math.radians((0,45,-90,170)[(index//23)%4])*.5
        frame['relativeRotation'] = [0,0,math.sin(half),math.cos(half)]
requests['sequencePaths'] = paths
request_sha = save('pivot_runtime_requests.json',requests)
basis = calibration['calibration']; rotation = unreal.Quat(*basis['handBasis']['rotation'])
skeleton = unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(
    unreal.load_asset(basis['sourceMesh']).get_editor_property('skeleton'),
    unreal.load_asset(basis['targetMesh']).get_editor_property('skeleton'),rotation)
sequences = [unreal.AlsLyraControlRigLibrary.create_weapon_sequence(unreal.load_asset(entries[p]['source']),
             unreal.load_asset(p),skeleton,rotation,None) for p in paths]
if any(s is None for s in sequences): raise ValueError('Missing transient Pivot source')
settled = {}
for path, sequence in zip(paths[:len(distance['assets'])],sequences,strict=False):
    d = json.loads(unreal.AlsLyraGraphLibrary.read_distance_sequence_data(sequence,'Distance'))
    d['path'] = path; d['markers'] = distance['assets'][path]['markers']; settled[path] = d
distance_sha = save('pivot_runtime_distance.json',{'schemaVersion':1,'requestSha256':request_sha,
                                                 'assets':settled,'policies':distance['policies']})
compressed = {}
for path, sequence in zip(paths,sequences,strict=True):
    text = unreal.AlsLyraGraphLibrary.read_root_compression_data(sequence)
    if not text: raise ValueError('Missing actual Pivot compressed root: '+path)
    compressed[path] = json.loads(text)
root_sha = save('pivot_runtime_roots.json',{'schemaVersion':1,'requestSha256':request_sha,'assets':compressed})
text = unreal.AlsLyraGraphLibrary.read_pivot_runtime_pose_trace(
    unreal.load_class(None,nodes['classes']['main']['class']),unreal.load_asset(basis['sourceMesh']),
    skeleton,sequences,json.dumps(requests,separators=(',',':')))
if not text: raise ValueError('Empty actual Pivot provider pose trace')
native = json.loads(text)
for path, sequence in zip(paths[:len(distance['assets'])],sequences,strict=False):
    d = json.loads(unreal.AlsLyraGraphLibrary.read_distance_sequence_data(sequence,'Distance'))
    d['path'] = path; d['markers'] = distance['assets'][path]['markers']
    if d!=settled[path]: raise ValueError('Pivot codec changed during traversal')
if len(native['traces'])!=9: raise ValueError('Incomplete Pivot runtime trace')
protect()
save('pivot_runtime_native.json',{'schemaVersion':1,'requestSha256':request_sha,'contractSha256':graph_sha,
    'distanceSha256':distance_sha,'rootSha256':root_sha,'dependencies':{n:sha(v) for n,v in files.items()},
    'assets':assets,'assetSha256':packages,'previousFixtureSha256':previous,'traces':native['traces'],
    'scope':'Actual original Pivot16-node root and PivotSM, real source visits/callbacks/common Sync, independent two Warp histories then outer HipFire Evaluate on transient ALS81. Explicit Main inputs; no whole Main/Notify/Montage/Demo acceptance.'})
unreal.log(f'LYRA_PIVOT_RUNTIME_NATIVE_OK traces=9 frames=3780 poseFrames=3528 logical=81 sequences={len(sequences)} packages={len(packages)} assets_saved=0')
