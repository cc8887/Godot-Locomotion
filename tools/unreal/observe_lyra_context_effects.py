"""Read current context library with original authored tag tables in memory."""
import hashlib
import json
from pathlib import Path
import unreal

repo=Path(__file__).resolve().parents[2]
root=repo/'assets/generated/lyra_als'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
previous={p.relative_to(root).as_posix():sha(p) for p in root.rglob('*.json')}
contract=json.loads((root/'notify_contract_v1.json').read_bytes())
content=Path(unreal.Paths.project_content_dir())
packages=dict(contract['assetSha256'])
for path in ('/Game/ContextEffects/CFX_DefaultSkin.CFX_DefaultSkin','/Game/ContextEffects/DT_AnimEffectTags.DT_AnimEffectTags',
 '/Game/ContextEffects/DT_SurfaceTypes.DT_SurfaceTypes','/Game/PhysicsMaterials/PM_Character.PM_Character',
 '/Game/PhysicsMaterials/PM_Concrete.PM_Concrete','/Game/PhysicsMaterials/PM_Glass.PM_Glass'):
    packages[path]=sha(content/(path.split('.')[0].removeprefix('/Game/')+'.uasset'))
def protect():
    for name,digest in previous.items():assert sha(root/name)==digest,name
    for path,digest in packages.items():assert sha(content/(path.split('.')[0].removeprefix('/Game/')+'.uasset'))==digest,path
protect()
policy=json.loads(unreal.LyraContextEffectsOracleLibrary.read_policy())
protect()
output=repo/'artifacts/lyra-analysis/notify-context-policy-observation-first.json'
with output.open('x',encoding='utf-8',newline='\n') as f:f.write(json.dumps(policy,separators=(',',':'))+'\n')
unreal.log(f'LYRA_CONTEXT_POLICY_OBSERVED rows={len(policy["rows"])} queries={len(policy["queries"])} settings={policy["settings"]} assets_saved=0')
