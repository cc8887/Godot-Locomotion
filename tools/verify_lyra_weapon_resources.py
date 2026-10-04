"""Validate original weapon notify behavior and independent exported resources."""
import hashlib
import json
import re
from pathlib import Path
repo=Path(__file__).resolve().parents[1]
root=repo/'assets/generated/lyra_als'
logs=repo/'artifacts/lyra-analysis'
project=repo.parent/'GASP58'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
load=lambda p:json.loads(p.read_bytes())
def read(p):
    raw=p.read_bytes()
    return raw.decode('utf-16' if raw.startswith((b'\xff\xfe',b'\xfe\xff')) else 'utf-8-sig')
def package_file(path):
    package=path.split('.')[0]
    if package.startswith('/Game/'):return project/'Content'/(package.removeprefix('/Game/')+'.uasset')
    if package.startswith('/ShooterCore/'):return project/'Plugins/GameFeatures/ShooterCore/Content'/(package.removeprefix('/ShooterCore/')+'.uasset')
    raise ValueError(path)
native=load(root/'weapon_notify_v1_native.json')
policy=load(root/'weapon_notify_v1_policy.json')
catalog=load(root/'weapon_resources/catalog.json')
textures=load(root/'weapon_resources/textures.json')
for data in (native,policy,catalog,textures):
    for name,digest in data.get('dependencies',{}).items():assert sha(root/name)==digest,name
    for name,digest in data.get('previousFixtureSha256',{}).items():assert sha(root/name)==digest,name
for name,digest in native['protectedProjectSha256'].items():assert sha(project/name)==digest,name
for path,digest in catalog['assetSha256'].items():assert sha(package_file(path))==digest,path
for path,digest in textures['assetSha256'].items():assert sha(project/path)==digest,path
for name,digest in native['pluginSourceSha256'].items():
    assert sha(repo/'tools/unreal/LyraWeaponNotifyOracle'/name)==digest,name
    assert sha(repo/'artifacts/unreal/lyra-weapon-notify-oracle/package'/name)==digest,name
