"""Original SaveCachedPose lifecycle with controlled leaves."""
import hashlib
import json
import os
from pathlib import Path
import unreal

repo = Path(__file__).resolve().parents[2]
assets = repo / 'assets/generated/lyra_als'
out = repo / 'artifacts/lyra-analysis'
tag = os.environ['LYRA_CACHE_OWNER_TAG']
assert tag.replace('-', '').isalnum()
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read = lambda p: json.loads(p.read_bytes())
project = Path(unreal.Paths.get_project_file_path()).parent
previous = {p.relative_to(assets).as_posix(): sha(p) for p in assets.rglob('*.json')}
protected = {p.relative_to(project).as_posix(): sha(p) for p in [project/'GASP58.uproject', *(project/'Config').rglob('*.ini')]}
packages = read(out/'main-phases-v3-closure.json')['assetSha256']
source = repo/'tools/unreal/LyraCacheOwnerOracle'
sources = {p.relative_to(source).as_posix(): sha(p) for p in source.rglob('*') if p.is_file()}
engine = Path(unreal.Paths.engine_dir()).resolve()
engine_files = list(read(out/'cache-owner-v1-engine-sources.json'))
engine_sources = {p: sha(engine/p) for p in engine_files}


def protect():
    for p, d in previous.items(): assert sha(assets/p) == d, p
    for p, d in protected.items(): assert sha(project/p) == d, p
    for p, d in packages.items():
        path = p.split('.')[0]
        if path.startswith('/Game/'):
            file = project/'Content'/(path.removeprefix('/Game/')+'.uasset')
        elif path.startswith('/ShooterCore/'):
            file = project/'Plugins/GameFeatures/ShooterCore/Content'/(path.removeprefix('/ShooterCore/')+'.uasset')
        else:
            raise ValueError(path)
        assert sha(file) == d, p
    for p, d in sources.items(): assert sha(source/p) == d, p
    for p, d in engine_sources.items(): assert sha(engine/p) == d, p


def save(kind, value):
    path = out/f'{tag}-{kind}.json'
    with path.open('x', encoding='utf-8', newline='\n') as f:
        f.write(json.dumps(value, separators=(',', ':'), allow_nan=False)+'\n')
    return sha(path)


graphs = read(assets/'main_layer_graph_v1.json')['classes']
steps=[]
def step(op,counter,frame,**args):steps.append(dict(op=op,counter=counter,frame=frame,**args))
step('init',1,0);step('init',1,1)
step('bones',1,0);step('bones',1,0)
step('update',1,1,weights=[[.25,.75,.75],[.5,.125],[.625,.625]])
step('evaluate',1,1,marker=2.5);step('evaluate',1,1,marker=-3.25)
step('nested',1,1,marker=4.5)
step('init',1,9)
step('bones',1,2);step('bones',1,2)
step('evaluate',1,1,marker=6.75)
step('update',2,3,weights=[[],[0,.25],[]])
step('init',2,3);step('init',2,4)
step('bones',2,3);step('evaluate',2,3,marker=7.5)
step('nested',32767,8,marker=-8.25)
step('bones',32767,8);step('bones',-32768,9);step('bones',-32768,9)
step('init',32767,8);step('init',-32768,9);step('init',-32768,10)
step('evaluate',-32768,9,marker=10.125)
step('update',-32768,9,weights=[[1,1],[.75,.5],[.125,.5]])
step('evaluate',-32768,10,marker=11.25)
step('bones',1,0);step('init',1,0);step('evaluate',0,0,marker=12.5)
requests=[dict(profile=profile,main=graphs['main']['classPath'],provider=graphs[profile]['classPath'],mesh='/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny.SKM_Manny',steps=steps) for profile in ('unarmed','pistol','rifle')]
protect();request_sha=save('requests',dict(schemaVersion=1,cases=requests));results=[]
try:
    for request in requests:
        result=json.loads(unreal.LyraWholeMainOracleLibrary.read_cache_lifecycle(json.dumps(request,separators=(',',':'))));result['profile']=request['profile'];results.append(result)
    native_sha=save('native',dict(schemaVersion=1,cases=results))
    assert len(results)==3 and all(len(r['rows'])==len(steps) and len(r['initial'])==3 for r in results)
    for result in results:
        assert all(row['initialization']['counter']==row['bones']['counter']==row['evaluation']['counter']==row['update']['counter']==-1 for row in result['initial'])
        for request,row in zip(steps,result['rows'],strict=True):
            assert all(n['update']['counter']==n['update']['frame']==-1 for n in row['states'])
            if request['op']=='evaluate':assert row['outputs'][0]==row['outputs'][1]
            if request['op']=='nested':assert row['outputs'][0]!=row['outputs'][1]!=row['outputs'][2] and row['outputs'][2]==row['outputs'][3]
    protect();save('closure',dict(schemaVersion=1,requestSha256=request_sha,nativeSha256=native_sha,engineVersion=unreal.SystemLibrary.get_engine_version(),engineSourceSha256=engine_sources,probeSourceSha256=sources,previousFixtureSha256=previous,protectedProject=protected,assetSha256=packages,scope=dict(originalSaveCachedPose=True,originalCompiledOwners=True,controlledLeaves=True,naturalComponentCounters=False,fullRequiredBonesLOD=False,fullGraphReinitialization=False,fullLayerCallSchedule=False,assetsSaved=0,goalComplete=False)))
    unreal.log('LYRA_CACHE_OWNER_NATIVE_OK profiles=3 nodes=9 steps='+str(len(steps)*3)+' assets_saved=0')
finally:protect()
