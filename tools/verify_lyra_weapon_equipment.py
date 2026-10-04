"""Audit observed equipment prerequisites, native graphs and real Godot roles."""
import hashlib
import json
import re
import struct
import xml.etree.ElementTree as ET
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'
project = repo.parent / 'GASP58'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
load = lambda p: json.loads(p.read_bytes())

def read(p):
    data = p.read_bytes()
    return data.decode('utf-16' if data.startswith((b'\xff\xfe', b'\xfe\xff')) else 'utf-8-sig')

def package_file(path):
    path = path.split('.')[0]
    if path.startswith('/Game/'):
        return project / 'Content' / (path.removeprefix('/Game/') + '.uasset')
    if path.startswith('/ShooterCore/'):
        return project / 'Plugins/GameFeatures/ShooterCore/Content' / (path.removeprefix('/ShooterCore/') + '.uasset')
    raise ValueError(path)

native = load(root / 'weapon_equipment_v1_native.json')
request = load(root / 'weapon_equipment_v1_requests.json')
policy = load(root / 'weapon_equipment_v1_policy.json')
assert native['requestSha256'] == sha(root / 'weapon_equipment_v1_requests.json')
for data in (native, policy):
    for p, digest in data['dependencies'].items(): assert sha(root / p) == digest, p
    for p, digest in data['pluginSourceSha256'].items():
        assert sha(repo / 'tools/unreal/LyraWeaponEquipmentOracle' / p) == digest, p
        assert sha(repo / 'artifacts/unreal/lyra-weapon-equipment-oracle/package-fixed' / p) == digest, p
for p, digest in native['assetSha256'].items(): assert sha(package_file(p)) == digest, p
for p, digest in native['previousFixtureSha256'].items(): assert sha(root / p) == digest, p
for p, digest in native['protectedProjectSha256'].items(): assert sha(project / p) == digest, p
assert len(native['previousFixtureSha256']) == 840 and len(native['assetSha256']) == 706
assert not (project / 'Plugins/LyraWeaponEquipmentOracle').exists()
assert 'Result: Succeeded' in read(logs / 'weapon-equipment-build-package-fixed.log')
for name in ('first', 'repeat'):
    text = read(logs / f'weapon-equipment-ue-{name}.log')
    assert text.count('LYRA_WEAPON_EQUIPMENT_NATIVE_OK traces=9 frames=5040 skin=7 previous=840 packages=706 assets_saved=0') == 1
    assert 'LYRA_WEAPON_EQUIPMENT_PROCESS_EXIT code=0' in text
    assert not re.search(r'Assertion failed:|Fatal error:|LogPython: Error:|LYRA_WEAPON_EQUIPMENT_FAILED', text)
for definition in policy['definitions'].values():
    assert definition['meshIsActorRoot'] and definition['socket'] == 'weapon_r'
    assert definition['parentTickGroup'] == definition['rootTickGroup'] == definition['meshTickGroup'] == 0
    assert len(definition['tickChain']) == 1
    assert all(e['parentTickPrerequisite'] and e['childCanTick'] and e['parentCanTick'] for e in definition['tickChain'])
frames = poses = received = attachments = 0
for t, q in zip(native['trace']['traces'], request['traces'], strict=True):
    assert t['kind'] == q['kind'] and t['hz'] == q['hz'] and q['hz'] in (30, 60, 120)
    assert t['frames'][0]['frozen'][0]['position'] == struct.unpack('f', struct.pack('f', 1 / q['hz']))[0]
    attachments += len(t['attachments'])
    for f, i in zip(t['frames'], q['frames'], strict=True):
        frames += 1
        assert ('pose' in f) == i['sample']
        if 'pose' in f:
            poses += 1
            assert len(f['pose']) == 7 and f['curves'] == [] and f['attributes'] == 0
        assert len(f['received']) == len(i['notifies'])
        for r in f['received']:
            received += 1
            assert r['returnValue'] is False and r['following'] is False and r['positionBeforeTick'] == 0
assert (frames, poses, received, attachments) == (5040, 4323, 72, 36)

