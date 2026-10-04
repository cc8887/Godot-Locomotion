"""Original ContextEffects notify, interface, component and library probe."""
import hashlib
import json
from pathlib import Path
import unreal

repo=Path(__file__).resolve().parents[2]
root=repo/'assets/generated/lyra_als'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
outputs={'context_effects_v1_policy.json','context_effects_v1_native.json'}
previous={p.relative_to(root).as_posix():sha(p) for p in root.rglob('*.json') if p.name not in outputs}
contract=json.loads((root/'notify_contract_v1.json').read_bytes())
project=Path(unreal.Paths.get_project_file_path());content=Path(unreal.Paths.project_content_dir())
protected={project:sha(project)}
for p in (project.parent/'Config').rglob('*.ini'):protected[p]=sha(p)
packages=dict(contract['assetSha256'])
for path in ('/Game/ContextEffects/CFX_DefaultSkin.CFX_DefaultSkin','/Game/ContextEffects/DT_AnimEffectTags.DT_AnimEffectTags',
 '/Game/ContextEffects/DT_SurfaceTypes.DT_SurfaceTypes','/Game/PhysicsMaterials/PM_Character.PM_Character',
 '/Game/PhysicsMaterials/PM_Concrete.PM_Concrete','/Game/PhysicsMaterials/PM_Glass.PM_Glass'):
    packages[path]=sha(content/(path.split('.')[0].removeprefix('/Game/')+'.uasset'))
def protect():
    for p,digest in protected.items():assert sha(p)==digest,str(p)
    for name,digest in previous.items():assert sha(root/name)==digest,name
    for path,digest in packages.items():assert sha(content/(path.split('.')[0].removeprefix('/Game/')+'.uasset'))==digest,path
def write(name,data):
    path=root/name
    if path.exists():assert json.loads(path.read_bytes())==data,'Independent original ContextEffects differs: '+name
    else:
        with path.open('x',encoding='utf-8',newline='\n') as f:f.write(json.dumps(data,separators=(',',':'))+'\n')
protect()
policy=json.loads(unreal.LyraContextEffectsOracleLibrary.read_policy())
original_ini=repo/'artifacts/lyra-analysis/lyra-original-context-DefaultGame.ini'
ini=original_ini.read_text(encoding='utf-8-sig')
surface_map=[dict(surface=i,tag='SurfaceType.'+name) for i,name in enumerate(('Default','Character','Concrete','Glass'))]
assert all(f'TagName="{v["tag"]}"' in ini for v in surface_map)
assert len(policy['rows'])==6 and len(policy['queries'])==64 and all(not e['vfx'] for r in policy['rows'] for e in r['effects'])
plugin=repo/'tools/unreal/LyraContextEffectsOracle'
hashes={p.relative_to(plugin).as_posix():sha(p) for p in sorted(plugin.rglob('*')) if p.is_file() and p.suffix in ('.cpp','.h','.cs','.uplugin')}
policy.update(schemaVersion=1,dependencies={'notify_contract_v1.json':sha(root/'notify_contract_v1.json')},
    surfaceMap=surface_map,sourceConfig=dict(ref='origin/5.8',path='Samples/Games/Lyra/Config/DefaultGame.ini',sha256=sha(original_ini)),
    audioPlaybackDeferred=True,pluginSourceSha256=hashes,assetSha256=packages)
calls=[]
for a in contract['assets']:
    for e in a['events']:
        if e['notifyClass']=='/Script/LyraGame.AnimNotify_LyraContextEffects':
            for scenario in range(6):calls.append(dict(asset=a['source'],index=e['index'],scenario=scenario))
assert len(calls)==686*6
base=calls[2]
calls.extend([{**base,'contexts':['SurfaceType.Default','SurfaceType.Glass']},
    {**base,'contexts':['SurfaceType.Glass'],'convert':False},
    {**base,'contexts':['SurfaceType.Default'],'library':False},
    {**base,'scenario':4,'contexts':['SurfaceType.Glass']},
    {**base,'contexts':['SurfaceType.Default'],'unload':True}])
request=dict(calls=calls)
trace=json.loads(unreal.LyraContextEffectsOracleLibrary.read_trace(json.dumps(request)))
assert len(trace['calls'])==len(calls) and len(trace['messages'])==len(calls)*2
for i,call in enumerate(calls):
    a,b=trace['messages'][i*2:i*2+2]
    assert a['receiver']=='actor' and b['receiver']=='component' and a['call']==b['call']==i
    assert a['contexts']==b['contexts']==[] and a['hit']==b['hit']==(call['scenario']!=4)
    assert a['physicalMaterial']==b['physicalMaterial']==a['hit'],(i,call,a,b)
    expected_surface=call['scenario'] if call['scenario']<4 else 2
    assert a['surface']==b['surface']==(expected_surface if a['hit'] else -1)
    contexts=list(call.get('contexts',[]))
    if a['hit'] and call.get('convert',True):contexts.append(surface_map[expected_surface]['tag'])
    contexts=set(contexts)
    query=next(q for q in policy['queries'] if q['effect']==a['effect'] and set(q['contexts'])==contexts)
    assert b['audioCount']==(0 if call.get('unload',False) else len(query['audio'])) and b['vfxCount']==0,(i,b,query)
protect()
write('context_effects_v1_policy.json',policy)
native=dict(schemaVersion=1,dependencies={'notify_contract_v1.json':sha(root/'notify_contract_v1.json'),
    'context_effects_v1_policy.json':sha(root/'context_effects_v1_policy.json')},previousFixtureSha256=previous,
    protectedProjectSha256={p.relative_to(project.parent).as_posix():v for p,v in protected.items()},
    pluginSourceSha256=hashes,engineVersion=unreal.SystemLibrary.get_engine_version(),request=request,trace=trace,
    scope=dict(originalNotify=True,originalComponent=True,actualChaosQueries=True,audioPlayback=False,wholeMainContinuous=False,assetsSaved=0))
write('context_effects_v1_native.json',native)
unreal.log(f'LYRA_CONTEXT_EFFECTS_NATIVE_OK objects=686 calls={len(calls)} messages={len(trace["messages"])} queries=64 packages={len(packages)} previous={len(previous)} assets_saved=0')
