"""Audit real weapon AnimBP/Montage captures, both Godot builds, and preserved resources."""
import hashlib
import json
import re
import xml.etree.ElementTree as ET
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

native=load(root/'weapon_montage_v1_native.json')
requests=load(root/'weapon_montage_v1_requests.json')
policy=load(root/'weapon_montage_v1_policy.json')
catalog=load(root/'weapon_resources/catalog.json')
assert native['requestSha256']==sha(root/'weapon_montage_v1_requests.json')
for document in (native,policy):
    for name,digest in document['dependencies'].items():assert sha(root/name)==digest,name
    for name,digest in document['assetSha256'].items():assert sha(package_file(name))==digest,name
    for name,digest in document['pluginSourceSha256'].items():
        assert sha(repo/'tools/unreal/LyraWeaponMontageOracle'/name)==digest,name
        assert sha(repo/'artifacts/unreal/lyra-weapon-montage-oracle/package-fixed'/name)==digest,name
assert len(native['previousFixtureSha256'])==837
for name,digest in native['previousFixtureSha256'].items():assert sha(root/name)==digest,name
for name,digest in native['protectedProjectSha256'].items():assert sha(project/name)==digest,name
assert not (project/'Plugins/LyraWeaponMontageOracle').exists()
assert 'Result: Succeeded' in read(logs/'weapon-montage-build-package-fixed.log')
for filename in ('weapon-montage-ue-first.log','weapon-montage-ue-repeat.log'):
    text=read(logs/filename)
    assert text.count('LYRA_WEAPON_MONTAGE_NATIVE_OK traces=9 frames=8820 skin=7 previous=837 packages=706 assets_saved=0')==1
    assert 'LYRA_WEAPON_MONTAGE_PROCESS_EXIT code=0' in text
    assert not re.search(r'Assertion failed:|Fatal error:|LogPython: Error:|LYRA_WEAPON_MONTAGE_FAILED',text)

reference_differences={}
for kind,definition in policy['definitions'].items():
    mesh=next(m for m in catalog['meshes'] if m['kind']==kind)['metadata']
    assert len(definition['names'])==7
    nodes=definition['nodes']
    root_node=next(n for n in nodes if n['type'].endswith('.AnimNode_Root'))
    slot=next(n for n in nodes if n['type'].endswith('.AnimNode_Slot'))
    reference=next(n for n in nodes if n['type'].endswith('.AnimNode_RefPose'))
    assert len(nodes)==3 and root_node['source']==slot['index'] and slot['source']==reference['index']
    assert slot['name']=='DefaultSlot' and not slot['alwaysUpdate'] and reference['refPoseType']==0
    assert definition['rootMotionMode']==3
    differences=[]
    for i,name in enumerate(definition['names']):
        index=mesh['logicalBoneNames'].index(name)
        parent=definition['parents'][i]
        assert mesh['logicalParents'][index]==(-1 if parent<0 else mesh['logicalBoneNames'].index(definition['names'][parent]))
        if definition['reference'][i]!=mesh['referencePose'][index]:differences.append(name)
    reference_differences[kind]=differences
assert all(reference_differences.values())
frames=poses=0
for t,q in zip(native['trace']['traces'],requests['traces'],strict=True):
    assert t['kind']==q['kind'] and t['hz']==q['hz'] and q['hz'] in (30,60,120)
    assert not t['frames'][0]['frozen'] and not t['frames'][-1]['frozen']
    for f,i in zip(t['frames'],q['frames'],strict=True):
        frames+=1
        assert ('pose' in f)==i['sample']
        if 'pose' in f:
            poses+=1
            assert len(f['pose'])==7 and not f['curves'] and f['attributes']==0
assert frames==8820 and poses==7560

