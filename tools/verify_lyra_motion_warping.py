"""Verify original policies, continuous component parity and unchanged production regressions."""
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from functools import cache
from pathlib import Path

repo=Path(__file__).resolve().parents[1]
root=repo/'assets/generated/lyra_als'
logs=repo/'artifacts/lyra-analysis'
project=repo.parent/'GASP58'
@cache
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def load(p):return json.loads(p.read_bytes())
def read(p):
    b=p.read_bytes();return b.decode('utf-16' if b.startswith((b'\xff\xfe',b'\xfe\xff')) else 'utf-8-sig')
def package_file(path):
    path=path.split('.')[0]
    if path.startswith('/Game/'):return project/'Content'/(path.removeprefix('/Game/')+'.uasset')
    if path.startswith('/ShooterCore/'):return project/'Plugins/GameFeatures/ShooterCore/Content'/(path.removeprefix('/ShooterCore/')+'.uasset')
    raise ValueError(path)
prefix='motion_warping_v1_'
policy=load(root/(prefix+'policy.json'))
usage=load(root/(prefix+'usage.json'))
request=load(root/(prefix+'requests.json'))
native=load(root/(prefix+'native.json'))
feature=load(root/(prefix+'game_feature.json'))
assert usage['policySha256']==sha(root/(prefix+'policy.json'))
assert native['requestSha256']==sha(root/(prefix+'requests.json')) and native['usageSha256']==sha(root/(prefix+'usage.json'))
for p,d in policy['dependencies'].items():assert sha(root/p)==d,p
for data,count in ((policy,847),(usage,848),(feature,851)):
    assert len(data['previousFixtureSha256'])==count
    for p,d in data['previousFixtureSha256'].items():assert sha(root/p)==d,p
    for p,d in data['protectedProject'].items():assert sha(project/p)==d,p
for data,count in ((policy,707),(usage,708)):
    assert len(data['assetSha256'])==count
    for p,d in data['assetSha256'].items():assert sha(package_file(p))==d,p
assert sha(package_file(feature['path']))==feature['assetSha256'] and feature['assetsSaved']==0 and not feature['featureActivated']
assert len(feature['actions'])==4 and not any('MotionWarping' in a['nativeText'] for a in feature['actions'])
source=repo/'tools/unreal/LyraMotionWarpingOracle'
package=repo/'artifacts/unreal/lyra-motion-warping-oracle/package-full'
assert len(usage['probeSourceSha256'])==5
for p,d in usage['probeSourceSha256'].items():assert sha(source/p)==sha(package/p)==d,p
assert not (project/'Plugins/LyraMotionWarpingOracle').exists()
host=load(project/'Binaries/Win64/UnrealEditor.modules')
assert load(package/'Binaries/Win64/UnrealEditor.modules')['BuildId']==host['BuildId']
for group,names,marker in (('policy',('first','repeat'),'LYRA_MOTION_WARPING_POLICY_OK windows=4 graphs=1 previous=847 assets_saved=0'),
                           ('native',('registry','repeat'),'LYRA_MOTION_WARPING_NATIVE_OK traces=120 frames=16800 previous=848 packages=708 assets_saved=0'),
                           ('feature',('first','repeat'),'LYRA_MOTION_WARPING_FEATURE_OK actions=4 previous=851 assets_saved=0')):
    for name in names:
        t=read(logs/f'motion-warping-{group}-ue-{name}.log')
        assert marker in t and f'LYRA_MOTION_WARPING_{group.upper()}_EXIT code=0' in t
        assert not re.search(r'Ensure condition failed|Assertion failed|Fatal error|LogPython: Error|LYRA_MOTION_WARPING_FAILED',t)
assert len(policy['windows'])==4 and all(w['properties']['warp_target_name']=='Align' for w in policy['windows'])
u=usage['trace'];assert u['registrySearchCompleted'] and len(u['windowEndRoots'])==4
finger=policy['windows'][0]['montage']
assert u['referencers'][finger]==['/ShooterCore/Game/Emote/GA_Emote']
assert all(not refs for p,refs in u['referencers'].items() if p!=finger)
assert not any('MotionWarping' in c['class'] for c in u['heroSCSComponents'])
assert any(d['name']=='Montage to Play' and finger in d['value'] for d in u['abilityDefaults'])
assert any(p['name']=='MontageToPlay' and p['links'] for n in u['abilityNodes'] for p in n['pins'])
frames=nonzero=active=disabled=0
assert len(request['traces'])==len(native['trace']['traces'])==120
assert set(t['mode'] for t in request['traces'])=={'normal','change','remove','missing_then_add','stop_replay','replace_replay','seek','reverse','pause','disable'}
for t,n in zip(request['traces'],native['trace']['traces'],strict=True):
    assert len(t['frames'])==len(n['frames'])==t['hz']*2 and t['hz'] in (30,60,120)
    assert not n['settings']['searchSegments']
    for f in n['frames']:
        assert f['local']==dict(position=[0,0,0],rotation=[0,0,0,1],scale=[1,1,1])
        nonzero+=any(abs(p)>1e-9 for p in f['warped']['position'])
        for m in f['modifiers']:
            active+=m['state']==1;disabled+=m['state']==3
        frames+=1
