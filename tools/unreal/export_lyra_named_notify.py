"""Original asset named events through the engine's actual external dispatcher."""
import hashlib
import json
from pathlib import Path
import unreal
repo=Path(__file__).resolve().parents[2]
root=repo/'assets/generated/lyra_als'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
load=lambda p:json.loads(p.read_bytes())
names={'named_notify_v1_policy.json','named_notify_v1_requests.json','named_notify_v1_native.json'}
previous={p.relative_to(root).as_posix():sha(p) for p in root.rglob('*.json') if p.name not in names}
project=Path(unreal.Paths.get_project_file_path())
protected={p.relative_to(project.parent).as_posix():sha(p) for p in [project,*(project.parent/'Config').rglob('*.ini')]}
packages={}
for n in ('notify_dispatch_v1_policy.json','motion_warping_v1_usage.json','motion_warping_v1_game_feature.json'):
    d=load(root/n)
    if isinstance(d['assetSha256'],dict):packages.update(d['assetSha256'])
    else:packages[d['path']]=d['assetSha256']
probe=repo/'tools/unreal/LyraNamedNotifyOracle'
sources={p.relative_to(probe).as_posix():sha(p) for p in probe.rglob('*') if p.is_file()}
def package_file(p):
    p=p.split('.')[0]
    if p.startswith('/Game/'):return project.parent/'Content'/(p.removeprefix('/Game/')+'.uasset')
    if p.startswith('/ShooterCore/'):return project.parent/'Plugins/GameFeatures/ShooterCore/Content'/(p.removeprefix('/ShooterCore/')+'.uasset')
    raise ValueError(p)
def protect():
    for p,d in previous.items():assert sha(root/p)==d,p
    for p,d in protected.items():assert sha(project.parent/p)==d,p
    for p,d in packages.items():assert sha(package_file(p))==d,p
    for p,d in sources.items():assert sha(probe/p)==d,p
def write(n,data):
    data=json.loads(json.dumps(data,separators=(',',':'),allow_nan=False))
    p=root/n
    if p.exists():assert load(p)==data,'Independent named reference changed: '+n
    else:
        with p.open('x',encoding='utf-8',newline='\n') as f:f.write(json.dumps(data,separators=(',',':'),allow_nan=False)+'\n')
    return sha(p)
