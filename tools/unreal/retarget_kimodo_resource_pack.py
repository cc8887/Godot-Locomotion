"""Batch native SOMA -> Mocara Manny -> aligned ALS, with per-job receipts.

Run as a UE Python commandlet with KIMODO_PACK_REQUEST and KIMODO_PACK_OUTPUT.
Only writes a version-specific /Game/KimodoMocara/Production subtree and the
Mocara diagnostic scratch assets used by its public verification command.
"""
import gc
import hashlib
import json
import os
import runpy
from pathlib import Path
import unreal

def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()

def write(path,value):
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_text(json.dumps(value,indent=2,allow_nan=False)+'\n',encoding='utf-8')

def main():
    request=json.loads(Path(os.environ['KIMODO_PACK_REQUEST']).read_bytes())
    output=Path(os.environ['KIMODO_PACK_OUTPUT']).resolve()
    output.mkdir(parents=True,exist_ok=True)
    version=request['version']
    if not version.replace('_','').isalnum():raise ValueError('Unsafe version name')
    project='/Game/KimodoMocara/Production/'+version
    tools=Path(__file__).resolve().parent
    manny='/Game/Characters/UE5_Mannequins/Meshes/SKM_Manny.SKM_Manny'
    meshes={'v4':'/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/Mannequin'}
    registry=unreal.AssetRegistryHelpers.get_asset_registry()
    for data in registry.get_assets_by_path('/ALS/ALS/Character',recursive=True):
        if str(data.asset_class_path.asset_name)=='SkeletalMesh':
            mesh=data.get_asset()
            if mesh.get_editor_property('skeleton').get_path_name()=='/ALS/ALS/Character/SK_Als.SK_Als':
                meshes['refactored']=mesh.get_path_name();break
    # The standalone export host may only contain V4. The publisher verifies
    # all physical bind transforms/hierarchy before adapting that readback to
    # Refactored's case-only names; it rejects genuinely different target rigs.
    if 'refactored' not in meshes:meshes['refactored']=meshes['v4']
    for edition,mesh in meshes.items():
        os.environ.update(KIMODO_ALS_TARGET_MESH=mesh,
            KIMODO_ALS_RIG_PATH=project+'/Rigs/IK_'+edition,
            KIMODO_RETARGETER_PATH=project+'/Rigs/RTG_'+edition,
            KIMODO_ALSRIG_OUT=str(output/('rig-'+edition+'.json')))
        unreal.EditorAssetLibrary.make_directory(project+'/Rigs')
        runpy.run_path(str(tools/'create_als_ikrig.py'),run_name='__main__')
        rig=json.loads((output/('rig-'+edition+'.json')).read_bytes())
        if not rig.get('retargetPoseAligned'):raise RuntimeError('Target alignment failed: '+edition)
    completed=[]
    for job in request['jobs']:
        identifier=hashlib.sha256(job['source'].encode()).hexdigest()
        receipt=output/'receipts'/(identifier+'.json')
        if receipt.exists():
            saved=json.loads(receipt.read_bytes())
            if saved['bvhSha256']==sha(job['bvhPath']) and all(sha(saved[k])==saved[k+'Sha256'] for k in ('fbx','raw')):
                completed.append(saved);continue
            raise RuntimeError('Existing retarget receipt differs; use a new version')
        if sha(job['bvhPath'])!=job['bvhSha256']:raise RuntimeError('Authoring BVH changed')
        command='Mocara.VerifyRetarget "'+job['bvhPath']+'" '+manny
        unreal.SystemLibrary.execute_console_command(None,command)
        paths=unreal.EditorAssetLibrary.list_assets('/Game/Mocara/Verify',recursive=False,include_folder=False)
        paths=[p for p in paths if '/AS_RtVerify_' in p and '_SKM_Manny_' in p]
        if len(paths)!=1:raise RuntimeError('Mocara Manny output is ambiguous: '+str(paths))
        source=unreal.load_asset(paths[0])
        if not isinstance(source,unreal.AnimSequence):raise RuntimeError('No Mocara Manny animation')
        name='AS_'+identifier[:20]
        manny_path=project+'/Manny/'+name
        unreal.EditorAssetLibrary.make_directory(project+'/Manny')
        source=unreal.EditorAssetLibrary.duplicate_asset(paths[0],manny_path)
        if not isinstance(source,unreal.AnimSequence):raise RuntimeError('Could not preserve Manny source')
        unreal.EditorAssetLibrary.save_loaded_asset(source,only_if_is_dirty=False)
        edition=job['edition'];target_dir=project+'/'+edition
        fbx=output/'fbx'/(identifier+'.fbx')
        os.environ.update(KIMODO_INPUT_ASSET_PATH=source.get_path_name(),KIMODO_ASSET_PATH=target_dir+'/'+name,
            KIMODO_FBX_OUT=str(fbx),KIMODO_SEARCH='',KIMODO_REPLACE='',
            KIMODO_ALS_TARGET_MESH=meshes[edition],KIMODO_ALS_TARGET_DIR=target_dir,
            KIMODO_ALS_SKELETON_NAME=unreal.load_asset(meshes[edition]).get_editor_property('skeleton').get_name(),
            KIMODO_RETARGETER_PATH=project+'/Rigs/RTG_'+edition)
        runpy.run_path(str(tools/'retarget_als_final.py'),run_name='__main__')
        metadata=json.loads(Path(str(fbx)+'.source.json').read_bytes())
        anim=unreal.load_asset(metadata['retargetedAssetPath'])
        raw=output/'raw'/(identifier+'.json')
        text=unreal.AlsSourcePoseKeysLibrary.read_source_pose_keys(anim)
        data=json.loads(text,parse_int=lambda v:-0.0 if v=='-0' else int(v))
        if data['source']!=anim.get_path_name():raise RuntimeError('Raw identity differs')
        expected_skeleton=unreal.load_asset(meshes[edition]).get_editor_property('skeleton').get_path_name()
        if data['skeletonSource']!=expected_skeleton:raise RuntimeError('Foreign target skeleton')
        write(raw,data)
        saved=dict(job,fbx=str(fbx),fbxSha256=sha(fbx),raw=str(raw),rawSha256=sha(raw),
                   retargetedAssetPath=anim.get_path_name(),targetSkeleton=expected_skeleton)
        write(receipt,saved);completed.append(saved)
        write(output/'progress.json',{'status':'running','completed':len(completed),'total':len(request['jobs'])})
        unreal.log('KIMODO_PACK_JOB_OK '+job['node'])
        gc.collect()
    write(output/'retarget-bundle.json',{'status':'retargeted','version':version,'jobs':completed,'meshes':meshes})
    unreal.log('KIMODO_PACK_RETARGET_OK jobs='+str(len(completed)))

main()
