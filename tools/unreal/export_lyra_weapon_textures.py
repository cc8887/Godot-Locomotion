"""Export original texture source pixels; preserve the original FBX basenames."""
import hashlib
import json
from pathlib import Path
import unreal
repo=Path(__file__).resolve().parents[2]
root=repo/'assets/generated/lyra_als'
destination=root/'weapon_resources'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
project=Path(unreal.Paths.get_project_file_path())
protected={p:sha(p) for p in [project,*(project.parent/'Config').rglob('*.ini'),*root.rglob('*.json')]}
packages={}
entries=[]
for kind in ('Pistol','Rifle','Shotgun'):
    directory='Texture' if kind=='Shotgun' else 'Textures'
    for channel,suffix in [('diffuse','D'),('normal','Combined_N' if kind=='Rifle' else 'N'),('aorm','AORM'),('mask','Mask' if kind=='Shotgun' else 'Masks')]:
        name='T_'+kind+'_'+suffix
        package=project.parent/'Content/Weapons'/kind/directory/(name+'.uasset')
        packages[package]=sha(package)
        source=f'/Game/Weapons/{kind}/{directory}/{name}.{name}'
        filename=('T_'+kind+'_Combined_N' if channel=='normal' else name)+'.png'
        target=destination/filename
        if not target.exists():
            task=unreal.AssetExportTask()
            for key,value in [('object',unreal.load_asset(source)),('exporter',unreal.TextureExporterPNG()),('filename',str(target)),('automated',True),('prompt',False),('replace_identical',False)]:task.set_editor_property(key,value)
            assert unreal.Exporter.run_asset_export_task(task) and target.is_file(),task.get_editor_property('errors')
        entries.append(dict(kind=kind.lower(),channel=channel,source=source,file=filename,sha256=sha(target)))
for p,d in {**protected,**packages}.items():assert sha(p)==d,str(p)
catalog=dict(schemaVersion=1,dependencies={'weapon_resources/catalog.json':sha(destination/'catalog.json')},entries=entries,
    assetSha256={p.relative_to(project.parent).as_posix():d for p,d in packages.items()},materialEquivalent=False,assetsSaved=0)
target=destination/'textures.json'
if target.exists():assert json.loads(target.read_bytes())==catalog
else:
    with target.open('x',encoding='utf-8',newline='\n') as f:f.write(json.dumps(catalog,separators=(',',':'))+'\n')
unreal.log('LYRA_WEAPON_TEXTURES_OK textures=12 original_assets_saved=0')
