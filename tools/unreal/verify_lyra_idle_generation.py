"""Verify fresh retargeting against canonical data without saving extra packages."""
import hashlib
import json
import math
import os
from pathlib import Path
import unreal

root=Path(os.environ['LYRA_OUTPUT_ROOT'])
catalog=json.loads((root/'locomotion_extras/catalog.json').read_bytes())
calibration=json.loads((root/'logical_controls/calibration.json').read_bytes())['calibration']
world=unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
unreal.SystemLibrary.execute_console_command(world,'Editor.AsyncAssetCompilation 2')
if unreal.SystemLibrary.get_console_variable_int_value('Editor.AsyncAssetCompilation')!=2:
    raise ValueError('Compilation pause was not applied')
rows=[row for row in catalog['entries'] if not row['additive']]
directory='/Game/GodotLyraRetarget/IdleBreaksColdValidation'
expected={directory+'/LY_'+row['source'].split('.')[-1] for row in rows}
if any(unreal.EditorAssetLibrary.does_asset_exist(path) for path in expected):
    raise ValueError('Validation targets must be new and unsaved')
for row in rows:
    unreal.AlsSourceAnimationLibrary.finish_source_compression(unreal.load_asset(row['source']))
inputs=unreal.IKRetargetBatchOperationInputs()
for key,value in (('source_mesh',unreal.load_asset(calibration['sourceMesh'])),
    ('target_mesh',unreal.load_asset(calibration['targetMesh'])),('ik_retarget_asset',unreal.load_asset(calibration['retargeter'])),
    ('assets_to_retarget',[unreal.EditorAssetLibrary.find_asset_data(row['source']) for row in rows]),
    ('target_path',directory),('prefix','LY_'),('include_referenced_assets',False),
    ('overwrite_existing_files',False),('retain_additive_flags',False)):
    inputs.set_editor_property(key,value)
results=unreal.IKRetargetBatchOperation.run_batch_retarget(inputs)
if {result.get_asset().get_path_name().split('.')[0] for result in results}!=expected:
    raise ValueError('Unexpected fresh target set')
keys_compared=0
for row in rows:
    fresh=unreal.load_asset(directory+'/LY_'+row['source'].split('.')[-1])
    actual=json.loads(unreal.AlsSourcePoseKeysLibrary.read_source_pose_keys(fresh))
    canonical=json.loads(unreal.AlsSourcePoseKeysLibrary.read_source_pose_keys(unreal.load_asset(row['target'])))
    for payload in (actual,canonical):
        payload.pop('source',None);payload.pop('skeletonSource',None)
    if actual!=canonical:raise ValueError('Fresh retarget data differs '+row['slot'])
    for track in actual['tracks']:
        for field in ('positions','rotations','scales'):
            if any(not math.isfinite(v) for key in track[field] for v in key):raise ValueError('Nonfinite fresh target data')
    keys_compared+=actual['sampledKeyCount']
    unreal.AlsSourceAnimationLibrary.finish_source_compression(fresh)
    if (Path(unreal.Paths.project_content_dir())/(directory[6:]+'/LY_'+row['source'].split('.')[-1]+'.uasset')).exists():
        raise ValueError('Validation unexpectedly saved a package')
for asset,digest in catalog['assetSha256'].items():
    stem=asset.split('.')[0]
    base=unreal.Paths.project_content_dir() if stem.startswith('/Game/') else unreal.Paths.engine_content_dir()
    if hashlib.sha256((Path(base)/(stem.split('/',2)[2]+'.uasset')).read_bytes()).hexdigest()!=digest:
        raise ValueError('Fresh generation changed a protected asset')
unreal.log('LYRA_IDLE_GENERATION_NATIVE_OK freshTargets=5 sampledKeys='+str(keys_compared)+
    ' canonicalRawExact=true compilationMode=2 assets_saved=0')
