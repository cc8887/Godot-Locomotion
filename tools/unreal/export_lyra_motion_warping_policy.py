"""Read original MW windows, modifier values and GA_Emote graph without saving assets."""
import hashlib
import json
import os
import re
import tempfile
from pathlib import Path
import unreal

repo=Path(__file__).resolve().parents[2]
root=repo/'assets/generated/lyra_als'
name='motion_warping_v1_policy.json'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
load=lambda p:json.loads(p.read_bytes())
project=Path(unreal.Paths.get_project_file_path())
previous={p.relative_to(root).as_posix():sha(p) for p in root.rglob('*.json') if p.name!=name}
protected={p.relative_to(project.parent).as_posix():sha(p) for p in [project,*(project.parent/'Config').rglob('*.ini')]}
packages=dict(load(root/'root_movement_v1_policy.json')['assetSha256'])

def package_file(path):
    path=path.split('.')[0]
    if path.startswith('/Game/'):return project.parent/'Content'/(path.removeprefix('/Game/')+'.uasset')
    if path.startswith('/ShooterCore/'):return project.parent/'Plugins/GameFeatures/ShooterCore/Content'/(path.removeprefix('/ShooterCore/')+'.uasset')
    raise ValueError(path)

def protect():
    for p,d in previous.items():assert sha(root/p)==d,p
    for p,d in protected.items():assert sha(project.parent/p)==d,p
    for p,d in packages.items():assert sha(package_file(p))==d,p

ability='/ShooterCore/Game/Emote/GA_Emote.GA_Emote'
packages[ability]=sha(package_file(ability))
protect()
bridge=unreal.BlueprintLispPythonBridge
listed=bridge.list_graphs(ability)
assert listed.success and not listed.saved_package,(ability,listed.message)
variables=bridge.list_member_variables(ability)
assert variables.success and not variables.saved_package,variables.message
graphs={}
for line in listed.dsl_text.splitlines():
    match=re.fullmatch(r'\[[^]]+\]\s+(.+)',line.strip());assert match,line
    graph=match.group(1)
    result=bridge.export_graph_to_text(ability,graph,False,True)
    assert result.success and not result.saved_package,(graph,result.message)
    graphs[graph]=dict(dsl=result.dsl_text,warnings=list(result.warnings))

fd,scratch_name=tempfile.mkstemp(prefix='lyra-mw-policy-',suffix='.t3d',dir=repo/'artifacts/lyra-analysis')
os.close(fd);scratch=Path(scratch_name)
def text(obj):
    task=unreal.AssetExportTask();task.object=obj;task.filename=str(scratch);task.automated=True;task.prompt=False;task.replace_identical=True;task.exporter=unreal.ObjectExporterT3D()
    assert unreal.Exporter.run_asset_export_task(task) and not task.errors,obj.get_path_name()
    return scratch.read_text(encoding='utf-8-sig')

fields=('warp_target_name','warp_point_anim_provider','warp_point_anim_transform','warp_point_anim_bone_name',
        'warp_translation','ignore_z_axis','warp_to_feet_location','add_translation_easing_func','add_translation_easing_curve',
        'warp_rotation','rotation_type','rotation_method','subtract_remaining_root_motion','additional_rotation_offset',
        'warp_rotation_time_multiplier','warp_max_rotation_rate','max_speed_clamp_ratio')
def value(v):
    if v is None or isinstance(v,(int,float,bool,str)):return v
    if isinstance(v,unreal.Name):return str(v)
    if isinstance(v,unreal.Object):return v.get_path_name()
    if isinstance(v,unreal.Rotator):return [v.pitch,v.yaw,v.roll]
    if isinstance(v,unreal.Transform):
        p,q,s=v.translation,v.rotation,v.scale3d
        return dict(position=[p.x,p.y,p.z],rotation=[q.x,q.y,q.z,q.w],scale=[s.x,s.y,s.z])
    if hasattr(v,'value'):return dict(name=str(v),value=v.value)
    raise TypeError(type(v))

windows=[]
notify_assets={a['source']:a for a in load(root/'notify_contract_v1.json')['assets']}
try:
    for row in load(root/'root_movement_v1_policy.json')['references']:
        montage=unreal.load_asset(row['montage']);assert montage is not None,row
        events=unreal.AnimationLibrary.get_animation_notify_events(montage)
        for index,event in enumerate(events):
            state=event.get_editor_property('notify_state_class')
            if not isinstance(state,unreal.AnimNotifyState_MotionWarping):continue
            modifier=state.get_editor_property('root_motion_modifier')
            assert isinstance(modifier,unreal.RootMotionModifier_SkewWarp),modifier
            windows.append(dict(montage=row['montage'],index=index,state=state.get_path_name(),modifier=modifier.get_path_name(),
                modifierClass=modifier.get_class().get_path_name(),
                triggerTime=unreal.AnimationLibrary.get_anim_notify_event_trigger_time(event),
                endTriggerTime=notify_assets[row['montage']]['events'][index]['endTriggerTime'],
                properties={f:value(modifier.get_editor_property(f)) for f in fields},
                nativeText=text(modifier),stateNativeText=text(state)))
finally:
    scratch.unlink(missing_ok=True)
assert len(windows)==4,len(windows)
protect()
data=dict(schemaVersion=1,engineVersion=unreal.SystemLibrary.get_engine_version(),
    dependencies={p:sha(root/p) for p in ('root_movement_v1_policy.json','root_movement_v1_actor_policy.json','notify_contract_v1.json')},
    previousFixtureSha256=previous,protectedProject=protected,assetSha256=packages,windows=windows,
    ability=dict(path=ability,variables=json.loads(variables.dsl_text),graphs=graphs),
    scope=dict(assetsSaved=0,originalGraphEvidence=True,originalAbilityExecuted=False,modifierRuntimeExecuted=False))
out=root/name
if out.exists():assert load(out)==data,'Independent MotionWarping policy changed'
else:
    with out.open('x',encoding='utf-8',newline='\n') as f:f.write(json.dumps(data,ensure_ascii=False,separators=(',',':'))+'\n')
unreal.log('LYRA_MOTION_WARPING_POLICY_OK windows='+str(len(windows))+' graphs='+str(len(graphs))+' previous='+str(len(previous))+' assets_saved=0')
