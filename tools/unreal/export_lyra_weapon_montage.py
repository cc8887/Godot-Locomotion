"""Execute original weapon AnimBP roots and Montage instances, with actual mesh bones."""
import hashlib
import json
from pathlib import Path
import unreal

repo=Path(__file__).resolve().parents[2]
root=repo/'assets/generated/lyra_als'
prefix='weapon_montage_v1_'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
catalog=json.loads((root/'weapon_resources/catalog.json').read_bytes())
project=Path(unreal.Paths.get_project_file_path())
protected={p.relative_to(project.parent).as_posix():sha(p) for p in [project,*(project.parent/'Config').rglob('*.ini')]}
previous={p.relative_to(root).as_posix():sha(p) for p in root.rglob('*.json') if not p.name.startswith(prefix)}
plugin=repo/'tools/unreal/LyraWeaponMontageOracle'
sources={p.relative_to(plugin).as_posix():sha(p) for p in plugin.rglob('*') if p.is_file()}
def package_file(path):
    path=path.split('.')[0]
    if path.startswith('/Game/'):return project.parent/'Content'/(path.removeprefix('/Game/')+'.uasset')
    if path.startswith('/ShooterCore/'):return project.parent/'Plugins/GameFeatures/ShooterCore/Content'/(path.removeprefix('/ShooterCore/')+'.uasset')
    raise ValueError(path)
def protect():
    for p,d in protected.items():assert sha(project.parent/p)==d,p
    for p,d in previous.items():assert sha(root/p)==d,p
    for p,d in catalog['assetSha256'].items():assert sha(package_file(p))==d,p
    for p,d in sources.items():assert sha(plugin/p)==d,p
def write(name,data):
    p=root/(prefix+name+'.json')
    if p.exists():assert json.loads(p.read_bytes())==data,'Native weapon capture changed: '+p.name
    else:
        with p.open('x',encoding='utf-8',newline='\n') as f:f.write(json.dumps(data,separators=(',',':'),allow_nan=False)+'\n')
    return sha(p)

protect()
assert unreal.AlsLyraControlRigLibrary.prefer_raw_track_data_model(True)
traces=[]
for mesh in catalog['meshes']:
    montages=[m for m in catalog['montages'] if m['skeleton']==mesh['skeleton']]
    assert len(montages)==2 and '_Fire.' in montages[0]['path'] and '_Reload.' in montages[1]['path']
    for hz in (30,60,120):
        frames=[dict(delta=1/hz,sample=i%7!=4,commands=[]) for i in range(14*hz)]
        def command(time,asset,rate=1,start=0,stop=False,blend=.07,stop_group=True):
            frames[round(time*hz)]['commands'].append(dict(asset=asset,rate=rate,start=start,stop=stop,blend=blend,stopGroup=stop_group))
        command(0,0)
        command(1,1,1.5 if mesh['kind']=='pistol' else 1)
        command(4.2,0)
        command(4.3,0)  # restart same original asset while prior instance fades
        command(4.4,1)
        command(4.5,0,stop_group=False)  # native overweight Slot normalization
        command(5.2,1,start=.2)
        command(5.4,1,stop=True)
        command(6,0,rate=-1,start=montages[0]['duration'])
        command(7.2,1,rate=.75)
        command(7.7,0)
        command(8.2,1,rate=2)
        command(9.1,1,stop=True,blend=0)
        command(10,1)
        command(10.2,1,stop=True,blend=.3)
        command(10.3,1,stop=True,blend=.05)
        command(11,0)
        for i in (round(4.5*hz)+1,round(6*hz)+2,round(10.3*hz)+1):frames[i]['delta']=0
        traces.append(dict(kind=mesh['kind'],hz=hz,mesh=mesh['source'],**{'class':mesh['animationBlueprint']},montages=[m['path'] for m in montages],frames=frames))
requests=dict(schemaVersion=1,traces=traces,phase='commandsAfterProxyFreezeAndRootEvaluation',rawData=True)
request_sha=write('requests',requests)
trace=json.loads(unreal.LyraWeaponMontageOracleLibrary.read_trace(json.dumps(requests)))
protect()
frames=sum(len(t['frames']) for t in trace['traces'])
assert frames==8820 and len(trace['traces'])==9
for t in trace['traces']:
    assert len(t['names'])==7 and t['rootMotionMode']==3
    nodes=t['nodes'];root_node=next(n for n in nodes if n['type'].endswith('.AnimNode_Root'))
    slot=next(n for n in nodes if n['type'].endswith('.AnimNode_Slot'))
    ref=next(n for n in nodes if n['type'].endswith('.AnimNode_RefPose'))
    assert root_node['source']==slot['index'] and slot['source']==ref['index'] and ref['refPoseType']==0 and not slot['alwaysUpdate']
    for f in t['frames']:
        if 'pose' in f:assert len(f['pose'])==7 and f['attributes']==0 and f['curves']==[]
definitions={}
for t in trace['traces']:
    definition={k:t[k] for k in ('kind','nodes','names','parents','reference','rootMotionMode')}
    if t['kind'] in definitions:assert definitions[t['kind']]==definition
    else:definitions[t['kind']]=definition
policy_sha=write('policy',dict(schemaVersion=1,dependencies={'weapon_resources/catalog.json':sha(root/'weapon_resources/catalog.json')},
    definitions=definitions,assetSha256=catalog['assetSha256'],pluginSourceSha256=sources))
native=dict(schemaVersion=1,requestSha256=request_sha,dependencies={'weapon_resources/catalog.json':sha(root/'weapon_resources/catalog.json'),prefix+'policy.json':policy_sha},
    previousFixtureSha256=previous,protectedProjectSha256=protected,assetSha256=catalog['assetSha256'],pluginSourceSha256=sources,
    trace=trace,scope=dict(originalWeaponAnimBlueprint=True,rawPose=True,commandsAfterEvaluation=True,componentTickOrdering=False,productionConsumer=False,assetsSaved=0))
write('native',native)
unreal.log(f'LYRA_WEAPON_MONTAGE_NATIVE_OK traces=9 frames={frames} skin=7 previous={len(previous)} packages={len(catalog["assetSha256"])} assets_saved=0')
