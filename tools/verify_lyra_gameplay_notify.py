"""Original Blueprint delivery, production transactions and preserved resources."""
import hashlib
import json
import re
from pathlib import Path

repo=Path(__file__).resolve().parents[1]
root=repo/'assets/generated/lyra_als'
logs=repo/'artifacts/lyra-analysis'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
load=lambda p:json.loads(p.read_bytes())
def read(p):
    data=p.read_bytes()
    if data.startswith((b'\xff\xfe',b'\xfe\xff')):return data.decode('utf-16')
    try:return data.decode('utf-8-sig')
    except UnicodeDecodeError:return data.decode('gb18030')
native=load(root/'gameplay_notify_dispatch_v1_native.json')
assert native['schemaVersion']==1
for name,digest in {**native['dependencies'],**native['previousFixtureSha256']}.items():assert sha(root/name)==digest,name
for name,digest in native['pluginSourceSha256'].items():
    assert sha(repo/'tools/unreal/LyraGameplayNotifyOracle'/name)==digest,name
    if name.startswith('Source/'):assert sha(repo/'artifacts/unreal/lyra-gameplay-notify-oracle/package-second'/name)==digest,name
assert native['scope']==dict(assetsSaved=0,originalBlueprintFunctions=True,abilityEventDelivery=True,
                            gameplayAbilitiesActivated=False,wholeMainContinuous=False,allTypedConsumers=False)
contract=load(root/'notify_contract_v1.json')
for path,digest in contract['assetSha256'].items():
    assert sha(repo.parent/'GASP58/Content'/(path.split('.')[0].removeprefix('/Game/')+'.uasset'))==digest,path
assets={a['source']:a for a in contract['assets']}
tags={
 '/Game/Characters/Heroes/Abilities/AN_Melee.AN_Melee_C':'GameplayEvent.MeleeHit',
 '/Game/Characters/Heroes/Abilities/AN_Reload.AN_Reload_C':'GameplayEvent.ReloadDone',
 '/Game/Effects/AnimationNotifies/AN_FootPlant_Left.AN_FootPlant_Left_C':None,
 '/Game/Effects/AnimationNotifies/AN_FootPlant_Right.AN_FootPlant_Right_C':None}
expected=[];objects=set()
for i,(call,result) in enumerate(zip(native['request']['calls'],native['trace']['calls'],strict=True)):
    event=assets[call['asset']]['events'][call['index']]
    objects.add(event['notifyObject'])
    assert result['object']==event['notifyObject'] and result['class']==event['notifyClass'] and result['returnValue'] is False
    if call['actor']!=2 and tags[event['notifyClass']]:
        expected.append(dict(call=i,actor=call['actor'],tag=tags[event['notifyClass']],payloadEventTag='None',magnitude=0,defaultPayload=True))
    assert result['eventCount']==len(expected)
assert len(objects)==747 and len(native['request']['calls'])==3735 and native['trace']['events']==expected and len(expected)==36
for name in ('notify-gameplay-ue-fourth.log','notify-gameplay-ue-repeat.log'):
    text=read(logs/name)
    assert text.count('LYRA_GAMEPLAY_NOTIFY_NATIVE_OK objects=747 calls=3735 events=36 packages=676 previous=824 assets_saved=0')==1
    assert 'LYRA_EXPORT_PROCESS_EXIT_OK mode=gameplay-notify code=0' in text and 'Python script executed successfully' in text
    assert not re.search(r'LogPython: Error:|Fatal error:|Assertion failed:|Ensure condition failed:|LogAbilitySystem: Error:',text)
roles={};gates={}
for config in ('debug','optimize'):
    build=read(logs/f'notify-gameplay-{config}-build-final.log')
    assert re.search(r'0\s*(个警告|Warning)',build) and re.search(r'0\s*(个错误|Error)',build)
    text=read(logs/f'notify-gameplay-{config}-matrix.log')
    assert not re.search(r'^\s*(ERROR|WARNING):',text,re.M)
    assert len(re.findall(r'LYRA_GAMEPLAY_PROCESS_EXIT label=\S+ code=0',text))==7
    assert 'LYRA_GAMEPLAY_NOTIFY_GODOT_OK calls=3735 events=36 retries=3735 rejected=18750 lateReceivers=36 reentrant=36 native=True allConsumers=False' in text
    assert 'LYRA_MONTAGE_NOTIFY_OK ' in text and re.search(r'LYRA_MONTAGE_NOTIFY_OK .* frames=33235 .* native=True',text)
    assert re.search(r'LYRA_NOTIFY_QUEUE_OK .* native=True ',text)
    roles[config]=[]
    for hz in (30,60,120):
        row=load(logs/f'notify-gameplay-pairs-{config}-{hz}.json')
        assert row['hz']==hz and row['roleFrames']==hz*8*6 and row['switches']==24 and row['replacements']==2
        assert row['gameplaySignals']==4 and row['meleeSignals']==2 and row['reloadSignals']==2 and row['gameplayNotifyConsumer']
        assert row['samePoseAndHistory'] and row['actualGodotPhysics'] and not row['nativeWholeMainParity'] and not row['productionAccepted']
        roles[config].append(row)
    ordinary=load(logs/f'notify-gameplay-ten-{config}.json')
    assert ordinary['characters']==10 and len(ordinary['companions'])==9
    player=ordinary['player']['model']
    assert player['frames']==480 and player['gameplayNotifyConsumer'] and player['montageNotifyCallbacks']==0
    assert not player['typedNotifyConsumers'] and not player['contextEffectsConsumer'] and not player['weaponNotifyConsumer'] and not player['motionWarpingConsumer']
    assert all(c['published']==480 for c in ordinary['companions'])
    gates[config]=dict(roles=roles[config],ordinary=ordinary)
assert gates['debug']==gates['optimize'],'Debug and Optimize reports differ'
assert 'LYRA_GAMEPLAY_DEBUG_RESTORED hashVerified=true' in read(logs/'notify-gameplay-optimize-matrix.log')
for file in ('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb'):
    assert sha(repo/'.godot/mono/temp/bin/Debug'/file)==sha(logs/'notify-gameplay-debug-assemblies'/file),file
result=dict(schemaVersion=1,nativeSha256=sha(root/'gameplay_notify_dispatch_v1_native.json'),
    originalObjects=747,originalCalls=3735,nativeGameplayEvents=36,originalPassiveFootPlantObjects=738,
    protectedJson=len(native['previousFixtureSha256']),protectedPackages=len(contract['assetSha256']),
    gates=gates,scope=native['scope'])
output=logs/'lyra-gameplay-notify-verification.json'
with output.open('x',encoding='utf-8',newline='\n') as f:f.write(json.dumps(result,separators=(',',':'))+'\n')
print('LYRA_GAMEPLAY_NOTIFY_VERIFIED objects=747 calls=3735 events=36 production_events=12/config source_and_montage_regressions=True')
