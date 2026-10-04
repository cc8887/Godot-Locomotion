"""Capture original equipment transforms and weapon graph after original Notify.

The actual attachment prerequisites are observed; animation calls are manually
ordered to match them. This does not claim a continuous full-world tick oracle.
"""
import hashlib
import json
import math
from pathlib import Path
import unreal

repo = Path(__file__).resolve().parents[2]
root = repo / 'assets/generated/lyra_als'
prefix = 'weapon_equipment_v1_'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
catalog = json.loads((root / 'weapon_resources/catalog.json').read_bytes())
notify = json.loads((root / 'weapon_notify_v1_policy.json').read_bytes())
project = Path(unreal.Paths.get_project_file_path())
protected = {p.relative_to(project.parent).as_posix(): sha(p) for p in [project, *(project.parent / 'Config').rglob('*.ini')]}
previous = {p.relative_to(root).as_posix(): sha(p) for p in root.rglob('*.json') if not p.name.startswith(prefix)}
plugin = repo / 'tools/unreal/LyraWeaponEquipmentOracle'
sources = {p.relative_to(plugin).as_posix(): sha(p) for p in plugin.rglob('*') if p.is_file()}

def package_file(path):
    path = path.split('.')[0]
    if path.startswith('/Game/'):
        return project.parent / 'Content' / (path.removeprefix('/Game/') + '.uasset')
    if path.startswith('/ShooterCore/'):
        return project.parent / 'Plugins/GameFeatures/ShooterCore/Content' / (path.removeprefix('/ShooterCore/') + '.uasset')
    raise ValueError(path)

def protect():
    for p, d in protected.items(): assert sha(project.parent / p) == d, p
    for p, d in previous.items(): assert sha(root / p) == d, p
    for p, d in catalog['assetSha256'].items(): assert sha(package_file(p)) == d, p
    for p, d in sources.items(): assert sha(plugin / p) == d, p

def write(name, data):
    p = root / (prefix + name + '.json')
    if p.exists():
        assert json.loads(p.read_bytes()) == data, 'Independent equipment capture changed: ' + p.name
    else:
        with p.open('x', encoding='utf-8', newline='\n') as f:
            f.write(json.dumps(data, separators=(',', ':'), allow_nan=False) + '\n')
    return sha(p)

def atom(position, rotation, scale=1):
    return dict(position=position, rotation=rotation, scale=[scale] * 3)

protect()
assert len(previous) == 840
assert unreal.AlsLyraControlRigLibrary.prefer_raw_track_data_model(True)
targets = [atom([0, 0, 0], [0, 0, 0, 1]),
           atom([120, -35, 180], [0, 0, math.sin(.6), math.cos(.6)]),
           atom([-240, 170, 25], [math.sin(.4), 0, 0, math.cos(.4)], .7),
           atom([720, 30, 110], [0, math.sin(-.35), 0, math.cos(-.35)], 1.4)]
traces = []
for mesh in catalog['meshes']:
    kind = mesh['kind']
    equipment = next(e for e in notify['equipment'] if e['name'].lower() == kind)
    events = [e for e in notify['events'] if e['asset'].split('/')[3].lower() == kind]
    montages = [m for m in catalog['montages'] if m['skeleton'] == mesh['skeleton']]
    assert len(events) == 3 and len(montages) == 2
    for hz in (30, 60, 120):
        frames = [dict(delta=1 / hz, sample=i % 7 != 4, notifies=[]) for i in range(8 * hz)]
        for time, event in [(0, 0), (.1, 0), (.2, 1), (2.5, 2), (3, 0), (4, 1), (4.2, 0), (5, 2)]:
            frames[round(time * hz)]['notifies'].append(dict(asset=event, index=events[event]['index']))
        frames[round(3 * hz)]['delta'] = 0
        traces.append(dict(kind=kind, hz=hz, mesh=mesh['source'], **{'class': mesh['animationBlueprint']},
                           definition=equipment['definition'], montages=[m['path'] for m in montages],
                           notifyAssets=[e['asset'] for e in events], socketTargets=targets, frames=frames))
request_sha = write('requests', dict(schemaVersion=1, traces=traces, phase='originalReceivedNotifyBeforeWeaponAdvance', rawData=True))
trace = json.loads(unreal.LyraWeaponEquipmentOracleLibrary.read_trace(json.dumps(dict(traces=traces))))
protect()
assert len(trace['traces']) == 9
frame_count = sum(len(t['frames']) for t in trace['traces'])
assert frame_count == 5040
definitions = {}
for t in trace['traces']:
    definition = t['equipment']
    assert definition['socket'] == 'weapon_r' and definition['tickChain']
    assert all(e['parentTickPrerequisite'] for e in definition['tickChain'])
    if t['kind'] in definitions: assert definitions[t['kind']] == definition
    else: definitions[t['kind']] = definition
    assert len(t['attachments']) == 4
    for f in t['frames']:
        for n in f['received']:
            assert n['returnValue'] is False and n['following'] is False and n['positionBeforeTick'] == 0
        if 'pose' in f: assert len(f['pose']) == 7 and f['curves'] == [] and f['attributes'] == 0
dependencies = {p: sha(root / p) for p in ['weapon_resources/catalog.json', 'weapon_notify_v1_policy.json', 'weapon_montage_v1_policy.json']}
policy_sha = write('policy', dict(schemaVersion=1, dependencies=dependencies, definitions=definitions,
                                pluginSourceSha256=sources, phase='originalReceivedNotifyBeforeWeaponAdvance'))
write('native', dict(schemaVersion=1, requestSha256=request_sha,
    dependencies={**dependencies, prefix + 'policy.json': policy_sha}, trace=trace,
    previousFixtureSha256=previous, protectedProjectSha256=protected, assetSha256=catalog['assetSha256'], pluginSourceSha256=sources,
    scope=dict(originalEquipmentManager=True, actualAttachmentPrerequisites=True, fullPrecisionAttachment=True,
               originalNotify=True, originalWeaponGraph=True, manuallyOrderedComponentAnimation=True,
               wholeWorldTick=False, wholeMainContinuous=False, productionConsumer=False, assetsSaved=0)))
unreal.log(f'LYRA_WEAPON_EQUIPMENT_NATIVE_OK traces=9 frames={frame_count} skin=7 previous={len(previous)} packages={len(catalog["assetSha256"])} assets_saved=0')
