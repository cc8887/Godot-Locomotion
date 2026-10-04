"""Read the original Shooter pawn's controller and CMC rotation policies."""
import hashlib
import json
from pathlib import Path
import unreal

repo=Path(__file__).resolve().parents[2]
root=repo/'assets/generated/lyra_als'
name='root_movement_v1_actor_policy.json'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
previous={p.relative_to(root).as_posix():sha(p) for p in root.rglob('*.json') if p.name!=name}
project=Path(unreal.Paths.get_project_file_path())
protected={p.relative_to(project.parent).as_posix():sha(p) for p in [project,*(project.parent/'Config').rglob('*.ini')]}
path='/ShooterCore/Game/B_Hero_ShooterMannequin.B_Hero_ShooterMannequin_C'
package=project.parent/'Plugins/GameFeatures/ShooterCore/Content/Game/B_Hero_ShooterMannequin.uasset'
digest=sha(package)
cls=unreal.load_class(None,path)
if cls is None:raise RuntimeError('Missing original Shooter pawn')
pawn=unreal.get_default_object(cls)
movement=pawn.get_editor_property('character_movement')
data=dict(schemaVersion=1,pawnClass=path,useControllerRotationYaw=pawn.get_editor_property('use_controller_rotation_yaw'),
          allowPhysicsRotationDuringAnimRootMotion=movement.get_editor_property('allow_physics_rotation_during_anim_root_motion'),
          assetSha256=digest,previousFixtureSha256=previous,protectedProject=protected)
for p,d in previous.items():assert sha(root/p)==d,p
for p,d in protected.items():assert sha(project.parent/p)==d,p
assert sha(package)==digest
out=root/name
if out.exists():assert json.loads(out.read_bytes())==data,'Independent pawn policy changed'
else:
    with out.open('x',encoding='utf-8',newline='\n') as f:f.write(json.dumps(data,separators=(',',':'))+'\n')
unreal.log('LYRA_ROOT_MOVEMENT_ACTOR_OK useControllerYaw='+str(data['useControllerRotationYaw'])+' physicsRootRotation='+str(data['allowPhysicsRotationDuringAnimRootMotion'])+' assets_saved=0')
