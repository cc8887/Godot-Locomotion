"""Derived ALS actions and immutable ALS81 data for every Main Montage track."""
import hashlib
import json
import math
import os
from pathlib import Path
import unreal

assert unreal.AlsLyraControlRigLibrary.prefer_raw_track_data_model(True)
root = Path(os.environ['LYRA_OUTPUT_ROOT'])
destination = root / 'montage_actions'
destination.mkdir(exist_ok=True)
(destination / 'clips').mkdir(exist_ok=True)
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
content = Path(unreal.Paths.project_content_dir())
package_file = lambda p: content / (p.split('.')[0].removeprefix('/Game/') + '.uasset')
dependencies = {p: sha(root / p) for p in ('montage_catalog_v2.json', 'logical_controls/calibration.json', 'logical_controls/catalog.json')}
montages = json.loads((root / 'montage_catalog_v2.json').read_bytes())
calibration = json.loads((root / 'logical_controls/calibration.json').read_bytes())
prior = json.loads((root / 'logical_controls/catalog.json').read_bytes())
packages = dict(montages['assetSha256'])
previous = {str(p.relative_to(root)).replace('\\', '/'): sha(p) for p in root.rglob('*.json') if destination not in p.parents}
inventory_path = destination / 'inventory.json'
if inventory_path.exists():
    previous = json.loads(inventory_path.read_bytes())['previousFixtureSha256']

def protect():
    for path, digest in packages.items():
        assert sha(package_file(path)) == digest, 'Changed protected package: ' + path
    for path, digest in previous.items():
        assert sha(root / path) == digest, 'Changed previous JSON: ' + path

def save(name, value):
    path = destination / name
    if path.exists():
        assert json.loads(path.read_bytes()) == value, 'Immutable Montage action resource differs: ' + name
    else:
        path.write_text(json.dumps(value, separators=(',', ':'), allow_nan=False), encoding='utf-8')
    return sha(path)

protect()
sources = list(montages['sequences'])
assert len(sources) == len({p.split('.')[-1] for p in sources}) == 55
directory = '/Game/GodotLyraRetarget/MontageActions'
rows = [dict(slot='montage_' + p.split('.')[-1].lower(), source=p,
             target=directory + '/LY_' + p.split('.')[-1] + '.LY_' + p.split('.')[-1], category='montage_actions',
             additive=montages['sequences'][p]['additiveType'] != 'AAT_None') for p in sources]
external = {r['source']: r for r in prior['entries']}
outside_bases = {v['baseAsset'] for v in montages['sequences'].values() if v['baseAsset'] and v['baseAsset'] not in sources}
assert outside_bases == {'/Game/Characters/Heroes/Mannequin/Animations/Locomotion/Rifle/MM_Rifle_Idle_ADS.MM_Rifle_Idle_ADS'}
assert all(p in external for p in outside_bases)
save('inventory.json', dict(schemaVersion=1, dependencies=dependencies, rows=rows,
                           externalBases={p: external[p] for p in sorted(outside_bases)},
                           assetSha256=packages, previousFixtureSha256=previous))
world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
unreal.SystemLibrary.execute_console_command(world, 'Editor.AsyncAssetCompilation 2')
assert unreal.SystemLibrary.get_console_variable_int_value('Editor.AsyncAssetCompilation') == 2
unreal.log('LYRA_MONTAGE_ACTIONS_COMPILE_MODE 2')
basis = calibration['calibration']
hand = unreal.Quat(*basis['handBasis']['rotation'])
source_mesh = unreal.load_asset(basis['sourceMesh'])
target_mesh = unreal.load_asset(basis['targetMesh'])
retargeter = unreal.load_asset(basis['retargeter'])
originals = {p: unreal.load_asset(p) for p in sources}
assert all(a.get_editor_property('skeleton') == source_mesh.get_editor_property('skeleton') for a in originals.values())
missing = [r for r in rows if not unreal.EditorAssetLibrary.does_asset_exist(r['target'])]
if missing:
    inputs = unreal.IKRetargetBatchOperationInputs()
    for key, value in (('assets_to_retarget', [unreal.EditorAssetLibrary.find_asset_data(r['source']) for r in missing]),
                       ('source_mesh', source_mesh), ('target_mesh', target_mesh), ('ik_retarget_asset', retargeter),
                       ('target_path', directory), ('prefix', 'LY_'), ('include_referenced_assets', False),
                       ('overwrite_existing_files', False), ('retain_additive_flags', False)):
        inputs.set_editor_property(key, value)
    results = unreal.IKRetargetBatchOperation.run_batch_retarget(inputs)
    assert {r.get_asset().get_path_name() for r in results} == {r['target'] for r in missing}, 'Unexpected derived action assets'
