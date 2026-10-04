"""Original ALS81 LegIK node, continuous bend history and same CDO for each provider."""
import hashlib
import json
import math
import os
import struct
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
repo = Path(__file__).resolve().parents[2]
prefix = 'leg_ik_v1'
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
        assert json.loads(p.read_bytes()) == data, 'Immutable LegIK fixture differs: ' + kind
    else:
        p.write_text(json.dumps(data, separators=(',', ':')), encoding='utf-8')
    return sha(p)

protect()
runtime_cvars = {name: unreal.SystemLibrary.get_console_variable_int_value(name) for name in
    ('a.AnimNode.LegIK.Enable', 'a.AnimNode.LegIK.EnableTwoBone', 'a.AnimNode.LegIK.ForceAlwaysSolve')}
assert list(runtime_cvars.values()) == [1, 1, 0], runtime_cvars
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
profiles = {}
classes = []
for profile in ('unarmed', 'pistol', 'rifle'):
    cls = graph['classes'][profile]
    nodes = cls['graphs']['FullBody_SkeletalControls']['nodes']
    node = next(n for n in nodes if n['index'] == 107)
    assert node['type'] == '/Script/AnimGraphRuntime.AnimNode_LegIK'
    settings = node['settings']
    assert settings['softPercentLength'] == settings['softAlpha'] == 1
    assert settings['maxIterations'] == 12 and settings['reachPrecision'] == f32(.01)
    assert settings['alphaInputType'] == 'Curve' and settings['alphaCurveName'] == 'DisableLegIK'
    assert [(l['fKFootBone']['boneName'], l['iKFootBone']['boneName']) for l in settings['legsDefinition']] == [('foot_l', 'ik_foot_l'), ('foot_r', 'ik_foot_r')]
    for leg in settings['legsDefinition']:
        assert leg['numBonesInLimb'] == 2 and not leg['bEnableRotationLimit'] and leg['bEnableKneeTwistCorrection']
        assert leg['footBoneForwardAxis'] == 'Y' and leg['hingeRotationAxis'] == 'Z' and leg['twistOffsetCurveName'] == 'None'
    profiles[profile] = dict(classPath=cls['classPath'], node=107, settings=settings)
    classes.append(unreal.load_class(None, cls['classPath']))
requests = dict(sequencePaths=paths, traces=[])
alphas = [0, f32(1e-5), f32(1.00001e-5), .25, .5, f32(.99998), f32(.999995), 1]
for provider, profile in enumerate(profiles):
    for hz in (30, 60, 120):
        frames = []
        for i in range(hz * 6):
            t = i / hz
            asset = (i // 31) % len(paths)
            time = f32((t * .73) % lengths[asset])
            old = f32(max(0, time - 1 / hz))
            frames.append(dict(delta=f32(1 / hz), asset=asset, time=time, previous=old, sourceDelta=f32(time-old),
                mode=(i // max(1, hz // 3)) % 6, offset=[math.sin(t * 2.91) * 25, math.cos(t * 3.31) * 18, math.sin(t * 1.13) * 22],
                rotate=i % 5 != 2, angle=math.sin(t * 1.9) * 1.7, straight=i % 19 < 3,
                alpha=alphas[(i // 7) % len(alphas)], recache=i == 0 or i % 173 == 172))
        requests['traces'].append(dict(profile=profile, provider=provider, hz=hz, frames=frames))
native = json.loads(unreal.AlsLyraLegIKLibrary.read_trace(skeleton, classes, sequences, json.dumps(requests, separators=(',', ':'))))
(repo / 'artifacts/lyra-analysis/leg-ik-native-diagnostic.json').write_text(json.dumps(native, separators=(',', ':')), encoding='utf-8')
counts = dict(frames=0, poses=0, disabled=0, partial=0, recache=0, straight=0, changed=0, historyUpdates=0)
for trace, req in zip(native['traces'], requests['traces'], strict=True):
    assert (trace['profile'], trace['hz']) == (req['profile'], req['hz'])
    assert len(trace['frames']) == len(req['frames'])
    for row, frame in zip(trace['frames'], req['frames'], strict=True):
        assert len(row['input']['pose']) == len(row['output']['pose']) == 81
        assert row['input']['curves'] == row['output']['curves'] and row['input']['attributes'] == row['output']['attributes']
        assert row['input'].get('rootMotion') == row['output'].get('rootMotion')
        assert row['changedBones'] in (0, 3, 6)
        counts['frames'] += 1
        counts['poses'] += 1
        counts['disabled'] += frame['alpha'] <= f32(1e-5)
        counts['partial'] += f32(1e-5) < frame['alpha'] < f32(1 - f32(1e-5))
        counts['recache'] += frame['recache']
        counts['straight'] += frame['straight']
        counts['changed'] += row['changedBones'] > 0
        counts['historyUpdates'] += row['history'] != row['historyBefore']
assert counts['frames'] == 3780 and counts['changed'] > 1000 and counts['historyUpdates'] > 100
protect()
source = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
names = ('Private/AlsLyraLegIKLibrary.cpp', 'Public/AlsLyraLegIKLibrary.h', 'Private/AlsLyraControlRigLibrary.cpp', 'Private/AlsLyraCyclePoseLibrary.cpp')
source_sha = {p: sha(source / p) for p in names}
for tree in ('source', 'package'):
    mirror = repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter'
    for p, digest in source_sha.items():
        assert sha(mirror / p) == digest, (tree, p)
request_sha = save('requests', requests)
policy_sha = save('policy', dict(schemaVersion=1, stage='OriginalLegIK', skeleton='ALS81', dependencies=deps,
    profiles=profiles, runtimeCVars=runtime_cvars, defaultTwoBoneSolver=True, forceAlwaysSolve=False, twistCurveAbsent=True))
native.update(schemaVersion=1, dependencies=deps, requestSha256=request_sha, policySha256=policy_sha,
    assetSha256=packages, previousFixtureSha256=previous, probeSourceSha256=source_sha, counts=counts,
    scope='Original LegIK CDO on ALS81 continuous controlled source/goal poses, same operator and retained bend history; not complete SkeletalControls, FootPlacement, Main or production.')
save('native', native)
unreal.log('LYRA_LEG_IK_NATIVE_OK frames=3780 changed=' + str(counts['changed']) + ' previous=' + str(len(previous)) + ' assets_saved=0')