assert (frames,nonzero,active,disabled)==(16800,3030,3662,1289)
archive=logs/'motion-warping-native-first-incomplete-registry'
assert load(archive/(prefix+'native.json'))['trace']==native['trace']
assert load(archive/(prefix+'requests.json'))==request
metrics=[]
for cfg in ('debug','optimize'):
    t=read(logs/f'motion-warping-{cfg}-gate-final.log')
    assert not re.search(r'^\s*(ERROR|WARNING):',t,re.M)
    exits=re.findall(r'LYRA_MOTION_WARPING_RUN_EXIT name=([^ ]+) code=(\d+)',t)
    assert len(exits)==6 and all(code=='0' for _,code in exits) and 'LYRA_MOTION_WARPING_MATRIX_OK' in t
    line=next(x for x in t.splitlines() if x.startswith('LYRA_MOTION_WARPING_NATIVE_GODOT_OK'))
    row=dict(re.findall(r'(\w+)=([^ ]+)',line));metrics.append(row)
    assert row['frames']==row['retries']=='16800' and row['traces']=='120' and row['nonzero']=='3030' and row['active']=='3662' and row['disabled']=='1289' and row['rejected']=='50400'
    assert float(row['positionCm'])<=1e-8 and float(row['quaternion'])<=1e-10 and float(row['scale'])<=1e-12
    assert row['recordedContext']=='true' and row['capsuleIntegrated']==row['wholeMainNative']=='false'
    for filename,digest in re.findall(r'LYRA_MOTION_WARPING_ASSEMBLY file=([^ ]+) sha256=(\w+)',t):
        folder=repo/'.godot/mono/temp/bin'/('Debug' if cfg=='debug' else 'ExportRelease')
        assert sha(folder/filename).upper()==digest
    assert 'LYRA_ROOT_MOVEMENT_NATIVE_GODOT_OK traces=53 frames=8564 present=5569' in t
    assert 'LYRA_ROOT_MOVEMENT_CONTROLLED_COLLISION_OK cases=5' in t and 'LYRA_ROOT_MOVEMENT_LATE_FRAME_OK rejected=1 moves=0' in t
    assert 'LYRA_ROOT_MOVEMENT_PHYSICS_GODOT_OK hz=60 roles=6 frames=480 moves=2880 root=1856 normal=1024' in t
    assert 'LYRA_MONTAGE_SAMPLING_GODOT_OK frames=15870' in t
    assert 'LYRA_WEAPON_EQUIPMENT_ROLE_GODOT_OK hz=60 roles=6 frames=480' in t
assert metrics[0]==metrics[1]
debug=load(logs/'motion-warping-debug-main-final.json')
assert debug==load(logs/'motion-warping-optimize-main-final.json')==load(logs/'root-movement-debug-main-final6.json')
assert debug['characters']==10 and not debug['player']['model']['motionWarpingConsumer']
for p in (logs/'motion-warping-debug-build-second.log',logs/'motion-warping-export-release-build-final.log'):
    t=read(p);assert re.search(r'(?m)^\s*0\s*(个错误|Error)',t) and re.search(r'(?m)^\s*0\s*(个警告|Warning)',t) and not re.search(r'error CS\d+',t)
assert 'LYRA_MOTION_WARPING_DEBUG_RESTORED hashVerified=true' in read(logs/'motion-warping-optimize-gate-final.log')
for name in ('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb'):
    assert sha(repo/'.godot/mono/temp/bin/Debug'/name)==sha(logs/'motion-warping-debug-backup-final'/name)
c=ET.parse(logs/'motion-warping-core-first.trx').find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters')
assert c is not None and c.get('passed')==c.get('total')=='193' and c.get('failed')==c.get('notExecuted')=='0'
summary=dict(schemaVersion=1,policyProcesses=2,nativeProcesses=2,featureProcesses=2,nativeTraces=120,nativeFrames=frames,
    nonzeroWarpFrames=nonzero,activeModifierFrames=active,disabledModifierFrames=disabled,
    metrics=metrics[0],coreTests=193,previousJson=847,runtimeProtectedJson=848,featureProtectedJson=851,
    originalPackages=708,featurePackage=1,ordinaryBaselineEqual=True,debugRestored=True,
    scope=dict(originalSkewWarpProfile=True,recordedOriginalContext=True,transactionalComponent=True,
        capsuleIntegrated=False,originalAbilityExecuted=False,wholeUEWorldPhysics=False,wholeMainNative=False,goalComplete=False))
(logs/'motion-warping-verification.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(summary,ensure_ascii=False,separators=(',',':')))
