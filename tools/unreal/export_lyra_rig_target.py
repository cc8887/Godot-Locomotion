"""Capture actual ALS-reference Main73 VM and full outputs, without pose observers."""
import hashlib
import json
import os
from pathlib import Path
import unreal

repo = Path(__file__).resolve().parents[2]
root = Path(os.environ['LYRA_OUTPUT_ROOT'])
prefix = 'rig_target_v1'
load = lambda name: json.loads((root / name).read_bytes())


def sha(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        while block := stream.read(1024 * 1024):
            digest.update(block)
    return digest.hexdigest()


previous = {str(path.relative_to(root)).replace('\\', '/'): sha(path)
            for path in root.rglob('*.json') if not path.name.startswith(prefix)}
destination = root / (prefix + '_native.json')
if destination.exists():
    previous = load(destination.name)['previousFixtureSha256']
packages = load('rig_reference_v1_native.json')['assetSha256']
content = Path(unreal.Paths.project_content_dir())
dependencies = {name: sha(root / name) for name in (
    'rig_traversal_v1_program.json', 'footplant_rig_ground_v2_requests.json',
    'rig_reference_v1_policy.json', 'rig_reference_v1_native.json',
    'logical_controls/calibration.json', 'logical_controls/catalog.json')}


def protect():
    for name, digest in previous.items():
        assert sha(root / name) == digest, name
    for path, digest in packages.items():
        assert sha(content / (path.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, path


def compare(a, b, forward, reverse, path=''):
    assert type(a) is type(b), path
    if isinstance(a, dict):
        assert a.keys() == b.keys(), path
        cached = a.keys() == {'Key', 'Index', 'ContainerVersion'}
        for key in a:
            if cached and key == 'ContainerVersion':
                x, y = a[key], b[key]
                assert (x == -1) == (y == -1), path
                if x != -1:
                    assert forward.setdefault(x, y) == y and reverse.setdefault(y, x) == x, path
            else:
                compare(a[key], b[key], forward, reverse, path + '/' + key)
    elif isinstance(a, list):
        assert len(a) == len(b), path
        for i, (x, y) in enumerate(zip(a, b, strict=True)):
            compare(x, y, forward, reverse, path + '/' + str(i))
    else:
        assert a == b, (path, a, b)


protect()
assert unreal.AlsLyraControlRigLibrary.prefer_raw_track_data_model(True)
calibration = load('logical_controls/calibration.json')
cal = calibration['calibration']
source = unreal.load_asset(cal['sourceMesh'])
target = unreal.load_asset(cal['targetMesh'])
hand = unreal.Quat(*cal['handBasis']['rotation'])
skeleton = unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(
    source.get_editor_property('skeleton'), target.get_editor_property('skeleton'), hand)
requests = load('footplant_rig_ground_v2_requests.json')
catalog = load('logical_controls/catalog.json')['entries']
sequences = []
for path in requests['sequencePaths']:
    row = next(row for row in catalog if row['target'] == path)
    sequence = unreal.AlsLyraControlRigLibrary.create_weapon_sequence(
        unreal.load_asset(row['source']), unreal.load_asset(path), skeleton, hand, None)
    assert sequence
    unreal.AlsSourceAnimationLibrary.finish_source_compression(sequence)
    sequences.append(sequence)
graph = load('footplant_rig_graph_v1.json')
program = json.loads(unreal.AlsLyraRigTargetLibrary.read_program(unreal.load_class(None, graph['rig'] + '_C')))
compare(load('rig_traversal_v1_program.json'), program, {}, {}, 'unchanged authored program')
text = unreal.AlsLyraRigTargetLibrary.read_trace(unreal.load_class(None, graph['main'] + '_C'),
    source, skeleton, sequences, json.dumps(requests, separators=(',', ':')))
assert text
captured = json.loads(text)
del text
counts = dict(frames=0, poses=0, solves=0, visits=0, sweeps=0, partial=0,
              leftHits=0, rightHits=0, construction=0, targetReferenceFrames=0)
for trace, request in zip(captured['traces'], requests['traces'], strict=True):
    assert (trace['mode'], trace['hz']) == (request['mode'], request['hz'])
    assert trace['settings']['bSetRefPoseFromSkeleton'] is True
    assert trace['skeletonNames'] == calibration['layout']['logicalBoneNames']
    assert trace['traceChannelName'] == 'Traversable' and trace['groundResponse'] == 2
    for frame in trace['frames']:
        assert frame['nodeUpdated']['bSetRefPoseFromSkeleton'] is True
        counts['frames'] += 1
        counts['targetReferenceFrames'] += 1
        counts['poses'] += 'output' in frame
        counts['solves'] += 0 in frame['visits']
        counts['construction'] += 401 in frame['visits']
        counts['visits'] += len(frame['visits'])
        counts['partial'] += 1e-5 < frame['alpha'] < .99999
        counts['sweeps'] += sum('SphereTraceByTraceChannel::Execute' in program['instructions'][i]['text'] for i in frame['visits'])
        counts['leftHits'] += frame['after']['variables']['DidLeftFootTraceHit']
        counts['rightHits'] += frame['after']['variables']['DidRightFootTraceHit']
        assert frame['after']['variables']['ThighLength'] == 42.57203674316406
        assert frame['after']['variables']['CalfLength'] == 40.19668960571289
assert counts['frames'] == 2520 and counts['poses'] == 2154 and counts['solves'] > 1800
assert counts['leftHits'] > 200 and counts['rightHits'] > 200
probe_root = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
probe_paths = ['Private/AlsLyraRigTargetLibrary.cpp', 'Public/AlsLyraRigTargetLibrary.h']
captured.update(schemaVersion=1, counts=counts, dependencies=dependencies,
                probeSourceSha256={name: sha(probe_root / name) for name in probe_paths},
                previousFixtureSha256=previous, assetSha256=packages,
                reference='AlsCompactReference', production=False)
if destination.exists():
    original = load(destination.name)
    for a, b in zip(original['traces'], captured['traces'], strict=True):
        compare(a, b, {}, {}, 'independent target trace')
    compare({k: v for k, v in original.items() if k != 'traces'},
            {k: v for k, v in captured.items() if k != 'traces'}, {}, {}, 'manifest')
else:
    destination.write_text(json.dumps(captured, separators=(',', ':'), allow_nan=False), encoding='utf-8')
protect()
unreal.log('LYRA_RIG_TARGET_NATIVE_OK frames=%d poses=%d solves=%d visits=%d sweeps=%d leftHits=%d rightHits=%d protectedPackages=%d protectedJson=%d assets_saved=0' %
           (counts['frames'], counts['poses'], counts['solves'], counts['visits'], counts['sweeps'], counts['leftHits'], counts['rightHits'], len(packages), len(previous)))
