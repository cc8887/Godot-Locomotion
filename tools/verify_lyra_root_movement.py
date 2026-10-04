"""Audit the final Montage-only extraction, real capsule route and its scope."""
import hashlib
import json
import re
import struct
import xml.etree.ElementTree as ET
from pathlib import Path

repo=Path(__file__).resolve().parents[1]
root=repo/'assets/generated/lyra_als'
logs=repo/'artifacts/lyra-analysis'
project=repo.parent/'GASP58'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
load=lambda p:json.loads(p.read_bytes())
def read(p):
    b=p.read_bytes()
    return b.decode('utf-16' if b.startswith((b'\xff\xfe',b'\xfe\xff')) else 'utf-8-sig')
def package_file(path):
    path=path.split('.')[0]
    if path.startswith('/Game/'):return project/'Content'/(path.removeprefix('/Game/')+'.uasset')
    if path.startswith('/ShooterCore/'):return project/'Plugins/GameFeatures/ShooterCore/Content'/(path.removeprefix('/ShooterCore/')+'.uasset')
    raise ValueError(path)
policy=load(root/'root_movement_v1_policy.json')
native=load(root/'root_movement_v1_native.json')
request=load(root/'root_movement_v1_requests.json')
actor=load(root/'root_movement_v1_actor_policy.json')
assert native['requestSha256']==sha(root/'root_movement_v1_requests.json') and native['policySha256']==sha(root/'root_movement_v1_policy.json')
for p,d in policy['dependencies'].items():assert sha(root/p)==d,p
for data in (policy,actor):
    for p,d in data['previousFixtureSha256'].items():assert sha(root/p)==d,p
    for p,d in data['protectedProject'].items():assert sha(project/p)==d,p
for p,d in policy['assetSha256'].items():assert sha(package_file(p))==d,p
assert sha(package_file(actor['pawnClass']))==actor['assetSha256']
assert len(policy['previousFixtureSha256'])==843 and len(actor['previousFixtureSha256'])==846 and len(policy['assetSha256'])==706
source=repo/'tools/unreal/LyraRootMovementOracle'
package=repo/'artifacts/unreal/lyra-root-movement-oracle/package-ready'
for p,d in policy['probeSourceSha256'].items():assert sha(source/p)==sha(package/p)==d,p
assert not (project/'Plugins/LyraRootMovementOracle').exists()
assert actor['useControllerRotationYaw'] and not actor['allowPhysicsRotationDuringAnimRootMotion']
for name in ('ready','repeat'):
    t=read(logs/f'root-movement-ue-{name}.log')
    assert 'LYRA_ROOT_MOVEMENT_NATIVE_OK traces=53 frames=8564 previous=843 packages=706 assets_saved=0' in t
    assert 'LYRA_ROOT_MOVEMENT_PROCESS_EXIT code=0' in t and not re.search(r'LogPython: Error:|Fatal error:|LYRA_ROOT_MOVEMENT_FAILED',t)
for name in ('first','repeat'):
    t=read(logs/f'root-movement-actor-ue-{name}.log')
    assert 'LYRA_ROOT_MOVEMENT_ACTOR_OK useControllerYaw=True physicsRootRotation=False assets_saved=0' in t
    assert 'LYRA_ROOT_MOVEMENT_ACTOR_EXIT code=0' in t and not re.search(r'LogPython: Error:|Fatal error:',t)
assert native['trace']['mode']==3 and native['trace']['references']==policy['references']
assert len(request['traces'])==len(native['trace']['traces'])==53 and sum(r['enabled'] for r in policy['references'])==4
identity=dict(position=[0,0,0],rotation=[0,0,0,1],scale=[1,1,1])
frames=present=0
for r,n in zip(request['traces'],native['trace']['traces'],strict=True):
    assert len(r['frames'])==len(n['frames'])
    for f in n['frames']:
        assert not f['secondPresent'] and f['local']==identity
        present+=f['present'];frames+=1
