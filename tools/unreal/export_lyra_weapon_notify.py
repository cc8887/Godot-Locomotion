"""Read original weapon notify pins and execute the authored BP, without saves."""
import hashlib
import json
from pathlib import Path
import unreal

repo=Path(__file__).resolve().parents[2]
root=repo/'assets/generated/lyra_als'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
outputs={'weapon_notify_v1_policy.json','weapon_notify_v1_native.json'}
previous={p.relative_to(root).as_posix():sha(p) for p in root.rglob('*.json') if p.name not in outputs}
current=dict(previous)
if (root/'weapon_notify_v1_native.json').exists():
    previous=json.loads((root/'weapon_notify_v1_native.json').read_bytes())['previousFixtureSha256']
contract=json.loads((root/'notify_contract_v1.json').read_bytes())
project=Path(unreal.Paths.get_project_file_path())
protected={project:sha(project)}
for p in (project.parent/'Config').rglob('*.ini'):protected[p]=sha(p)
packages=dict(contract['assetSha256'])
def package_file(path):
    package=path.split('.')[0]
    if package.startswith('/Game/'):return project.parent/'Content'/(package.removeprefix('/Game/')+'.uasset')
    if package.startswith('/ShooterCore/'):return project.parent/'Plugins/GameFeatures/ShooterCore/Content'/(package.removeprefix('/ShooterCore/')+'.uasset')
    raise ValueError(path)
def protect():
    for p,digest in protected.items():assert sha(p)==digest,str(p)
    for name,digest in previous.items():assert sha(root/name)==digest,name
    for name,digest in current.items():assert sha(root/name)==digest,name
    for path,digest in packages.items():assert sha(package_file(path))==digest,path
def write(name,data):
    path=root/name
    if path.exists():assert json.loads(path.read_bytes())==data,'Independent weapon notify differs: '+name
    else:
        with path.open('x',encoding='utf-8',newline='\n') as f:f.write(json.dumps(data,separators=(',',':'))+'\n')
protect()
policy=json.loads(unreal.LyraWeaponNotifyOracleLibrary.read_policy())
events=[]
for a in contract['assets']:
    for e in a['events']:
        if e['notifyClass'].endswith('/AN_PlayWeaponMontage.AN_PlayWeaponMontage_C'):
            events.append(dict(asset=a['source'],index=e['index'],payload=e['payload']))
assert len(events)==9
for row in policy['equipment']:
    for path in [row['definition'],row['instanceType'],*[a['class'] for a in row['actors']]]:
        packages[path]=sha(package_file(path.removesuffix('_C')))
for e in events:
    path=e['payload']['MontageToPlay']['path']
    packages[path]=sha(package_file(path))
plugin=repo/'tools/unreal/LyraWeaponNotifyOracle'
hashes={p.relative_to(plugin).as_posix():sha(p) for p in sorted(plugin.rglob('*')) if p.is_file() and p.suffix in ('.cpp','.h','.cs','.uplugin')}
policy.update(schemaVersion=1,dependencies={'notify_contract_v1.json':sha(root/'notify_contract_v1.json')},events=events,pluginSourceSha256=hashes,assetSha256=packages)
calls=[dict(asset=e['asset'],index=e['index'],kind=['Pistol','Rifle','Shotgun'].index(e['asset'].split('/')[3]),scenario=s)
    for e in events for s in range(4)]
request=dict(calls=calls)
trace=json.loads(unreal.LyraWeaponNotifyOracleLibrary.read_trace(json.dumps(request)))
assert len(trace['calls'])==36
for i,result in enumerate(trace['calls']):
    event=events[i//4];scenario=i%4;weapons=result['weapons']
    assert result['returnValue'] is False and all(w['following'] is False for w in weapons)
    if scenario==1:assert weapons==[]
    elif scenario==3:assert len(weapons)==1 and weapons[0]['montage']=='' and weapons[0]['animationClass']==''
    else:
        assert len(weapons)==(2 if scenario==2 else 1)
        assert weapons[0]['montage']==event['payload']['MontageToPlay']['path']
        assert weapons[0]['rate']==event['payload']['RateScale'] and weapons[0]['position']==0
        if scenario==2:assert weapons[1]['montage']==''
protect()
write('weapon_notify_v1_policy.json',policy)
native=dict(schemaVersion=1,dependencies={'notify_contract_v1.json':sha(root/'notify_contract_v1.json'),
    'weapon_notify_v1_policy.json':sha(root/'weapon_notify_v1_policy.json')},previousFixtureSha256=previous,
    protectedProjectSha256={p.relative_to(project.parent).as_posix():v for p,v in protected.items()},
    pluginSourceSha256=hashes,engineVersion=unreal.SystemLibrary.get_engine_version(),request=request,trace=trace,
    scope=dict(originalNotify=True,originalEquipmentManager=True,wholeMainContinuous=False,assetsSaved=0))
write('weapon_notify_v1_native.json',native)
unreal.log(f'LYRA_WEAPON_NOTIFY_NATIVE_OK objects=9 calls={len(calls)} packages={len(packages)} previous={len(previous)} assets_saved=0')
