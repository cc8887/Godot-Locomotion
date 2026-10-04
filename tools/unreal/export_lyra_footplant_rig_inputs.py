"""FootPlant input boundary: original PreForwardsSolve plus native UpdateInput-only history."""
import hashlib
import json
import math
import os
import struct
from pathlib import Path

import unreal


root = Path(os.environ['LYRA_OUTPUT_ROOT'])
repo = Path(__file__).resolve().parents[2]
prefix = 'footplant_rig_inputs_v1'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
load = lambda p: json.loads((root / p).read_bytes())
f32 = lambda v: struct.unpack('<f', struct.pack('<f', v))[0]
graph = load('footplant_rig_graph_v1.json')
packages = dict(graph['assetSha256'])
previous = {str(p.relative_to(root)).replace('\\', '/'): sha(p)
            for p in root.rglob('*.json') if not p.name.startswith(prefix)}
if (root / (prefix + '_native.json')).exists():
    previous = load(prefix + '_native.json')['previousFixtureSha256']
content = Path(unreal.Paths.project_content_dir())


def protect():
    for path, digest in previous.items():
        assert sha(root / path) == digest, path
    for path, digest in packages.items():
        asset = content / (path.split('.')[0].removeprefix('/Game/') + '.uasset')
        assert sha(asset) == digest, path


def save(kind, value):
    p = root / (prefix + '_' + kind + '.json')
    if p.exists():
        original = json.loads(p.read_bytes())
        if kind == 'native':
            # FCachedRigElement.ContainerVersion is GetTopologyVersionHash(),
            # which hashes URigHierarchy's address. Preserve the original bytes
            # and compare this token bijectively within each separate Rig only.
            # Key, Index, invalidity, topology changes and every other value stay exact.
            occurrences = 0
            def compare(a, b, forward, reverse):
                nonlocal occurrences
                assert type(a) is type(b), 'Changed native value type'
                if isinstance(a, dict):
                    assert a.keys() == b.keys(), 'Changed native field layout'
                    cached = a.keys() == {'Key', 'Index', 'ContainerVersion'}
                    for key in a:
                        if cached and key == 'ContainerVersion':
                            x, y = a[key], b[key]
                            assert (x == -1) == (y == -1), 'Changed cache validity'
                            if x != -1:
                                assert forward.setdefault(x, y) == y and reverse.setdefault(y, x) == x, 'Changed topology identity history'
                                occurrences += x != y
                        else:
                            compare(a[key], b[key], forward, reverse)
                elif isinstance(a, list):
                    assert len(a) == len(b), 'Changed native array length'
                    for x, y in zip(a, b, strict=True):
                        compare(x, y, forward, reverse)
                else:
                    assert a == b, 'Immutable FootPlant native value differs'
            assert original.keys() == value.keys()
            for key in original:
                if key == 'traces':
                    for a, b in zip(original[key], value[key], strict=True):
                        compare(a, b, {}, {})
                else:
                    compare(original[key], value[key], {}, {})
            unreal.log('LYRA_FOOTPLANT_CACHE_IDENTITY_COMPARE_OK remappedOccurrences=%d allOtherValuesExact=true originalBytesPreserved=true' % occurrences)
        else:
            assert original == value, 'Immutable FootPlant capture differs: ' + kind
    else:
        p.write_text(json.dumps(value, separators=(',', ':'), allow_nan=False), encoding='utf-8')
    return sha(p)


protect()
assert unreal.AlsLyraControlRigLibrary.prefer_raw_track_data_model(True)
cal = load('logical_controls/calibration.json')['calibration']
source_mesh = unreal.load_asset(cal['sourceMesh'])
target_mesh = unreal.load_asset(cal['targetMesh'])
for asset in (source_mesh, target_mesh, source_mesh.get_editor_property('skeleton'),
              target_mesh.get_editor_property('skeleton'), unreal.load_asset(cal['retargeter'])):
    path = asset.get_path_name()
    packages[path] = sha(content / (path.split('.')[0].removeprefix('/Game/') + '.uasset'))
protect()
hand = unreal.Quat(*cal['handBasis']['rotation'])
skeleton = unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(
    source_mesh.get_editor_property('skeleton'), target_mesh.get_editor_property('skeleton'), hand)
