"""Read-only original source-state rule, notify history and named dispatch."""
import hashlib
import json
from pathlib import Path
import unreal

repo=Path(__file__).resolve().parents[2]
root=repo/'assets/generated/lyra_als'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
load=lambda p:json.loads(p.read_bytes())
names={'notify_dispatch_v1_policy.json','notify_dispatch_v1_requests.json','notify_dispatch_v1_native.json'}
previous={p.relative_to(root).as_posix():sha(p) for p in root.rglob('*.json') if p.name not in names}
project=Path(unreal.Paths.get_project_file_path())
protected={p.relative_to(project.parent).as_posix():sha(p) for p in [project,*(project.parent/'Config').rglob('*.ini')]}
packages=load(root/'emote_v1_policy.json')['assetSha256']
probe=repo/'tools/unreal/LyraNotifyDispatchOracle'
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
def write(name,data):
    p=root/name
    if p.exists():assert load(p)==data,'Independent notify reference changed: '+name
    else:
        with p.open('x',encoding='utf-8',newline='\n') as f:f.write(json.dumps(data,separators=(',',':'),allow_nan=False)+'\n')
    return sha(p)
protect()
classes=[v['class'] for k,v in load(root/'linked_layer_contracts.json')['classes'].items() if k!='interface']
policy=json.loads(unreal.LyraNotifyDispatchOracleLibrary.read_policy(json.dumps(dict(classes=classes))))
(repo/'artifacts/lyra-analysis/notify-dispatch-policy-observed.json').write_text(json.dumps(policy,indent=2),encoding='utf-8')
machine=policy['mainMachineContextIndex']
assert policy['mainMachineNotifyMetadata'] and len(policy['notifyCalls'])==1
assert policy['notifyCalls'][0]['function']=='WasAnimNotifyStateActiveInSourceState'
assets=load(root/'notify_contract_v1.json')['assets']
transitions=[a for a in assets if any('TransitionToLocomotion' in e['notifyStateClass'] for e in a['events'])]
named=[a for a in assets if any(e['name']=='SaveAttack' for e in a['events'])]
assert len(transitions)==36 and len(named)==3
def frame(windows=(),replace=True,remove=False,delta=1/60):
    return dict(delta=delta,replaceHistory=replace,removeHandlers=remove,windows=list(windows))
def window(a,previous,delta,state=4,weight=1,active=True,leader=True,looping=False,context=True):
    w=dict(asset=a['source'],previous=previous,delta=delta,current=previous+delta,
        looping=looping,active=active,leader=leader,weight=weight)
    if context:w['states']=[[machine,state]]
    return w
traces=[]
for hz in (30,60,120):
    for a in transitions:
        for reverse in (False,True):
            frames=[]
            for i in range(int(a['length']*hz)+3):
                p=min(a['length'],i/hz) if not reverse else max(0,a['length']-i/hz)
                frames.append(frame([window(a,p,(-1 if reverse else 1)/hz)],delta=1/hz))
            frames.extend([frame(),frame()])
            traces.append(dict(hz=hz,mode='reverse' if reverse else 'forward',asset=a['source'],frames=frames))
    for a in named:
        frames=[frame([window(a,min(a['length'],i/hz),1/hz)],delta=1/hz) for i in range(int(a['length']*hz)+3)]
        frames.extend([frame(),frame()])
        traces.append(dict(hz=hz,mode='named',asset=a['source'],frames=frames))
a,b=transitions[:2]
e=next(e for e in a['events'] if 'TransitionToLocomotion' in e['notifyStateClass'])
be=next(e for e in b['events'] if 'TransitionToLocomotion' in e['notifyStateClass'])
def crossing(asset,event,**kw):return window(asset,event['triggerTime']-.01,.02,**kw)
cases={
    'no_context':[crossing(a,e,context=False)],
    'wrong_state':[crossing(a,e,state=2)],
    'inactive':[crossing(a,e,active=False)],
    'low_weight':[crossing(a,e,weight=1e-7)],
    'follower':[crossing(a,e,leader=False)],
    'first_wrong':[crossing(a,e,state=2),crossing(b,be)],
    'first_right':[crossing(b,be),crossing(a,e,state=2)],
    'reached_end':[window(a,e['endTriggerTime']-.01,.02)],
    'append_history':[crossing(a,e)],
}
for mode,windows in cases.items():
    frames=[frame(windows),frame(replace=mode!='append_history'),frame(),frame()]
    traces.append(dict(hz=60,mode=mode,asset=a['source'],frames=frames))
for a in named:
    es=[e for e in a['events'] if e['name'] in ('SaveAttack','ResetCombo')]
    frames=[frame([window(a,e['triggerTime']-.01,.02)],remove=i==1) for i,e in enumerate(es)]
    frames.extend([frame(),frame()])
    traces.append(dict(hz=60,mode='remove_named_handler',asset=a['source'],frames=frames))
request=dict(schemaVersion=1,traces=traces)
native=json.loads(unreal.LyraNotifyDispatchOracleLibrary.read_trace(json.dumps(request,separators=(',',':'))))
assert len(native['traces'])==len(traces)
for t,n in zip(traces,native['traces'],strict=True):assert len(t['frames'])==len(n['frames'])
assert any(f['before']['pivotRule'] for t in native['traces'] for f in t['frames'])
protect()
ps=write('notify_dispatch_v1_policy.json',dict(schemaVersion=1,engineVersion=unreal.SystemLibrary.get_engine_version(),
    dependencies={p:sha(root/p) for p in ('notify_contract_v1.json','linked_layer_contracts.json','runtime_graph.json')},
    previousFixtureSha256=previous,protectedProject=protected,assetSha256=packages,probeSourceSha256=sources,trace=policy))
rs=write('notify_dispatch_v1_requests.json',request)
write('notify_dispatch_v1_native.json',dict(schemaVersion=1,policySha256=ps,requestSha256=rs,trace=native,
    scope=dict(originalGeneratedRule=True,originalQueue=True,originalHistory=True,originalTransitionState=True,
        originalExternalNamedDispatch=True,controlledSourceContexts=True,wholeMainEvaluated=False,assetsSaved=0)))
unreal.log('LYRA_NOTIFY_DISPATCH_NATIVE_OK traces='+str(len(traces))+' frames='+str(sum(len(t['frames']) for t in traces))+
    ' machine='+str(machine)+' previous='+str(len(previous))+' packages='+str(len(packages))+' assets_saved=0')