assert frames==8564 and present==5569
metrics=[]
for cfg in ('debug','optimize'):
    t=read(logs/f'root-movement-{cfg}-gate-final6.log')
    assert not re.search(r'^\s*(ERROR|WARNING):',t,re.M)
    runs=re.findall(r'LYRA_ROOT_MOVEMENT_RUN_EXIT name=([^ ]+) code=(\d+)',t)
    assert len(runs)==7 and all(code=='0' for _,code in runs)
    assert 'LYRA_ROOT_MOVEMENT_MATRIX_OK' in t and 'frames=8564 present=5569 retries=8564' in t
    assert t.count('LYRA_ROOT_MOVEMENT_CONTROLLED_COLLISION_OK cases=5')==3 and t.count('LYRA_ROOT_MOVEMENT_LATE_FRAME_OK rejected=1 moves=0')==3
    rows=[dict(re.findall(r'(\w+)=([^ ]+)',line)) for line in t.splitlines() if line.startswith('LYRA_ROOT_MOVEMENT_PHYSICS_GODOT_OK')]
    assert len(rows)==3
    for row,hz in zip(rows,(30,60,120),strict=True):
        assert int(row['hz'])==hz and int(row['frames'])==hz*8 and int(row['roles'])==6
        assert int(row['moves'])==int(row['preRetries'])==int(row['animationRetries'])==hz*8*6
        assert int(row['root'])+int(row['normal'])==int(row['moves']) and int(row['stops'])==6 and int(row['switches'])==12
        assert row['originalNonzero']==row['motionWarping']==row['wholeWorldNative']=='false'
    metrics.append(rows)
    assert 'LYRA_MONTAGE_SAMPLING_GODOT_OK frames=15870' in t and 'rootP=0 rootQ=0 rootS=0' in t
    assert 'LYRA_WEAPON_EQUIPMENT_ROLE_GODOT_OK hz=60 roles=6 frames=480' in t and 'LYRA_WEAPON_CALLBACK_LIFETIME_GODOT_OK disposed=1 queuedDeletion=1 rebind=1' in t
    for filename,digest in re.findall(r'LYRA_ROOT_MOVEMENT_ASSEMBLY file=([^ ]+) sha256=(\w+)',t):
        folder=repo/'.godot/mono/temp/bin'/('Debug' if cfg=='debug' else 'ExportRelease')
        assert sha(folder/filename).upper()==digest
    b=read(logs/('root-movement-debug-build-final6.log' if cfg=='debug' else 'root-movement-export-release-build-final6.log'))
    assert re.search(r'(?m)^\s*0\s*(个错误|Error)',b) and re.search(r'(?m)^\s*0\s*(个警告|Warning)',b) and not re.search(r'error CS\d+',b)
assert metrics[0]==metrics[1]
debug=load(logs/'root-movement-debug-main-final6.json')
assert debug==load(logs/'root-movement-optimize-main-final6.json')
assert debug['characters']==10
roles=[debug['player']['model'],*debug['companions']]
assert all(r['capsuleMoves']==r['published']==480 and r['rootCapsuleMoves']==0 for r in roles)
def strip(v):
    if isinstance(v,dict):return {k:strip(x) for k,x in v.items() if k not in ('capsuleMoves','rootCapsuleMoves','montageOnlyRootMovement')}
    if isinstance(v,list):return [strip(x) for x in v]
    return v
assert strip(debug)==load(logs/'weapon-equipment-debug-main-final2.json')
assert debug['player']['model']['montageOnlyRootMovement'] and not debug['player']['model']['motionWarpingConsumer']
for name in ('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb'):
    assert sha(repo/'.godot/mono/temp/bin/Debug'/name)==sha(logs/'root-movement-debug-backup-final6'/name)
trx=ET.parse(logs/'root-movement-core-final5.trx')
c=trx.find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters')
assert c is not None and c.get('passed')==c.get('total')=='188' and c.get('failed')==c.get('notExecuted')=='0'
render=read(logs/'root-movement-render.log')
assert 'LYRA_ROOT_MOVEMENT_PHYSICS_GODOT_OK hz=60' in render and 'captures=3' in render
assert 'LYRA_ROOT_MOVEMENT_RENDER_EXIT code=0' in render and not re.search(r'^\s*(ERROR|WARNING):',render,re.M)
assert not (logs/'root-movement-render.stderr.log').read_bytes()
images={}
for frame in (30,90,270):
    p=logs/f'root-movement-render-{frame}.png';b=p.read_bytes()
    assert b[:8]==b'\x89PNG\r\n\x1a\n' and len(b)>10000 and struct.unpack('>II',b[16:24])==(1280,720)
    images[p.name]=sha(p)
summary=dict(schemaVersion=1,nativeProcesses=2,actorPolicyProcesses=2,nativeFrames=frames,nativeRootFrames=present,
             originalNonzeroRootFrames=0,roleMovesPerBuild=sum(int(x['moves']) for x in metrics[0]),
             prePhysicsRetriesPerBuild=sum(int(x['preRetries']) for x in metrics[0]),animationRetriesPerBuild=sum(int(x['animationRetries']) for x in metrics[0]),
             metrics=metrics[0],coreTests=188,ordinaryMoves=4800,ordinaryBaselineEqual=True,debugRestored=True,
             previousJson=843,actorProtectedJson=846,protectedPackages=706,render=images,
             scope=dict(montageOnlyExtraction=True,sharedImmutableRootResources=True,oneCapsuleMovePerFrame=True,productionLoopConnected=True,
                        controlledJoltNonzeroCases=5,originalMotionWarping=False,wholeUEWorldPhysics=False,nativeWholeMain=False,goalComplete=False))
(logs/'root-movement-verification.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(summary,ensure_ascii=False,separators=(',',':')))