paths = load('foot_placement_v1_requests.json')['sequencePaths']
catalog = load('logical_controls/catalog.json')['entries']
sequences, lengths = [], []
for path in paths:
    row = next(r for r in catalog if r['target'] == path)
    sequence = unreal.AlsLyraControlRigLibrary.create_weapon_sequence(
        unreal.load_asset(row['source']), unreal.load_asset(path), skeleton, hand, None)
    assert sequence is not None
    unreal.AlsSourceAnimationLibrary.finish_source_compression(sequence)
    sequences.append(sequence)
    lengths.append(row['playLength'])
rig_class = unreal.load_class(None, graph['rig'] + '_C')
program_text = unreal.AlsLyraFootPlantRigInputsLibrary.read_program(rig_class)
assert program_text
program = json.loads(program_text)
requests = {'sequencePaths': paths, 'traces': []}
for mode, hz in ((m, hz) for m in ('OriginalMain', 'OperatorBool', 'TransferOnly') for hz in (30, 60, 120)):
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
        frames.append({'delta': f32(0 if i % 101 == 100 else (.35 if i % 307 == 306 else 1/hz)),
                       'asset': asset, 'time': time, 'previous': old, 'sourceDelta': f32(time-old),
                       'componentP': [t*7, math.sin(t*2.1)*6, math.sin(t*1.3)*8 + (15 if 2.7 < t < 3.1 else 0)],
                       'componentQ': [0, 0, math.sin(yaw/2), math.cos(yaw/2)],
                       'floorPoint': [0, 0, math.sin(t*1.7)*18+(30 if 1.5 < t < 2 else 0)],
                       'floorNormal': normal, 'geometry': not (4.2 < t < 4.5),
                       'enabled': .2 <= t < 2.7 or 3.1 <= t < 5.5,
                       'crouching': 1.4 <= t < 2.4 or 4.4 <= t < 5.2,
                       'moving': 1 <= t < 3 or 4 <= t < 5,
                       'initialize': i == 0 or i == hz * 3,
                       'visited': not (.8 <= t < 1.1 or 3.4 <= t < 3.6),
                       'evaluate': not (1.1 < t < 1.3 or 4.6 < t < 4.8)})
    requests['traces'].append({'mode': mode, 'hz': hz, 'frames': frames})

native = {'traces': []}
for trace_request in requests['traces']:
    # Keep a whole native instance/continuous history per call, below UE's
    # 32-bit JSON archive limit; merge only after native serialization.
    text = unreal.AlsLyraFootPlantRigInputsLibrary.read_trace(
        unreal.load_class(None, graph['main'] + '_C'), source_mesh, skeleton, sequences,
        json.dumps({'sequencePaths': paths, 'traces': [trace_request]}, separators=(',', ':')))
    assert text
    captured = json.loads(text)
    assert len(captured['traces']) == 1
    native['traces'].append(captured['traces'][0])
    del text, captured
    unreal.log('LYRA_INPUT_TRACE_CAPTURED mode=%s hz=%d' % (trace_request['mode'], trace_request['hz']))

diagnostic = repo / 'artifacts/lyra-analysis/footplant-rig-inputs-diagnostic.json'
diagnostic.write_text(json.dumps(native, separators=(',', ':'), allow_nan=False), encoding='utf-8')
counts = dict(frames=0, poses=0, preSolve=0, preCalls=0, transferOnly=0, curves=0,
              partial=0, disabled=0, hidden=0, updateOnly=0, initialize=0)