assert not (project/'Plugins/LyraWeaponNotifyOracle').exists()
follow=[c for c in policy['calls'] if c['function']=='MontageSync_Follow']
assert len(follow)==1
follower=next(p for p in follow[0]['pins'] if p['name']=='MontageFollower')
assert follower['input'] and follower['links']==[] and follower['defaultObject']=='' and follower['defaultValue']==''
assert len(policy['events'])==9 and len(native['trace']['calls'])==36
for i,row in enumerate(native['trace']['calls']):
    event=policy['events'][i//4];scenario=i%4;weapons=row['weapons']
    assert row['returnValue'] is False and all(w['following'] is False for w in weapons)
    if scenario==1:assert weapons==[]
    elif scenario==3:assert len(weapons)==1 and weapons[0]['montage']=='' and weapons[0]['animationClass']==''
    else:
        assert len(weapons)==(2 if scenario==2 else 1)
        assert weapons[0]['montage']==event['payload']['MontageToPlay']['path'] and weapons[0]['rate']==event['payload']['RateScale'] and weapons[0]['position']==0
        if scenario==2:assert weapons[1]['montage']==''
for name in ('notify-weapon-ue-first.log','notify-weapon-ue-repeat.log'):
    text=read(logs/name)
    assert text.count('LYRA_WEAPON_NOTIFY_NATIVE_OK objects=9 calls=36 packages=691 previous=827 assets_saved=0')==1
    assert 'LYRA_WEAPON_EXPORT_PROCESS_EXIT code=0' in text
    assert not re.search(r'Fatal error:|Assertion failed:|LogPython: Error:',text)
for name in ('weapon-resources-ue-final.log','weapon-resources-ue-repeat.log'):
    text=read(logs/name)
    assert text.count('LYRA_WEAPON_RESOURCES_OK montages=6 sequences=6 meshes=3 previous=829 packages=706 assets_saved=0')==1
    assert 'LYRA_WEAPON_RESOURCES_PROCESS_EXIT code=0' in text
    assert not re.search(r'Fatal error:|Assertion failed:|LogPython: Error:',text)
assert len(catalog['sequences'])==6 and len(textures['entries'])==12
for entry in list(catalog['sequences'].values())+catalog['meshes']+textures['entries']:
    assert sha(root/'weapon_resources'/entry['file'])==entry['sha256'],entry['file']
for entry in catalog['sequences'].values():
    data=load(root/'weapon_resources'/entry['file'])
    assert data['raw']['tracks'] and len(data['native'])==5
for montage in catalog['montages']:
    assert len(montage['slots'])==1 and montage['slots'][0]['name']=='DefaultSlot'
    assert montage['notifies']['events']==[] and montage['rootMotion'] is False
gate='LYRA_WEAPON_RESOURCES_GODOT_OK clips=6 meshes=3 samples=30 bones=230 frames=180 publications=540 retries=540 rejected=1620 positionCm=0 rotation=0 scale=0 native=True production=False'
for config,build in [('debug','weapon-resources-debug-build-final-preview.log'),('optimize','weapon-resources-export-release-build-accepted.log')]:
    text=read(logs/build)
    assert re.search(r'0\s*(个警告|Warning)',text) and re.search(r'0\s*(个错误|Error)',text)
    text=read(logs/f'weapon-resources-{config}-gate-verified.log')
    assert gate in text and f'LYRA_WEAPON_PROCESS_EXIT configuration={config.title()} code=0' in text
    assert not re.search(r'^\s*(ERROR|WARNING):',text,re.M)
    if config=='optimize':assert 'LYRA_WEAPON_DEBUG_RESTORED hashVerified=true' in text
for p in (logs/'weapon-resources-debug-backup-verified').iterdir():
    assert sha(repo/'.godot/mono/temp/bin/Debug'/p.name)==sha(p),p.name
text=read(logs/'weapon-resources-preview.log')
assert gate in text and 'LYRA_WEAPON_RENDER_EXIT code=0' in text
assert not re.search(r'^\s*(ERROR|WARNING):',text+read(logs/'weapon-resources-preview.log.stderr'),re.M)
for frame in (1,60,120):assert (logs/f'weapon-resource-preview-{frame}.png').is_file()
assert not re.search(r'^\s*(ERROR|WARNING):',read(logs/'weapon-resources-godot-import-final.log'),re.M)
report=load(logs/'weapon-resources-main-regression.json')
assert report['characters']==10 and report['player']['switches']==6
assert report['player']['model']['published']==480 and all(c['published']==480 for c in report['companions'])
assert report['player']['model']['skinBones']==68 and report['player']['model']['logicalBones']==81
assert report['player']['model']['weaponNotifyConsumer'] is False and report['player']['model']['nativeWholeMainParity'] is False
assert not re.search(r'^\s*(ERROR|WARNING):',read(logs/'weapon-resources-main-regression.log'),re.M)
output=logs/'weapon-resources-verification.json'
result=dict(originalNotifyObjects=9,nativeCallsPerProcess=36,independentNativeProcesses=2,montages=6,clips=6,meshes=3,textures=12,
    originalFollowerConnected=False,sourceSamplingExact=True,samplesPerConfiguration=30,bonesPerConfiguration=230,
    publicationsPerConfiguration=540,retriesPerConfiguration=540,rejectedPerConfiguration=1620,
    debugRestored=True,renderCaptures=3,ordinaryRoleFrames=4800,previousNotifyJson=827,previousResourceJson=829,
    protectedResourcePackages=len(catalog['assetSha256']),protectedTexturePackages=len(textures['assetSha256']),
    sourceJsonSha256={n:sha(root/n) for n in ('weapon_notify_v1_policy.json','weapon_notify_v1_native.json','weapon_resources/catalog.json','weapon_resources/textures.json')},
    scope=dict(productionWeaponConsumer=False,continuousWeaponMontageNative=False,wholeMainNative=False,materialParity=False,goalComplete=False))
with output.open('x',encoding='utf-8') as f:json.dump(result,f,indent=2)
print('LYRA_WEAPON_RESOURCES_VERIFIED '+str(output))
