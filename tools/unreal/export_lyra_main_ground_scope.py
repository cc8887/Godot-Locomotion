"""Four original Main roots, one registered provider and one native Sync."""
import copy
import hashlib
import itertools
import json
import os
from pathlib import Path
import unreal

root=Path(os.environ['LYRA_OUTPUT_ROOT'])
repo=Path(__file__).resolve().parents[2]
sha=lambda b:hashlib.sha256(b).hexdigest()
names=('main_state_history_requests.json','main_state_history_native.json','main_state_history_roots.json',
       'main_pivot_requests.json','main_pivot_native.json','pivot_runtime_roots.json','pivot_runtime_distance.json',
       'start_runtime_distance.json','stop_runtime_distance.json','logical_controls/catalog.json','logical_controls/calibration.json',
       'main_lean/catalog.json','main_lean/inventory.json','source_nodes.json','linked_layer_contracts.json',
       'start_layer_graph.json','cycle_layer_graph.json','stop_layer_graph.json','pivot_layer_graph.json','main_update_policy.json',
       'cycle_source_definitions.json','cycle_layer_pose_policy.json','main_lean/composition_v3_policy.json')
files={name:(root/name).read_bytes() for name in names}
ground=json.loads(files['main_state_history_native.json']);pivot=json.loads(files['main_pivot_native.json'])
packages=pivot['assetSha256'];content=Path(unreal.Paths.project_content_dir())
prefix='main_ground_scope_v2'
previous={str(p.relative_to(root)).replace('\\','/'):sha(p.read_bytes()) for p in root.rglob('*.json')
          if p.name not in (prefix+'_requests.json',prefix+'_native.json')}
if (root/(prefix+'_native.json')).exists():
    previous=json.loads((root/(prefix+'_native.json')).read_bytes())['previousFixtureSha256']
def protect():
    for path,digest in packages.items():
        if sha((content/(path.split('.')[0].removeprefix('/Game/')+'.uasset')).read_bytes())!=digest:
            raise ValueError('Changed ground package: '+path)
    for name,digest in previous.items():
        if sha((root/name).read_bytes())!=digest:raise ValueError('Changed previous ground fixture: '+name)
def save(name,data):
    path=root/name
    if path.exists():
        if json.loads(path.read_bytes())!=data:raise ValueError('Immutable ground fixture differs: '+name)
    else:path.write_text(json.dumps(data,separators=(',',':')),encoding='utf-8')
    return sha(path.read_bytes())
protect()
assets={a['path']:copy.deepcopy(a) for a in ground['assets']}
assets.update({a['path']:copy.deepcopy(a) for a in pivot['assets']})
paths=list(assets);requests=copy.deepcopy(json.loads(files['main_state_history_requests.json']))
requests['sequencePaths']=paths
pivot_requests=json.loads(files['main_pivot_requests.json']);orders=list(itertools.permutations(range(4)))
weights=(.25,.2,.3,.25)
for trace,pt in zip(requests['traces'],pivot_requests['traces'],strict=True):
    if (trace['profile'],trace['hz'],trace['class'])!=(pt['profile'],pt['hz'],pt['class']):raise ValueError('Different ground provider')
    for key,value in pt['bindings'].items():
        if key in trace['bindings'] and trace['bindings'][key]!=value:raise ValueError('Conflicting ground binding: '+key)
        trace['bindings'][key]=value
    previous_weights=[0,0,0,0]
    for i,(frame,pf) in enumerate(zip(trace['frames'],pt['frames'],strict=True)):
        observation=copy.deepcopy(pf['observation']);observation.pop('snapshot');frame['observation']=observation
        frame.pop('componentInput',None);frame['relativeRotation']=copy.deepcopy(pf['relativeRotation']);frame['delta']=pf['delta'];frame['layer']=copy.deepcopy(pf['layer'])
        phase=i%9;active=[phase<4 or phase>=5 and phase-5==n for n in range(4)]
        current=4 if phase<4 or phase==5 else 2 if phase==6 else 1 if phase==7 else 3 if phase==8 else 0
        reset=pf['reinitialize'];frame.update(active=active[2],weight=weights[2],reinitialize=reset,order=list(orders[i%24]))
        frame['cycle']=dict(active=active[1],weight=weights[1],reinitialize=reset)
        frame['pivot']=dict(active=active[0],weight=weights[0],reinitialize=reset)
        movement=pf['movement']
        frame['stop']=dict(active=active[3],weight=weights[3],reinitialize=reset,machineCurrent=current,previousStopWeight=previous_weights[3],
            movement=dict(lastUpdateVelocity=movement['lastUpdateVelocity'],bUseSeparateBrakingFriction=False,BrakingFriction=0,
                          GroundFriction=movement['groundFriction'],BrakingFrictionFactor=2,BrakingDecelerationWalking=2048))
        frame['stateRoots']=dict(current=current,previousStartWeight=previous_weights[2],previousCycleWeight=previous_weights[1],previousStopWeight=previous_weights[3])
        previous_weights=[weights[n] if active[n] else 0 for n in range(4)]
