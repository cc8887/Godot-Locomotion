"""Check runtime logs, immutable resources, source freeze and assembly restore."""
import hashlib
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[1]
out = repo / 'artifacts/lyra-analysis'
tag = 'unlink-production-v9'
read = lambda p: json.loads(p.read_bytes())
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()

def log(path):
    b = path.read_bytes()
    return b.decode('utf-16') if b.startswith((b'\xff\xfe', b'\xfe\xff')) else b.decode('utf-8-sig')

sources = read(out / f'{tag}-frozen-sources.json')
correction = read(out / f'{tag}-audit-correction.json')
for p, digest in sources.items():
    if p == 'tools/verify_lyra_character_unlink.py':
        assert digest == correction['originalSha256'], p
        assert sha(repo/p) == correction['correctedSha256'], p
    else:
        assert sha(repo/p) == digest, p
previous = read(out/'default-main-v5-frozen-sources-accepted.json')
for p, digest in previous.items():
    if p not in sources:
        assert sha(repo/p) == digest, p

closure = read(out/'default-layer-runtime-v2-closure.json')
assets = repo/'assets/generated/lyra_als'
current = {p.relative_to(assets).as_posix(): sha(p) for p in assets.rglob('*.json')}
expected = dict(closure['previousFixtureSha256'])
expected['default_layer_graphs_v1.json'] = closure['resourceSha256']
assert current == expected and len(current) == 870
project = Path('../GASP58')
for p, digest in closure['protectedProject'].items():
    assert sha(project/p) == digest, p
for p, digest in closure['assetSha256'].items():
    path = p.split('.')[0]
    if path.startswith('/Game/'):
        file = project/'Content'/(path.removeprefix('/Game/')+'.uasset')
    elif path.startswith('/ShooterCore/'):
        file = project/'Plugins/GameFeatures/ShooterCore/Content'/(path.removeprefix('/ShooterCore/')+'.uasset')
    else:
        raise ValueError(path)
    assert sha(file) == digest, p

cases = {'unlink-30':30, 'unlink-60':60, 'unlink-120':120,
         'unlink-three':60, 'unlink-mixed':60, 'unlink-per-call':60}
processes = 0
counts = {}
ordinary = {}
for config in ('debug', 'optimize'):
    build = log(out/f'{tag}-build-{config}.log')
    assert re.search(r'^\s*0\s*(个警告|Warning)',build,re.M)
    assert re.search(r'^\s*0\s*(个错误|Error)',build,re.M)
    report = read(out/f'{tag}-{config}-verification.json')
    assert report['passed'] and report['fullMainUnlink'] and not report['goalComplete']
    assert len(report['runs']) == 13
    values = {}
    for row in report['runs']:
        path = Path(row['log']); text = log(path)
        assert sha(path) == row['logSha256'].lower() and row['exitCode'] == 0 and row['passed']
        assert 'LYRA_DEFAULT_ROUTES_EXIT=0' in text
        assert not re.search(r'^\s*(ERROR|WARNING):',text,re.M)
        if row['name'] in cases:
            match = re.search(r'LYRA_CHARACTER_UNLINK_OK hz=(\d+) frames=(\d+) self=(\d+) retry=(\d+) rejected=(\d+) switches=(\d+) montageSelf=(\d+) rigChanged=(\d+) rigCompleted=(\d+) airSelf=(\d+)', text)
            assert match is not None
            hz,frames,self_frames,retry,rejected,switches,montage,rig,completed,air = map(int,match.groups())
            assert hz == cases[row['name']] and (frames,self_frames,retry,switches)==(36*hz,12*hz,18*hz,24)
            assert rejected>0 and montage>0 and rig>0 and completed==self_frames and air>0
            values[row['name']] = dict(hz=hz,frames=frames,self=self_frames,retry=retry,rejected=rejected,montage=montage,rig=rig,rigCompleted=completed,air=air)
        if 'report' in row:
            report_path=Path(row['report'])
            assert sha(report_path) == row['reportSha256'].lower()
            ordinary[config,row['name']]=read(report_path)
        processes += 1
    counts[config]=values
    for group in ('whole-main','other-groups'):
        whole=read(out/f'{tag}-{group}-{config}-verification.json')
        assert whole['passed'] and len(whole['runs'])==2
        for row in whole['runs']:
            text=log(Path(row['log']))
            assert sha(Path(row['log']))==row['logSha256'].lower() and row['passed'] and row['exitCode']==0 and not re.search(r'^\s*(ERROR|WARNING):',text,re.M)
            assert 'LYRA_MULTI_OWNER_MAIN_GRAPH_OK' in text and 'LYRA_WHOLE_MAIN_DIAGNOSTIC_OK frames=1080' in text and 'retry=1080' in text
            assert whole['workerFields'] and whole['preUpdateFields'] and whole['movementFields'] and whole['graphFields']
            assert row['frames']==1080 and row['boundary']=='final'
            processes += 1

assert counts['debug'] == counts['optimize']
for config in counts:
    assert sum(c['frames'] for c in counts[config].values())==14040
    assert sum(c['self'] for c in counts[config].values())==4680
    assert sum(c['retry'] for c in counts[config].values())==7020
for name in ('ordinary-ten','ordinary-emote'):
    assert ordinary['debug',name] == ordinary['optimize',name]
    assert ordinary['debug',name] == read(out/f'default-main-v5-debug-{name}.json')

backup=out/f'{tag}-optimize-debug-backup'
for p in backup.iterdir():
    assert sha(p) == sha(repo/'.godot/mono/temp/bin/Debug'/p.name), p.name
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
tests=ET.parse(out/f'{tag}-core.trx').find('.//t:Counters',ns).attrib
assert tests['total']==tests['passed']=='59' and tests['failed']==tests['notExecuted']=='0'
assert processes == 34
result=dict(auditPassed=True,godotProcesses=processes,coreTests=59,counts=counts,
            resources=len(current),protectedPackages=len(closure['assetSha256']),
            scope=dict(productionCharacterUnlink=True,zeroLinkedInstances=True,
                       defaultFinalRigExecuted=True,emptySharedSync=True,physicalMontageContinues=True,
                       newAlsDefaultNativeFinalPose=False,completeInitializeCacheBones=False,
                       partialBindings=False,fullPrivateFields=False,fullPhysicsParity=False,goalComplete=False),
            sourceSha256=sources,auditorSha256=sha(Path(__file__)))
with (out/f'{tag}-integrity.json').open('x',encoding='utf-8') as f:
    json.dump(result,f,indent=2)
print(f'LYRA_CHARACTER_UNLINK_AUDIT_OK processes={processes} core=59 resources=870 goalComplete=false')
