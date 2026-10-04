"""Complete the current ALS81 locomotion resource bindings, preserving old bytes."""
import hashlib
import json
import math
import os
from pathlib import Path
import unreal

root=Path(os.environ['LYRA_OUTPUT_ROOT'])
destination=root/'locomotion_extras';destination.mkdir(exist_ok=True)
(destination/'clips').mkdir(exist_ok=True)
sha=lambda data:hashlib.sha256(data).hexdigest()
files={name:(root/name).read_bytes() for name in ('locomotion_layer_closures.json',
    'logical_controls/catalog.json','logical_controls/calibration.json',
    'unarmed_jump_additive_catalog.json','pistol_jump_additive_catalog.json','rifle_jump_additive_catalog.json')}
dependencies={name:sha(data) for name,data in files.items()}
inventory=json.loads(files['locomotion_layer_closures.json'])
calibration=json.loads(files['logical_controls/calibration.json'])
packages=dict(inventory['assetSha256'])
content=Path(unreal.Paths.project_content_dir())
engine_content=Path(unreal.Paths.engine_content_dir())
def package_sha(path):
    stem=path.split('.')[0]
    directory=content if stem.startswith('/Game/') else engine_content if stem.startswith('/Engine/') else None
    if directory is None:raise ValueError('Unsupported mount '+path)
    return sha((directory/(stem.split('/',2)[2]+'.uasset')).read_bytes())
def check_packages():
    for path,digest in packages.items():
        if package_sha(path)!=digest:raise ValueError('Changed protected package '+path)
def save(name,value):
    path=destination/name
    if path.exists():
        if json.loads(path.read_bytes())!=value:raise ValueError('Immutable extra resource differs '+name)
    else:path.write_text(json.dumps(value,separators=(',',':'),allow_nan=False),encoding='utf-8')
    return sha(path.read_bytes())
check_packages()
world=unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
unreal.SystemLibrary.execute_console_command(world,'Editor.AsyncAssetCompilation 2')
compilation_mode=unreal.SystemLibrary.get_console_variable_int_value('Editor.AsyncAssetCompilation')
if compilation_mode!=2:raise ValueError('Could not pause intermediate asset compilation')
unreal.log('LYRA_IDLE_RECOVERY_COMPILE_MODE '+str(compilation_mode))
basis=calibration['calibration'];hand=unreal.Quat(*basis['handBasis']['rotation'])
source_mesh=unreal.load_asset(basis['sourceMesh']);target_mesh=unreal.load_asset(basis['targetMesh'])
retargeter=unreal.load_asset(basis['retargeter'])
rows=[]
for profile in ('unarmed','pistol','rifle'):
    provider=inventory['providers'][profile]
    for source in provider['sequenceTargets']:
        if source not in inventory['missingSequences']:continue
        if 'IdleBreak_' in source:
            name=source.split('.')[-1]
            target='/Game/GodotLyraRetarget/IdleBreaks/LY_'+name
            rows.append({'slot':profile+'_idle_break_'+name.split('IdleBreak_')[1].lower(),
                'profile':profile,'source':source,'target':target+'.'+target.split('/')[-1],'additive':False})
        elif 'Jump_RecoveryAdditive' in source:
            prior=json.loads(files[profile+'_jump_additive_catalog.json'])['clips'][0]
            if prior['source']!=source:raise ValueError('Jump recovery binding changed')
            for field,path in (('sourceUassetSha256',source),('targetUassetSha256',prior['target'])):
                if package_sha(path)!=prior[field]:raise ValueError('Changed prior jump recovery '+path)
                packages[path]=prior[field]
            rows.append({'slot':profile+'_jump_recovery_additive','profile':profile,'source':source,
                'target':prior['target'],'additive':True,'priorMetadata':prior['metadata']})
        else:raise ValueError('Unexpected locomotion resource gap '+source)