targets = {r['source']: unreal.load_asset(r['target']) for r in rows}
for p in outside_bases:
    targets[p] = unreal.load_asset(external[p]['target'])
    packages.setdefault(external[p]['target'], sha(package_file(external[p]['target'])))
new_paths = {r['source'] for r in missing}
pending_file = destination / 'pending_targets.json'
pending = json.loads(pending_file.read_bytes())['targets'] if pending_file.exists() and not (destination / 'catalog.json').exists() else {}
for row in rows:
    target = targets[row['source']]
    assert isinstance(target, unreal.AnimSequence) and target.get_editor_property('skeleton') == target_mesh.get_editor_property('skeleton')
    original = originals[row['source']]
    needs_restore = any(target.get_editor_property(key) != original.get_editor_property(key)
        for key in ('additive_anim_type', 'ref_pose_type', 'ref_frame_index'))
    base = montages['sequences'][row['source']]['baseAsset']
    expected_sequence = targets[base] if row['additive'] and base else None
    needs_restore = needs_restore or target.get_editor_property('ref_pose_seq') != expected_sequence
    if row['source'] in new_paths or needs_restore:
        if row['source'] not in new_paths:
            assert row['target'] in pending and sha(package_file(row['target'])) == pending[row['target']], 'Changed unfinished derived asset'
        # Restore only newly generated assets after the retarget batch. Keeping
        # flags during the batch would restore a foreign Manny base pointer.
        for key in ('additive_anim_type', 'ref_pose_type', 'ref_frame_index'):
            target.set_editor_property(key, original.get_editor_property(key))
        target.set_editor_property('ref_pose_seq', expected_sequence)
    metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(target))
    original_metadata = montages['sequences'][row['source']]
    for key in ('additiveType', 'basePoseType', 'baseFrame', 'sequencePlayLength', 'enableRootMotion',
                'forceRootLock', 'rootMotionRootLock', 'floatCurveNames', 'animatedBoneAttributeCount', 'transformCurveCount'):
        actual, expected = metadata[key], original_metadata[key]
        if key == 'floatCurveNames':
            actual, expected = [v.casefold() for v in actual], [v.casefold() for v in expected]
        assert actual == expected, (row['slot'], key, actual, expected)
    expected_base = original_metadata['baseAsset']
    assert metadata['baseAsset'] == (targets[expected_base].get_path_name() if row['additive'] and expected_base else None)
    unreal.AlsSourceAnimationLibrary.finish_source_compression(target)
    if row['source'] in new_paths or needs_restore:
        assert unreal.EditorAssetLibrary.save_loaded_asset(target, only_if_is_dirty=False), row['target']
    packages[row['target']] = sha(package_file(row['target']))
    unreal.log('LYRA_MONTAGE_ACTION_TARGET_OK ' + row['slot'])
protect()
extended = unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(source_mesh.get_editor_property('skeleton'),
    target_mesh.get_editor_property('skeleton'), hand)
layout = json.loads(unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(extended))
for key in ('rawBoneNames', 'logicalBoneNames', 'logicalParents', 'logicalToPhysical', 'referencePose', 'virtualBones'):
    assert layout[key] == calibration['layout'][key], key
