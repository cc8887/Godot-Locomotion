"""FBX mesh export needs a full Editor world with a real render MeshObject."""
import hashlib
from pathlib import Path
import unreal
repo=Path(__file__).resolve().parents[2]
root=repo/'assets/generated/lyra_als'
destination=root/'weapon_resources'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
project=Path(unreal.Paths.get_project_file_path())
protected={p:sha(p) for p in [project,*(project.parent/'Config').rglob('*.ini'),*root.rglob('*.json')]}
packages={}
for kind,name in [('pistol','SK_Pistol'),('rifle','SK_Rifle'),('shotgun','SKM_Shotgun')]:
    package=project.parent/'Content/Weapons'/kind.title()/'Mesh'/(name+'.uasset')
    packages[package]=sha(package)
    mesh=unreal.load_asset(f'/Game/Weapons/{kind.title()}/Mesh/{name}.{name}')
    filename=destination/(kind+'.fbx')
    assert not filename.exists(), 'Preserve original FBX: '+str(filename)
    options=unreal.FbxExportOption()
    for key,value in [('fbx_export_compatibility',unreal.FbxExportCompatibility.FBX_2020),('ascii',True),('force_front_x_axis',True),('map_skeletal_motion_to_root',False),('export_preview_mesh',False),('level_of_detail',False),('bake_material_inputs',unreal.FbxMaterialBakeMode.DISABLED)]:options.set_editor_property(key,value)
    task=unreal.AssetExportTask()
    for key,value in [('object',mesh),('exporter',unreal.SkeletalMeshExporterFBX()),('filename',str(filename)),('options',options),('automated',True),('prompt',False),('replace_identical',False)]:task.set_editor_property(key,value)
    assert unreal.Exporter.run_asset_export_task(task) and filename.is_file(),task.get_editor_property('errors')
    unreal.log(f'LYRA_WEAPON_FBX_OK kind={kind} sha256={sha(filename)} bytes={filename.stat().st_size}')
for p,d in {**protected,**packages}.items():assert sha(p)==d,str(p)
unreal.log('LYRA_WEAPON_MESHES_OK meshes=3 original_assets_saved=0')
