"""Verify first generation with the corrected additive policy, preserving production assets."""
import json
import math
import os
import re
import runpy
from pathlib import Path
import unreal

resources = runpy.run_path(str(Path(__file__).with_name('export_lyra_main_lean.py')))
test_name = os.environ.get('LYRA_MAIN_LEAN_COLD_NAME', 'MainLeanCold_20261001A')
if re.fullmatch(r'MainLeanCold_[A-Za-z0-9_]{1,48}', test_name) is None:
    raise ValueError('Invalid owned cold verification directory name')
directory = '/Game/GodotLyraRetarget/Verification/' + test_name
proof_file = 'main-lean-cold-proof.json' if test_name == 'MainLeanCold_20261001A' else 'main-lean-cold-' + test_name.lower() + '-proof.json'
output = resources['root'].parents[2] / 'artifacts/lyra-analysis' / proof_file
if output.exists():
    raise ValueError('Existing cold proof is preserved; choose a fresh verification name')
rows = resources['samples']
targets = [directory + '/LYCold_' + row['source'].split('.')[-1] for row in rows]
if any(unreal.EditorAssetLibrary.does_asset_exist(path) for path in targets):
    raise ValueError('Cold test requires a fresh named verification directory; existing assets are preserved')
inputs = unreal.IKRetargetBatchOperationInputs()
inputs.set_editor_property('assets_to_retarget', [unreal.EditorAssetLibrary.find_asset_data(row['source']) for row in rows])
inputs.set_editor_property('source_mesh', resources['source_mesh']); inputs.set_editor_property('target_mesh', resources['target_mesh'])
inputs.set_editor_property('ik_retarget_asset', resources['retargeter']); inputs.set_editor_property('target_path', directory)
inputs.set_editor_property('prefix', 'LYCold_'); inputs.set_editor_property('include_referenced_assets', False)
inputs.set_editor_property('overwrite_existing_files', False); inputs.set_editor_property('retain_additive_flags', False)
result = unreal.IKRetargetBatchOperation.run_batch_retarget(inputs)
if {asset.get_asset().get_path_name().split('.')[0] for asset in result} != set(targets):
    raise ValueError('Unexpected first-generation validation outputs')
sequences = [unreal.load_asset(path) for path in targets]
if any(sequence.get_editor_property('ref_pose_seq') is not None for sequence in sequences):
    raise ValueError('Batch restored a Manny additive base despite disabled retention')
resources['install_new_additive_bases'](sequences, [False] * 3, rows)
max_position, max_quaternion, max_scale = 0, 0, 0
for sequence, production in zip(sequences, resources['target_sequences'], strict=True):
    if sequence.get_editor_property('ref_pose_seq') != sequences[0] or sequence.get_editor_property('skeleton') != resources['target_mesh'].get_editor_property('skeleton'):
        raise ValueError('First generation uses a foreign skeleton/additive base')
    if not unreal.EditorAssetLibrary.save_loaded_asset(sequence, only_if_is_dirty=False):
        raise ValueError('Failed to save owned first-generation verification asset')
    unreal.AlsSourceAnimationLibrary.finish_source_compression(sequence)
    length = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(sequence))['sequencePlayLength']
    for time in [0, length * .125, length * .37, length * .5, length * .75, length, length * 1.5, -.01]:
        actual = json.loads(unreal.AlsSourceAnimationLibrary.read_raw_animation_pose(sequence, time, True, False, False))['pose']
        expected = json.loads(unreal.AlsSourceAnimationLibrary.read_raw_animation_pose(production, time, True, False, False))['pose']
        if len(actual) != 79 or len(expected) != 79:
            raise ValueError('Incomplete first-generation ALS pose')
        for a, b in zip(actual, expected, strict=True):
            max_position = max(max_position, math.dist(a['position'], b['position']))
            max_quaternion = max(max_quaternion, min(math.dist(a['rotation'], b['rotation']), math.dist(a['rotation'], [-v for v in b['rotation']])))
            max_scale = max(max_scale, math.dist(a['scale'], b['scale']))
if max_position != 0 or max_quaternion != 0 or max_scale != 0:
    raise ValueError('Corrected first generation changed validated target geometry')
resources['check_packages']()
proof = {'status': 'pass', 'newTargets': 3, 'samples': 24, 'logical': 79, 'skin': 68,
         'positionCm': max_position, 'quaternion': max_quaternion, 'scale': max_scale,
         'retainAdditiveFlags': False, 'base': sequences[0].get_path_name(),
         'targets': {sequence.get_path_name(): resources['package_sha'](sequence.get_path_name()) for sequence in sequences},
         'originalPackages': 492, 'originalPackagesUnchanged': True}
output.write_text(json.dumps(proof, indent=2), encoding='utf-8')
unreal.log('LYRA_MAIN_LEAN_COLD_OK new_targets=3 samples=24 position=0 quaternion=0 scale=0')
