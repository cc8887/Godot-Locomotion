"""Read original profiles and real frozen Montage blend histories; no asset saves."""
import copy
import hashlib
import json
import os
import struct
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
repo = Path(__file__).resolve().parents[2]
prefix = 'montage_blend_v1'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
f32 = lambda x: struct.unpack('<f', struct.pack('<f', x))[0]
catalog = json.loads((root / 'montage_catalog_v2.json').read_bytes())
calibration = json.loads((root / 'logical_controls/calibration.json').read_bytes())
graph = json.loads((root / 'main_layer_graph_v1.json').read_bytes())
requests = copy.deepcopy(json.loads((root / 'montage_slots_v2_requests.json').read_bytes()))
requests['boneNames'] = calibration['layout']['logicalBoneNames']
requests['mathCases'] = []
# Exact threshold neighbours, early reversal, all authored blend options.
for mode in (0, 1):
    for option in (0, 1, 2):
        for factor in (-.1, 0, .000009999, .00001, .000010001, .1, .25, .5, .99998, .99999, 1, 2):
            for alpha in (0, .00001, .07, .37, .75, 1):
                for start, begin, desired in ((0, 0, 1), (.37, .309394, 0), (1, 1, 0), (.1, .028, 0)):
                    requests['mathCases'].append(dict(mode=mode, option=option, factor=f32(factor),
                        alpha=f32(alpha), startAlpha=f32(start), begin=f32(begin), desired=f32(desired)))
profile_assets = [i for i, a in enumerate(catalog['assets']) if a['blendOutProfile']]
assert len(profile_assets) == 6
for trace in requests['traces']:
    hz = trace['hz']
    for asset in profile_assets[:3]:
        time = 6 + asset * .75
        for delay, duration in ((.08, .4), (.12, .04), (.14, .8)):
            trace['frames'][round((time + delay) * hz)]['commands'].append(dict(
                asset=asset, stop=True, blend=f32(duration), rate=1, start=0, instanceStop=delay != .08))

previous = {str(p.relative_to(root)).replace('\\', '/'): sha(p) for p in root.rglob('*.json')
            if not p.name.startswith(prefix)}
if (root / (prefix + '_native.json')).exists():
    previous = json.loads((root / (prefix + '_native.json')).read_bytes())['previousFixtureSha256']
packages = dict(json.loads((root / 'montage_actions/catalog.json').read_bytes())['assetSha256'])
assert all(packages[p] == digest for p, digest in catalog['assetSha256'].items())
content = Path(unreal.Paths.project_content_dir())

def protect():
    for p, digest in previous.items():
        assert sha(root / p) == digest, p
    for p, digest in packages.items():
        assert sha(content / (p.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, p

def save(kind, value):
    path = root / (prefix + '_' + kind + '.json')
    if path.exists():
        assert json.loads(path.read_bytes()) == value, 'Immutable blend fixture differs: ' + kind
    else:
        path.write_text(json.dumps(value, separators=(',', ':')), encoding='utf-8')
    return sha(path)

protect()
text = unreal.AlsLyraMontageBlendLibrary.read_blend_trace(
    unreal.load_class(None, graph['classes']['main']['classPath']),
    unreal.load_asset(calibration['calibration']['sourceMesh']),
    [unreal.load_asset(a['path']) for a in catalog['assets']], json.dumps(requests, separators=(',', ':')))
assert text, 'No original blend capture'
native = json.loads(text)
assert len(native['profiles']) == 1 and len(native['bindings']) == 45
names = {n.lower() for n in requests['boneNames']}
source_skeleton = json.loads((root / 'skeletal_control_defaults.json').read_bytes())['skeletons']['source']
source_layout = source_skeleton['layout']
source_names = source_layout['logicalBoneNames']
source_parents = source_layout['logicalParents']
source_indices = {n.lower(): i for i, n in enumerate(source_names)}
target_indices = {n.lower(): i for i, n in enumerate(requests['boneNames'])}
for profile in native['profiles']:
    assert profile['mode'] in (0, 1)
    assert len(profile['factors']) == 81
    assert profile['skeleton'] == source_skeleton['asset']
    entry_scales = {e['bone'].lower(): e['scale'] for e in profile['entries']}
    omitted = []
    for e in profile['entries']:
        if e['bone'].lower() in names:
            continue
        index = source_indices[e['bone'].lower()]
        while index >= 0 and source_names[index].lower() not in names:
            index = source_parents[index]
        assert index >= 0, ('No retained ancestor for original profile bone', e)
        ancestor = source_names[index]
        target = target_indices[ancestor.lower()]
        assert profile['factors'][target] == e['scale'] == entry_scales.get(ancestor.lower(), 1), (
            'Omitted deformation bone has a different transition policy', e, ancestor)
        omitted.append(dict(bone=e['bone'], scale=e['scale'], ancestor=ancestor, targetIndex=target))
    profile['omittedTargetBones'] = omitted
for a, b in zip(catalog['assets'], native['bindings'], strict=True):
    for direction in ('in', 'out'):
        expected = a['blend' + direction.title() + 'Profile']
        actual = '' if b[direction] == -1 else native['profiles'][b[direction]]['path']
        assert actual == expected
counts = dict(frames=0, frozen=0, profileFrames=0, boneWeights=0, earlyReversal=0)
for trace in native['traces']:
    for frame in trace['frames']:
        counts['frames'] += 1
        for row in frame['frozen']:
            counts['frozen'] += 1
            if row['profile'] >= 0:
                counts['profileFrames'] += 1
                counts['boneWeights'] += len(row['boneWeights'])
                counts['earlyReversal'] += 0 < row['startAlpha'] < 1
assert counts['frames'] == 13440 and counts['earlyReversal'] and counts['boneWeights']
protect()
source = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
probe = {p: sha(source / p) for p in ('Private/AlsLyraMontageBlendLibrary.cpp', 'Public/AlsLyraMontageBlendLibrary.h')}
for tree in ('source', 'package'):
    mirror = repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter'
    for p, digest in probe.items():
        assert sha(mirror / p) == digest, (tree, p)
native.update(schemaVersion=1, requestSha256=save('requests', requests),
    dependencies={p: sha(root / p) for p in ('montage_catalog_v2.json', 'montage_slots_v2_requests.json',
        'main_layer_graph_v1.json', 'logical_controls/calibration.json', 'montage_actions/catalog.json', 'skeletal_control_defaults.json')},
    assetSha256=packages, previousFixtureSha256=previous, probeSourceSha256=probe, counts=counts,
    scope='Original physical Montage frozen blends and native profile weight calculation. No Slot pose or production claim.')
save('policy', dict(schemaVersion=1, dependencies=native['dependencies'], profiles=native['profiles'],
    bindings=native['bindings'], montagePaths=requests['montagePaths'], boneNames=requests['boneNames'],
    adaptation='Exact-name retained bones; absent deformation bones require identical nearest retained ancestor factors. No extra ALS skin bones.'))
save('native', native)
unreal.log(f'LYRA_MONTAGE_BLEND_NATIVE_OK frames={counts["frames"]} frozen={counts["frozen"]} profiles=1 boneWeights={counts["boneWeights"]} assets_saved=0')
