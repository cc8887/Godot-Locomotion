"""FootPlant v2: explicitly block the Rig's resolved custom trace channel."""
import hashlib
import json
import math
import os
import struct
from pathlib import Path

import unreal


root = Path(os.environ['LYRA_OUTPUT_ROOT'])
repo = Path(__file__).resolve().parents[2]
prefix = 'footplant_rig_ground_v2'
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
program_text = unreal.AlsLyraFootPlantRigGroundLibrary.read_program(rig_class)
assert program_text
program = json.loads(program_text)
requests = {'sequencePaths': paths, 'traces': []}
for mode, hz in ((m, hz) for m in ('OriginalMain', 'OperatorBool') for hz in (30, 60, 120)):
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

text = unreal.AlsLyraFootPlantRigGroundLibrary.read_trace(
    unreal.load_class(None, graph['main'] + '_C'), source_mesh, skeleton, sequences,
    json.dumps(requests, separators=(',', ':')))
assert text
native = json.loads(text)
diagnostic = repo / 'artifacts/lyra-analysis/footplant-rig-ground-diagnostic.json'
diagnostic.write_text(json.dumps(native, separators=(',', ':'), allow_nan=False), encoding='utf-8')
counts = dict(frames=0, poses=0, partial=0, active=0, disabled=0, hidden=0, updateOnly=0,
              initialize=0, crouching=0, moving=0, changed=0, originalMainPoses=0,
              leftHits=0, rightHits=0, pelvisOffset=0, slope=0, slopeCrouching=0, sanityHits=0)
names = load('logical_controls/calibration.json')['layout']['logicalBoneNames']
for req, trace in zip(requests['traces'], native['traces'], strict=True):
    assert (req['mode'], req['hz']) == (trace['mode'], trace['hz']) and len(req['frames']) == len(trace['frames'])
    assert trace['program']['instructions']
    assert trace['traceChannelName'] == 'Traversable' and trace['groundResponse'] == 2
    assert trace['skeletonNames'] == names, trace['skeletonNames']
    for frame, row in zip(req['frames'], trace['frames'], strict=True):
        counts['frames'] += 1
        counts['hidden'] += not frame['visited']
        counts['updateOnly'] += frame['visited'] and not frame['evaluate']
        counts['initialize'] += frame['initialize']
        assert row['groundSanityHit'] == frame['geometry'], (req['mode'], req['hz'], frame)
        counts['sanityHits'] += row['groundSanityHit']
        for name, values in row['after']['work'].items():
            if name.endswith('_SpringState'):
                assert values and all(set(v) == {'velocity', 'target', 'valid'} for v in values), name
            if name.endswith('_ScaleBiasClamp'):
                assert all('initialized' in v and 'interpolated' in v for v in values), name
        if frame['visited']:
            variables = row['updated']['variables']
            assert variables['isCrouching'] == frame['crouching'], (req['hz'], counts, variables)
            assert variables['isMoving2D'] == frame['moving'], (req['hz'], counts, variables)
        if 'output' in row:
            variables = row['after']['variables']
            counts['leftHits'] += variables['DidLeftFootTraceHit']
            counts['rightHits'] += variables['DidRightFootTraceHit']
            counts['pelvisOffset'] += variables['CurrentPelvisOffsetZ'] != 0
            counts['slope'] += variables['isCharacterSloping']
            counts['slopeCrouching'] += variables['isSlopingAndCrouching']
            counts['poses'] += 1
            assert len(row['input']['pose']) == len(row['output']['pose']) == 81
            assert 0 <= row['alpha'] <= 1
            counts['partial'] += 0 < row['alpha'] < 1
            counts['active'] += row['alpha'] == 1
            counts['disabled'] += row['alpha'] == 0
            counts['crouching'] += frame['crouching']
            counts['moving'] += frame['moving']
            counts['changed'] += row['input']['pose'] != row['output']['pose']
            if req['mode'] == 'OriginalMain':
                counts['originalMainPoses'] += 1
                assert row['alpha'] == 1 and row['nodeUpdated']['bAlphaBoolEnabled'], row
assert counts['frames'] == 2520 and counts['poses'] > 1800
assert counts['active'] > 400 and counts['partial'] > 50 and counts['changed'] > 400, counts
assert counts['leftHits'] > 200 and counts['rightHits'] > 200 and counts['pelvisOffset'] > 100, counts
assert counts['slope'] > 50 and counts['slopeCrouching'] > 20, counts
protect()
source = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
source_sha = {p: sha(source / p) for p in ('Private/AlsLyraFootPlantRigGroundLibrary.cpp',
                                        'Public/AlsLyraFootPlantRigGroundLibrary.h')}
for tree in ('source', 'package'):
    for path, digest in source_sha.items():
        assert sha(repo / 'artifacts/unreal/gasp58-lyra-masks' / tree /
                   'AlsV4AssetExporter/Source/AlsV4AssetExporter' / path) == digest
dependencies = {p: sha(root / p) for p in ('footplant_rig_graph_v1.json', 'main_layer_graph_v1.json',
                                        'logical_controls/calibration.json', 'logical_controls/catalog.json',
                                        'foot_placement_v1_requests.json')}
request_sha = save('requests', requests)
program_sha = save('program', program)
policy_sha = save('policy', {'schemaVersion': 1, 'stage': 'OriginalMain73ControlRig', 'skeleton': 'ALS81',
                             'mainNode': 73, 'rig': graph['rig'], 'dependencies': dependencies,
                             'initializeFromOriginalRig': True, 'setRefPoseFromSkeleton': False,
                             'originalBindings': True, 'originalMainBoolEnabled': True,
                             'operatorBoolOverride': 'Separate unbound native OperatorBool node with original reflected settings and property mappings: execute compiled Main handler to populate source fields, override resolved operator bool; no original Main coverage claim',
                             'geometry': 'Actual UE World box explicitly blocks all channels, including resolved Traversable; every frame verifies independent sweep', 'production': False})
native.update(schemaVersion=1, dependencies=dependencies, counts=counts, requestSha256=request_sha,
              policySha256=policy_sha, programSha256=program_sha, assetSha256=packages,
              previousFixtureSha256=previous, probeSourceSha256=source_sha)
native_sha = save('native', native)
save('update', {'schemaVersion': 1, 'nativeSha256': native_sha, 'requestSha256': request_sha,
                'counts': counts, 'policySha256': policy_sha, 'programSha256': program_sha,
                'traces': [{'mode': t['mode'], 'hz': t['hz'], 'settings': t['settings'],
                            'initialBoolBlend': t['initialBoolBlend'],
                            'frames': [{'alpha': r['alpha'], 'boolBefore': r['boolBefore'],
                                        'boolUpdated': r['boolUpdated'], 'poseEvaluated': 'output' in r,
                                        'beforeDelta': r['before']['delta'],
                                        'updatedDelta': r['updated']['delta'], 'afterDelta': r['after']['delta'],
                                        'crouching': r['updated']['variables']['isCrouching'],
                                        'moving': r['updated']['variables']['isMoving2D']}
                                       for r in t['frames']]}
                           for t in native['traces']]})
protect()
unreal.log('LYRA_FOOTPLANT_RIG_GROUND_NATIVE_OK frames=%d poses=%d partial=%d leftHits=%d rightHits=%d pelvis=%d slope=%d assets_saved=0' % (
    counts['frames'], counts['poses'], counts['partial'], counts['leftHits'], counts['rightHits'], counts['pelvisOffset'], counts['slope']))
