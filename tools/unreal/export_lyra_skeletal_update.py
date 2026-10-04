"""Capture original SkeletalControls updates and isolated Root/Weapon operators on ALS81."""
import hashlib
import json
import math
import os
import struct
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
repo = Path(__file__).resolve().parents[2]
prefix = 'skeletal_update_v1'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
f32 = lambda v: struct.unpack('<f', struct.pack('<f', v))[0]
graph = json.loads((root / 'main_layer_graph_v1.json').read_bytes())
packages = graph['assetSha256']
previous = {p.relative_to(root).as_posix(): sha(p) for p in root.rglob('*.json') if not p.name.startswith(prefix)}
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
        assert json.loads(p.read_bytes()) == data, 'Immutable SkeletalControls fixture differs: ' + kind
    else:
        p.write_text(json.dumps(data, separators=(',', ':')), encoding='utf-8')
    return sha(p)

protect()
deps = {p: sha(root / p) for p in ('main_layer_graph_v1.json', 'pose_layer_contracts.json',
    'logical_controls/catalog.json', 'logical_controls/calibration.json', 'logical_controls/curve_bank.json', 'skeletal_control_defaults.json')}
cal = json.loads((root / 'logical_controls/calibration.json').read_bytes())['calibration']
catalog = json.loads((root / 'logical_controls/catalog.json').read_bytes())['entries']
quat = unreal.Quat(*cal['handBasis']['rotation'])
skeleton = unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(unreal.load_asset(cal['sourceMesh']).get_editor_property('skeleton'),
    unreal.load_asset(cal['targetMesh']).get_editor_property('skeleton'), quat)
paths = json.loads((root / 'left_hand_layer_v4_requests.json').read_bytes())['sequencePaths']
sequences, lengths = [], []
for path in paths:
    r = next(r for r in catalog if r['target'] == path)
    s = unreal.AlsLyraControlRigLibrary.create_weapon_sequence(unreal.load_asset(r['source']), unreal.load_asset(path), skeleton, quat, None)
    unreal.AlsSourceAnimationLibrary.finish_source_compression(s)
    sequences.append(s)
    lengths.append(r['playLength'])
