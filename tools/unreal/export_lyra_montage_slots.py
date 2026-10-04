"""Actual original Montage clocks and Main Slot update contexts, read only."""
import hashlib
import json
import os
import struct
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
repo = Path(__file__).resolve().parents[2]
prefix = 'montage_slots_v2'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
f32 = lambda v: struct.unpack('<f', struct.pack('<f', v))[0]
catalog = json.loads((root / 'montage_catalog_v2.json').read_bytes())
graph = json.loads((root / 'main_layer_graph_v1.json').read_bytes())
basis = json.loads((root / 'logical_controls/calibration.json').read_bytes())['calibration']
content = Path(unreal.Paths.project_content_dir())
packages = catalog['assetSha256']
previous = {str(p.relative_to(root)).replace('\\', '/'): sha(p) for p in root.rglob('*.json') if not p.name.startswith(prefix)}
if (root / (prefix + '_native.json')).exists():
    previous = json.loads((root / (prefix + '_native.json')).read_bytes())['previousFixtureSha256']

def protect():
    for p, digest in packages.items():
        assert sha(content / (p.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, p
    for p, digest in previous.items():
        assert sha(root / p) == digest, p

def save(kind, data):
    path = root / (prefix + '_' + kind + '.json')
    if path.exists():
        assert json.loads(path.read_bytes()) == data, 'Immutable Montage Slot fixture differs: ' + kind
    else:
        path.write_text(json.dumps(data, separators=(',', ':')), encoding='utf-8')
    return sha(path)

protect()
assets = catalog['assets']
assert len(assets) == 45 and all(a['blendModeIn'] == a['blendModeOut'] == 0 for a in assets)
paths = [a['path'] for a in assets]
index = lambda name: next(i for i, a in enumerate(assets) if a['path'].split('/')[-1].split('.')[0] == name)
requests = dict(montagePaths=paths, traces=[])
for hz in (30, 60, 120):
    frames = [dict(delta=f32(1 / hz), initialize=i == 0 or i == hz * 55,
                   visited=not hz * 54 <= i < hz * 55 and i % 37 != 17,
                   weight=f32((.25, .7, 1)[(i // 11) % 3]),
                   rootModifier=f32((0, .3, 1)[(i // 13) % 3]), active=i % 19 != 7, commands=[])
              for i in range(hz * 64)]

    def play(time, asset, rate=1, start=0):
        frames[round(time * hz)]['commands'].append(dict(asset=asset, stop=False, blend=0, rate=f32(rate), start=f32(start),
                                                       stopGroup=time < 40 or time >= 43))

    def stop(time, asset, blend):
        frames[round(time * hz)]['commands'].append(dict(asset=asset, stop=True, blend=f32(blend), rate=1, start=0))

    play(0, 0)
    for asset in range(45):
        play(6 + asset * .75, asset, (1, .8, 1.5)[asset % 3], .03 if asset % 4 == 1 else 0)
    for i in range(24):
        play(40 + i * .1, index('AM_MM_Rifle_Fire'))
        if i % 3 == 0:
            play(40 + i * .1, index('AM_MM_HitReact_Front_Lgt_01'))
    play(44, index('AM_MM_Pistol_Reload'))
    play(44.08, index('AM_MM_Rifle_Reload'))
    stop(44.12, index('AM_MM_Rifle_Reload'), .4)
    stop(44.2, index('AM_MM_Rifle_Reload'), .05)
    play(46, index('AM_MM_Dash_Left'), 1.25)
    play(48, index('AM_MM_Rifle_Equip'))
    play(53, 0)
    play(59, index('AM_MM_HitReact_Front_Lgt_01'))
    requests['traces'].append(dict(hz=hz, frames=frames))

text = unreal.AlsLyraMontageLibrary.read_slot_update_trace(
    unreal.load_class(None, graph['classes']['main']['classPath']), unreal.load_asset(basis['sourceMesh']),
    [unreal.load_asset(p) for p in paths], json.dumps(requests, separators=(',', ':')))
assert text, 'Native Main Slot probe returned no data'
native = json.loads(text)
counts = dict(frames=0, slots=0, updated=0, inactive=0, hidden=0, full=0, overlap=0, instances=0)
played = set()
for trace, request in zip(native['traces'], requests['traces'], strict=True):
    for row, frame in zip(trace['frames'], request['frames'], strict=True):
        counts['frames'] += 1
        counts['hidden'] += not frame['visited']
        assert len(row['slots']) == 5
        for slot in row['slots']:
            counts['slots'] += 1
            counts['updated'] += slot['updated']
            counts['inactive'] += slot['updated'] and not slot['active']
            counts['full'] += slot['slotWeight'] >= 1 - 1e-5
            counts['overlap'] += slot['totalWeight'] > 1
        for instance in row['instances']:
            played.add(instance['asset'])
            counts['instances'] += 1
assert played == set(range(45)), played
assert counts['frames'] == 13440 and counts['overlap'] and counts['full'] and counts['inactive'] and counts['hidden']
protect()
source = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
probe = {p: sha(source / p) for p in ('Private/AlsLyraMontageLibrary.cpp', 'Public/AlsLyraMontageLibrary.h')}
for tree in ('source', 'package'):
    mirror = repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter'
    for p, digest in probe.items():
        assert sha(mirror / p) == digest, (tree, p)
native.update(schemaVersion=1, requestSha256=save('requests', requests),
              dependencies={p: sha(root / p) for p in ('montage_catalog_v2.json', 'main_layer_graph_v1.json', 'logical_controls/calibration.json')},
              assetSha256=packages, previousFixtureSha256=previous, probeSourceSha256=probe, counts=counts,
              scope='Actual 45 original Montage clocks, weight lifecycles and five Main Slot update/source contexts. Source leaves record updates; no pose, Notify dispatch, RootMotion extraction, Main composition or production claim.')
save('native', native)
unreal.log(f'LYRA_MONTAGE_SLOTS_NATIVE_OK frames={counts["frames"]} slots={counts["slots"]} assets=45 previous={len(previous)} assets_saved=0')
