"""Read actual Main/provider cache update queues; never save source assets."""
import hashlib
import json
import os
import struct
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
repo = Path(__file__).resolve().parents[2]
prefix = 'main_cache_v1'
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
        assert json.loads(p.read_bytes()) == data, 'Immutable Main cache fixture differs: ' + kind
    else:
        p.write_text(json.dumps(data, separators=(',', ':')), encoding='utf-8')
    return sha(p)
protect()
requests = dict(traces=[])
pairs = [(1, 0), (0, 1), (.5, .5), (.25, .75), (.75, .25), (0, 0),
         (1e-6, 2e-6), (1e-5, 1e-5), (.49999997, .5), (.50000006, .5)]
for profile in ('unarmed', 'pistol', 'rifle'):
    for hz in (30, 60, 120):
        frames = []
        for i in range(hz * 4):
            a, b = pairs[(i // 5) % len(pairs)]
            readers = [dict(reader=76, weight=f32(a), rootMotionWeight=f32(.25), active=i % 3 != 0, shared=i % 7 != 0),
                       dict(reader=75, weight=f32(b), rootMotionWeight=f32(.75), active=i % 3 == 0, shared=i % 11 != 0)]
            if i % 13 == 0:
                readers.reverse()
            if i % 17 == 0:
                readers.append(readers[0].copy())
            if i % 19 == 0:
                readers.clear()
            frames.append(dict(delta=f32(0 if i % 41 == 0 else 1 / hz), visited=i % 23 != 0,
                preAimSource=f32([1, .25, 0, 1e-5, 1.000001e-5][(i // 7) % 5]),
                upperSource=f32([1, .65, 0, 1e-5, 1.000001e-5][(i // 11) % 5]),
                preAimInactive=i % 5 == 0, upperInactive=i % 3 == 0, readers=readers))
        requests['traces'].append(dict(profile=profile, hz=hz, **{'class': graph['classes'][profile]['classPath']}, frames=frames))
text = unreal.AlsLyraMainCacheLibrary.read_trace(unreal.load_class(None, graph['classes']['main']['classPath']), json.dumps(requests, separators=(',', ':')))
assert text, 'Main cache probe returned no data'
native = json.loads(text)
protect()
probe_names = ('Private/AlsLyraMainCacheLibrary.cpp', 'Public/AlsLyraMainCacheLibrary.h')
source = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
source_sha = {p: sha(source / p) for p in probe_names}
for tree in ('source', 'package'):
    mirror = repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter'
    for p, digest in source_sha.items():
        assert sha(mirror / p) == digest, (tree, p)
counts = dict(frames=sum(len(t['frames']) for t in native['traces']),
              updates=sum(len(f['updates']) for t in native['traces'] for f in t['frames']),
              skipped=sum(sum(s['count'] for s in f['skipped']) for t in native['traces'] for f in t['frames']))
assert counts['frames'] == 2520 and counts['updates'] > 4000 and counts['skipped'] > 2000, counts
orders = {}
for trace in native['traces']:
    order = {k: trace[k] for k in ('mainOrder', 'providerOrder')}
    if trace['profile'] in orders:
        assert orders[trace['profile']] == order
    orders[trace['profile']] = order
policy_sha = save('policy', dict(schemaVersion=1, orders=orders,
    dependencies={'main_layer_graph_v1.json': sha(root / 'main_layer_graph_v1.json')},
    stage='OriginalMainCacheUpdate', providerOffset=103, mainRootMask=0))
native.update(schemaVersion=1, dependencies={'main_layer_graph_v1.json': sha(root / 'main_layer_graph_v1.json')},
    requestSha256=save('requests', requests), policySha256=policy_sha, assetSha256=packages, previousFixtureSha256=previous,
    probeSourceSha256=source_sha, counts=counts,
    scope='Actual Main/provider Save/UseCachedPose update nodes and original queue order. Resolved Slot source contexts, no active Montage, pose evaluation, initialization, inertia or complete Main oracle.')
save('native', native)
unreal.log('LYRA_MAIN_CACHE_NATIVE_OK frames=' + str(counts['frames']) + ' updates=' + str(counts['updates']) + ' previous=' + str(len(previous)) + ' assets_saved=0')