names = cal and load('logical_controls/calibration.json')['layout']['logicalBoneNames']
descriptor = None
for req, trace in zip(requests['traces'], native['traces'], strict=True):
    assert (req['mode'], req['hz']) == (trace['mode'], trace['hz'])
    assert len(req['frames']) == len(trace['frames'])
    assert trace['traceChannelName'] == 'Traversable' and trace['groundResponse'] == 2
    assert trace['skeletonNames'] == names
    d = trace['descriptor']
    assert len(d['mapping']) == 81
    assert [m['pose'] for m in d['mapping']] == list(range(81))
    if descriptor is None:
        descriptor = d
    else:
        assert descriptor == d, 'Changed ALS input layout between native instances'
    for frame, row in zip(req['frames'], trace['frames'], strict=True):
        counts['frames'] += 1
        counts['hidden'] += not frame['visited']
        counts['updateOnly'] += frame['visited'] and not frame['evaluate']
        counts['initialize'] += frame['initialize']
        assert row['groundSanityHit'] == frame['geometry']
        expected = frame['visited'] and frame['evaluate'] and row['alpha'] > 0.00001
        assert ('preSolve' in row) == expected
        assert row['preCalls'] == (int(expected) if req['mode'] != 'TransferOnly' else 0)
        counts['preCalls'] += row['preCalls']
        counts['preSolve'] += expected
        for key in ('before', 'updated', 'after', 'preSolve'):
            if key not in row: continue
            state = row[key]
            assert len(state['hierarchy']) == 98 and len(state['curves']) == 109
            assert all(set(v) == {'value', 'set'} for v in state['curves'].values())
            counts['curves'] += len(state['curves'])
        if 'output' in row:
            counts['poses'] += 1
            counts['partial'] += 0 < row['alpha'] < 1
            counts['disabled'] += row['alpha'] == 0
            assert len(row['input']['pose']) == len(row['output']['pose']) == 81
            if req['mode'] == 'TransferOnly':
                counts['transferOnly'] += 1
                assert row['input'] == row['output'], 'TransferOnly must not run a solver or alter the source output'
assert counts['frames'] == 3780 and counts['preCalls'] > 1500 and counts['transferOnly'] > 900, counts
assert counts['partial'] > 50 and counts['disabled'] > 50, counts
protect()
source = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
source_sha = {p: sha(source / p) for p in ('Private/AlsLyraFootPlantRigInputsLibrary.cpp',
                                        'Public/AlsLyraFootPlantRigInputsLibrary.h')}
for tree in ('source', 'package'):
    for path, digest in source_sha.items():
        assert sha(repo / 'artifacts/unreal/gasp58-lyra-masks' / tree /
                   'AlsV4AssetExporter/Source/AlsV4AssetExporter' / path) == digest
dependencies = {p: sha(root / p) for p in ('footplant_rig_graph_v1.json', 'main_layer_graph_v1.json',
                                        'logical_controls/calibration.json', 'logical_controls/catalog.json',
                                        'foot_placement_v1_requests.json')}
request_sha = save('requests', requests)
program_sha = save('program', program)
policy_sha = save('policy', {'schemaVersion': 1, 'stage': 'OriginalMain73UpdateInput', 'skeleton': 'ALS81',
                             'mainNode': 73, 'rig': graph['rig'], 'dependencies': dependencies,
                             'initializeFromOriginalRig': True, 'setRefPoseFromSkeleton': False,
                             'originalBoundary': 'Actual OnPreForwardsSolve delegate after real Main73 UpdateInput, before VM execution',
                             'transferOnly': 'Actual source Evaluate and virtual native Main73 UpdateInput, without Rig VM or UpdateOutput; retained output is unchanged source pose',
                             'descriptor': 'Independent native adapter mapping routine with actual node configuration and FBoneContainer; actual live mapping enabled flag, no private adapter access',
                             'operatorBoolOverride': 'Separate unbound native OperatorBool node, original settings and property mappings, compiled Main handler, resolved bool override',
                             'production': False})
native.update(schemaVersion=1, dependencies=dependencies, counts=counts, requestSha256=request_sha,
              policySha256=policy_sha, programSha256=program_sha, assetSha256=packages,
              previousFixtureSha256=previous, probeSourceSha256=source_sha)
native_sha = save('native', native)
save('input', {'schemaVersion': 1, 'nativeSha256': native_sha, 'dependencies': dependencies,
               'requestSha256': request_sha, 'traces': [
                   {'mode': t['mode'], 'hz': t['hz'], 'descriptor': t['descriptor'],
                    'initial': t['program']['initial']['hierarchy'],
                    'initialCurves': t['frames'][0]['before']['curves'],
                    'frames': [{'imported': 'preSolve' in r, 'input': r.get('input'),
                                'hierarchy': r['after']['hierarchy'], 'curves': r['after']['curves']}
                               for r in t['frames']]}
                   for t in native['traces'] if t['mode'] == 'TransferOnly']})
protect()
unreal.log('LYRA_FOOTPLANT_RIG_INPUTS_NATIVE_OK frames=%d poses=%d preSolve=%d preCalls=%d transferOnly=%d assets_saved=0' % (
    counts['frames'], counts['poses'], counts['preSolve'], counts['preCalls'], counts['transferOnly']))