def cmd(op,target=0,**kw):return dict(op=op,target=target,**kw)
def add(owner,name='SaveAttack',target=0):return cmd('add',target,owner=owner,name=name)
def remove(owner,name='SaveAttack',target=0):return cmd('remove',target,owner=owner,name=name)
def react(owner,*commands):return cmd('react',owner=owner,commands=commands)
def flags(target=0,receive=False,propagate=False):return cmd('flags',target,receive=receive,propagate=propagate)
assets=[a for a in load(root/'notify_contract_v1.json')['assets'] if any(e['name']=='SaveAttack' for e in a['events'])]
assert len(assets)==3
a=assets[0];e=next(e for e in a['events'] if e['name']=='SaveAttack')
def window(asset=a,previous=e['triggerTime']-.001,delta=.002):return dict(asset=asset['source'],previous=previous,delta=delta,current=previous+delta)
def frame(commands=(),windows=None,sender=0):return dict(commands=list(commands),windows=[window()] if windows is None else windows,sender=sender)
protect()
traces=[]
for original in (True,False):
    for hz in (30,60,120):
        for asset in assets:
            registers=[add(owner,name) for name in ('SaveAttack','ResetCombo') for owner in (0,1)]
            frames=[frame(registers if i==0 else [],[window(asset,min(asset['length'],i/hz),1/hz)]) for i in range(int(asset['length']*hz)+3)]
            traces.append(dict(original=original,hz=hz,mode='continuous',frames=frames))
    cases={
        'duplicates':[frame([add(0),add(1),add(0)]),frame()],
        'remove_all':[frame([add(0),add(1),add(0),remove(0)]),frame()],
        'remove_swap_order':[frame([add(0),add(1),add(2),add(3),remove(0)]),frame([remove(2)])],
        'remove_during':[frame([add(0),add(1),add(2),react(2,remove(0))]),frame()],
        'remove_self_duplicate':[frame([add(0),add(1),add(1),react(1,remove(1))]),frame()],
        'add_during':[frame([add(0),add(1),react(1,add(2))]),frame()],
        'remove_add_during':[frame([add(0),add(1),add(2),react(2,remove(0),add(3))]),frame()],
        'dead_owner':[frame([add(0),add(1),cmd('kill',owner=0)]),frame([add(2)])],
        'kill_during':[frame([add(0),add(1),react(1,cmd('kill',owner=0))]),frame()],
        'case_insensitive':[frame([add(0,'saveattack'),add(1,'SAVEATTACK')]),frame([remove(0,'SaveAttack')])],
        'linked_external_not_broadcast':[frame([add(0,target=1),add(1),flags(propagate=True),flags(1,receive=True)])],
        'sender_linked_self':[frame([add(0,target=1),add(1)],sender=1)],
        'sender_linked_propagate':[frame([add(0,target=1),add(1),flags(0,receive=True),flags(1,propagate=True),flags(2,receive=True),flags(3,receive=True)],sender=1)],
        'flags_after_external':[frame([add(0),react(0,flags(propagate=True),flags(1,receive=True),flags(3,receive=True))])],
        'unlink_sender':[frame([add(0,target=1),flags(0,receive=True),flags(1,propagate=True),react(0,cmd('unlink',1))],sender=1)],
        'nested_dispatch':[frame([add(0),add(1),react(1,cmd('dispatch'))])],
    }
    if not original:
        cases.update({
            'intercept':[frame([add(0),cmd('intercept',value=True)]),frame([cmd('intercept',value=False)])],
            'main_method_unlink':[frame([flags(propagate=True),flags(1,receive=True),flags(2,receive=True),cmd('methodReact',commands=[cmd('unlink',2)])])],
            'linked_method_unlink':[frame([flags(propagate=True),flags(1,receive=True),flags(2,receive=True),cmd('methodReact',1,commands=[cmd('unlink',2)])])],
            'receive_changed_in_method':[frame([flags(propagate=True),flags(1,receive=True),cmd('methodReact',1,commands=[flags(2,receive=True)])])],
            'post_changed_in_method':[frame([flags(propagate=True),flags(1,receive=True),flags(3,receive=True),cmd('methodReact',1,commands=[cmd('post',3,value=False)])])],
        })
    for mode,frames in cases.items():traces.append(dict(original=original,hz=60,mode=mode,frames=frames))
requests=dict(schemaVersion=1,traces=traces)
native=json.loads(unreal.LyraNamedNotifyOracleLibrary.read_trace(json.dumps(requests,separators=(',',':'))))
assert len(native['traces'])==len(traces)
for t,n in zip(traces,native['traces'],strict=True):assert len(t['frames'])==len(n['frames'])
protect()
ps=write('named_notify_v1_policy.json',dict(schemaVersion=1,engineVersion=unreal.SystemLibrary.get_engine_version(),
    dependencies={p:sha(root/p) for p in ('notify_contract_v1.json','notify_dispatch_v1_policy.json','linked_layer_contracts.json')},
    previousFixtureSha256=previous,protectedProject=protected,assetSha256=packages,probeSourceSha256=sources,
    scope=dict(originalGeneratedClassesLoaded=True,controlledInstanceFunctions=True,controlledLinkedRoster=True,
        originalTriggerAnimNotifies=True,originalMulticastDelegate=True,originalNamedAssets=True,wholeMainEvaluated=False,assetsSaved=0)))
rs=write('named_notify_v1_requests.json',requests)
write('named_notify_v1_native.json',dict(schemaVersion=1,policySha256=ps,requestSha256=rs,trace=native))
unreal.log('LYRA_NAMED_NOTIFY_NATIVE_OK traces='+str(len(traces))+' frames='+str(sum(len(t['frames']) for t in traces))+
    ' previous='+str(len(previous))+' packages='+str(len(packages))+' assets_saved=0')