sequences = {}
for p in outside_bases:
    sequences[p] = unreal.AlsLyraControlRigLibrary.create_weapon_sequence(unreal.load_asset(p), targets[p], extended, hand, None)
    assert sequences[p] is not None, p
    external_keys = json.loads(unreal.AlsSourcePoseKeysLibrary.read_source_pose_keys(sequences[p]))
    cached_keys = json.loads((root / 'logical_controls' / external[p]['file']).read_bytes())['raw']
    for key in ('frameRateNumerator', 'frameRateDenominator', 'sampledKeyCount', 'playLength', 'tracks'):
        assert external_keys[key] == cached_keys[key], 'External base raw data changed: ' + key
all_by_source = {r['source']: r for r in rows} | {p: external[p] for p in outside_bases}
entries, curves, native, curve_native, playback = [], [], [], [], []
skin_error = 0
for row in rows:
    source = originals[row['source']]
    target = targets[row['source']]
    original_metadata = montages['sequences'][row['source']]
    base_path = original_metadata['baseAsset']
    assert not base_path or base_path == row['source'] or base_path in sequences, ('Additive base must be evaluated first', base_path)
    sequence = unreal.AlsLyraControlRigLibrary.create_weapon_sequence(source, target, extended, hand,
        sequences.get(base_path) if base_path != row['source'] else None)
    assert sequence is not None, 'Cannot extend action weapon channel: ' + row['source']
    sequences[row['source']] = sequence
    keys = json.loads(unreal.AlsSourcePoseKeysLibrary.read_source_pose_keys(sequence))
    keys['source'] = row['target']; keys['skeletonSource'] = calibration['layout']['source']
    metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(sequence))
    metadata['baseAsset'] = targets[base_path].get_path_name() if row['additive'] and base_path else None
    metadata['retargetTransformsSourceName'] = row['target'].split('.')[0]
    weapon_present = any(t['bone'].casefold() == 'weapon_r' for t in keys['tracks'])
    original_keys = json.loads(unreal.AlsSourcePoseKeysLibrary.read_source_pose_keys(source))
    assert weapon_present == any(t['bone'].casefold() == 'weapon_r' for t in original_keys['tracks'])
    base_source = row['source'] if row['additive'] and original_metadata['basePoseType'] == 'ABPT_LocalAnimFrame' else base_path
    base_slot = all_by_source[base_source]['slot'] if row['additive'] else None
    base_keys = keys if base_source == row['source'] else json.loads(unreal.AlsSourcePoseKeysLibrary.read_source_pose_keys(sequences[base_source])) if row['additive'] else None
    base_length = targets[base_source].get_play_length() if row['additive'] else 0
    base_time = base_length * max(0, min(1, metadata['baseFrame'] / base_keys['sampledKeyCount'])) if row['additive'] else 0
    length = keys['playLength']
    file = 'clips/' + row['slot'] + '.json'
    digest = save(file, dict(schemaVersion=1, calibrationSha256=dependencies['logical_controls/calibration.json'],
                            source=row['source'], target=row['target'], metadata=metadata, raw=keys, weaponTrackPresent=weapon_present))
    entries.append(row | dict(file=file, sha256=digest, keyCount=keys['sampledKeyCount'], playLength=length,
                             sequencePlayLength=metadata['sequencePlayLength'], baseSlot=base_slot, baseSampleTime=base_time,
                             weaponTrackPresent=weapon_present))
    times = sorted({-.01, 0, length * .123456789, length * .37, length * .5, length * .75, length, length * 1.5,
                    *([base_time] if row['additive'] else []),
                    *[s[k] for a in montages['assets'] for t in a['slots'] for s in t['segments']
                      if s['animation'] == row['source'] for k in ('clipStart', 'clipEnd')]})
    for time in times:
        raw = json.loads(unreal.AlsSourceAnimationLibrary.read_raw_bone_pose(sequence, time, False, False, True))['pose']
        output = json.loads(unreal.AlsSourceAnimationLibrary.read_raw_animation_pose(sequence, time, True, False, False))['pose']
        old_output = json.loads(unreal.AlsSourceAnimationLibrary.read_raw_animation_pose(target, time, True, False, False))['pose']
        assert len(raw) == len(output) == 81 and len(old_output) == 79
        for a, b in zip(output[:68], old_output[:68], strict=True):
            for channel in ('position', 'rotation', 'scale'):
                skin_error = max(skin_error, math.dist(a[channel], b[channel]))
        native.append(dict(slot=row['slot'], seconds=time, raw=raw, output=output))
    curve_times = set(times)
    for hz in (30, 60, 120):
        curve_times.update(i / hz for i in range(math.floor(length * hz) + 1))
    trace = json.loads(unreal.AlsLyraGraphLibrary.read_source_curve_trace(sequence, json.dumps(sorted(curve_times))))
    for curve in trace['metadata']['curves']:
        flags = {sample['raw'][curve['name']]['flags'] for sample in trace['rows']}
        assert len(flags) == 1
        curve['elementFlags'] = flags.pop()
    assert len(trace['metadata']['attributes']) == metadata['animatedBoneAttributeCount'] and metadata['transformCurveCount'] == 0
    curves.append(dict(slot=row['slot'], source=row['source'], target=row['target'], playLength=length, additive=row['additive'],
                       baseSlot=base_slot, baseSampleTime=base_time, curves=trace['metadata']['curves'], attributes=trace['metadata']['attributes'], transformCurves=0))
    curve_native.extend(dict(slot=row['slot'], **sample) for sample in trace['rows'])
    playback.append(dict(slot=row['slot'], sourceMetadata=original_metadata,
        targetMetadata=json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(target)),
        sourceSync=json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(source)),
        targetSync=json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(target)),
        sourceNotifies=json.loads(unreal.AlsSourceAnimationLibrary.read_source_notify_metadata(source)),
        targetNotifies=json.loads(unreal.AlsSourceAnimationLibrary.read_source_notify_metadata(target))))
    unreal.log(f'LYRA_MONTAGE_ACTION_CLIP_OK slot={row["slot"]} samples={len(times)} curveRows={len(trace["rows"])}')