pattern=r'LYRA_WEAPON_MONTAGE_GODOT_OK traces=9 frames=8820 poses=7560 bones=52920 retries=8820 rejected=79389 updateOnly=1260 reverse=385 zero=36 overweight=393 reference=3995 hiddenSource=4350 positionCm=0 quaternion=0 scale=0 originalGraph=true characterIntegration=false'
reports=[]
for configuration in ('debug','optimize'):
    text=read(logs/f'weapon-montage-{configuration}-gate.log')
    assert len(re.findall(pattern,text))==1
    assert 'LYRA_WEAPON_MONTAGE_MODEL_OK frames=180 models=3 publications=540 retries=540 rejected=1620 characterIntegration=false' in text
    assert 'LYRA_WEAPON_RESOURCES_GODOT_OK clips=6 meshes=3 samples=30 bones=230 frames=180 publications=540 retries=540 rejected=1620 positionCm=0 rotation=0 scale=0 native=True production=False' in text
    assert f'LYRA_WEAPON_MONTAGE_MATRIX_OK configuration={configuration.title()}' in text
    assert not re.search(r'^\s*(ERROR|WARNING):',text,re.M)
    for name in ('original-weapon-graph','original-resource-regression','ordinary-ten-rebind'):
        assert f'LYRA_WEAPON_MONTAGE_RUN_EXIT name={name} code=0' in text
    assemblies=dict(re.findall(r'LYRA_WEAPON_MONTAGE_ASSEMBLY file=(\S+) sha256=(\w+)',text))
    assert len(assemblies)==3
    directory=repo/'.godot/mono/temp/bin'/('ExportRelease' if configuration=='optimize' else 'Debug')
    for name,digest in assemblies.items():assert sha(directory/name).upper()==digest,name
    if configuration=='optimize':assert 'LYRA_WEAPON_MONTAGE_DEBUG_RESTORED hashVerified=true' in text
    report=load(logs/f'weapon-montage-{configuration}-main.json')
    assert report['characters']==10 and len(report['companions'])==9
    model=report['player']['model']
    assert model['frames']==480 and model['published']==480 and model['skinBones']==68 and model['logicalBones']==81
    assert model['actualGodotPhysics'] and model['finalRig'] and report['player']['switches']==6
    assert model['weaponNotifyConsumer'] is False and model['nativeWholeMainParity'] is False and model['productionAccepted'] is False
    assert all(c['published']==480 for c in report['companions'])
    reports.append(report)
assert reports[0]==reports[1]
for name in ('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb'):
    assert sha(repo/'.godot/mono/temp/bin/Debug'/name)==sha(logs/'weapon-montage-debug-backup'/name)
build=read(logs/'weapon-montage-export-release-build-accepted.log')
assert 'configuration=ExportRelease Optimize=true' in build and 'LYRA_WEAPON_MONTAGE_BUILD_EXIT code=0' in build
for filename in ('weapon-montage-debug-build-accepted.log','weapon-montage-export-release-build-accepted.log'):
    text=read(logs/filename)
    assert re.search(r'0\s*(个警告|Warning)',text) and re.search(r'0\s*(个错误|Error)',text)
trx=ET.parse(logs/'weapon-montage-core-normalization.trx').getroot()
counts=trx.find('{*}ResultSummary/{*}Counters').attrib
assert counts['total']=='11' and counts['passed']=='11' and counts['failed']=='0' and counts['notExecuted']=='0'
render=read(logs/'weapon-montage-render.log')
assert re.search(pattern,render) and len(re.findall(r'LYRA_WEAPON_MONTAGE_CAPTURE frame=',render))==3
assert not re.search(r'^\s*(ERROR|WARNING):',render,re.M)
assert (logs/'weapon-montage-render.stderr.log').read_bytes()==b''
for frame in (1,60,120):assert (logs/f'weapon-montage-render-{frame}.png').stat().st_size>10000

report=dict(nativeProcesses=2,traces=9,framesPerConfiguration=frames,posesPerConfiguration=poses,bonesPerConfiguration=poses*7,
    cancelledRetriesPerConfiguration=8820,rejectedPerConfiguration=79389,modelPublicationsPerConfiguration=540,
    meshReferenceDifferences=reference_differences,coreTests=11,renderCaptures=3,ordinaryRoleFramesPerConfiguration=4800,
    debugRestored=True,previousJson=837,protectedPackages=706,
    scope=dict(originalWeaponGraphAndOwnBank=True,productionWeaponConsumer=False,componentTickOrdering=False,
        wholeMainNative=False,materialParity=False,goalComplete=False))
with (logs/'weapon-montage-verification.json').open('x',encoding='utf-8',newline='\n') as f:f.write(json.dumps(report,indent=2)+'\n')
print(json.dumps(report,ensure_ascii=False))
