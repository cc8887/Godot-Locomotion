"""Capture the original LeftHand layer and Main-to-linked curve-copy timing."""
import hashlib
import json
import os
import struct
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
repo = Path(__file__).resolve().parents[2]
prefix = 'left_hand_layer_v4'
sha = lambda b: hashlib.sha256(b).hexdigest()
f32 = lambda v: struct.unpack('<f', struct.pack('<f', v))[0]
graph = json.loads((root / 'main_layer_graph_v1.json').read_bytes())
packages = graph['assetSha256']
previous = {str(p.relative_to(root)).replace('\\', '/'): sha(p.read_bytes()) for p in root.rglob('*.json') if not p.name.startswith(prefix)}
existing = root / (prefix + '_native.json')
if existing.exists():
    previous = json.loads(existing.read_bytes())['previousFixtureSha256']
content = Path(unreal.Paths.project_content_dir())

def protect():
    for p, digest in packages.items():
        assert sha((content / (p.split('.')[0].removeprefix('/Game/') + '.uasset')).read_bytes()) == digest, p
    for p, digest in previous.items():
        assert sha((root / p).read_bytes()) == digest, p

def save(suffix, value):
    p = root / (prefix + '_' + suffix + '.json')
    if p.exists():
        assert json.loads(p.read_bytes()) == value, 'Immutable LeftHand fixture differs: ' + suffix
    else:
        p.write_text(json.dumps(value, separators=(',', ':')), encoding='utf-8')
    return sha(p.read_bytes())

protect()
names = ('main_layer_graph_v1.json', 'logical_controls/calibration.json', 'logical_controls/catalog.json',
         'locomotion_extras/catalog.json', 'locomotion_resources.json', 'pose_layer_contracts.json', 'cycle_layer_pose_policy.json')
files = {p: (root / p).read_bytes() for p in names}
resources = json.loads(files['locomotion_resources.json'])
entries = json.loads(files['logical_controls/catalog.json'])['entries'] + json.loads(files['locomotion_extras/catalog.json'])['entries']
lookup = {e['target']: e for e in entries}
paths = list(dict.fromkeys(resources['providers'][p]['start'][kind]['forward']
    for p in ('unarmed', 'pistol', 'rifle') for kind in ('Jog_Start_Cardinals', 'ADS_Start_Cardinals')))
basis = json.loads(files['logical_controls/calibration.json'])['calibration']
rotation = unreal.Quat(*basis['handBasis']['rotation'])
skeleton = unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(
    unreal.load_asset(basis['sourceMesh']).get_editor_property('skeleton'),
    unreal.load_asset(basis['targetMesh']).get_editor_property('skeleton'), rotation)
sequences = [unreal.AlsLyraControlRigLibrary.create_weapon_sequence(
    unreal.load_asset(lookup[p]['source']), unreal.load_asset(p), skeleton, rotation, None) for p in paths]
assert all(sequences)
# GetAnimationPose's root provider reads compressed tracks. Complete transient
# compression and validate the exact established codec data before evaluating.
for path, sequence in zip(paths, sequences, strict=True):
    assert json.loads(unreal.AlsLyraGraphLibrary.read_root_compression_data(sequence)) == resources['compressedRoots']['assets'][path], path
