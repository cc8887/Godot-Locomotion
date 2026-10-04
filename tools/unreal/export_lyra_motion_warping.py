"""Execute original Montage -> CharacterAdapter -> MotionWarping -> mesh conversion."""
import hashlib
import json
import math
from pathlib import Path
import unreal

repo=Path(__file__).resolve().parents[2]
root=repo/'assets/generated/lyra_als'
prefix='motion_warping_v1_'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
load=lambda p:json.loads(p.read_bytes())
policy=load(root/(prefix+'policy.json'))
previous={p.relative_to(root).as_posix():sha(p) for p in root.rglob('*.json') if p.name not in (prefix+'usage.json',prefix+'requests.json',prefix+'native.json')}
project=Path(unreal.Paths.get_project_file_path())
protected={p.relative_to(project.parent).as_posix():sha(p) for p in [project,*(project.parent/'Config').rglob('*.ini')]}
packages=dict(policy['assetSha256'])
actor=load(root/'root_movement_v1_actor_policy.json')
packages[actor['pawnClass']]=actor['assetSha256']
plugin=repo/'tools/unreal/LyraMotionWarpingOracle'
sources={p.relative_to(plugin).as_posix():sha(p) for p in plugin.rglob('*') if p.is_file()}

def package_file(path):
    path=path.split('.')[0]
    if path.startswith('/Game/'):return project.parent/'Content'/(path.removeprefix('/Game/')+'.uasset')
    if path.startswith('/ShooterCore/'):return project.parent/'Plugins/GameFeatures/ShooterCore/Content'/(path.removeprefix('/ShooterCore/')+'.uasset')
    raise ValueError(path)

def protect():
    for p,d in previous.items():assert sha(root/p)==d,p
    for p,d in protected.items():assert sha(project.parent/p)==d,p
    for p,d in packages.items():assert sha(package_file(p))==d,p
    for p,d in sources.items():assert sha(plugin/p)==d,p

def write(name,data):
    p=root/(prefix+name+'.json')
    if p.exists():assert load(p)==data,'Independent MW capture changed: '+p.name
    else:
        with p.open('x',encoding='utf-8',newline='\n') as f:f.write(json.dumps(data,ensure_ascii=False,separators=(',',':'),allow_nan=False)+'\n')
    return sha(p)

def atom(position,yaw=0,scale=1):return dict(position=position,rotation=[0,0,math.sin(yaw/2),math.cos(yaw/2)],scale=[scale]*3)

protect()
usage=json.loads(unreal.LyraMotionWarpingOracleLibrary.read_usage())
registry=unreal.AssetRegistryHelpers.get_asset_registry()
registry.search_all_assets(True)
registry.wait_for_completion()
registry.scan_paths_synchronous(['/Game','/ShooterCore'],True)
options=unreal.AssetRegistryDependencyOptions(include_soft_package_references=True,include_hard_package_references=True,
    include_searchable_names=False,include_soft_management_references=False,include_hard_management_references=False)
usage['referencers']={w['montage']:sorted(str(v) for v in registry.get_referencers(w['montage'].split('.')[0],options)) for w in policy['windows']}
usage['registrySearchCompleted']=True

catalog=load(root/'montage_catalog_v2.json')['assets']
paths=[a['path'] for a in catalog]
traces=[]
modes=('normal','change','remove','missing_then_add','stop_replay','replace_replay','seek','reverse','pause','disable')
for window in policy['windows']:
    asset=paths.index(window['montage'])
    for hz in (30,60,120):
        for mode in modes:
            frames=[]
            for i in range(hz*2):
                f=dict(delta=1/hz,plays=[],stop=-1,stopTime=.1,seek=-1,targetOperation=0,
                       target=atom([300,90,35],-1.1),warpPaused=False,rootPaused=False,disable=False)
                if i==0:
                    f['plays']=[dict(asset=asset,rate=-1 if mode=='reverse' else 1,start=.5 if mode=='reverse' else 0)]
                    if mode!='missing_then_add':f['targetOperation']=1
                if mode in ('change','missing_then_add') and i==hz//6:
                    f['targetOperation']=1;f['target']=atom([260,-150,42],2.1)
                if mode=='remove' and i==hz//6:f['targetOperation']=2
                if mode=='stop_replay':
                    if i==hz//5:f['stop']=asset
                    if i==hz//2:f['plays']=[dict(asset=asset,rate=1.5,start=0)]
                if mode=='replace_replay':
                    if i==hz//6:f['plays']=[dict(asset=0,rate=1,start=0)]
                    if i==hz//3:f['plays']=[dict(asset=asset,rate=1,start=0)]
                if mode=='seek' and i==hz//6:f['seek']=window['endTriggerTime']+.2
                if mode=='pause':
                    if i==hz//6:f['targetOperation']=1;f['warpPaused']=True
                    if i==hz//4:f['targetOperation']=1;f['rootPaused']=True
                    if i==hz//3:f['targetOperation']=1
                if mode=='disable' and i==hz//6:f['disable']=True
                frames.append(f)
            traces.append(dict(asset=asset,hz=hz,mode=mode,actor=atom([125,-73,90],.63),
                relative=atom([17,-9,-90],-math.pi/2,(1,.7,1.4)[(30,60,120).index(hz)]),capsuleHalfHeight=90,frames=frames))
request=dict(schemaVersion=1,mesh='/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny.SKM_Manny',montages=paths,traces=traces)
native=json.loads(unreal.LyraMotionWarpingOracleLibrary.read_trace(json.dumps(request,separators=(',',':'))))
assert len(native['traces'])==len(traces)==120
for t,n in zip(traces,native['traces'],strict=True):assert len(t['frames'])==len(n['frames'])
protect()
usage_sha=write('usage',dict(schemaVersion=1,policySha256=sha(root/(prefix+'policy.json')),previousFixtureSha256=previous,
    protectedProject=protected,assetSha256=packages,probeSourceSha256=sources,trace=usage))
request_sha=write('requests',request)
write('native',dict(schemaVersion=1,usageSha256=usage_sha,requestSha256=request_sha,trace=native,
    scope=dict(originalMontages=True,originalCharacterAdapter=True,originalMotionWarpingComponent=True,
        originalNotifyModifierTemplates=True,manualWorldIntegration=True,worldPhysics=False,
        originalAbilityExecuted=False,wholeMainEvaluated=False,assetsSaved=0)))
unreal.log('LYRA_MOTION_WARPING_NATIVE_OK traces='+str(len(traces))+' frames='+str(sum(len(t['frames']) for t in traces))+' previous='+str(len(previous))+' packages='+str(len(packages))+' assets_saved=0')
