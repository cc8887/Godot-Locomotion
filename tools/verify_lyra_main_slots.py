"""Verify original Main Slot/cache Update and the bounded real-host integration."""
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

from locomotion_paths import project_path

repo=Path(__file__).resolve().parents[1]
root=repo/'assets/generated/lyra_als'
logs=repo/'artifacts/lyra-analysis'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
load=lambda p:json.loads((root/p).read_bytes())
data=load('main_slots_v1_native.json');requests=load('main_slots_v1_requests.json')
assert data['schemaVersion']==1 and data['rootMotionMode']==3
assert data['requestSha256']==sha(root/'main_slots_v1_requests.json')
for section in ('dependencies','previousFixtureSha256'):
    for p,d in data[section].items():assert sha(root/p)==d,p
for p,d in data['assetSha256'].items():
    assert sha(project_path('Content')/(p.split('.')[0].removeprefix('/Game/')+'.uasset'))==d,p
source=repo/'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
for fixture in (data,load('slot_pose_v1_native.json'),load('montage_sampling_v1_native.json'),load('montage_blend_v1_native.json')):
    for p,d in fixture['probeSourceSha256'].items():
        assert sha(source/p)==d,p
        for tree in ('source','package'):
            assert sha(repo/'artifacts/unreal/gasp58-lyra-masks'/tree/'AlsV4AssetExporter/Source/AlsV4AssetExporter'/p)==d,(tree,p)
frames=events=skipped=covered=hidden=inactive=upper_zero=add_skip=0
assets=set();profiles=set();hz=set();profile_pairs=set()
for trace,q in zip(data['traces'],requests['traces'],strict=True):
    assert trace['profile']==q['profile'] and trace['hz']==q['hz']
    profiles.add(trace['profile']);hz.add(trace['hz']);profile_pairs.add((trace['profile'],trace['hz']))
    for row,f in zip(trace['frames'],q['frames'],strict=True):
        frames+=1;events+=len(row['events']);skipped+=sum(s['count'] for s in row['skipped'])
        assets.update(v['asset'] for v in row['frozen'])
        covered+=f['visited'] and not any(v['id']==83 for v in row['events'])
        hidden+=not f['visited'];inactive+=sum(v['kind']=='source' and not v['active'] for v in row['events'])
        upper_zero+=sum(v['kind']=='slot' and v['id']==0 and v['root']==0 for v in row['events'])
        add_skip+=any(v['kind']=='cache' and v['id']==78 for v in row['events']) and not any(v['kind']=='slot' and v['id']==1 for v in row['events'])
assert (frames,events,skipped,covered,hidden,inactive,upper_zero,add_skip)==(47610,580701,42444,10431,1107,29013,36072,12381)
assert assets==set(range(45)) and profiles=={'unarmed','pistol','rifle'} and hz=={30,60,120} and len(profile_pairs)==9
checks={}
def read(name):return (logs/name).read_text(encoding='utf-8-sig',errors='replace')
for name in ('lyra-main-slots-fixed-ue.log','lyra-main-slots-repeat-ue.log'):
    text=read(name)
    assert text.count('LYRA_MAIN_SLOTS_NATIVE_OK frames=47610 events=580701 assets_saved=0')==1,name
    assert 'LYRA_EXPORT_PROCESS_EXIT_OK mode=main-slots code=0' in text,name
    assert not re.search(r'LogPython: Error|Assertion failed|Ensure condition failed|LYRA_MAIN_SLOTS_CAPTURE_FAILED',text),name
    checks[name]=dict(exit=0,warnings=len(re.findall('Warning:',text)))
for name in ('main-slots-physical-build.log','main-slots-optimize-final.log'):
    text=read(name);assert re.search(r'0\s*(个警告|Warning)',text) and re.search(r'0\s*(个错误|Error)',text),name
    checks[name]=dict(errors=0,warnings=0)
markers={
    'main-slots-godot-first.log':['LYRA_MAIN_SLOTS_GODOT_OK frames=47610 events=580701'],
    'main-slots-host-godot-physical.log':['LYRA_MAIN_SLOTS_HOST_GODOT_OK frames=40320 retry=40320 covered=10269'],
    'main-slots-optimize-godot.log':['LYRA_MAIN_SLOTS_GODOT_OK frames=47610 events=580701','LYRA_MAIN_SLOTS_HOST_GODOT_OK frames=40320 retry=40320 covered=10269'],
    'main-slots-cache-regression.log':['LYRA_MAIN_CACHE_GODOT_OK frames=2520'],
    'main-slots-main-feedback-regression.log':['LYRA_MAIN_POSE_HOST_FEEDBACK_GODOT_OK frames=11340']}
for name,expected in markers.items():
    text=read(name)
    for marker in expected:assert text.count(marker)==1,(name,marker)
    assert text.count('LYRA_GODOT_PROCESS_EXIT_OK code=0')==len(expected) and not re.search('ERROR:|WARNING:',text),name
    checks[name]=dict(exit=0)
text=read('main-slots-optimize-godot.log');assert 'LYRA_DEBUG_ASSEMBLIES_RESTORED hashVerified=true' in text
for name in ('GodotALS.dll','Als.Core.dll','Als.Import.dll'):
    assert f'LYRA_OPTIMIZED_ASSEMBLY {name} SHA256={sha(repo/".godot/mono/temp/bin/ExportRelease"/name).upper()}' in text,name
for name in ('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb'):
    assert sha(repo/'.godot/mono/temp/bin/Debug'/name)==sha(logs/'main-slots-debug-assemblies'/name),name
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
c=ET.parse(logs/'main-slots-core.trx').getroot().find('t:ResultSummary/t:Counters',ns).attrib
assert c['total']==c['passed']=='243' and c['failed']==c['notExecuted']=='0'
text=read('main-slots-build-fixed.log');assert 'BUILD SUCCESSFUL' in text and 'GODOT_ALS_EXTERNAL_EXPORTER_OK' in text and 'error C' not in text
report=dict(frames=frames,events=events,skipped=skipped,covered=covered,hidden=hidden,inactive=inactive,
    upperRootZero=upper_zero,additiveSkipped=add_skip,hostFrames=40320,hostCovered=10269,hostPlayers=105961,
    corePassed=243,packages=len(data['assetSha256']),previous=len(data['previousFixtureSha256']),checks=checks,
    activeSlotUpdateHost=True,activeSlotPoseHost=False,nativeWholeMain=False,production=False,wholeGoal=False)
(logs/'lyra-main-slots-verification.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('LYRA_MAIN_SLOTS_FINAL_VERIFIED',json.dumps(report),'whole_goal=false')
