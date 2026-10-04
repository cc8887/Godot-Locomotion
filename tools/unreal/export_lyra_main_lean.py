"""Retarget missing Main Lean samples and export an immutable ALS81 extension."""
import hashlib
import json
import math
import os
import runpy
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
destination = root / 'main_lean'; destination.mkdir(exist_ok=True)
sha = lambda data: hashlib.sha256(data).hexdigest()
if not (destination / 'inventory.json').exists():
    runpy.run_path(str(Path(__file__).with_name('inspect_lyra_main_lean.py')))
inventory_bytes = (destination / 'inventory.json').read_bytes()
inventory = json.loads(inventory_bytes)
calibration_bytes = (root / 'logical_controls/calibration.json').read_bytes()
calibration = json.loads(calibration_bytes)
base_catalog_bytes = (root / 'logical_controls/catalog.json').read_bytes()
content = Path(unreal.Paths.project_content_dir())
package_sha = lambda path: sha((content / (path.split('.')[0].removeprefix('/Game/') + '.uasset')).read_bytes())
packages = json.loads((root / 'cycle_runtime_native.json').read_bytes())['assetSha256'].copy()
packages.update(inventory['assetSha256'])


def check_packages():
    if any(package_sha(path) != digest for path, digest in packages.items()):
        raise ValueError('Changed Main Lean source/target provenance')


def save(name, data):
    path = destination / name; path.parent.mkdir(exist_ok=True)
    if path.exists():
        if json.loads(path.read_bytes()) != data:
            raise ValueError('Existing Main Lean resource differs: ' + name)
    else:
        path.write_text(json.dumps(data, separators=(',', ':'), allow_nan=False), encoding='utf-8')
    return sha(path.read_bytes())


def install_new_additive_bases(sequences, existing_flags, sample_rows):
    for sequence, was_present, row in zip(sequences, existing_flags, sample_rows, strict=True):
        if not was_present:
            original = unreal.load_asset(row['source'])
            # PostEditChange clears RefPoseSeq while AdditiveAnimType is None.
            # Enable local additive against the target's own reference first,
            # then install its ALS animation base as the final property edit.
            sequence.set_editor_property('additive_anim_type', original.get_editor_property('additive_anim_type'))
            sequence.set_editor_property('ref_pose_type', original.get_editor_property('ref_pose_type'))
            sequence.set_editor_property('ref_frame_index', original.get_editor_property('ref_frame_index'))
            sequence.set_editor_property('ref_pose_seq', sequences[0])


check_packages()
basis = calibration['calibration']; hand = unreal.Quat(*basis['handBasis']['rotation'])
source_mesh = unreal.load_asset(basis['sourceMesh']); target_mesh = unreal.load_asset(basis['targetMesh'])
retargeter = unreal.load_asset(basis['retargeter'])
samples = inventory['samples']
expected_sources = ['MM_Rifle_Jog_Lean_Center', 'MM_Rifle_Jog_Leans_Left', 'MM_Rifle_Jog_Lean_Right']
if [row['source'].split('.')[-1] for row in samples] != expected_sources:
    raise ValueError('Changed original Main Lean samples')
target_directory = '/Game/GodotLyraRetarget/MainLean'
targets = [target_directory + '/LY_' + name for name in expected_sources]
existing = [unreal.EditorAssetLibrary.does_asset_exist(path) for path in targets]
missing = [row for row, present in zip(samples, existing, strict=True) if not present]
if missing:
    inputs = unreal.IKRetargetBatchOperationInputs()
    inputs.set_editor_property('assets_to_retarget', [unreal.EditorAssetLibrary.find_asset_data(row['source']) for row in missing])
    inputs.set_editor_property('source_mesh', source_mesh); inputs.set_editor_property('target_mesh', target_mesh)
    inputs.set_editor_property('ik_retarget_asset', retargeter); inputs.set_editor_property('target_path', target_directory)
    inputs.set_editor_property('prefix', 'LY_'); inputs.set_editor_property('include_referenced_assets', False)
    inputs.set_editor_property('overwrite_existing_files', False)
    # Retaining flags restores the original Manny base pointer AFTER remapping
    # references in UE's batch operation. Keep target generation nonadditive;
    # restore against its own reference/ALS base without a foreign skeleton.
    inputs.set_editor_property('retain_additive_flags', False)
    results = unreal.IKRetargetBatchOperation.run_batch_retarget(inputs)
    if {result.get_asset().get_path_name().split('.')[0] for result in results} != {path for path, present in zip(targets, existing, strict=True) if not present}:
        raise ValueError('Unexpected Main Lean retarget outputs')
target_sequences = [unreal.load_asset(path) for path in targets]
center = target_sequences[0]
install_new_additive_bases(target_sequences, existing, samples)
for sequence, was_present, row in zip(target_sequences, existing, samples, strict=True):
    if sequence.get_editor_property('skeleton') != target_mesh.get_editor_property('skeleton'):
        raise ValueError('Main Lean target does not use ALS Skeleton')
    if sequence.get_editor_property('ref_pose_seq') != center:
        if was_present:
            raise ValueError('Existing Main Lean target has a different additive base')
        sequence.set_editor_property('ref_pose_seq', center)
    metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(sequence))
    for key in ('additiveType', 'basePoseType', 'baseFrame', 'sequencePlayLength', 'floatCurveNames', 'animatedBoneAttributeCount', 'transformCurveCount'):
        if metadata[key] != row['metadata'][key]:
            raise ValueError('Retarget changed original Main Lean policy: ' + key)
    if not was_present and not unreal.EditorAssetLibrary.save_loaded_asset(sequence, only_if_is_dirty=False):
        raise ValueError('Could not save new Main Lean target')
    unreal.AlsSourceAnimationLibrary.finish_source_compression(sequence)
    packages[sequence.get_path_name()] = package_sha(sequence.get_path_name())
