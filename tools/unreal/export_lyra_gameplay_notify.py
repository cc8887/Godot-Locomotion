"""Execute original Melee/Reload/passive FootPlant Blueprint notify objects."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root=Path(os.environ['LYRA_OUTPUT_ROOT'])
repo=Path(__file__).resolve().parents[2]
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
contract_path=root/'notify_contract_v1.json'
contract=json.loads(contract_path.read_bytes())
previous={p.relative_to(root).as_posix():sha(p) for p in root.rglob('*.json')
          if p.name not in ('gameplay_notify_dispatch_v1_native.json',)}
content=Path(unreal.Paths.project_content_dir())
project=Path(unreal.Paths.get_project_file_path());project_sha=sha(project)
def protect():
    assert sha(project)==project_sha
    for name,digest in previous.items():assert sha(root/name)==digest,name
    for path,digest in contract['assetSha256'].items():
        assert sha(content/(path.split('.')[0].removeprefix('/Game/')+'.uasset'))==digest,path
protect()
classes={
 '/Game/Characters/Heroes/Abilities/AN_Melee.AN_Melee_C':'GameplayEvent.MeleeHit',
 '/Game/Characters/Heroes/Abilities/AN_Reload.AN_Reload_C':'GameplayEvent.ReloadDone',
 '/Game/Effects/AnimationNotifies/AN_FootPlant_Left.AN_FootPlant_Left_C':None,
 '/Game/Effects/AnimationNotifies/AN_FootPlant_Right.AN_FootPlant_Right_C':None}
calls=[];expected=[];objects=[]
for asset in contract['assets']:
    for event in asset['events']:
        cls=event['notifyClass']
        if cls not in classes:continue
        objects.append(event['notifyObject'])
        for actor in (0,1,0,1,2):
            index=len(calls)
            calls.append(dict(asset=asset['source'],index=event['index'],actor=actor))
            if actor!=2 and classes[cls]:
                expected.append(dict(call=index,actor=actor,tag=classes[cls],payloadEventTag='None',magnitude=0,defaultPayload=True))
request=dict(schemaVersion=1,calls=calls)
trace=json.loads(unreal.LyraGameplayNotifyOracleLibrary.read_trace(json.dumps(request)))
assert len(trace['calls'])==len(calls) and trace['events']==expected, (len(trace['calls']),len(trace['events']),trace['events'][:5],expected[:5])
for index,(call,actual) in enumerate(zip(calls,trace['calls'])):
    event=next(a for a in contract['assets'] if a['source']==call['asset'])['events'][call['index']]
    assert actual['object']==event['notifyObject'] and actual['class']==event['notifyClass']
    assert actual['returnValue'] is False
    assert actual['eventCount']==sum(e['call']<=index for e in expected)
protect()
plugin=repo/'tools/unreal/LyraGameplayNotifyOracle'
hashes={p.relative_to(plugin).as_posix():sha(p) for p in sorted(plugin.rglob('*'))
        if p.is_file() and p.suffix in ('.cpp','.h','.cs','.uplugin') and not any(v in p.parts for v in ('Intermediate','Binaries'))}
result=dict(schemaVersion=1,dependencies={'notify_contract_v1.json':sha(contract_path)},previousFixtureSha256=previous,
    pluginSourceSha256=hashes,engineVersion=unreal.SystemLibrary.get_engine_version(),request=request,trace=trace,
    scope=dict(assetsSaved=0,originalBlueprintFunctions=True,abilityEventDelivery=True,
               gameplayAbilitiesActivated=False,wholeMainContinuous=False,allTypedConsumers=False))
output=root/'gameplay_notify_dispatch_v1_native.json'
if output.exists():assert json.loads(output.read_bytes())==result,'Independent gameplay dispatch differs'
else:
    with output.open('x',encoding='utf-8',newline='\n') as f:f.write(json.dumps(result,separators=(',',':'))+'\n')
unreal.log(f'LYRA_GAMEPLAY_NOTIFY_NATIVE_OK objects={len(objects)} calls={len(calls)} events={len(expected)} packages={len(contract["assetSha256"])} previous={len(previous)} assets_saved=0')
