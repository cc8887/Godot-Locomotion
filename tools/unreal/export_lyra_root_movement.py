"""Read original Montage-only motion and real mesh/CMC conversions; no asset saves."""
import hashlib
import json
import math
from pathlib import Path
import unreal

repo = Path(__file__).resolve().parents[2]
root = repo / 'assets/generated/lyra_als'
prefix = 'root_movement_v1_'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
catalog = json.loads((root / 'montage_catalog_v2.json').read_bytes())
weapon = json.loads((root / 'weapon_resources/catalog.json').read_bytes())
project = Path(unreal.Paths.get_project_file_path())
protected = {p.relative_to(project.parent).as_posix(): sha(p) for p in [project, *(project.parent / 'Config').rglob('*.ini')]}
previous = {p.relative_to(root).as_posix(): sha(p) for p in root.rglob('*.json') if not p.name.startswith(prefix)}
plugin = repo / 'tools/unreal/LyraRootMovementOracle'
sources = {p.relative_to(plugin).as_posix(): sha(p) for p in plugin.rglob('*') if p.is_file()}
packages = {**catalog['assetSha256'], **weapon['assetSha256']}
def package_file(path):
    path = path.split('.')[0]
    if path.startswith('/Game/'):
        return project.parent / 'Content' / (path.removeprefix('/Game/') + '.uasset')
    if path.startswith('/ShooterCore/'):
        return project.parent / 'Plugins/GameFeatures/ShooterCore/Content' / (path.removeprefix('/ShooterCore/') + '.uasset')
    raise ValueError(path)
def protect():
    for p,d in protected.items(): assert sha(project.parent / p) == d, p
    for p,d in previous.items(): assert sha(root / p) == d, p
    for p,d in sources.items(): assert sha(plugin / p) == d, p
    for p,d in packages.items(): assert sha(package_file(p)) == d, p
def write(name,data):
    p = root / (prefix + name + '.json')
    if p.exists(): assert json.loads(p.read_bytes()) == data, 'Independent root capture changed: ' + p.name
    else:
        with p.open('x',encoding='utf-8',newline='\n') as f: f.write(json.dumps(data,separators=(',',':'),allow_nan=False)+'\n')
    return sha(p)
def atom(position,yaw=0,scale=1):
    return dict(position=position,rotation=[0,0,math.sin(yaw/2),math.cos(yaw/2)],scale=[scale]*3)
protect()
assert len(previous) == 843
assets = catalog['assets']
traces=[]
for asset,a in enumerate(assets):
    for hz in ((30,60,120) if a['rootMotion'] else (60,)):
        count=hz*10 if a['rootMotion'] else 4
        frames=[]
        for i in range(count):
            transform=i%4
            f=dict(delta=0 if i==hz*4 else 1/hz,plays=[],stop=-1,stopTime=.2,
                   actor=atom([125,-73,90],transform*.63),
                   relative=atom([17,-9,-90],-math.pi/2,(1,.7,1.4,1)[transform]),
                   velocity=[120,-90,-170],falling=i%2==1)
            if i==0:f['plays'].append(dict(asset=asset,rate=1,start=0,stopGroup=True))
            if a['rootMotion']:
                if i==hz*2: f['plays'].append(dict(asset=asset,rate=1.5,start=.17,stopGroup=True))
                if i==hz*3: f['stop']=asset
                if i==hz*5: f['plays'].append(dict(asset=asset,rate=-1,start=a['duration']-.04,stopGroup=True))
                if i==hz*7: f['plays'].append(dict(asset=0,rate=1,start=0,stopGroup=True))
                if i==hz*8: f['plays'].append(dict(asset=asset,rate=1,start=0,stopGroup=True))
            frames.append(f)
        traces.append(dict(asset=asset,hz=hz,frames=frames))
request=dict(schemaVersion=1,mainClass='/Game/Characters/Heroes/Mannequin/Animations/ABP_Mannequin_Base.ABP_Mannequin_Base_C',
             mesh='/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny.SKM_Manny',montages=[a['path'] for a in assets],traces=traces)
data=json.loads(unreal.LyraRootMovementOracleLibrary.read_trace(json.dumps(request,separators=(',',':'))))
assert data['mode']==3 and len(data['references'])==45 and len(data['traces'])==len(traces)
assert sum(r['enabled'] for r in data['references'])==4
for t,n in zip(traces,data['traces'],strict=True):
    assert len(t['frames'])==len(n['frames']) and all(not f['secondPresent'] for f in n['frames'])
protect()
request_sha=write('requests',request)
deps={p:sha(root/p) for p in ('main_anim_defaults.json','montage_catalog_v2.json','montage_sampling_v1_policy.json','montage_sampling_v1_roots.json')}
policy_sha=write('policy',dict(schemaVersion=1,mode=3,dependencies=deps,references=data['references'],firstTrackOnly=True,
                             montageWeightIgnored=True,fallingPreservesVertical=True,providerAttributeDrivesCapsule=False,
                             assetSha256=packages,previousFixtureSha256=previous,protectedProject=protected,probeSourceSha256=sources))
write('native',dict(schemaVersion=1,requestSha256=request_sha,policySha256=policy_sha,trace=data,
                    scope='Actual original Montage UpdateWeight/Advance/Consume plus real mesh ConvertLocalRootMotionToWorld and CMC Calc/Constrain. No whole-world physics, MotionWarping callbacks or complete Main evaluation.'))
unreal.log(f'LYRA_ROOT_MOVEMENT_NATIVE_OK traces={len(traces)} frames={sum(len(t["frames"]) for t in traces)} previous={len(previous)} packages={len(packages)} assets_saved=0')
