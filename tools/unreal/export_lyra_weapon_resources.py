"""Original independent weapon rigs, six Montage/clip resources and FBX meshes."""
import hashlib
import json
from pathlib import Path
import unreal

repo=Path(__file__).resolve().parents[2]
root=repo/'assets/generated/lyra_als'
destination=root/'weapon_resources'
destination.mkdir(exist_ok=True)
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
policy=json.loads((root/'weapon_notify_v1_policy.json').read_bytes())
project=Path(unreal.Paths.get_project_file_path())
protected={p:sha(p) for p in [project,*(project.parent/'Config').rglob('*.ini')]}
previous={p.relative_to(root).as_posix():sha(p) for p in root.rglob('*.json') if destination not in p.parents}
packages=dict(policy['assetSha256'])
def package_file(path):
    package=path.split('.')[0]
    if package.startswith('/Game/'):return project.parent/'Content'/(package.removeprefix('/Game/')+'.uasset')
    if package.startswith('/ShooterCore/'):return project.parent/'Plugins/GameFeatures/ShooterCore/Content'/(package.removeprefix('/ShooterCore/')+'.uasset')
    raise ValueError(path)
def add(path):
    if path:packages.setdefault(path,sha(package_file(path)))
def protect():
    for p,d in protected.items():assert sha(p)==d,str(p)
    for p,d in previous.items():assert sha(root/p)==d,p
    for p,d in packages.items():assert sha(package_file(p))==d,p
def write(name,data):
    path=destination/name
    if path.exists():assert json.loads(path.read_bytes())==data,'Independent weapon resource differs: '+name
    else:
        with path.open('x',encoding='utf-8',newline='\n') as f:f.write(json.dumps(data,separators=(',',':'),allow_nan=False)+'\n')
    return sha(path)
protect()
assert unreal.AlsLyraControlRigLibrary.prefer_raw_track_data_model(True)
montage_paths=sorted({e['payload']['MontageToPlay']['path'] for e in policy['events']})
montages=json.loads(unreal.AlsLyraMontageLibrary.read_catalog([unreal.load_asset(p) for p in montage_paths]))['assets']
sequences={}
for m in montages:
    add(m['skeleton'])
    m['notifies']=json.loads(unreal.AlsSourceAnimationLibrary.read_source_notify_metadata(unreal.load_asset(m['path'])))
    for track in m['slots']:
        for segment in track['segments']:
            path=segment['animation']
            if path in sequences:continue
            add(path)
            obj=unreal.load_asset(path)
            metadata=json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(obj))
            raw=json.loads(unreal.AlsSourcePoseKeysLibrary.read_source_pose_keys(obj))
            assert raw['tracks'], 'Empty authored DataModel tracks: '+path
            skeleton=json.loads(unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(obj.get_editor_property('skeleton')))
            native=[dict(time=t,pose=json.loads(unreal.AlsSourceAnimationLibrary.read_raw_animation_pose(obj,t,True,False,False)))
                for t in sorted({0,.123456789,obj.get_play_length()*.37,obj.get_play_length()*.5,obj.get_play_length()})]
            data=dict(source=path,metadata=metadata,raw=raw,skeleton=skeleton,native=native)
            filename=path.split('.')[-1]+'.json'
            sequences[path]=dict(file=filename,sha256=write(filename,data))
meshes=[]
for kind,name in [('pistol','SK_Pistol'),('rifle','SK_Rifle'),('shotgun','SKM_Shotgun')]:
    source=f'/Game/Weapons/{kind.title()}/Mesh/{name}.{name}'
    add(source)
    mesh=unreal.load_asset(source)
    skeleton=mesh.get_editor_property('skeleton')
    add(skeleton.get_path_name())
    blueprint=f'/Game/Weapons/{kind.title()}/Animations/ABP_Weap_{kind.title()}.ABP_Weap_{kind.title()}'
    add(blueprint)
    filename=destination/(kind+'.fbx')
    assert filename.is_file(), 'Run export_lyra_weapon_meshes.py in a full Editor first: '+str(filename)
    meshes.append(dict(kind=kind,source=source,skeleton=skeleton.get_path_name(),animationBlueprint=blueprint+'_C',
        file=filename.name,sha256=sha(filename),metadata=json.loads(unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(skeleton)),
        graph=json.loads(unreal.AlsLyraGraphLibrary.read_runtime_graph(unreal.EditorAssetLibrary.load_blueprint_class(blueprint))),
        sourceNodes=json.loads(unreal.AlsLyraGraphLibrary.read_source_nodes(unreal.EditorAssetLibrary.load_blueprint_class(blueprint)))))
protect()
catalog=dict(schemaVersion=1,dependencies={'weapon_notify_v1_policy.json':sha(root/'weapon_notify_v1_policy.json')},
    previousFixtureSha256=previous,assetSha256=packages,montages=montages,sequences=sequences,meshes=meshes,
    scope=dict(independentWeaponSkeleton=True,retargeted=False,nativeRawSampling=True,continuousWeaponPose=False,assetsSaved=0))
write('catalog.json',catalog)
unreal.log(f'LYRA_WEAPON_RESOURCES_OK montages={len(montages)} sequences={len(sequences)} meshes={len(meshes)} previous={len(previous)} packages={len(packages)} assets_saved=0')
