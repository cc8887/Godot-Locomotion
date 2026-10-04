"""Original GA lifecycle when replacement/cancellation follows natural blend-out."""
import hashlib
import json
from pathlib import Path
import unreal

repo=Path(__file__).resolve().parents[2]
root=repo/'assets/generated/lyra_als'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
load=lambda p:json.loads(p.read_bytes())
names={'emote_edges_v1_requests.json','emote_edges_v1_native.json'}
previous={p.relative_to(root).as_posix():sha(p) for p in root.rglob('*.json') if p.name not in names}
policy=load(root/'emote_v1_policy.json')
project=Path(unreal.Paths.get_project_file_path())
def package_file(p):
    p=p.split('.')[0]
    if p.startswith('/Game/'):return project.parent/'Content'/(p.removeprefix('/Game/')+'.uasset')
    if p.startswith('/ShooterCore/'):return project.parent/'Plugins/GameFeatures/ShooterCore/Content'/(p.removeprefix('/ShooterCore/')+'.uasset')
    raise ValueError(p)
def protect():
    for p,d in previous.items():assert sha(root/p)==d,p
    for p,d in policy['protectedProject'].items():assert sha(project.parent/p)==d,p
    for p,d in policy['assetSha256'].items():assert sha(package_file(p))==d,p
    for p,d in policy['probeSourceSha256'].items():assert sha(repo/'tools/unreal/LyraEmoteOracle'/p)==d,p
def write(name,data):
    p=root/name
    if p.exists():assert load(p)==data,'Independent Emote edge changed: '+name
    else:
        with p.open('x',encoding='utf-8',newline='\n') as f:f.write(json.dumps(data,separators=(',',':'),allow_nan=False)+'\n')
    return sha(p)
protect()
base=load(root/'emote_v1_requests.json')
traces=[]
for hz in (30,60,120):
    for mode in ('interrupt_fade','cancel_fade','move_fade','interrupt_fade_cancel','interrupt_fade_move','crouch_late'):
        frames=[]
        for i in range(hz*7):
            f=dict(delta=1/hz,crouched=False,activate=i==0,cancel=False,interrupt=False,applyUncrouch=True,movement=True,oldVelocity=[0,0,0])
            edge=hz*51//10
            if mode.startswith('interrupt_fade') and i==edge:f['interrupt']=True
            if (mode=='cancel_fade' and i==edge) or (mode=='interrupt_fade_cancel' and i==hz*6):f['cancel']=True
            if mode in ('move_fade','interrupt_fade_move') and i>=edge:f['oldVelocity']=[1,0,0]
            if mode=='crouch_late' and i>=hz//5:f['crouched']=True;f['applyUncrouch']=False
            frames.append(f)
        traces.append(dict(hz=hz,mode=mode,frames=frames))
request={k:v for k,v in base.items() if k!='traces'};request['traces']=traces
native=json.loads(unreal.LyraEmoteOracleLibrary.read_trace(json.dumps(request,separators=(',',':'))))
assert len(native['traces'])==18
protect()
request_sha=write('emote_edges_v1_requests.json',request)
write('emote_edges_v1_native.json',dict(schemaVersion=1,policySha256=sha(root/'emote_v1_policy.json'),requestSha256=request_sha,
    previousFixtureSha256=previous,trace=native,scope=dict(originalGeneratedAbility=True,originalLyraASC=True,originalAbilityTask=True,
        controlledMovementCallback=True,wholeWorldPhysics=False,resourceNotifyDispatch=False,assetsSaved=0)))
unreal.log('LYRA_EMOTE_EDGES_OK traces=18 frames='+str(sum(len(t['frames']) for t in traces))+' previous='+str(len(previous))+' assets_saved=0')
