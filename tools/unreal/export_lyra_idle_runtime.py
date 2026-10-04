"""Original IdleSM/IdleStance and callbacks on transient ALS81; immutable capture."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root=Path(os.environ['LYRA_OUTPUT_ROOT']);repo=Path(__file__).resolve().parents[2]
sha=lambda b:hashlib.sha256(b).hexdigest()
case_set=os.environ.get('LYRA_IDLE_CASESET','continuous')
if case_set not in ('continuous','gates'):raise ValueError('Unknown Idle caseset')
prefix='idle_runtime_gates_' if case_set=='gates' else 'idle_runtime_v2_'
own={prefix+n+'.json' for n in ('requests','roots','native')}
names=('locomotion_layer_closures.json','source_nodes.json','runtime_graph.json',
       'logical_controls/catalog.json','logical_controls/calibration.json','locomotion_extras/catalog.json',
       'root_motion_policy.json','air_runtime_native.json')
files={n:(root/n).read_bytes() for n in names}
closures=json.loads(files[names[0]]);nodes=json.loads(files[names[1]])
entries=json.loads(files['logical_controls/catalog.json'])['entries']+json.loads(files['locomotion_extras/catalog.json'])['entries']
calibration=json.loads(files['logical_controls/calibration.json'])['calibration']
packages=json.loads(files['air_runtime_native.json'])['assetSha256']
previous={str(p.relative_to(root)).replace('\\','/'):sha(p.read_bytes()) for p in root.rglob('*.json') if p.name not in own}
if (root/(prefix+'native.json')).exists():previous=json.loads((root/(prefix+'native.json')).read_bytes())['previousFixtureSha256']
content=Path(unreal.Paths.project_content_dir())
def protect():
 for p,h in packages.items():
  if sha((content/(p.split('.')[0].removeprefix('/Game/')+'.uasset')).read_bytes())!=h:raise ValueError('Changed Idle package: '+p)
 for p,h in previous.items():
  if sha((root/p).read_bytes())!=h:raise ValueError('Changed prior Idle fixture: '+p)
def save(name,data):
 p=root/(prefix+name+'.json')
 if p.exists():
  if json.loads(p.read_bytes())!=data:raise ValueError('Immutable Idle capture differs: '+name)
 else:p.write_text(json.dumps(data,separators=(',',':')),encoding='utf-8')
 return sha(p.read_bytes())
protect()
keys=('Idle_Hipfire','Idle_ADS','Crouch_Idle','Crouch_Idle_Entry','Crouch_Idle_Exit',
      'TurnInPlace_Left','TurnInPlace_Right','Crouch_TurnInPlace_Left','Crouch_TurnInPlace_Right')
paths=[];lookup={e['target']:e for e in entries};requests=dict(schemaVersion=2,traces=[])
for profile in ('unarmed','pistol','rifle'):
 p=closures['providers'][profile];bindings={}
 if p['layers']['FullBody_IdleState']['root']!=36 or len(p['layers']['FullBody_IdleState']['nodes'])!=14:raise ValueError('Changed Idle closure')
 def target(source):
  choices=[e for e in entries if e['source']==source]
  if len(choices)>1:
   slot=('hipfire_crouch' if profile=='unarmed' else 'pistol_crouch_idle') if 'Crouch_Idle' in source else 'idle'
   choices=[e for e in choices if e['slot']==slot]
  if len(choices)!=1:raise ValueError('Ambiguous Idle source: '+source+' '+str([e['slot'] for e in choices]))
  value=choices[0]['target']
  if value not in paths:paths.append(value)
  return value
 for k in keys:bindings[k]=target(p['defaults']['fields'][k]['value'])
 breaks=[target(s) for s in p['defaults']['fields']['Idle_Breaks']['value']]
 for hz in (30,60,120):
  frames=[]
  for i in range(hz*42):
   t=i/hz;block=int(t)
   # Long natural Idle windows enter actual break playback before later turn,
   # ADS/crouch interruptions, hidden re-entry and retained curve feedback.
   yaw=0 if t<16 or t>=34 else (70,-70,50,50.000001,-50.000001,0)[int((t-16)*2)%6]
   crouch=22<=t<24 or 28<=t<30;ads=24<=t<25 or 31<=t<32
   frames.append(dict(delta=1/hz,main={'IsCrouching':crouch,'GameplayTag_IsADS':ads,'GameplayTag_IsFiring':25<=t<26,
      'HasVelocity':27<=t<28,'IsJumping':30<=t<31,'RootYawOffset':yaw},montage=26<=t<27,
      location=([0,0,0] if t<18 or t>=34 else [block%10+.999999,-.5,100]),
      visited=not(32.2<=t<32.35),active=not(21.5<=t<21.75),weight=(1,.63,.001,1e-5,1.00001e-5)[i//17%5] if 20<=t<22 else 1,
      initialize=i==0 or i==round(hz*33),evaluate=not(22.1<=t<22.25)))
  requests['traces'].append(dict(profile=profile,hz=hz,**{'class':p['class']},bindings=bindings,breaks=breaks,frames=frames))
if case_set=='gates':
 original=requests['traces'];requests['traces']=[]
 for trace in original:
  for gate in ('IsCrouching','GameplayTag_IsADS','GameplayTag_IsFiring','montage','HasVelocity','IsJumping'):
   hz=trace['hz'];frames=[]
   for i in range(hz*7):
    t=i/hz;main={k:False for k in ('IsCrouching','GameplayTag_IsADS','GameplayTag_IsFiring','HasVelocity','IsJumping')};main['RootYawOffset']=0
    blocked=6.2<=t<6.75
    if gate!='montage':main[gate]=blocked
    frames.append(dict(delta=1/hz,main=main,montage=blocked and gate=='montage',location=[0,0,0],
                       visited=True,active=True,weight=1,initialize=i==0,evaluate=t>=6))
   requests['traces'].append({**trace,'gate':gate,'frames':frames})
requests['sequencePaths']=paths
rotation=unreal.Quat(*calibration['handBasis']['rotation'])
skeleton=unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(unreal.load_asset(calibration['sourceMesh']).get_editor_property('skeleton'),unreal.load_asset(calibration['targetMesh']).get_editor_property('skeleton'),rotation)
sequences=[unreal.AlsLyraControlRigLibrary.create_weapon_sequence(unreal.load_asset(lookup[p]['source']),unreal.load_asset(p),skeleton,rotation,None) for p in paths]
if any(s is None for s in sequences):raise ValueError('Missing Idle transient source')
assets=[];roots={}
for path,s in zip(paths,sequences,strict=True):
 m=json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(s));sy=json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(s))
 assets.append(dict(path=path,length=m['sequencePlayLength'],rateScale=sy['rateScale'],markers=sy['markers']))
 roots[path]=json.loads(unreal.AlsLyraGraphLibrary.read_root_compression_data(s))
text=unreal.AlsLyraIdleLibrary.read_idle_trace(unreal.load_class(None,nodes['classes']['main']['class']),unreal.load_asset(calibration['sourceMesh']),skeleton,sequences,json.dumps(requests,separators=(',',':')))
if not text:raise ValueError('Empty original Idle trace')
native=json.loads(text)
for path,s in zip(paths,sequences,strict=True):
 if roots[path]!=json.loads(unreal.AlsLyraGraphLibrary.read_root_compression_data(s)):raise ValueError('Idle compressed root changed: '+path)
protect()
counts=dict(frames=sum(len(t['frames']) for t in native['traces']),poses=sum('output' in f for t in native['traces'] for f in t['frames']))
states={f['idle']['state'] for t in native['traces'] for f in t['frames']}
if counts['frames']!=26460 or states!=({0,3} if case_set=='gates' else {0,1,2,3}):raise ValueError('Incomplete actual Idle coverage: '+str((counts,states)))
request_sha=save('requests',requests);root_sha=save('roots',dict(schemaVersion=1,requestSha256=request_sha,assets=roots))
source_root=repo/'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
source_names=('Public/AlsLyraIdleLibrary.h','Private/AlsLyraIdleLibrary.cpp','Private/AlsLyraAirLibrary.cpp','Private/AlsLyraCycleLibrary.cpp','Private/AlsLyraCyclePoseLibrary.cpp','Private/AlsLyraPoseProbe.h')
source_sha={n:sha((source_root/n).read_bytes()) for n in source_names}
for tree in ('source','package'):
 built=repo/'artifacts/unreal/gasp58-lyra-masks'/tree/'AlsV4AssetExporter/Source/AlsV4AssetExporter'
 for n,h in source_sha.items():
  if sha((built/n).read_bytes())!=h:raise ValueError('Different built Idle probe: '+tree+'/'+n)
native.update(schemaVersion=2,requestSha256=request_sha,rootSha256=root_sha,assets=assets,counts=counts,
 dependencies={n:sha(b) for n,b in files.items()},assetSha256=packages,previousFixtureSha256=previous,probeSourceSha256=source_sha,
 scope='Original IdleSM/IdleStance, actual five sources, callbacks, submitted curve feedback, one Main Sync and ALS81 pose; controlled Main fields, no whole Main/Demo')
save('native',native)
unreal.log('LYRA_IDLE_RUNTIME_NATIVE_OK frames='+str(counts['frames'])+' poses='+str(counts['poses'])+' assets='+str(len(paths))+' packages=508 previous='+str(len(previous))+' assets_saved=0')
