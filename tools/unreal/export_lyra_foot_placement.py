"""Original ALS81 FootPlacement node, continuous bend history and same CDO for each provider."""
import hashlib
import json
import math
import os
import struct
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
repo = Path(__file__).resolve().parents[2]
prefix = 'foot_placement_v1'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
f32 = lambda v: struct.unpack('<f', struct.pack('<f', v))[0]
graph = json.loads((root / 'main_layer_graph_v1.json').read_bytes())
packages = graph['assetSha256']
previous = {str(p.relative_to(root)).replace('\\', '/'): sha(p) for p in root.rglob('*.json') if not p.name.startswith(prefix)}
if (root / (prefix + '_native.json')).exists():
    previous = json.loads((root / (prefix + '_native.json')).read_bytes())['previousFixtureSha256']
content = Path(unreal.Paths.project_content_dir())

def protect():
    for name, digest in packages.items():
        assert sha(content / (name.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, name
    for name, digest in previous.items():
        assert sha(root / name) == digest, name

def save(kind, data):
    p = root / (prefix + '_' + kind + '.json')
    if p.exists():
        assert json.loads(p.read_bytes()) == data, 'Immutable FootPlacement fixture differs: ' + kind
    else:
        p.write_text(json.dumps(data, separators=(',', ':')), encoding='utf-8')
    return sha(p)

protect()
runtime_cvars = {name: unreal.SystemLibrary.get_console_variable_int_value(name) for name in
    ('a.AnimNode.FootPlacement.Enable', 'a.AnimNode.FootPlacement.Enable.Lock')}
assert list(runtime_cvars.values()) == [1, 1], runtime_cvars
deps = {p: sha(root / p) for p in ('main_layer_graph_v1.json', 'logical_controls/catalog.json',
    'logical_controls/calibration.json', 'logical_controls/curve_bank.json', 'skeletal_control_defaults.json')}
cal = json.loads((root / 'logical_controls/calibration.json').read_bytes())['calibration']
catalog = json.loads((root / 'logical_controls/catalog.json').read_bytes())['entries']
quat = unreal.Quat(*cal['handBasis']['rotation'])
skeleton = unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(unreal.load_asset(cal['sourceMesh']).get_editor_property('skeleton'),
    unreal.load_asset(cal['targetMesh']).get_editor_property('skeleton'), quat)
paths = json.loads((root / 'left_hand_layer_v4_requests.json').read_bytes())['sequencePaths']
sequences = []
lengths = []
for path in paths:
    r = next(r for r in catalog if r['target'] == path)
    s = unreal.AlsLyraControlRigLibrary.create_weapon_sequence(unreal.load_asset(r['source']), unreal.load_asset(path), skeleton, quat, None)
    unreal.AlsSourceAnimationLibrary.finish_source_compression(s)
    sequences.append(s)
    lengths.append(r['playLength'])
profiles, classes = {}, []
for profile in ('unarmed', 'pistol', 'rifle'):
    cls = graph['classes'][profile]
    settings = next(n['settings'] for n in cls['graphs']['FullBody_SkeletalControls']['nodes'] if n['index'] == 105)
    assert settings['plantSpeedMode'] == 'Manual' and settings['plantSettings']['lockType'] == 'Unlocked'
    assert all(l['speedCurveName'] == l['disableLockCurveName'] == l['disableLegCurveName'] == 'None' for l in settings['legDefinitions'])
    profiles[profile] = dict(classPath=cls['classPath'], settings=settings)
    classes.append(unreal.load_class(None, cls['classPath']))
requests = dict(sequencePaths=paths, traces=[])
alphas = [0, f32(1e-5), .25, .5, f32(.99998), 1]
for provider, profile in enumerate(profiles):
    for hz in (30, 60, 120):
        frames = []
        for i in range(hz * 6):
            t = i / hz
            asset = (i // 31) % len(paths)
            time = f32((t * .73) % lengths[asset])
            old = f32(max(0, time - 1 / hz))
            normal = [math.sin(t * 1.9) * .3, math.cos(t * 1.17) * .2, 1]
            length = math.sqrt(sum(v*v for v in normal))
            normal = [v/length for v in normal]
            yaw = math.sin(t * .73) * .4
            frames.append(dict(delta=f32(0 if i % 101 == 100 else (.35 if i % 307 == 306 else 1/hz)),
                asset=asset, time=time, previous=old, sourceDelta=f32(time-old),
                componentP=[t*7, math.sin(t*2.1)*6, math.sin(t*1.3)*8 + (15 if 2.7 < t < 3.1 else 0)],
                componentQ=[0,0,math.sin(yaw/2),math.cos(yaw/2)],
                floorPoint=[0,0,math.sin(t*1.7)*18+(30 if 1.5 < t < 2 else 0)], floorNormal=normal,
                walking=not (2.2 < t < 2.6), blocking=not (3.5 < t < 3.8), geometry=not (4.2 < t < 4.5),
                velocity=[7,12.6*math.cos(t*2.1),10.4*math.cos(t*1.3)],
                alpha=alphas[(i//11)%len(alphas)], initialize=i==0 or i%173==172,
                visited=not (.8 <= t < 1.1 or 3.1 <= t < 3.3), evaluate=not (1.1 < t < 1.3 or 4.6 < t < 4.8)))
        requests['traces'].append(dict(profile=profile, provider=provider, hz=hz, frames=frames))
native = json.loads(unreal.AlsLyraFootPlacementLibrary.read_trace(unreal.load_class(None,graph['classes']['main']['classPath']),
    unreal.load_asset(cal['sourceMesh']), skeleton, classes, sequences, json.dumps(requests,separators=(',',':'))))
(repo/'artifacts/lyra-analysis/foot-placement-native-diagnostic.json').write_text(json.dumps(native,separators=(',',':')),encoding='utf-8')
counts = dict(frames=0,poses=0,active=0,hits=0,grounded=0,hidden=0,updateOnly=0,initialize=0)
for trace, req in zip(native['traces'], requests['traces'], strict=True):
    assert (trace['profile'],trace['hz']) == (req['profile'],req['hz']) and len(trace['frames']) == len(req['frames'])
    for row, frame in zip(trace['frames'],req['frames'],strict=True):
        counts['frames'] += 1
        counts['hidden'] += not frame['visited']
        counts['updateOnly'] += frame['visited'] and not frame['evaluate']
        counts['initialize'] += frame['initialize']
        if 'output' in row:
            counts['poses'] += 1
            counts['active'] += row['changedBones'] == 3
            counts['hits'] += sum(h['walkable'] for h in row['hits'])
            counts['grounded'] += row['after']['onGround']
            assert row['changedBones'] in (0,3) and len(row['input']['pose']) == len(row['output']['pose']) == 81
            for channel in ('curves','attributes','rootMotion'):
                assert row['input'].get(channel) == row['output'].get(channel)
assert counts['frames'] == 3780 and counts['active'] > 1000 and counts['hits'] > 1000 and counts['grounded'] > 1000, counts
protect()
source = repo/'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
source_sha = {p:sha(source/p) for p in ('Private/AlsLyraFootPlacementLibrary.cpp','Public/AlsLyraFootPlacementLibrary.h',
    'Private/AlsLyraControlRigLibrary.cpp','Private/AlsLyraCyclePoseLibrary.cpp')}
for tree in ('source','package'):
    for name,digest in source_sha.items():
        assert sha(repo/'artifacts/unreal/gasp58-lyra-masks'/tree/'AlsV4AssetExporter/Source/AlsV4AssetExporter'/name) == digest
request_sha = save('requests',requests)
policy_sha = save('policy',dict(schemaVersion=1,stage='OriginalFootPlacement',skeleton='ALS81',dependencies=deps,
    profiles=profiles,runtimeCVars=runtime_cvars,manualSpeedFallback=60,lockType='Unlocked',resolvedAlphaInput=True,
    geometry='Actual UE World box sphere sweeps; character floor observations controlled',production=False))
native.update(schemaVersion=1,dependencies=deps,requestSha256=request_sha,policySha256=policy_sha,counts=counts,
    assetSha256=packages,previousFixtureSha256=previous,probeSourceSha256=source_sha,
    scope='Original FootPlacement CDO operator and retained histories on ALS81, controlled source/time/resolved alpha and Character floor, actual physical box queries; not whole SkeletalControls/Main or Godot production collision validation.')
save('native',native)
unreal.log('LYRA_FOOT_PLACEMENT_NATIVE_OK frames=3780 poses='+str(counts['poses'])+' previous='+str(len(previous))+' assets_saved=0')