assert len(entries) == 55 and sum(e['additive'] for e in entries) == 27 and skin_error == 0, skin_error
protect()
catalog_sha = save('catalog.json', dict(schemaVersion=1, dependencies=dependencies, entries=entries, curves=curves,
    assetSha256=packages, previousFixtureSha256=previous, skinPreservation=skin_error,
    probeSourceSha256={p: sha(Path(__file__).parent / 'AlsV4AssetExporter/Source/AlsV4AssetExporter' / p)
        for p in ('Public/AlsLyraControlRigLibrary.h', 'Private/AlsLyraControlRigLibrary.cpp', 'Private/AlsV4AssetExporterModule.cpp')},
    exportScriptSha256=sha(Path(__file__))))
save('native.json', dict(schemaVersion=1, catalogSha256=catalog_sha, rows=native, curveRows=curve_native))
save('playback.json', dict(schemaVersion=1, catalogSha256=catalog_sha, entries=playback))
save('bindings.json', dict(schemaVersion=1, montageCatalogSha256=dependencies['montage_catalog_v2.json'], catalogSha256=catalog_sha,
    sequences={e['source']: e['slot'] for e in entries},
    assets=[dict(path=a['path'], tracks=[dict(slot=t['name'], sequence=next(e['slot'] for e in entries if e['source'] == t['segments'][0]['animation'])) for t in a['slots']]) for a in montages['assets']]))
unreal.log(f'LYRA_MONTAGE_ACTIONS_RESOURCES_OK actions=55 additive=27 logical=81 skin=68 samples={len(native)} curveRows={len(curve_native)} new_targets={len(missing)} skinError=0')
assert unreal.AlsLyraControlRigLibrary.prefer_raw_track_data_model(False)
