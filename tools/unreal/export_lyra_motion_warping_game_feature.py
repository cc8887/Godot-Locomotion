"""Read ShooterCore component injection; no GameFeature activation or asset saves."""
import hashlib
import json
import os
import tempfile
from pathlib import Path
import unreal

repo=Path(__file__).resolve().parents[2]
root=repo/'assets/generated/lyra_als'
name='motion_warping_v1_game_feature.json'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
project=Path(unreal.Paths.get_project_file_path())
previous={p.relative_to(root).as_posix():sha(p) for p in root.rglob('*.json') if p.name!=name}
protected={p.relative_to(project.parent).as_posix():sha(p) for p in [project,*(project.parent/'Config').rglob('*.ini')]}
path='/ShooterCore/ShooterCore.ShooterCore'
package=project.parent/'Plugins/GameFeatures/ShooterCore/Content/ShooterCore.uasset'
digest=sha(package)
asset=unreal.load_asset(path);assert asset is not None,path
fd,temp=tempfile.mkstemp(prefix='lyra-mw-feature-',suffix='.t3d',dir=repo/'artifacts/lyra-analysis')
os.close(fd);scratch=Path(temp)
actions=[]
try:
    for a in asset.get_editor_property('actions'):
        task=unreal.AssetExportTask();task.object=a;task.filename=str(scratch);task.automated=True;task.prompt=False;task.replace_identical=True;task.exporter=unreal.ObjectExporterT3D()
        assert unreal.Exporter.run_asset_export_task(task) and not task.errors,a.get_path_name()
        actions.append(dict(path=a.get_path_name(),classPath=a.get_class().get_path_name(),nativeText=scratch.read_text(encoding='utf-8-sig')))
finally:scratch.unlink(missing_ok=True)
for p,d in previous.items():assert sha(root/p)==d,p
for p,d in protected.items():assert sha(project.parent/p)==d,p
assert sha(package)==digest
data=dict(schemaVersion=1,path=path,assetSha256=digest,previousFixtureSha256=previous,protectedProject=protected,actions=actions,assetsSaved=0,featureActivated=False)
out=root/name
if out.exists():assert json.loads(out.read_bytes())==data,'Independent component injection policy changed'
else:
    with out.open('x',encoding='utf-8',newline='\n') as f:f.write(json.dumps(data,ensure_ascii=False,separators=(',',':'))+'\n')
unreal.log('LYRA_MOTION_WARPING_FEATURE_OK actions='+str(len(actions))+' previous='+str(len(previous))+' assets_saved=0')
