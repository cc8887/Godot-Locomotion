"""Verify five Slot pose composition and the bounded Main pose host."""
import hashlib
import json
import math
import re
import xml.etree.ElementTree as ET
from pathlib import Path

repo=Path(__file__).resolve().parents[1]
root=repo/'assets/generated/lyra_als'
logs=repo/'artifacts/lyra-analysis'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
load=lambda p:json.loads((root/p).read_bytes())
read=lambda p:(logs/p).read_text(encoding='utf-8-sig',errors='replace')
data=load('main_slot_composition_v2_native.json')
requests=load('main_slot_composition_v2_requests.json')
policy=load('main_composition_v2_policy.json')
assert data['schemaVersion']==1 and data['rootMotionMode']==3
assert data['requestSha256']==sha(root/'main_slot_composition_v2_requests.json')
source=repo/'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
# Preserve rejected V1 and diagnostic captures as evidence. Only V2 is the
# accepted pose oracle; its transient proxy refreshes the target skeleton.
fixtures=[data,load('main_slot_composition_v1_native.json'),load('main_slot_composition_debug_v1_native.json'),
          load('main_slots_v1_native.json'),load('slot_pose_v1_native.json'),
          load('montage_sampling_v1_native.json'),load('montage_blend_v1_native.json')]
for fixture in fixtures:
    for p,d in fixture['probeSourceSha256'].items():
        assert sha(source/p)==d,p
        for tree in ('source','package'):
            assert sha(repo/'artifacts/unreal/gasp58-lyra-masks'/tree/'AlsV4AssetExporter/Source/AlsV4AssetExporter'/p)==d,(tree,p)
for section in ('dependencies','previousFixtureSha256'):
    for p,d in data[section].items():assert sha(root/p)==d,p
for p,d in data['assetSha256'].items():
    assert sha(Path('../GASP58/Content')/(p.split('.')[0].removeprefix('/Game/')+'.uasset'))==d,p
frames=poses=attributes=roots=0
pairs=set();assets=set();profile_assets=set()
for trace,q in zip(data['traces'],requests['traces'],strict=True):
    profile=trace['profile'];assert trace['proxySkeletonMatchesTarget']
    assert trace['mask']==policy['policies'][profile]['mask']
    assert profile==q['profile'] and trace['hz']==q['hz'];pairs.add((profile,trace['hz']))
    for row,f in zip(trace['frames'],q['frames'],strict=True):
        frames+=1;assets.update(v['asset'] for v in row['frozen']);profile_assets.update(v['asset'] for v in row['frozen'] if v['profile'])
        outputs=row['outputs'];assert len(outputs)==(2 if f['sample'] else 0)
        if outputs:assert outputs[0]==outputs[1]
        for o in outputs:
            poses+=1;attributes+=len(o['attributes']);roots+=int('rootMotion'in o)
            assert len(o['pose'])==81
assert (frames,poses,attributes,roots)==(47610,19944,74856,17976)
assert len(pairs)==9 and assets==set(range(45)) and profile_assets
bad=load('main_slot_composition_debug_v1_native.json')['traces'][0]['frames'][473]['stages']
# The rejected fixture blended no upper pose after skeleton replacement.
for axis in range(3):
    assert bad['cache78']['pose'][65]['position'][axis]==bad['leaf83']['pose'][65]['position'][axis]+bad['slot1']['pose'][65]['position'][axis]
assert bad['slot0']['pose'][65]['position']!=bad['cache78']['pose'][65]['position']
assert 'MainSlotComposition/0/473/bone5' in read('main-slot-composition-godot-root-fixture.log')
checks={}
for name in ('lyra-main-slot-composition-v2-ue.log','lyra-main-slot-composition-v2-repeat-ue.log'):
    text=read(name)
    assert text.count('LYRA_MAIN_SLOT_COMPOSITION_NATIVE_OK frames=47610 poses=19944 assets_saved=0')==1,name
    assert 'LYRA_EXPORT_PROCESS_EXIT_OK mode=main-slot-composition-v2 code=0'in text,name
    assert not re.search(r'LogPython: Error|Assertion failed|Ensure condition failed|LYRA_MAIN_SLOT_COMPOSITION_FAILED',text),name
    checks[name]=dict(exit=0,warnings=len(re.findall('Warning:',text)),errors=len(re.findall('Error:',text)))
