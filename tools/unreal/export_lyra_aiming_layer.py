"""Actual ALS81 FullBody_Aiming, original cached input, two sources and provider weights."""
import hashlib
import json
import math
import os
import struct
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
repo = Path(__file__).resolve().parents[2]
prefix = 'aiming_layer_v1'
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
        assert json.loads(p.read_bytes()) == data, 'Immutable Aiming fixture differs: ' + kind
    else:
        p.write_text(json.dumps(data, separators=(',', ':')), encoding='utf-8')
    return sha(p)

protect()
deps = {p: sha(root / p) for p in ('main_layer_graph_v1.json', 'linked_layer_contracts.json',
    'logical_controls/catalog.json', 'logical_controls/calibration.json', 'logical_controls/curve_bank.json', 'aim_weight_v1_policy.json')}
cal = json.loads((root / 'logical_controls/calibration.json').read_bytes())['calibration']
catalog = json.loads((root / 'logical_controls/catalog.json').read_bytes())['entries']
quat = unreal.Quat(*cal['handBasis']['rotation'])
skeleton = unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(unreal.load_asset(cal['sourceMesh']).get_editor_property('skeleton'),
    unreal.load_asset(cal['targetMesh']).get_editor_property('skeleton'), quat)
aim_sequences = []
policies = []
spaces = []
for profile in ('unarmed', 'pistol', 'rifle'):
    fields = graph['classes'][profile]['defaults']['fields']
    path = fields['IdleAimOffset']['value']
    space = unreal.load_asset(path)
    spaces.append(path)
    rows = sorted([r for r in catalog if r['slot'].startswith('aim_' + profile + '_')], key=lambda r: r['sampleIndex'])
    assert [r['sampleIndex'] for r in rows] == list(range(15))
    center = next(r for r in rows if r['point'][:2] == [0, 0])
    extended = unreal.AlsLyraControlRigLibrary.create_weapon_sequence(unreal.load_asset(center['source']), unreal.load_asset(center['target']), skeleton, quat, None)
    sequences = [extended if r is center else unreal.AlsLyraControlRigLibrary.create_weapon_sequence(
        unreal.load_asset(r['source']), unreal.load_asset(r['target']), skeleton, quat, extended) for r in rows]
    samples = []
    for r, sequence, source in zip(rows, sequences, space.get_editor_property('sample_data'), strict=True):
        assert source.get_editor_property('animation').get_path_name() == r['source']
        unreal.AlsSourceAnimationLibrary.finish_source_compression(sequence)
        sync = json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(sequence))
        samples.append(dict(slot=r['slot'], source=r['source'], target=r['target'], point=r['point'], sync=sync,
            rate=source.get_editor_property('rate_scale'), singleFrame=source.get_editor_property('use_single_frame_for_blending'),
            frameIndex=source.get_editor_property('frame_index_to_sample'), mirror=source.get_editor_property('mirror')))
    aim_sequences.extend(sequences)
    policies.append(dict(profile=profile, path=path, samples=samples,
        axes=[dict(min=p.get_editor_property('min'), max=p.get_editor_property('max'), wrap=p.get_editor_property('wrap_input')) for p in space.get_editor_property('blend_parameters')],
        filters=[dict(time=p.get_editor_property('interpolation_time'), type=str(p.get_editor_property('interpolation_type')), damping=p.get_editor_property('damping_ratio'), maxSpeed=p.get_editor_property('max_speed')) for p in space.get_editor_property('interpolation_param')],
        weightSpeed=space.get_editor_property('target_weight_interpolation_speed_per_sec'), ease=space.get_editor_property('target_weight_interpolation_ease_in_out'),
        useGrid=space.get_editor_property('interpolate_using_grid'), axisToScale=str(space.get_editor_property('axis_to_scale_animation')),
        legacyLength=space.get_editor_property('use_legacy_sample_point_animation_length_calculations'), matchSyncPhases=space.get_editor_property('should_match_sync_phases'),
        meshBlend=space.get_editor_property('allow_mesh_space_blending'), notifyMode=str(space.get_editor_property('notify_trigger_mode')),
        perBoneOverrides=len(space.get_editor_property('manual_per_bone_overrides')),
        grid=json.loads(unreal.AlsSourceAnimationLibrary.read_blend_space_triangulation_reference(space, '{"inputs":[[0,0]]}'))['data']))
diagnostic = repo / 'artifacts/lyra-analysis/aiming-layer-policy-diagnostic.json'
diagnostic.write_text(json.dumps(policies, separators=(',', ':')), encoding='utf-8')
base_paths = json.loads((root / 'left_hand_layer_v4_requests.json').read_bytes())['sequencePaths']
base_sequences = []
lengths = []
for path in base_paths:
    r = next(r for r in catalog if r['target'] == path)
    sequence = unreal.AlsLyraControlRigLibrary.create_weapon_sequence(unreal.load_asset(r['source']), unreal.load_asset(path), skeleton, quat, None)
    unreal.AlsSourceAnimationLibrary.finish_source_compression(sequence)
    base_sequences.append(sequence)
    lengths.append(r['playLength'])
