"""Five original Air layers on transient ALS81, one original linked owner/Sync."""
import copy
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT']); repo = Path(__file__).resolve().parents[2]
sha = lambda data: hashlib.sha256(data).hexdigest()
prefix = 'air_runtime'
own = {prefix+'_'+n+'.json' for n in ('requests', 'distance', 'roots', 'native')}
names = ('locomotion_layer_closures.json', 'source_nodes.json', 'logical_controls/catalog.json',
         'logical_controls/calibration.json', 'cycle_layer_graph.json', 'cycle_layer_pose_policy.json',
         'root_motion_policy.json', 'main_machine_runtime_v2_native.json')
files = {n: (root/n).read_bytes() for n in names}
closures, nodes, catalog, calibration = map(json.loads, list(files.values())[:4])
packages = json.loads(files[names[-1]])['assetSha256']
previous = {str(p.relative_to(root)).replace('\\', '/'): sha(p.read_bytes()) for p in root.rglob('*.json') if p.name not in own}
if (root/(prefix+'_native.json')).exists():
    previous = json.loads((root/(prefix+'_native.json')).read_bytes())['previousFixtureSha256']
content = Path(unreal.Paths.project_content_dir())

def protect():
    for p, digest in packages.items():
        if sha((content/(p.split('.')[0].removeprefix('/Game/')+'.uasset')).read_bytes()) != digest: raise ValueError('Changed Air package: '+p)
    for p, digest in previous.items():
        if sha((root/p).read_bytes()) != digest: raise ValueError('Changed prior Air fixture: '+p)

def save(name, data):
    p = root/(prefix+'_'+name+'.json')
    if p.exists():
        if json.loads(p.read_bytes()) != data: raise ValueError('Immutable Air capture differs: '+name)
    else: p.write_text(json.dumps(data, separators=(',', ':')), encoding='utf-8')
    return sha(p.read_bytes())