basis=json.loads(files['logical_controls/calibration.json'])['calibration'];rotation=unreal.Quat(*basis['handBasis']['rotation'])
skeleton=unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(unreal.load_asset(basis['sourceMesh']).get_editor_property('skeleton'),
    unreal.load_asset(basis['targetMesh']).get_editor_property('skeleton'),rotation)
entries={e['target']:e for e in json.loads(files['logical_controls/catalog.json'])['entries']}
sequences=[unreal.AlsLyraControlRigLibrary.create_weapon_sequence(unreal.load_asset(entries[p]['source']),unreal.load_asset(p),skeleton,rotation,None) for p in paths]
if any(s is None for s in sequences):raise ValueError('Missing ground transient sequence')
roots=json.loads(files['main_state_history_roots.json'])['assets']
for path,data in json.loads(files['pivot_runtime_roots.json'])['assets'].items():
    if path in roots and roots[path]!=data:raise ValueError('Conflicting compressed ground root')
    roots[path]=data
def inventory():
    for path,sequence in zip(paths,sequences,strict=True):
        metadata=json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(sequence))
        if metadata['length']!=assets[path]['length'] or metadata['rateScale']!=assets[path]['rateScale']:raise ValueError('Changed ground timing: '+path)
        if json.loads(unreal.AlsLyraGraphLibrary.read_root_compression_data(sequence))!=roots[path]:raise ValueError('Changed ground compressed root: '+path)
        assets[path]['markers']=metadata['markers']
inventory();before=copy.deepcopy(assets)
lean=[]
for e in json.loads(files['main_lean/catalog.json'])['entries']:
    sequence=unreal.AlsLyraControlRigLibrary.create_weapon_sequence(unreal.load_asset(e['source']),unreal.load_asset(e['target']),skeleton,rotation,lean[0] if lean else None)
    if sequence is None:raise ValueError('Missing ground Lean source')
    lean.append(sequence)
nodes=json.loads(files['source_nodes.json'])
text=unreal.AlsLyraGraphLibrary.read_main_ground_scope_trace(unreal.load_class(None,nodes['classes']['main']['class']),
    unreal.load_asset(basis['sourceMesh']),skeleton,sequences,unreal.load_asset(json.loads(files['main_lean/inventory.json'])['source']),lean,
    json.dumps(requests,separators=(',',':')))
if not text:raise ValueError('Empty four-root native trace')
native=json.loads(text);counts=dict(frames=0,poses=0,allActive=0,hidden=0)
for trace,authored in zip(native['traces'],requests['traces'],strict=True):
    for row,frame in zip(trace['frames'],authored['frames'],strict=True):
        frame['observation']['snapshot']=row['observation']['input'];frame['observation']['mode']=row['observation']['tailBefore']['mode']
        frame['componentInput']=row['observation']['componentInput']
        flags=['output' in row['pivot'],'output' in row['cycle'],'output' in row,'output' in row['stop']]
        if flags!=[frame['pivot']['active'],frame['cycle']['active'],frame['active'],frame['stop']['active']]:raise ValueError('Wrong ground relevance')
        counts['frames']+=1;counts['poses']+=sum(flags);counts['allActive']+=all(flags);counts['hidden']+=not any(flags)
if counts!=dict(frames=3780,poses=8400,allActive=1680,hidden=420):raise ValueError('Incomplete ground capture: '+str(counts))
inventory()
if assets!=before:raise ValueError('Ground transient metadata changed during traversal')
protect()
source_root=repo/'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
probe_sources=('Private/AlsLyraCycleLibrary.cpp','Private/AlsLyraGroundPivotProbe.inl','Public/AlsLyraGraphLibrary.h',
               'Private/AlsLyraMainObservationProbe.h','Private/AlsLyraPoseProbe.h')
source_sha={name:sha((source_root/name).read_bytes()) for name in probe_sources}
for tree in ('source','package'):
    exported=repo/'artifacts/unreal/gasp58-lyra-masks'/tree/'AlsV4AssetExporter/Source/AlsV4AssetExporter'
    for name,digest in source_sha.items():
        if sha((exported/name).read_bytes())!=digest:raise ValueError('Different built ground probe: '+tree+'/'+name)
request_sha=save(prefix+'_requests.json',requests)
native.update(schemaVersion=1,requestSha256=request_sha,assets=list(assets.values()),counts=counts,
              dependencies={name:sha(data) for name,data in files.items()},assetSha256=packages,previousFixtureSha256=previous,probeSourceSha256=source_sha)
save(prefix+'_native.json',native)
unreal.log('LYRA_MAIN_GROUND_SCOPE_NATIVE_OK traces=9 frames=3780 poses=8400 packages=508 previous='+str(len(previous))+' assets_saved=0')