if len(rows)!=8 or len({r['source'] for r in rows})!=8:raise ValueError('Incomplete resource gap set')
missing=[row for row in rows if not row['additive'] and not unreal.EditorAssetLibrary.does_asset_exist(row['target'])]
if missing:
    inputs=unreal.IKRetargetBatchOperationInputs()
    inputs.set_editor_property('assets_to_retarget',[unreal.EditorAssetLibrary.find_asset_data(row['source']) for row in missing])
    for key,value in (('source_mesh',source_mesh),('target_mesh',target_mesh),('ik_retarget_asset',retargeter),
        ('target_path','/Game/GodotLyraRetarget/IdleBreaks'),('prefix','LY_'),('include_referenced_assets',False),
        ('overwrite_existing_files',False),('retain_additive_flags',False)):
        inputs.set_editor_property(key,value)
    actual={asset.get_asset().get_path_name() for asset in unreal.IKRetargetBatchOperation.run_batch_retarget(inputs)}
    if actual!={row['target'] for row in missing}:raise ValueError('Unexpected Idle Break retarget outputs')
for row in rows:
    target=unreal.load_asset(row['target'])
    if target.get_editor_property('skeleton')!=target_mesh.get_editor_property('skeleton'):
        raise ValueError('Non-ALS extra resource')
    unreal.AlsSourceAnimationLibrary.finish_source_compression(target)
    if row in missing and not unreal.EditorAssetLibrary.save_loaded_asset(target,only_if_is_dirty=False):
        raise ValueError('Failed to save new derived Idle Break')
    packages[row['target']]=package_sha(row['target'])
extended=unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(source_mesh.get_editor_property('skeleton'),
    target_mesh.get_editor_property('skeleton'),hand)
layout=json.loads(unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(extended))
for key in ('rawBoneNames','logicalBoneNames','logicalParents','logicalToPhysical','referencePose','virtualBones'):
    if layout[key]!=calibration['layout'][key]:raise ValueError('Changed logical skeleton '+key)
