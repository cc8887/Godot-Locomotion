"""Read-only actual Main root across self, Link, Unlink and re-Link."""
import hashlib
import copy
import json
import os
from pathlib import Path
import unreal

repo=Path(__file__).resolve().parents[2]
assets=repo/'assets/generated/lyra_als'
out=repo/'artifacts/lyra-analysis'
tag=os.environ['LYRA_DEFAULT_MAIN_TAG']
assert tag.replace('-','').isalnum()
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
read=lambda p:json.loads(p.read_bytes())
project=Path(unreal.Paths.get_project_file_path()).parent
previous={p.relative_to(assets).as_posix():sha(p) for p in assets.rglob('*.json')}
protected={p.relative_to(project).as_posix():sha(p) for p in [project/'GASP58.uproject',*(project/'Config').rglob('*.ini')]}
packages=read(out/'default-layer-runtime-v2-closure.json')['assetSha256']
source=repo/'tools/unreal/LyraDefaultMainOracle'
sources={p.relative_to(source).as_posix():sha(p) for p in source.rglob('*') if p.is_file()}

def package_file(p):
    p=p.split('.')[0]
    if p.startswith('/Game/'):return project/'Content'/(p.removeprefix('/Game/')+'.uasset')
    if p.startswith('/ShooterCore/'):return project/'Plugins/GameFeatures/ShooterCore/Content'/(p.removeprefix('/ShooterCore/')+'.uasset')
    raise ValueError(p)

def protect():
    assert {p.relative_to(assets).as_posix():sha(p) for p in assets.rglob('*.json')}==previous
    for p,d in protected.items():assert sha(project/p)==d,p
    for p,d in packages.items():assert sha(package_file(p))==d,p
    for p,d in sources.items():assert sha(source/p)==d,p

def save(kind,value):
    p=out/f'{tag}-{kind}.json'
    with p.open('x',encoding='utf-8',newline='\n') as f:f.write(json.dumps(value,separators=(',',':'),allow_nan=False)+'\n')
    return sha(p)

contracts=read(assets/'linked_layer_contracts.json')
traces=[]
for hz in (30,60,120):
    frames=[]
    operations={hz:'link',hz*2:'unlink',hz*3:'link',hz*3+hz//2:'link',hz*4:'unlink',hz*5:'unlink-null'}
    for n in range(hz*6):
        phase=n//hz
        f=dict(delta=1/hz,location=[111.25+n*300/hz,-23.5,150],yaw=n*45/hz,
            pitch=25 if n%hz<hz//2 else -15,velocity=[300,0,125 if phase==2 else 0],
            acceleration=[512,0,0],ground=phase!=2,crouching=phase==3,
            evaluate=n%7!=6,mainProperties=dict(GameplayTag_IsADS=phase==4,
                GameplayTag_IsFiring=phase==1,GameplayTag_IsDashing=False,bEnableRootYawOffset=True,GroundDistance=150 if phase==2 else 0))
        if n in operations:f['operation']=operations[n]
        frames.append(f)
    traces.append(dict(hz=hz,frames=frames))
for t in copy.deepcopy(traces):
    for f in t['frames']:f.pop('operation',None)
    traces.append(t)
request=dict(schemaVersion=1,main=contracts['classes']['main']['class'],
    provider=contracts['classes']['unarmed']['class'],mesh='/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny.SKM_Manny',traces=traces)
protect()
request_sha=save('requests',request)
try:
    native=json.loads(unreal.LyraWholeMainOracleLibrary.read_default_main(json.dumps(request,separators=(',',':'))))
    assert len(native['traces'])==6
    for t,q in zip(native['traces'],traces):
        assert t['hz']==q['hz'] and len(t['frames'])==len(q['frames'])
        for row,f in zip(t['frames'],q['frames']):
            assert len(row['calls'])==14
            assert row['mainUpdated']['WorldLocation']==f['location'], 'Native Main worker did not observe this frame'
            assert row['mainUpdated']['WorldVelocity']==f['velocity'], 'Native Main worker velocity stayed stale'
            assert not row['mainUpdated']['IsFirstUpdate']
            self_call=all(c['self'] for c in row['calls'])
            assert self_call==(row['linkedInstances']==0)
            if self_call:
                assert row['upstreamUpdates']==row['upstreamEvaluations']==0
                if f['evaluate']:assert row['preRig']['pose']
            assert row['preRigUpdates']==1 and row['preRigEvaluations']==int(f['evaluate'])
            if f['evaluate']:assert len(row['output']['pose'])==164
    native_sha=save('native',native)
    protect()
    save('closure',dict(schemaVersion=1,requestSha256=request_sha,nativeSha256=native_sha,
        engineVersion=unreal.SystemLibrary.get_engine_version(),probeSourceSha256=sources,
        captureSourceSha256=sha(Path(__file__)),previousFixtureSha256=previous,protectedProject=protected,assetSha256=packages,
        scope=dict(originalMainRoot=True,originalLinkUnlink=True,originalFinalRig=True,rawManny164=True,
            controlledPhysicalInputs=True,montagePlaying=False,als81=False,godotDefaultMainIntegrated=False,assetsSaved=0,goalComplete=False)))
    unreal.log('LYRA_DEFAULT_MAIN_NATIVE_OK traces=6 frames=2520 full_root=true assets_saved=0')
finally:protect()
