"""Verify the ALS81 action closure, native evidence and immutable dependencies."""
import hashlib
import json
import re
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
directory = root / 'montage_actions'
logs = repo / 'artifacts/lyra-analysis'
content = Path('../GASP58/Content')
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
load = lambda p: json.loads(p.read_bytes())
catalog = load(directory / 'catalog.json')
original = load(root / 'montage_catalog_v2.json')
prior = load(root / 'logical_controls/catalog.json')
assert catalog['schemaVersion'] == 1 and catalog['skinPreservation'] == 0
assert len(catalog['entries']) == 55 and sum(e['additive'] for e in catalog['entries']) == 27
assert set(e['source'] for e in catalog['entries']) == set(original['sequences'])
for p, digest in catalog['dependencies'].items():
    assert sha(root / p) == digest, p
for p, digest in catalog['previousFixtureSha256'].items():
    assert sha(root / p) == digest, 'Changed previous JSON: ' + p
for p, digest in catalog['assetSha256'].items():
    assert sha(content / (p.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, 'Changed package: ' + p
assert original['assetSha256'].items() <= catalog['assetSha256'].items()
assert sha(repo / 'tools/unreal/export_lyra_montage_resources.py') == catalog['exportScriptSha256']
for p, digest in catalog['probeSourceSha256'].items():
    assert sha(repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter' / p) == digest
    for tree in ('source', 'package'):
        assert sha(repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter' / p) == digest
entries = {e['slot']: e for e in catalog['entries']}
all_sources = {e['source']: e for e in catalog['entries'] + prior['entries']}
curves = {e['slot']: e for e in catalog['curves']}
assert set(entries) == set(curves)
local = mesh = nonzero = attributes = float_curves = 0
for slot, e in entries.items():
    path = directory / e['file']
    assert path.resolve().is_relative_to((directory / 'clips').resolve()) and sha(path) == e['sha256']
    clip = load(path); raw = clip['raw']; metadata = clip['metadata']; source = original['sequences'][e['source']]
    assert clip['source'] == e['source'] and clip['target'] == raw['source'] == e['target']
    assert raw['sampledKeyCount'] == e['keyCount'] and raw['playLength'] == e['playLength']
    for key in ('additiveType', 'basePoseType', 'baseFrame', 'sequencePlayLength', 'enableRootMotion',
                'forceRootLock', 'rootMotionRootLock', 'animatedBoneAttributeCount', 'transformCurveCount'):
        assert metadata[key] == source[key], (slot, key)
    assert [n.casefold() for n in metadata['floatCurveNames']] == [n.casefold() for n in source['floatCurveNames']]
    assert len(curves[slot]['attributes']) == metadata['animatedBoneAttributeCount']
    assert [c['name'] for c in curves[slot]['curves']] == metadata['floatCurveNames']
    assert metadata['transformCurveCount'] == curves[slot]['transformCurves'] == 0
    attributes += metadata['animatedBoneAttributeCount']; float_curves += len(metadata['floatCurveNames'])
    bone_names = [t['bone'].casefold() for t in raw['tracks']]
    assert ('weapon_r' in bone_names) == e['weaponTrackPresent'] == clip['weaponTrackPresent']
    assert 'vb ik_hand_l_weaponspace' not in bone_names
    base_time = 0; base_slot = None
    if e['additive']:
        assert metadata['additiveType'] in ('AAT_LocalSpaceBase', 'AAT_RotationOffsetMeshSpace')
        local += metadata['additiveType'] == 'AAT_LocalSpaceBase'
        mesh += metadata['additiveType'] == 'AAT_RotationOffsetMeshSpace'
        base_source = e['source'] if metadata['basePoseType'] == 'ABPT_LocalAnimFrame' else source['baseAsset']
        base = all_sources[base_source]; base_slot = base['slot']
        assert metadata['baseAsset'] == (None if metadata['basePoseType'] == 'ABPT_LocalAnimFrame' else base['target'])
        length = base.get('sequencePlayLength', base['playLength'])
        base_time = length * min(1, max(0, metadata['baseFrame'] / base['keyCount']))
        nonzero += base_time > 0
    assert e['baseSlot'] == curves[slot]['baseSlot'] == base_slot
    assert e['baseSampleTime'] == curves[slot]['baseSampleTime'] == base_time
assert (local, mesh, nonzero, attributes, float_curves) == (24, 3, 4, 200, 3)
digest = sha(directory / 'catalog.json')
native = load(directory / 'native.json'); playback = load(directory / 'playback.json'); bindings = load(directory / 'bindings.json')
for document in (native, playback, bindings):
    assert document['schemaVersion'] == 1 and document['catalogSha256'] == digest
assert {r['slot'] for r in native['rows']} == {r['slot'] for r in native['curveRows']} == {r['slot'] for r in playback['entries']} == set(entries)
assert all(len(r['raw']) == len(r['output']) == 81 for r in native['rows'])
notify_count = 0
for row in playback['entries']:
    assert row['sourceSync']['markers'] == row['targetSync']['markers']
    assert row['sourceSync']['rateScale'] == row['targetSync']['rateScale']
    assert row['sourceNotifies']['events'] == row['targetNotifies']['events'], row['slot']
    notify_count += len(row['sourceNotifies']['events'])
assert bindings['montageCatalogSha256'] == sha(root / 'montage_catalog_v2.json')
assert bindings['sequences'] == {e['source']: e['slot'] for e in entries.values()}
assert bindings['assets'] == [dict(path=a['path'], tracks=[dict(slot=t['name'], sequence=all_sources[t['segments'][0]['animation']]['slot'])
    for t in a['slots']]) for a in original['assets']]
tracks = sum(len(a['tracks']) for a in bindings['assets'])
assert len(bindings['assets']) == 45 and tracks == 60

def checked_log(name, marker):
    text = (logs / name).read_text(encoding='utf-8-sig')
    assert len(re.findall(marker, text)) == 1, name
    assert not re.search(r'(?m)^ERROR:|LogPython: Error:|LogWindows: Error:|Assertion failed|Ensure condition failed', text), name
    return text

warnings = {}
for name in ('lyra-montage-actions-resources-final-ue.log', 'lyra-montage-actions-repeat-ue.log'):
    text = checked_log(name, 'LYRA_MONTAGE_ACTIONS_RESOURCES_OK actions=55 additive=27 logical=81 skin=68')
    assert 'LYRA_EXPORT_PROCESS_EXIT_OK mode=montage-resources code=0' in text
    assert 'LYRA_RAW_TRACK_DATA_MODEL_STARTUP' in text
    warnings[name] = len(re.findall('Warning:', text))
godot_results = {}
for name in ('lyra-montage-resources-godot.log', 'lyra-montage-resources-godot-optimize.log'):
    text = checked_log(name, 'LYRA_MONTAGE_RESOURCES_GODOT_OK sources=300 actions=55')
    assert 'code=0' in text and 'WARNING:' not in text
    marker = next(line for line in text.splitlines() if line.startswith('LYRA_MONTAGE_RESOURCES_GODOT_OK'))
    godot_results[name] = dict(re.findall(r'(\w+)=([^ ]+)', marker))
assert len({tuple(sorted(v.items())) for v in godot_results.values()}) == 1
for name in ('lyra-montage-resources-debug.log', 'lyra-montage-resources-optimize.log'):
    text = (logs / name).read_text(encoding='utf-8-sig')
    assert '已成功生成' in text and '0 个警告' in text and '0 个错误' in text
build = (logs / 'lyra-montage-actions-exporter-build-resources.log').read_text(encoding='utf-8-sig')
assert 'BUILD SUCCESSFUL' in build and 'AutomationTool exiting with ExitCode=0' in build
report = dict(status='pass_resources', actions=55, localAdditive=local, meshAdditive=mesh, nonzeroBases=nonzero,
    sources=300, montages=45, tracks=tracks, logical=81, skin=68, skinPreservation=0,
    nativePoseSamples=len(native['rows']), curveRows=len(native['curveRows']), sourceAttributes=attributes,
    sourceFloatCurves=float_curves, animatedWeapons=sum(e['weaponTrackPresent'] for e in entries.values()),
    referenceWeapons=sum(not e['weaponTrackPresent'] for e in entries.values()), sequenceNotifies=notify_count, packages=len(catalog['assetSha256']),
    previousJson=len(catalog['previousFixtureSha256']), ueWarnings=warnings, godot=godot_results,
    scope='Resources and original native source sampling; Slot pose, Notify consumers, root motion and production Main remain open.',
    files={str(p.relative_to(directory)): dict(sha256=sha(p), bytes=p.stat().st_size) for p in sorted(directory.rglob('*.json'))})
(logs / 'lyra-montage-resources-verification.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print('LYRA_MONTAGE_RESOURCES_VERIFY_OK ' + json.dumps({k: v for k, v in report.items() if k != 'files'}))