requests = dict(spaces=spaces, sequencePaths=base_paths, traces=[])
for profile, n in zip(('unarmed', 'pistol', 'rifle'), range(3), strict=True):
    for hz in (30, 60, 120):
        frames = []
        for i in range(hz * 12):
            t = i / hz
            phase = int(t) % 9
            asset = (i // 31) % len(base_paths)
            time = f32((t * .77) % lengths[asset])
            old = f32(max(0, time - 1 / hz))
            frames.append(dict(delta=f32(0 if i % 101 == 100 else 1 / hz),
                main=dict(IsCrouching=phase in (1, 3, 7), IsOnGround=phase not in (3, 4), GameplayTag_IsADS=phase in (2, 3, 4),
                    TimeSinceFiredWeapon=[.49999999999999994, .5, .5000000000000001, 99][(i // 17) % 4] if phase == 5 else 99,
                    RootYawOffset=[0, 9.999999999999998, 10, -10, 90][(i // 13) % 5], HasAcceleration=phase in (0, 6, 7)),
                AimYaw=math.sin(t * 2.91) * 210 if i % 23 else [-180, -90, 0, 90, 180][(i // 23) % 5],
                AimPitch=math.cos(t * 3.31) * 110 if i % 29 else [-90, 0, 90][(i // 29) % 3],
                visited=not (.8 <= t < 1.1 or 4.7 <= t < 4.9 or 9 <= t < 9.25), evaluate=i % 7 != 3,
                initialize=i == 0 or i % 173 == 172, active=i % 41 < 35,
                weight=f32([1, .5, .001, .8][(i // 19) % 4]),
                asset=asset, time=time, previous=old, sourceDelta=f32(time-old), flags=[0, 1, 2, 3][i % 4],
                finalFeedback={'applyHipfireOverridePose': f32([0, .125, -1, 1e-7][(i // 19) % 4])} if phase == 8 else {}))
        requests['traces'].append(dict(profile=profile, hz=hz, space=n, **{'class': graph['classes'][profile]['classPath']}, frames=frames))
native = json.loads(unreal.AlsLyraAimingLibrary.read_trace(unreal.load_class(None, graph['classes']['main']['classPath']),
    unreal.load_asset(cal['sourceMesh']), skeleton, aim_sequences, base_sequences, json.dumps(requests, separators=(',', ':'))))
(repo / 'artifacts/lyra-analysis/aiming-layer-native-diagnostic.json').write_text(json.dumps(native, separators=(',', ':')), encoding='utf-8')
counts = dict(frames=0, poses=0, hidden=0, updateOnly=0, mixed=0, twoSources=0)
for trace, req in zip(native['traces'], requests['traces'], strict=True):
    assert (trace['profile'], trace['hz']) == (req['profile'], req['hz'])
    for row, frame in zip(trace['frames'], req['frames'], strict=True):
        assert row['inputUpdates'] == int(frame['visited']), (trace['profile'], trace['hz'], row)
        assert ('output' in row) == (frame['visited'] and frame['evaluate'])
        if 'output' in row:
            assert row['inputEvaluations'] == 1 and len(row['output']['pose']) == 81
        counts['frames'] += 1
        counts['poses'] += 'output' in row
        counts['hidden'] += not frame['visited']
        counts['updateOnly'] += frame['visited'] and not frame['evaluate']
        counts['mixed'] += frame['visited'] and 1e-5 < row['blendPin'] < 1 - 1e-5
        counts['twoSources'] += frame['visited'] and row['a']['weight'] > 1e-5 and row['b']['weight'] > 1e-5
assert counts['frames'] == 7560 and counts['poses'] > 5000 and counts['mixed'] > 0
protect()
source = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
names = ('Private/AlsLyraAimingLibrary.cpp', 'Public/AlsLyraAimingLibrary.h', 'Private/AlsLyraAimWeightLibrary.cpp', 'Private/AlsLyraCycleLibrary.cpp', 'Private/AlsLyraCyclePoseLibrary.cpp', 'Private/AlsLyraControlRigLibrary.cpp')
source_sha = {p: sha(source / p) for p in names}
for tree in ('source', 'package'):
    mirror = repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter'
    for p, digest in source_sha.items():
        assert sha(mirror / p) == digest, (tree, p)
request_sha = save('requests', requests)
policy_sha = save('policy', dict(schemaVersion=1, stage='OriginalFullBody_Aiming', skeleton='ALS81', dependencies=deps, spaces=policies))
native.update(schemaVersion=1, dependencies=deps, requestSha256=request_sha, policySha256=policy_sha,
    assetSha256=packages, previousFixtureSha256=previous, probeSourceSha256=source_sha, counts=counts,
    scope='Actual original linked FullBody_Aiming global weights, cache traversal, sources, common Sync and pose on ALS81; controlled input and Main fields; not complete Main or production.')
save('native', native)
unreal.log('LYRA_AIMING_LAYER_NATIVE_OK frames=7560 poses=' + str(counts['poses']) + ' mixed=' + str(counts['mixed']) + ' previous=' + str(len(previous)) + ' assets_saved=0')
