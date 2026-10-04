"""Capture provider pre-graph weights and actual exposed FullBody_Aiming pins."""
import hashlib
import json
import math
import os
import struct
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
repo = Path(__file__).resolve().parents[2]
prefix = 'aim_weight_v1'
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
        assert json.loads(p.read_bytes()) == data, 'Immutable Aiming weight fixture differs: ' + kind
    else:
        p.write_text(json.dumps(data, separators=(',', ':')), encoding='utf-8')
    return sha(p)

protect()
deps = {name: sha(root / name) for name in ('main_layer_graph_v1.json', 'linked_layer_contracts.json', 'logical_controls/calibration.json')}
calibration = json.loads((root / 'logical_controls/calibration.json').read_bytes())['calibration']
requests = dict(traces=[])
for profile in ('unarmed', 'pistol', 'rifle'):
    for hz in (30, 60, 120):
        frames = []
        for i in range(hz * 18):
            t = i / hz
            phase = int(t) % 9
            yaw = [0, math.nextafter(10, 0), 10, math.nextafter(10, math.inf),
                   -10, math.nextafter(-10, 0), 90][(i // 13) % 7]
            fire = [math.nextafter(.5, 0), .5, math.nextafter(.5, math.inf), .01, 99][(i // 17) % 5]
            frames.append(dict(delta=f32(0 if i % 101 == 100 else (1 if i % 307 == 306 else 1 / hz)),
                main=dict(IsCrouching=phase in (1, 3, 7), IsOnGround=phase not in (3, 4),
                    GameplayTag_IsADS=phase in (2, 3, 4), TimeSinceFiredWeapon=fire if phase == 5 else 99,
                    RootYawOffset=yaw, HasAcceleration=phase in (0, 6, 7)),
                AimYaw=(math.sin(t * 3.17) * 179.123456789), AimPitch=(math.cos(t * 2.23) * 89.765432101),
                visited=not (.8 <= t < 1.1 or 4.7 <= t < 4.9 or 9 <= t < 9.25),
                evaluateMain=i % 7 != 3,
                finalFeedback={'applyHipfireOverridePose': f32([0, .125, -1, 1e-7][(i // 19) % 4])} if phase == 8 else {}))
        requests['traces'].append(dict(profile=profile, hz=hz, **{'class': graph['classes'][profile]['classPath']}, frames=frames))
native = json.loads(unreal.AlsLyraAimWeightLibrary.read_trace(
    unreal.load_class(None, graph['classes']['main']['classPath']),
    unreal.load_asset(calibration['sourceMesh']), json.dumps(requests, separators=(',', ':'))))
counts = dict(frames=0, pins=0, hidden=0, retainedFeedback=0, zeroDelta=0, clampedDelta=0, mixed=0)
policies = {}
for profile in ('unarmed', 'pistol', 'rifle'):
    fields = graph['classes'][profile]['defaults']['fields']
    policies[profile] = {k: fields[k] for k in ('RaiseWeaponAfterFiringWhenCrouched', 'RaiseWeaponAfterFiringDuration',
        'HipFireUpperBodyOverrideWeight', 'AimOffsetBlendWeight', 'IdleAimOffset', 'RelaxedAimOffset')}
for trace, req in zip(native['traces'], requests['traces'], strict=True):
    assert trace['profile'] == req['profile'] and trace['hz'] == req['hz']
    feedback = 0
    for row, frame in zip(trace['frames'], req['frames'], strict=True):
        assert row['feedbackBefore'] == feedback
        assert ('pins' in row) == frame['visited']
        if frame['visited']:
            assert row['blendPin'] == f32(row['after']['aim'])
            for pins in row['pins'].values():
                assert pins['x'] == f32(frame['AimYaw']) and pins['y'] == f32(frame['AimPitch'])
                assert pins['alpha'] == 1
        if frame['evaluateMain']:
            feedback = frame['finalFeedback'].get('applyHipfireOverridePose', 0)
        assert row['feedbackAfter'] == feedback
        counts['frames'] += 1
        counts['pins'] += frame['visited']
        counts['hidden'] += not frame['visited']
        counts['retainedFeedback'] += not frame['evaluateMain']
        counts['zeroDelta'] += frame['delta'] == 0
        counts['clampedDelta'] += frame['delta'] == 1
        counts['mixed'] += 1e-5 < row['after']['aim'] < 1 - 1e-5
assert counts['frames'] == 11340 and counts['mixed'] > 0 and counts['hidden'] > 0, counts
protect()
source = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
names = ('Private/AlsLyraAimWeightLibrary.cpp', 'Public/AlsLyraAimWeightLibrary.h', 'Private/AlsLyraCycleLibrary.cpp')
source_sha = {p: sha(source / p) for p in names}
for tree in ('source', 'package'):
    mirror = repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter'
    for p, digest in source_sha.items():
        assert sha(mirror / p) == digest, (tree, p)
request_sha = save('requests', requests)
policy_sha = save('policy', dict(schemaVersion=1, stage='OriginalProviderAimWeightUpdate', dependencies=deps,
    policies=policies, curve='applyHipfireOverridePose', function='Update Blend Weight Data',
    nodes=dict(blend=77, relaxed=79, idle=74), graphEvaluation=False))
native.update(schemaVersion=1, dependencies=deps, requestSha256=request_sha, policySha256=policy_sha,
    assetSha256=packages, previousFixtureSha256=previous, probeSourceSha256=source_sha, counts=counts,
    scope='Original linked provider global weight function and compiled Aiming exposed handlers; no pose, source tick, cache traversal, full Main or production validation.')
save('native', native)
unreal.log('LYRA_AIM_WEIGHT_NATIVE_OK frames=11340 pins=' + str(counts['pins']) + ' mixed=' + str(counts['mixed']) + ' previous=' + str(len(previous)) + ' assets_saved=0')