protect()
hooks = ('FullBody_JumpStartState', 'FullBody_JumpStartLoopState', 'FullBody_JumpApexState', 'FullBody_FallLoopState', 'FullBody_FallLandState')
fields = ('Jump_Start', 'Jump_StartLoop', 'Jump_Apex', 'Jump_FallLoop', 'Jump_FallLand')
paths = []; target_by_source = {}
for e in catalog['entries']: target_by_source.setdefault(e['source'], []).append(e)
entries = {e['target']: e for e in catalog['entries']}
requests = dict(schemaVersion=1, layers=[], traces=[])
cycle = json.loads(files['cycle_layer_graph.json'])['graphs']
for profile in ('unarmed', 'pistol', 'rifle'):
    provider = closures['providers'][profile]; bindings = {}; definitions = []
    for hook in hooks:
        graph = provider['layers'][hook]; blend = next(n for n in graph['nodes'] if n['type'].endswith('.AnimNode_LayeredBoneBlend'))
        b = blend['settings']; c = next(n['settings'] for n in cycle[profile]['nodes'] if n['type'].endswith('.AnimNode_LayeredBoneBlend'))
        keys = ('blendMode', 'blendMasks', 'bMeshSpaceRotationBlend', 'bMeshSpaceScaleBlend', 'bRootSpaceRotationBlend',
                'curveBlendOption', 'bBlendRootMotionBasedOnRootBone', 'bUpdateBasePoseFirst', 'lODThreshold')
        if len(graph['nodes']) != 4 or any(b[k] != c[k] for k in keys): raise ValueError('Changed Air blend closure: '+hook)
        links = {l['pin']: l['index'] for l in blend['links']}
        definitions.append(dict(name=hook, root=graph['root'], blend=blend['index'], base=links['BasePose'], hip=links['BlendPoses[0]']))
    if requests['layers'] and requests['layers'] != definitions: raise ValueError('Different Air compiled identities')
    requests['layers'] = definitions
    for key in (*fields, 'Aim_HipFirePose', 'Aim_HipFirePose_Crouch'):
        source = provider['defaults']['fields'][key]['value']; choices = target_by_source[source]
        if len(choices) > 1:
            slot = 'hipfire_crouch' if profile == 'unarmed' else 'pistol_crouch_idle'
            choices = [e for e in choices if e['slot'] == slot]
        if len(choices) != 1: raise ValueError('Ambiguous Air binding: '+key)
        target = choices[0]['target']; bindings[key] = target
        if target not in paths: paths.append(target)
    for hz in (30, 60, 120):
        frames = []
        for i in range(hz*6):
            stage = i//max(1, hz//3); visits = []
            for n in range(5):
                visited = n == stage % 5 or (stage % 4 == 3) or n == (stage+1) % 5 and stage % 4 == 2
                if i % 53 in (21, 22): visited = False
                visits.append(dict(visited=visited, active=i % 11 < 8, weight=(.001, 1e-5, 1.00001e-5, .63, 1)[(i//7+n) % 5],
                                   initialize=i == 0 or i == round(hz*2.4)+n or i == round(hz*4.4)+n))
            frames.append(dict(delta=1/hz, main={'IsCrouching': i//9 % 2 == 1, 'GroundDistance': (0, 10, 50, 150, 199.9999, 200, 300, 500, 1000)[i//5 % 9]},
                layer={'HipFireUpperBodyOverrideWeight': (0, 1e-5, 1.00001e-5, .3, .8, 1, -.1, 1.2)[i//10 % 8]},
                visits=visits, order=list(range(5)) if i % 2 == 0 else list(reversed(range(5)))))
        requests['traces'].append(dict(profile=profile, hz=hz, **{'class': provider['class']}, bindings=bindings, frames=frames))
requests['sequencePaths'] = paths
basis = calibration['calibration']; rotation = unreal.Quat(*basis['handBasis']['rotation'])
skeleton = unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(unreal.load_asset(basis['sourceMesh']).get_editor_property('skeleton'),
    unreal.load_asset(basis['targetMesh']).get_editor_property('skeleton'), rotation)
sequences = [unreal.AlsLyraControlRigLibrary.create_weapon_sequence(unreal.load_asset(entries[p]['source']), unreal.load_asset(p), skeleton, rotation, None) for p in paths]
if any(s is None for s in sequences): raise ValueError('Missing Air transient ALS81 source')
assets = []; distance = {}; roots = {}
fall_paths = {t['bindings']['Jump_FallLand'] for t in requests['traces']}
for path, sequence in zip(paths, sequences, strict=True):
    metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(sequence))
    sync = json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(sequence))
    assets.append(dict(path=path, length=metadata['sequencePlayLength'], rateScale=sync['rateScale'], markers=sync['markers']))
    roots[path] = json.loads(unreal.AlsLyraGraphLibrary.read_root_compression_data(sequence))
    if path in fall_paths:
        d = json.loads(unreal.AlsLyraGraphLibrary.read_distance_sequence_data(sequence, 'GroundDistance')); d['path'] = path; d['markers'] = sync['markers']; distance[path] = d
text = unreal.AlsLyraAirLibrary.read_air_trace(unreal.load_class(None, nodes['classes']['main']['class']),
    unreal.load_asset(basis['sourceMesh']), skeleton, sequences, json.dumps(requests, separators=(',', ':')))
if not text: raise ValueError('Empty original Air trace')
native = json.loads(text)
for path, sequence in zip(paths, sequences, strict=True):
    if roots[path] != json.loads(unreal.AlsLyraGraphLibrary.read_root_compression_data(sequence)): raise ValueError('Air compressed root changed: '+path)
    if path in distance:
        d = json.loads(unreal.AlsLyraGraphLibrary.read_distance_sequence_data(sequence, 'GroundDistance')); d['path'] = path; d['markers'] = distance[path]['markers']
        if d != distance[path]: raise ValueError('Air distance codec changed: '+path)
protect()
counts = dict(frames=sum(len(t['frames']) for t in native['traces']), poses=sum('output' in r for t in native['traces'] for f in t['frames'] for r in f['roots']))
if counts['frames'] != 3780 or counts['poses'] < 7000: raise ValueError('Incomplete Air trace: '+str(counts))
request_sha = save('requests', requests)
distance_sha = save('distance', dict(schemaVersion=1, requestSha256=request_sha, assets=distance))
root_sha = save('roots', dict(schemaVersion=1, requestSha256=request_sha, assets=roots))
source_root = repo/'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
source_names = ('Public/AlsLyraAirLibrary.h', 'Private/AlsLyraAirLibrary.cpp', 'Private/AlsLyraCycleLibrary.cpp', 'Private/AlsLyraCyclePoseLibrary.cpp', 'Private/AlsLyraPoseProbe.h')
source_sha = {n: sha((source_root/n).read_bytes()) for n in source_names}
for tree in ('source', 'package'):
    built = repo/'artifacts/unreal/gasp58-lyra-masks'/tree/'AlsV4AssetExporter/Source/AlsV4AssetExporter'
    for n, digest in source_sha.items():
        if sha((built/n).read_bytes()) != digest: raise ValueError('Different built Air probe: '+tree+'/'+n)
native.update(schemaVersion=1, requestSha256=request_sha, distanceSha256=distance_sha, rootSha256=root_sha, assets=assets, counts=counts,
    dependencies={n: sha(b) for n,b in files.items()}, assetSha256=packages, previousFixtureSha256=previous, probeSourceSha256=source_sha,
    scope='Original five Air provider roots, controlled Main inputs and root visits, actual linked instance and one Main Sync, ALS81 poses; no full Main machine or ordinary Demo')
save('native', native)
unreal.log('LYRA_AIR_RUNTIME_NATIVE_OK frames=3780 poses='+str(counts['poses'])+' assets='+str(len(paths))+' packages=508 previous='+str(len(previous))+' assets_saved=0')