requests = dict(sequencePaths=paths, traces=[])
policies = {}
curve_names = ['DisableRHandIK', 'DisableLHandIK', 'DisableHandIKRetargeting', 'DisableLegIK', 'ScaleDownWeaponR']
values = [-.5, 0, 1e-7, .125, .5, 1, 1.5]
for profile in ('unarmed', 'pistol', 'rifle'):
    cls = graph['classes'][profile]
    policies[profile] = dict(disableHandIK=cls['defaults']['fields']['DisableHandIK']['value'],
        settings=cls['graphs']['FullBody_SkeletalControls'], classPath=cls['classPath'])
    assert len(policies[profile]['settings']['nodes']) == 12
    for hz in (30, 60, 120):
        frames = []
        for i in range(hz * 6):
            t = i / hz
            asset = (i // 31) % len(paths)
            time = f32((t * .73) % lengths[asset])
            old = f32(max(0, time - 1 / hz))
            frames.append(dict(delta=f32(0 if i % 101 == 100 else (.35 if i % 307 == 306 else 1 / hz)),
                main=dict(EnableControlRig=(i // max(1, hz // 5)) % 3 != 0,
                    UseFootPlacement=(i // max(1, hz // 3)) % 3 != 0),
                asset=asset, time=time, previous=old, sourceDelta=f32(time-old),
                initialize=i == 0 or i % 173 == 172 or .94 < t < .94 + 1 / hz,
                visited=not (.8 <= t < 1.1 or 3.4 <= t < 3.65),
                evaluate=not (1.7 <= t < 2.05 or 4.4 <= t < 4.65),
                evaluateMain=i % 7 != 3,
                finalFeedback={name: f32(values[(i // 11 + j * 2) % len(values)]) for j, name in enumerate(curve_names)} if i % 29 != 28 else {}))
        requests['traces'].append(dict(profile=profile, hz=hz, **{'class': cls['classPath']}, frames=frames))
native = json.loads(unreal.AlsLyraSkeletalUpdateLibrary.read_trace(
    unreal.load_class(None, graph['classes']['main']['classPath']), unreal.load_asset(cal['sourceMesh']), skeleton,
    sequences, json.dumps(requests, separators=(',', ':'))))
(repo / 'artifacts/lyra-analysis/skeletal-update-native-diagnostic.json').write_text(json.dumps(native, separators=(',', ':')), encoding='utf-8')
counts = dict(frames=0, poses=0, hidden=0, updateOnly=0, initialize=0, rootPartial=0, footPartial=0, footEvaluations=0, footAccumulated=0)
for trace, req in zip(native['traces'], requests['traces'], strict=True):
    assert (trace['profile'], trace['hz']) == (req['profile'], req['hz'])
    assert len(trace['frames']) == len(req['frames'])
    feedback = dict.fromkeys(curve_names, 0)
    for row, f in zip(trace['frames'], req['frames'], strict=True):
        assert row['feedbackBefore'] == feedback
        assert row['inputUpdates'] == int(f['visited'])
        assert ('output' in row) == (f['visited'] and f['evaluate'])
        if 'output' in row:
            for name in ('input', 'output', 'rootOperator', 'weaponOperator'):
                assert len(row[name]['pose']) == 81
                assert row[name]['curves'] == row['input']['curves'] and row[name]['attributes'] == row['input']['attributes']
                assert row[name].get('rootMotion') == row['input'].get('rootMotion')
            counts['poses'] += 1
            counts['footEvaluations'] += row['updated']['alphas'][5] > f32(1e-5)
        counts['frames'] += 1
        counts['hidden'] += not f['visited']
        counts['updateOnly'] += f['visited'] and not f['evaluate']
        counts['initialize'] += f['initialize']
        counts['rootPartial'] += 0 < row['updated']['alphas'][2] < 1
        counts['footPartial'] += 0 < row['updated']['alphas'][5] < 1
        counts['footAccumulated'] += row['updated']['footDelta'] > f['delta']
        if f['evaluateMain']:
            feedback = {n: f['finalFeedback'].get(n, 0) for n in curve_names}
assert counts['frames'] == 3780 and min(counts['rootPartial'], counts['footPartial'], counts['footEvaluations'], counts['footAccumulated']) > 0, counts
protect()
source = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
names = ('Private/AlsLyraSkeletalUpdateLibrary.cpp', 'Public/AlsLyraSkeletalUpdateLibrary.h',
    'Private/AlsLyraControlRigLibrary.cpp', 'Private/AlsLyraCyclePoseLibrary.cpp', 'Private/AlsLyraCycleLibrary.cpp')
source_sha = {p: sha(source / p) for p in names}
for tree in ('source', 'package'):
    mirror = repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter'
    for p, digest in source_sha.items():
        assert sha(mirror / p) == digest, (tree, p)
request_sha = save('requests', requests)
policy_sha = save('policy', dict(schemaVersion=1, stage='OriginalSkeletalControlUpdate', dependencies=deps,
    profiles=policies, curveNames=curve_names, alphaNodes=[103,102,104,110,109,105,107,106],
    boolBlendTime=f32(.2), footPlane='Fallback component ground; no collision terrain', fullFootSolverPorted=False))
native.update(schemaVersion=1, dependencies=deps, requestSha256=request_sha, policySha256=policy_sha,
    assetSha256=packages, previousFixtureSha256=previous, probeSourceSha256=source_sha, counts=counts,
    scope='Original global UpdateSkelControlData, compiled twelve-node graph update and Root/Weapon isolated operators. Full original pose including enabled FootPlacement captured on fallback ground for later solver port; not full Main or production.')
save('native', native)
unreal.log('LYRA_SKELETAL_UPDATE_NATIVE_OK frames=3780 poses=' + str(counts['poses']) + ' previous=' + str(len(previous)) + ' assets_saved=0')
