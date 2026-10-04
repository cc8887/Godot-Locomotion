"""Read actual installed Rig spring/AlphaInterp units, without modifying assets."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
repo = Path(__file__).resolve().parents[2]
prefix = 'rig_dynamics_v1'
load = lambda p: json.loads((root / p).read_bytes())


def sha(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        while data := f.read(1024 * 1024): h.update(data)
    return h.hexdigest()


requests = load(prefix + '_requests.json')
previous = {str(p.relative_to(root)).replace('\\', '/'): sha(p)
            for p in root.rglob('*.json') if not p.name.startswith(prefix)}
if (root / (prefix + '_native.json')).exists():
    previous = load(prefix + '_native.json')['previousFixtureSha256']
packages = load('footplant_rig_ground_v2_native.json')['assetSha256']
content = Path(unreal.Paths.project_content_dir())


def protect():
    for p, digest in previous.items(): assert sha(root / p) == digest, p
    for p, digest in requests['dependencies'].items(): assert sha(root / p) == digest, p
    for p, digest in packages.items():
        assert sha(content / (p.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, p


protect()
text = unreal.AlsLyraRigDynamicsLibrary.read_trace(json.dumps(requests, separators=(',', ':')))
assert text
native = json.loads(text)
counts = dict(traces=len(native['traces']), frames=0, calls=0, resets=0, omitted=0, vectorMotion=0,
              scalarMotion=0, alphaMotion=0, zeroDelta=0, tinyDelta=0, largeDelta=0)
for q, t in zip(requests['traces'], native['traces'], strict=True):
    assert q['name'] == t['name'] and len(t['initial']) == 7
    for f, r in zip(q['frames'], t['frames'], strict=True):
        assert len(r['calls']) == len(f['calls']) and len(r['after']) == 7
        counts['frames'] += 1
        counts['calls'] += len(f['calls'])
        counts['resets'] += f['reset']
        counts['omitted'] += not f['calls']
        counts['zeroDelta'] += f['delta'] == 0
        counts['tinyDelta'] += 0 < f['delta'] <= 1e-8
        counts['largeDelta'] += f['delta'] > .1
        for c, s in zip(f['calls'], r['calls'], strict=True):
            if c['owner'] < 2:
                assert s['outputVelocity'] == [0, 0, 0], 'Original VectorV2 does not write its output Velocity'
                counts['vectorMotion'] += s['velocity'] != [0, 0, 0]
            elif c['owner'] < 5:
                assert s['outputVelocity'] == s['velocity'] and s['simulated'] == s['result']
                counts['scalarMotion'] += s['velocity'] != 0
            else:
                assert s['initialized'] and s['result'] == s['interpolated']
                counts['alphaMotion'] += s['result'] != 0
assert counts['traces'] == 9 and counts['frames'] == 3360 and counts['calls'] == 19217
assert counts['vectorMotion'] > 500 and counts['scalarMotion'] > 500 and counts['alphaMotion'] > 100
assert counts['zeroDelta'] > 10 and counts['tinyDelta'] > 1 and counts['largeDelta'] > 1
protect()
source = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
probe = {p:sha(source / p) for p in ('Private/AlsLyraRigDynamicsLibrary.cpp', 'Public/AlsLyraRigDynamicsLibrary.h')}
for tree in ('source', 'package'):
    for p, digest in probe.items():
        assert sha(repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter' / p) == digest
native.update(schemaVersion=1, requestSha256=sha(root / (prefix + '_requests.json')), counts=counts,
              dependencies=requests['dependencies'], probeSourceSha256=probe, assetSha256=packages,
              previousFixtureSha256=previous, scope=requests['scope'], fullRig=False, production=False)
path = root / (prefix + '_native.json')
if path.exists(): assert json.loads(path.read_bytes()) == native, 'Immutable real Rig unit output differs'
else: path.write_text(json.dumps(native, separators=(',', ':'), allow_nan=False), encoding='utf-8')
protect()
unreal.log('LYRA_RIG_DYNAMICS_NATIVE_OK traces=9 frames=3360 calls=19217 assets_saved=0')