reports = []
role_metrics = []
for config in ('debug', 'optimize'):
    text = read(logs / f'weapon-equipment-{config}-gate-final2.log')
    assert f'LYRA_WEAPON_EQUIPMENT_MATRIX_OK configuration={config.title()}' in text
    assert not re.search(r'^\s*(ERROR|WARNING):', text, re.M)
    for name in ('equipment-30', 'equipment-60', 'equipment-120', 'prior-weapon-graph', 'prior-gameplay', 'prior-context', 'ordinary-ten-rebind'):
        assert f'LYRA_WEAPON_EQUIPMENT_RUN_EXIT name={name} code=0' in text
    assert text.count('LYRA_WEAPON_EQUIPMENT_NATIVE_GODOT_OK traces=9 frames=5040 poses=4323 bones=30261 notifies=72 retries=5040 attachments=36 positionCm=0 quaternion=0 scale=0') == 3
    assert text.count('LYRA_WEAPON_NOTIFY_CONSUMER_GODOT_OK calls=36 played=18 missing=18 retries=36') == 3
    assert text.count('LYRA_WEAPON_FRESH_LOOKUP_GODOT_OK callbacks=2') == 3
    assert text.count('LYRA_WEAPON_CALLBACK_LIFETIME_GODOT_OK disposed=1 queuedDeletion=1 rebind=1 reentrantPrepareRejected=3') == 3
    metrics = []
    for line in re.findall(r'LYRA_WEAPON_EQUIPMENT_ROLE_GODOT_OK[^\r\n]+', text):
        row = dict(re.findall(r'(\w+)=(\S+)', line)); hz = int(row['hz'])
        assert int(row['frames']) == hz * 8 and int(row['retries']) == hz * 48
        assert int(row['publications']) == hz * 32 and int(row['worldBones']) == hz * 224
        assert row['played'] == '16' and row['switches'] == '12' and row['same'] == '6'
        assert row['wholeMainNative'] == 'false' and row['finalSocket'] == 'true'
        assert float(row['worldPositionM']) <= 1e-4 and float(row['worldQuaternion']) <= 1e-6
        metrics.append(row)
    assert [int(m['hz']) for m in metrics] == [30, 60, 120]
    role_metrics.append(metrics)
    assemblies = dict(re.findall(r'LYRA_WEAPON_EQUIPMENT_ASSEMBLY file=(\S+) sha256=(\w+)', text))
    assert len(assemblies) == 3
    directory = repo / '.godot/mono/temp/bin' / ('ExportRelease' if config == 'optimize' else 'Debug')
    for name, digest in assemblies.items(): assert sha(directory / name).upper() == digest, name
    if config == 'optimize': assert 'LYRA_WEAPON_EQUIPMENT_DEBUG_RESTORED hashVerified=true' in text
    build = read(logs / f'weapon-equipment-{"export-release" if config == "optimize" else "debug"}-build-accepted-final2.log')
    assert re.search(r'0\s*(个警告|Warning)', build) and re.search(r'0\s*(个错误|Error)', build)
    report = load(logs / f'weapon-equipment-{config}-main-final2.json')
    model = report['player']['model']; actors = report['companions']
    assert report['characters'] == 10 and len(actors) == 9 and all(c['published'] == 480 for c in actors)
    assert model['published'] == model['frames'] == 480 and model['skinBones'] == 68 and model['logicalBones'] == 81
    assert model['weaponNotifyConsumer'] is True and model['nativeWholeMainParity'] is False and model['productionAccepted'] is False
    assert model['weaponPublications'] + sum(c['weaponPublications'] for c in actors) == 3168
    assert model['weaponPlayed'] + sum(c['weaponPlayed'] for c in actors) == 21
    assert model['weaponMissing'] + sum(c['weaponMissing'] for c in actors) == 0
    reports.append(report)
assert reports[0] == reports[1] and role_metrics[0] == role_metrics[1]

trx = ET.parse(logs / 'weapon-equipment-core.trx')
counters = trx.find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters')
assert counters is not None and counters.get('passed') == counters.get('total') == '187' and counters.get('failed') == counters.get('notExecuted') == '0'
for name in ('GodotALS.dll', 'GodotALS.pdb', 'Als.Core.dll', 'Als.Core.pdb', 'Als.Import.dll', 'Als.Import.pdb'):
    assert sha(repo / '.godot/mono/temp/bin/Debug' / name) == sha(logs / 'weapon-equipment-debug-backup' / name)
render = read(logs / 'weapon-equipment-render.log')
assert 'LYRA_WEAPON_EQUIPMENT_ROLE_GODOT_OK hz=60 roles=6' in render and 'captures=3' in render
assert 'LYRA_WEAPON_EQUIPMENT_RENDER_EXIT code=0' in render and not re.search(r'^\s*(ERROR|WARNING):', render, re.M)
assert not (logs / 'weapon-equipment-render.stderr.log').read_bytes()
images = {}
for frame in (30, 90, 270):
    path = logs / f'weapon-equipment-render-{frame}.png'; data = path.read_bytes()
    assert data[:8] == b'\x89PNG\r\n\x1a\n' and len(data) > 10000
    images[path.name] = dict(sha256=sha(path), size=struct.unpack('>II', data[16:24]))
summary = dict(schemaVersion=1, nativeProcesses=2, nativeFrames=frames, nativePoses=poses, nativeBones=poses * 7,
    originalNotifies=received, nativeAttachments=attachments, nativePQS=[0, 0, 0],
    nativeConsumerCases=36, steadyRoleFramesPerBuild=10080, rolePublicationsPerBuild=6720, worldBonesPerBuild=47040,
    liveWeaponNotifiesPerBuild=48, coreTests=187, ordinaryRoleFramesPerBuild=4800, ordinaryWeaponPublications=3168,
    ordinaryWeaponNotifies=21, roleMetrics=role_metrics[0], render=images, previousJson=840, protectedPackages=706, debugRestored=True,
    scope=dict(alsSkin68=True, typedLayers14=True, weaponNotifyConsumer=True, finalWeaponSocket=True, productionPistolRifle=True,
               originalTickPrerequisitesObserved=True, manuallyOrderedNativeAnimation=True, wholeUEWorldTick=False,
               nativeWholeMain=False, completeShotgunMain=False, materialParity=False, goalComplete=False))
(logs / 'weapon-equipment-verification.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps(summary, ensure_ascii=False, separators=(',', ':')))
