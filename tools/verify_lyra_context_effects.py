"""Original interfaces, library semantics and committed production delivery."""
import hashlib
import json
import re
import subprocess
from pathlib import Path

from locomotion_paths import unreal_source_path

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
native=load(root/'context_effects_v1_native.json')
policy=load(root/'context_effects_v1_policy.json')
for name,digest in {**native['dependencies'],**native['previousFixtureSha256']}.items():assert sha(root/name)==digest,name
for name,digest in policy['dependencies'].items():assert sha(root/name)==digest,name
for name,digest in native['pluginSourceSha256'].items():
    assert sha(repo/'tools/unreal/LyraContextEffectsOracle'/name)==digest,name
    assert sha(repo/'artifacts/unreal/lyra-context-effects-oracle/package'/name)==digest,name
project=repo.parent/'GASP58'
assert not (project/'Plugins/LyraContextEffectsOracle').exists(),'Temporary plugin must be archived outside the project'
for name,digest in native['protectedProjectSha256'].items():assert sha(project/name)==digest,name
for path,digest in policy['assetSha256'].items():assert sha(project/'Content'/(path.split('.')[0].removeprefix('/Game/')+'.uasset'))==digest,path
original=subprocess.check_output(['git','-C',str(unreal_source_path()),'show',policy['sourceConfig']['ref']+':'+policy['sourceConfig']['path']]).decode('utf-8')
capture=logs/'lyra-original-context-DefaultGame.ini'
assert sha(capture)==policy['sourceConfig']['sha256']
assert original.splitlines()==capture.read_text(encoding='utf-8-sig').splitlines()
assert policy['surfaceMap']==[dict(surface=i,tag='SurfaceType.'+t) for i,t in enumerate(('Default','Character','Concrete','Glass'))]
assert policy['settings']==[] and len(policy['rows'])==6 and len(policy['queries'])==64
assert all(e['audio'] and not e['vfx'] for r in policy['rows'] for e in r['effects'])
contract=load(root/'notify_contract_v1.json');assets={a['source']:a for a in contract['assets']}
objects=set()
assert len(native['request']['calls'])==4121 and len(native['trace']['messages'])==8242
for i,(call,result) in enumerate(zip(native['request']['calls'],native['trace']['calls'],strict=True)):
    e=assets[call['asset']]['events'][call['index']];objects.add(e['notifyObject'])
    assert e['notifyClass']=='/Script/LyraGame.AnimNotify_LyraContextEffects' and result['object']==e['notifyObject']
    a,b=native['trace']['messages'][i*2:i*2+2]
    assert a['call']==b['call']==i and a['receiver']=='actor' and b['receiver']=='component'
    assert a['contexts']==b['contexts']==[] and a['hit']==(call['scenario']!=4)
    assert a['physicalMaterial']==a['hit'] and a['bone']==e['payload']['socketName']
    assert result['messages']==2*(i+1) and b['vfxCount']==0
    for k in ('bone','effect','animation','hit','location','rotation','scale','volume','pitch','point','normal','physicalMaterial','surface','contexts'):assert a[k]==b[k],(i,k)
assert len(objects)==686
for name in ('notify-context-ue-final.log','notify-context-ue-repeat.log'):
    text=read(logs/name)
    assert text.count('LYRA_CONTEXT_EFFECTS_NATIVE_OK objects=686 calls=4121 messages=8242 queries=64 packages=682 previous=825 assets_saved=0')==1
    assert 'LYRA_CONTEXT_EXPORT_PROCESS_EXIT code=0' in text and 'Python script executed successfully' in text
    assert not re.search(r'LogPython: Error:|Fatal error:|Assertion failed:|Ensure condition failed:',text)
roles={};ordinary={}
for config in ('debug','optimize'):
    build=read(logs/f'notify-context-{config}-build-accepted.log')
    assert re.search(r'0\s*(个警告|Warning)',build) and re.search(r'0\s*(个错误|Error)',build)
    text=read(logs/f'notify-context-{config}-matrix.log')
    assert not re.search(r'^\s*(ERROR|WARNING):',text,re.M)
    assert len(re.findall(r'LYRA_CONTEXT_PROCESS_EXIT label=\S+ code=0',text))==8
    assert 'LYRA_CONTEXT_EFFECTS_GODOT_OK calls=4121 frames=4139 filtered=18 messages=8242 queries=64 retries=4121 rejected=40523 lateContacts=3434 native=True audioPlayback=False' in text
    assert 'LYRA_GAMEPLAY_NOTIFY_GODOT_OK calls=3735 events=36 ' in text
    assert re.search(r'LYRA_MONTAGE_NOTIFY_OK .* frames=33235 .* native=True',text) and 'LYRA_NOTIFY_QUEUE_OK ' in text
    roles[config]=[]
    for hz in (30,60,120):
        row=load(logs/f'notify-context-pairs-{config}-{hz}.json')
        assert row['hz']==hz and row['roleFrames']==hz*8*6 and row['switches']==24 and row['replacements']==2
        assert row['gameplaySignals']==4 and row['contextEffectsSignals']>0 and row['contextAudioSelections']>0
        assert row['typedCallbackOrder'] and row['contextEffectsConsumer'] and not row['audioPlayback']
        assert row['samePoseAndHistory'] and row['actualGodotPhysics'] and not row['nativeWholeMainParity']
        roles[config].append(row)
    ordinary[config]=load(logs/f'notify-context-ten-{config}.json')
    row=ordinary[config]
    assert row['characters']==10 and row['player']['switches']==6
    model=row['player']['model']
    assert model['frames']==model['published']==480 and model['sourceNotifyFrames']==4800
    assert model['contextEffectsConsumer'] and model['contextMessages']>0 and model['contextContacts']>0 and not model['contextAudioPlayback']
    assert model['montageNotifyCallbacks']==0 and not model['typedNotifyConsumers'] and not model['nativeWholeMainParity']
    assert len(row['companions'])==9 and all(c['published']==480 for c in row['companions'])
assert roles['debug']==roles['optimize'] and ordinary['debug']==ordinary['optimize']
assert 'LYRA_CONTEXT_DEBUG_RESTORED hashVerified=true' in read(logs/'notify-context-optimize-matrix.log')
backup=logs/'notify-context-debug-assemblies'
for name in ('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb'):
    assert sha(repo/'.godot/mono/temp/bin/Debug'/name)==sha(backup/name),name
result=dict(schemaVersion=1,originalObjects=686,nativeCalls=4121,nativeMessages=8242,nativeLibraryQueries=64,
    previousJsonPreserved=len(native['previousFixtureSha256']),packagesPreserved=len(policy['assetSha256']),
    standaloneFrames=4139,filteredFrames=18,retries=4121,rejected=40523,lateContacts=3434,
    pairedRoleFrames=sum(r['roleFrames'] for r in roles['debug']),pairedContextMessages=sum(r['contextEffectsSignals'] for r in roles['debug']),
    pairedAudioSelections=sum(r['contextAudioSelections'] for r in roles['debug']),ordinaryContextMessages=ordinary['debug']['player']['model']['contextMessages'],
    ordinaryContextContacts=ordinary['debug']['player']['model']['contextContacts'],sameBuildReports=True,debugRestored=True,
    audioPlayback=False,wholeMainNative=False,allNotifyConsumers=False)
output=logs/'lyra-context-effects-verification.json'
if output.exists():assert load(output)==result
else:output.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('LYRA_CONTEXT_EFFECTS_VERIFICATION_OK '+json.dumps(result,separators=(',',':')))