for name in ('main-slot-composition-debug-final-build.log','main-slot-pose-optimize.log'):
    text=read(name);assert re.search(r'0\s*(个警告|Warning)',text) and re.search(r'0\s*(个错误|Error)',text),name
    checks[name]=dict(errors=0,warnings=0)
component='LYRA_MAIN_SLOT_COMPOSITION_GODOT_OK frames=47610 poses=19944 covered=1968 caches=53928 retry=47610'
host='LYRA_MAIN_SLOT_POSE_HOST_GODOT_OK frames=40320 poses=39375 feedbackCurves=354375 fullChecks=10269'
for name,markers in {
    'main-slot-composition-godot-v2-first.log':[component],
    'main-slot-pose-host-godot-final.log':[host],
    'main-slot-pose-optimize-godot.log':[component,host],
    'main-slot-pose-cache-regression.log':['LYRA_MAIN_CACHE_GODOT_OK frames=2520'],
    'main-slot-pose-feedback-regression.log':['LYRA_MAIN_POSE_HOST_FEEDBACK_GODOT_OK frames=11340 poses=9762'],
    'main-slot-pose-update-regression.log':['LYRA_MAIN_SLOTS_GODOT_OK frames=47610 events=580701'],
    'main-slot-pose-host-update-regression.log':['LYRA_MAIN_SLOTS_HOST_GODOT_OK frames=40320 retry=40320 covered=10269'],
}.items():
    text=read(name)
    for marker in markers:assert text.count(marker)==1,(name,marker)
    assert text.count('LYRA_GODOT_PROCESS_EXIT_OK code=0')==len(markers) and not re.search('ERROR:|WARNING:',text),name
    if component in markers:
        assert 'frames=39888 bones=3230928'in text and 'rootP=0 rootQ=0 rootS=0'in text
    checks[name]=dict(exit=0)
optimized=read('main-slot-pose-optimize-godot.log')
assert 'LYRA_DEBUG_ASSEMBLIES_RESTORED hashVerified=true'in optimized
for name in ('GodotALS.dll','Als.Core.dll','Als.Import.dll'):
    assert f'LYRA_OPTIMIZED_ASSEMBLY {name} SHA256={sha(repo/".godot/mono/temp/bin/ExportRelease"/name).upper()}'in optimized
for name in ('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb'):
    assert sha(repo/'.godot/mono/temp/bin/Debug'/name)==sha(logs/'main-slot-pose-debug-assemblies'/name)
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
c=ET.parse(logs/'main-slot-pose-core.trx').getroot().find('t:ResultSummary/t:Counters',ns).attrib
assert c['total']==c['passed']=='243' and c['failed']==c['notExecuted']=='0'
text=read('main-slot-composition-v2-probe-build.log')
assert 'BUILD SUCCESSFUL'in text and 'GODOT_ALS_EXTERNAL_EXPORTER_OK'in text and 'error C'not in text
comparison=re.search(r'positionCm=(\S+) quaternion=(\S+) scale=(\S+)',read('main-slot-composition-godot-v2-first.log'))
report=dict(frames=frames,nativePoses=poses,comparedPoses=poses*2,bones=poses*2*81,attributes=attributes*2,rootComparisons=roots*2,
    maxPositionCm=float(comparison[1]),maxQuaternion=float(comparison[2]),maxScale=float(comparison[3]),rootExact=True,
    hostFrames=40320,hostPoses=39375,hostCovered=10269,hostFeedbackCurves=354375,
    corePassed=243,packages=len(data['assetSha256']),previous=len(data['previousFixtureSha256']),checks=checks,
    activeSlotPoseHost=True,nativeWholeMain=False,production=False,wholeGoal=False)
(logs/'lyra-main-slot-pose-verification.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('LYRA_MAIN_SLOT_POSE_FINAL_VERIFIED',json.dumps(report),'whole_goal=false')