check_packages()
extended = unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(source_mesh.get_editor_property('skeleton'),
    target_mesh.get_editor_property('skeleton'), hand)
entries, native, extended_sequences = [], [], []
skin_error = 0
slots = ['main_lean_center', 'main_lean_left', 'main_lean_right']
calibration_sha = sha(calibration_bytes)
for index, (row, target, slot) in enumerate(zip(samples, target_sequences, slots, strict=True)):
    source = unreal.load_asset(row['source'])
    sequence = unreal.AlsLyraControlRigLibrary.create_weapon_sequence(source, target, extended, hand,
        None if index == 0 else extended_sequences[0])
    if sequence is None:
        raise ValueError('Could not extend Main Lean source controls')
    extended_sequences.append(sequence)
    keys = json.loads(unreal.AlsSourcePoseKeysLibrary.read_source_pose_keys(sequence))
    keys['source'] = target.get_path_name(); keys['skeletonSource'] = calibration['layout']['source']
    metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(sequence))
    metadata['baseAsset'] = center.get_path_name()
    metadata['retargetTransformsSourceName'] = target.get_path_name().split('.')[0]
    file = 'clips/' + slot + '.json'
    digest = save(file, {'schemaVersion': 1, 'calibrationSha256': calibration_sha, 'source': row['source'],
                         'target': target.get_path_name(), 'metadata': metadata, 'raw': keys})
    entries.append({'slot': slot, 'category': 'main_lean', 'source': row['source'], 'target': target.get_path_name(),
                    'file': file, 'sha256': digest, 'keyCount': keys['sampledKeyCount'], 'playLength': keys['playLength'],
                    'additive': True, 'sampleIndex': index, 'position': row['position'], 'rateScale': row['rateScale']})
    times = [0, keys['playLength'] * .125, keys['playLength'] * .37, keys['playLength'] * .5,
             keys['playLength'] * .75, keys['playLength'], keys['playLength'] * 1.5, -.01]
    for time in times:
        raw = json.loads(unreal.AlsSourceAnimationLibrary.read_raw_bone_pose(sequence, time, False, False, True))['pose']
        output = json.loads(unreal.AlsSourceAnimationLibrary.read_raw_animation_pose(sequence, time, True, False, False))['pose']
        old_output = json.loads(unreal.AlsSourceAnimationLibrary.read_raw_animation_pose(target, time, True, False, False))['pose']
        if len(raw) != 81 or len(output) != 81 or len(old_output) != 79:
            raise ValueError('Incomplete Main Lean source pose')
        for a, b in zip(output[:68], old_output[:68], strict=True):
            for channel in ('position', 'rotation', 'scale'):
                skin_error = max(skin_error, math.dist(a[channel], b[channel]))
        native.append({'slot': slot, 'seconds': time, 'raw': raw, 'output': output})
if skin_error != 0:
    raise ValueError('Control extension changed Main Lean skin sample: ' + str(skin_error))
curves = [{'slot': e['slot'], 'source': e['source'], 'target': e['target'], 'playLength': e['playLength'],
           'additive': True, 'baseSlot': slots[0], 'curves': [], 'attributes': [], 'transformCurves': 0} for e in entries]
catalog_sha = save('catalog.json', {'schemaVersion': 1, 'baseCatalogSha256': sha(base_catalog_bytes),
    'calibrationSha256': calibration_sha, 'inventorySha256': sha(inventory_bytes), 'entries': entries, 'curves': curves,
    'assetSha256': packages, 'skinPreservation': skin_error})
blend_rows = []
space = unreal.load_asset(inventory['source'])
filters = [{'time': f.get_editor_property('interpolation_time'),
            'type': str(f.get_editor_property('interpolation_type')),
            'dampingRatio': f.get_editor_property('damping_ratio'),
            'maxSpeed': f.get_editor_property('max_speed')}
           for f in space.get_editor_property('interpolation_param')]
save('behavior.json', {'schemaVersion': 1, 'catalogSha256': catalog_sha,
    'inventorySha256': sha(inventory_bytes), 'axisFilters': filters,
    'weightEaseInOut': space.get_editor_property('target_weight_interpolation_ease_in_out'),
    'allowMeshSpaceBlending': space.get_editor_property('allow_mesh_space_blending'),
    'allowMarkerBasedSync': space.get_editor_property('allow_marker_based_sync'),
    'matchSyncPhases': space.get_editor_property('should_match_sync_phases'),
    'legacySampleLength': space.get_editor_property('use_legacy_sample_point_animation_length_calculations'),
    'notifyMode': str(space.get_editor_property('notify_trigger_mode')),
    'perBoneBlendMode': str(space.get_editor_property('per_bone_blend_mode')),
    'manualPerBoneOverrideCount': len(space.get_editor_property('manual_per_bone_overrides'))})
for angle in [-30, -20, -19.999, -13.75, -1.25, 0, .0123456789, 8.9, 19.999, 20, 30]:
    for normalized in [0, .37, 1]:
        output = json.loads(unreal.AlsSourceAnimationLibrary.read_retargeted_blend_space_pose2d(
            space, extended, extended_sequences, angle, 0, normalized))
        blend_rows.append({'angle': angle, 'normalized': normalized, 'output': output})
save('native.json', {'schemaVersion': 1, 'catalogSha256': catalog_sha, 'rows': native,
                     'blendRows': blend_rows, 'scope': 'Raw ALS81/Main local additive samples and static original BlendSpace pose; runtime weight smoothing/clocks separate.'})
check_packages()
unreal.log('LYRA_MAIN_LEAN_RESOURCES_OK samples=3 logical=81 skin=68 native=24 blend=33 new_targets=' + str(len(missing)))