requests = dict(sequencePaths=paths, traces=[])
feedback_values = [None, -1.2, -1, -.500001, -.5, -.15, -1.00001e-5, -1e-5, -1e-6, 0, .5, 1, 1.2]
for profile in ('unarmed', 'pistol', 'rifle'):
    for hz in (30, 60, 120):
        provider = resources['providers'][profile]
        frames = []
        for i in range(hz * 6):
            t = i / hz
            current = f32((i % 29) / hz)
            source_delta = f32(1 / hz)
            previous_time = f32(max(0, current - source_delta))
            value = feedback_values[(i // 7) % len(feedback_values)]
            path = provider['start']['Jog_Start_Cardinals' if (i // 29) % 2 == 0 else 'ADS_Start_Cardinals']['forward']
            frames.append(dict(delta=f32(1 / hz), asset=paths.index(path), time=current,
                previous=previous_time, sourceDelta=source_delta, flags=i % 4,
                active=not (.8 <= t < 1.1), evaluate=i % 7 != 3,
                initialize=i == 0 or i % 83 == 0, weight=f32([1, .5, 1e-5, .001, .8][(i // 19) % 5]),
                finalFeedback={} if value is None else {'DisableLeftHandPoseOverride': f32(value)}))
        requests['traces'].append(dict(profile=profile, hz=hz, **{'class': graph['classes'][profile]['classPath']}, frames=frames))
text = unreal.AlsLyraLeftHandLayerLibrary.read_trace(unreal.load_class(None, graph['classes']['main']['classPath']),
    unreal.load_asset(basis['sourceMesh']), skeleton, sequences, json.dumps(requests, separators=(',', ':')))
assert text, 'Empty original LeftHand layer trace'
native = json.loads(text)
counts = dict(frames=0, poses=0, hidden=0, updateOnly=0, applied=0, feedbackChanges=0)
policies = {}
for actual, trace in zip(native['traces'], requests['traces'], strict=True):
    assert len(actual['frames']) == len(trace['frames'])
    policy = dict(mask=actual['mask'], curveBindings=actual['curveBindings'])
    assert len(policy['mask']) == 81 and policy['mask'][0] == 0
    if trace['profile'] in policies:
        assert policies[trace['profile']] == policy
    policies[trace['profile']] = policy
    for row, frame in zip(actual['frames'], trace['frames'], strict=True):
        assert row['childAssetNull'] and row['inputUpdates'] == int(frame['active'])
        assert ('output' in row) == (frame['active'] and frame['evaluate'])
        if 'output' in row:
            assert len(row['output']['pose']) == 81 and len(row['input']['pose']) == 81
        counts['frames'] += 1
        counts['poses'] += 'output' in row
        counts['hidden'] += not frame['active']
        counts['updateOnly'] += frame['active'] and not frame['evaluate']
        counts['applied'] += row['blendWeight'] > 1e-5 and 'output' in row
        counts['feedbackChanges'] += row['feedbackBefore'] != row['feedbackAfter']
assert counts['frames'] == 3780 and counts['applied'] > 0 and counts['feedbackChanges'] > 0, counts
protect()
source_root = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
probe_names = ('Private/AlsLyraLeftHandLayerLibrary.cpp', 'Public/AlsLyraLeftHandLayerLibrary.h',
               'Private/AlsLyraCyclePoseLibrary.cpp', 'Private/AlsLyraPoseProbe.h')
source_sha = {p: sha((source_root / p).read_bytes()) for p in probe_names}
for tree in ('source', 'package'):
    compiled = repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter'
    for p, digest in source_sha.items():
        assert sha((compiled / p).read_bytes()) == digest, (tree, p)
dependencies = {p: sha(b) for p, b in files.items()}
request_sha = save('requests', requests)
policy_sha = save('policy', dict(schemaVersion=1, dependencies=dependencies, policies=policies,
    stage='OriginalLeftHandLayer', skeleton='ALS81', nullSequence=True))
native.update(schemaVersion=1, dependencies=dependencies, requestSha256=request_sha, policySha256=policy_sha,
    assetSha256=packages, previousFixtureSha256=previous, probeSourceSha256=source_sha, counts=counts,
    scope='Original four-node layer Update/Evaluate and real Main curve copy on ALS81. Controlled source leaves and enclosing feedback. Fixed providers; not complete Main or production.')
save('native', native)
unreal.log('LYRA_LEFT_HAND_LAYER_NATIVE_OK frames=3780 poses=' + str(counts['poses']) + ' applied=' + str(counts['applied']) + ' previous=' + str(len(previous)) + ' assets_saved=0')
