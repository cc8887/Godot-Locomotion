"""Verify original Main75 composition and its bounded actual Main host.

This does not certify the complete original Main graph or production Demo.
"""
import hashlib
import json
import re
import struct
import xml.etree.ElementTree as ET
from pathlib import Path

repo=Path(__file__).resolve().parents[1]
root=repo/'assets/generated/lyra_als'
logs=repo/'artifacts/lyra-analysis'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
load=lambda p:json.loads((root/p).read_bytes())
read=lambda p:(logs/p).read_text(encoding='utf-8-sig',errors='replace')
data=load('main_inertia_v1_native.json')
requests=load('main_inertia_v1_requests.json')
policy=load('main_composition_v2_policy.json')
assert data['schemaVersion']==1 and data['rootMotionMode']==3
assert data['requestSha256']==sha(root/'main_inertia_v1_requests.json')
source=repo/'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
fixture_names=['main_inertia_v1_native.json','main_inertia_debug_v1_native.json',
    'main_inertia_stages_v1_native.json','main_inertia_stages_v2_native.json',
    'main_slot_composition_v2_native.json','main_slot_composition_v1_native.json',
    'main_slot_composition_debug_v1_native.json','main_slots_v1_native.json',
    'slot_pose_v1_native.json','montage_sampling_v1_native.json','montage_blend_v1_native.json']
for name in fixture_names:
    fixture=load(name)
    for p,d in fixture['probeSourceSha256'].items():
        assert sha(source/p)==d,(name,p)
        for tree in ('source','package'):
            assert sha(repo/'artifacts/unreal/gasp58-lyra-masks'/tree/'AlsV4AssetExporter/Source/AlsV4AssetExporter'/p)==d,(tree,p)
for name in fixture_names[:4]:
    fixture=load(name)
    for section in ('dependencies','previousFixtureSha256'):
        for p,d in fixture[section].items():assert sha(root/p)==d,(name,p)
for p,d in data['assetSha256'].items():
    assert sha(Path('../GASP58/Content')/(p.split('.')[0].removeprefix('/Game/')+'.uasset'))==d,p
frames=poses=curves=attributes=roots=request_count=0
pairs=set();assets=set();profile_assets=set()
for trace,q in zip(data['traces'],requests['traces'],strict=True):
    assert trace['proxySkeletonMatchesTarget'] and trace['mask']==policy['policies'][trace['profile']]['mask']
    assert trace['profile']==q['profile'] and trace['hz']==q['hz'];pairs.add((trace['profile'],trace['hz']))
    for row,f in zip(trace['frames'],q['frames'],strict=True):
        frames+=1;assets.update(v['asset'] for v in row['frozen']);profile_assets.update(v['asset'] for v in row['frozen'] if v['profile'])
        # The requester receives float durations from the double JSON input.
        expected_requests=[struct.unpack('f',struct.pack('f',v))[0] for v in f['inertia']] if f['visited'] else []
        assert row['inertiaRequests']==expected_requests
        request_count+=len(row['inertiaRequests'])
        assert len(row['outputs'])==(2 if f['sample'] else 0)
        for o in row['outputs']:
            poses+=1;curves+=len(o['curves']);attributes+=len(o['attributes']);roots+=int('rootMotion' in o)
            assert len(o['pose'])==81
assert (frames,poses,curves,attributes,roots)==(40320,11250,24768,37392,8340)
assert len(pairs)==9 and assets==set(range(45)) and profile_assets and request_count>0
stages=load('main_inertia_stages_v2_native.json')['traces'][0]['frames']
assert len(stages)==1920
for stage,original in zip(stages,data['traces'][0]['frames'],strict=True):
    assert stage['outputs']==original['outputs'], 'Stage instrumentation changed original outputs.'
checks={}
for name,marker in {
    'lyra-main-inertia-ue.log':'LYRA_MAIN_INERTIA_NATIVE_OK frames=40320 poses=11250 assets_saved=0',
    'lyra-main-inertia-repeat-ue.log':'LYRA_MAIN_INERTIA_NATIVE_OK frames=40320 poses=11250 assets_saved=0',
    'lyra-main-inertia-stages-v2-ue.log':'LYRA_MAIN_INERTIA_STAGES_NATIVE_OK frames=1920 poses=1250 assets_saved=0',
}.items():
    text=read(name);assert text.count(marker)==1 and re.search('LYRA_EXPORT_PROCESS_EXIT_OK mode=.+ code=0',text),name
    assert not re.search('LogPython: Error|Assertion failed|Ensure condition failed|LYRA_MAIN_INERTIA_FAILED',text),name
    checks[name]=dict(exit=0,warnings=len(re.findall('Warning:',text)),errors=len(re.findall('Error:',text)))