entries=[];native=[];curves=[];curve_native=[];playback=[];skin_error=0
for row in rows:
    source=unreal.load_asset(row['source']);target=unreal.load_asset(row['target'])
    original=json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(source))
    target_metadata=json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(target))
    for key in ('additiveType','basePoseType','baseFrame','sequencePlayLength','enableRootMotion','forceRootLock','rootMotionRootLock'):
        if original[key]!=target_metadata[key]:raise ValueError('Retarget policy differs '+row['slot']+'/'+key)
    if row['additive']:
        # The older helper obtained this derived first-root value through its
        # then-current extraction provider. Package bytes remain the authority;
        # the current RAW first-root value is independently sampled below.
        differences=[key for key,value in row['priorMetadata'].items() if key!='rootLockFirstFrame' and target_metadata.get(key)!=value]
        if differences:raise ValueError('Existing Jump Recovery metadata changed: '+str(differences))
        local=target_metadata['basePoseType']=='ABPT_LocalAnimFrame'
        if target_metadata['additiveType']!='AAT_LocalSpaceBase' or (not local and target_metadata['baseAsset']!=row['target']):
            raise ValueError('Unsupported Jump Recovery base')
    elif target_metadata['additiveType']!='AAT_None':raise ValueError('Idle Break unexpectedly additive')
    sequence=unreal.AlsLyraControlRigLibrary.create_weapon_sequence(source,target,extended,hand,None)
    if sequence is None:raise ValueError('Cannot create ALS81 extra '+row['slot'])
    keys=json.loads(unreal.AlsSourcePoseKeysLibrary.read_source_pose_keys(sequence))
    keys['source']=row['target'];keys['skeletonSource']=calibration['layout']['source']
    metadata=json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(sequence))
    metadata['baseAsset']=target_metadata['baseAsset']
    metadata['retargetTransformsSourceName']=row['target'].split('.')[0]
    length=keys['playLength'];count=keys['sampledKeyCount']
    base_time=metadata['sequencePlayLength']*max(0,min(1,metadata['baseFrame']/count)) if row['additive'] else 0
    base_slot=row['slot'] if row['additive'] else None
    file='clips/'+row['slot']+'.json'
    digest=save(file,{'schemaVersion':1,'calibrationSha256':dependencies['logical_controls/calibration.json'],
        'source':row['source'],'target':row['target'],'metadata':metadata,'raw':keys})
    entries.append({key:row[key] for key in ('slot','profile','source','target','additive')} |
        {'category':'locomotion_extras','file':file,'sha256':digest,'keyCount':count,'playLength':length,
         'baseSlot':base_slot,'baseSampleTime':base_time,'sequencePlayLength':metadata['sequencePlayLength']})
    times=sorted({-0.01,0.0,length,length*1.5,base_time,*[length*i/28 for i in range(29)],
        *[length*(i+.37)/28 for i in range(28)]})
    for time in times:
        raw=json.loads(unreal.AlsSourceAnimationLibrary.read_raw_bone_pose(sequence,time,False,False,True))['pose']
        output=json.loads(unreal.AlsSourceAnimationLibrary.read_raw_animation_pose(sequence,time,True,False,False))
        old_output=json.loads(unreal.AlsSourceAnimationLibrary.read_raw_animation_pose(target,time,True,False,False))['pose']
        if len(raw)!=81 or len(output['pose'])!=81 or len(old_output)!=79:raise ValueError('Incomplete extra pose')
        for a,b in zip(output['pose'][:68],old_output[:68],strict=True):
            for channel in ('position','rotation','scale'):skin_error=max(skin_error,math.dist(a[channel],b[channel]))
        native.append({'slot':row['slot'],'seconds':time,'raw':raw,'output':output['pose']})
    trace=json.loads(unreal.AlsLyraGraphLibrary.read_source_curve_trace(sequence,json.dumps(times)))
    for curve in trace['metadata']['curves']:
        flags={sample['raw'][curve['name']]['flags'] for sample in trace['rows']}
        if len(flags)!=1:raise ValueError('Changing curve flags')
        curve['elementFlags']=flags.pop()
    curves.append({'slot':row['slot'],'source':row['source'],'target':row['target'],'playLength':length,
        'additive':row['additive'],'baseSlot':base_slot,'baseSampleTime':base_time,
        'curves':trace['metadata']['curves'],'attributes':trace['metadata']['attributes'],
        'transformCurves':metadata['transformCurveCount']})
    curve_native.extend({'slot':row['slot'],**sample} for sample in trace['rows'])
    playback.append({'slot':row['slot'], 'sourceMetadata':original,'targetMetadata':target_metadata,
        'legacyRootLockFirstFrame':row.get('priorMetadata',{}).get('rootLockFirstFrame'),
        'sourceSync':json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(source)),
        'targetSync':json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(target)),
        'sourceNotifies':json.loads(unreal.AlsSourceAnimationLibrary.read_source_notify_metadata(source)),
        'targetNotifies':json.loads(unreal.AlsSourceAnimationLibrary.read_source_notify_metadata(target))})
    unreal.log('LYRA_IDLE_RECOVERY_CLIP_OK '+row['slot']+' baseTime='+str(base_time))
if skin_error!=0:raise ValueError('Logical extension changed existing skin output '+str(skin_error))
check_packages()
for name,digest in dependencies.items():
    if sha((root/name).read_bytes())!=digest:raise ValueError('Changed old dependency '+name)
catalog_sha=save('catalog.json',{'schemaVersion':1,'dependencies':dependencies,'entries':entries,'curves':curves,
    'assetSha256':packages,'skinPreservation':skin_error})
save('native.json',{'schemaVersion':1,'catalogSha256':catalog_sha,'rows':native,'curveRows':curve_native})
save('playback.json',{'schemaVersion':1,'catalogSha256':catalog_sha,'entries':playback})
bindings={profile:{path:(targets or [next(row['target'] for row in rows if row['source']==path)])
    for path,targets in provider['sequenceTargets'].items()} for profile,provider in inventory['providers'].items()}
save('bindings.json',{'schemaVersion':1,'inventorySha256':dependencies['locomotion_layer_closures.json'],
    'catalogSha256':catalog_sha,'providers':bindings,'missingSequences':[]})
unreal.log('LYRA_IDLE_RECOVERY_RESOURCES_OK extras=8 idleBreaks=5 jumpAdditives=3 logical=81 skin=68 native='+str(len(native))+
    ' packages='+str(len(packages))+' new_targets='+str(len(missing))+' skinError=0')