for name in ('main-inertia-ispc-mesh-order-build.log','main-inertia-optimize-build.log'):
    text=read(name);assert re.search(r'0\s*(个警告|Warning)',text) and re.search(r'0\s*(个错误|Error)',text),name
    checks[name]=dict(errors=0,warnings=0)
component='LYRA_MAIN_INERTIA_GODOT_OK frames=40320 poses=11250 covered=2910 caches=25020 retry=40320'
host='LYRA_MAIN_INERTIA_HOST_GODOT_OK frames=40320 poses=39375 feedbackCurves=354375 fullChecks=10269'
for name,markers in {
    'main-inertia-godot-ispc-mesh-order.log':[component],
    'main-inertia-host-godot-mesh-order.log':[host],
    'main-inertia-optimize-godot.log':[component,host],
    'main-inertia-slot-composition-mesh-order.log':['LYRA_MAIN_SLOT_COMPOSITION_GODOT_OK frames=47610 poses=19944'],
    'main-inertia-godot-mesh-order-diagnostic.log':['LYRA_MAIN_INERTIA_DIAGNOSTIC_DONE failures=0 frames=1920 poses=1250'],
}.items():
    text=read(name)
    assert text.count('LYRA_GODOT_PROCESS_EXIT_OK code=0')==len(markers) and not re.search('ERROR:|WARNING:',text),name
    for marker in markers:assert text.count(marker)==1,(name,marker)
    if component in markers:
        assert 'frames=22500 bones=1822500 curves=49536 attributes=74784' in text
        assert 'roots=16680 rootP=0 rootQ=0 rootS=0' in text
    if host in markers:assert 'inertiaActive=4263 inertiaRequests=395' in text
    checks[name]=dict(exit=0)
optimized=read('main-inertia-optimize-godot.log')
assert 'LYRA_DEBUG_ASSEMBLIES_RESTORED hashVerified=true' in optimized
for name in ('GodotALS.dll','Als.Core.dll','Als.Import.dll'):
    assert f'LYRA_OPTIMIZED_ASSEMBLY {name} SHA256={sha(repo/".godot/mono/temp/bin/ExportRelease"/name).upper()}' in optimized
for name in ('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb'):
    assert sha(repo/'.godot/mono/temp/bin/Debug'/name)==sha(logs/'main-inertia-debug-assemblies'/name)
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
for name,count in [('main-inertia-math-final.trx',52),('main-inertia-standing-final.trx',6)]:
    counters=ET.parse(logs/name).getroot().find('t:ResultSummary/t:Counters',ns).attrib
    assert counters['total']==counters['passed']==str(count) and counters['failed']==counters['notExecuted']=='0',name
for name,label in [('main-inertia-godot-ispc-additive.log','MainInertia/0/162/bone52'),
                   ('main-inertia-godot-double-normalize.log','MainInertia/0/612/bone66'),
                   ('main-inertia-godot-ispc-full-additive.log','MainInertia/0/951/bone74')]:
    assert label in read(name) and 'LYRA_GODOT_PROCESS_EXIT_OK code=1' in read(name)
comparison=re.search(r'positionCm=(\S+) quaternion=(\S+) scale=(\S+)',read('main-inertia-godot-ispc-mesh-order.log'))
assert float(comparison[1])<=1e-8 and float(comparison[2])<=1e-10 and float(comparison[3])<=1e-12
report=dict(frames=frames,nativePoses=poses,comparedPoses=poses*2,bones=poses*2*81,
    curves=curves*2,attributes=attributes*2,rootComparisons=roots*2,rootExact=True,
    maxPositionCm=float(comparison[1]),maxQuaternion=float(comparison[2]),maxScale=float(comparison[3]),
    nativeRequests=request_count,hostFrames=40320,hostPoses=39375,hostFeedbackCurves=354375,
    hostInertiaActive=4263,hostInertiaRequests=395,corePassed=52,standingNativePassed=6,
    packages=len(data['assetSha256']),previous=len(data['previousFixtureSha256']),checks=checks,
    main75Component=True,main75Host=True,nativeWholeMain=False,production=False,wholeGoal=False)
(logs/'lyra-main-inertia-verification.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('LYRA_MAIN_INERTIA_FINAL_VERIFIED',json.dumps(report),'whole_goal=false')
