"""Capture compiled branches and actual visits without extra hierarchy getters."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
repo = Path(__file__).resolve().parents[2]
prefix = 'rig_traversal_v1'
load = lambda p: json.loads((root / p).read_bytes())

def sha(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        while b := f.read(1024 * 1024): h.update(b)
    return h.hexdigest()

base = load('footplant_rig_ground_v2_native.json')
requests = load('footplant_rig_ground_v2_requests.json')
packages = base['assetSha256']
previous = {str(p.relative_to(root)).replace('\\', '/'): sha(p)
            for p in root.rglob('*.json') if not p.name.startswith(prefix)}
if (root / (prefix + '_native.json')).exists():
    previous = load(prefix + '_native.json')['previousFixtureSha256']
content = Path(unreal.Paths.project_content_dir())
dependencies = {p: sha(root / p) for p in ('footplant_rig_ground_v2_native.json',
    'footplant_rig_ground_v2_requests.json', 'footplant_rig_inputs_v1_program.json',
    'logical_controls/calibration.json', 'logical_controls/catalog.json')}

def protect():
    for p, digest in previous.items(): assert sha(root / p) == digest, p
    for p, digest in packages.items():
        assert sha(content / (p.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, p

def compare(a, b, forward, reverse, path=''):
    assert type(a) is type(b), path
    if isinstance(a, dict):
        assert a.keys() == b.keys(), path
        cache = a.keys() == {'Key', 'Index', 'ContainerVersion'}
        for key in a:
            if cache and key == 'ContainerVersion':
                x, y = a[key], b[key]
                assert (x == -1) == (y == -1), path
                if x != -1:
                    assert forward.setdefault(x, y) == y and reverse.setdefault(y, x) == x, path
            else: compare(a[key], b[key], forward, reverse, path + '/' + key)
    elif isinstance(a, list):
        assert len(a) == len(b), path
        for i, (x, y) in enumerate(zip(a, b, strict=True)):
            compare(x, y, forward, reverse, path + '/' + str(i))
    else: assert a == b, (path, a, b)

protect()
assert unreal.AlsLyraControlRigLibrary.prefer_raw_track_data_model(True)
cal = load('logical_controls/calibration.json')['calibration']
source_mesh = unreal.load_asset(cal['sourceMesh'])
target_mesh = unreal.load_asset(cal['targetMesh'])
hand = unreal.Quat(*cal['handBasis']['rotation'])
skeleton = unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(
    source_mesh.get_editor_property('skeleton'), target_mesh.get_editor_property('skeleton'), hand)
catalog = load('logical_controls/catalog.json')['entries']
sequences = []
for path in requests['sequencePaths']:
    row = next(r for r in catalog if r['target'] == path)
    s = unreal.AlsLyraControlRigLibrary.create_weapon_sequence(
        unreal.load_asset(row['source']), unreal.load_asset(path), skeleton, hand, None)
    assert s
    unreal.AlsSourceAnimationLibrary.finish_source_compression(s)
    sequences.append(s)
graph = load('footplant_rig_graph_v1.json')
program = json.loads(unreal.AlsLyraRigTraversalLibrary.read_program(
    unreal.load_class(None, graph['rig'] + '_C')))
flow = program['flow']
assert len(flow['instructions']) == 436 and flow['branches']
# One original six-trace call preserves the established probe invocation shape.
text = unreal.AlsLyraRigTraversalLibrary.read_trace(unreal.load_class(None, graph['main'] + '_C'),
    source_mesh, skeleton, sequences, json.dumps(requests, separators=(',', ':')))
assert text
captured = json.loads(text)
del text
diagnostic = repo / 'artifacts/lyra-analysis/rig-traversal-capture-diagnostic.json'
diagnostic.write_text(json.dumps(captured, separators=(',', ':')), encoding='utf-8')
traces = []
counts = dict(frames=0, solved=0, visits=0, empty=0, construction=0,
              springs=0, sweeps=0, lazyBranches=len(flow['branches']))
for expected, actual, req in zip(base['traces'], captured['traces'], requests['traces'], strict=True):
    assert expected['mode'] == actual['mode'] == req['mode'] and expected['hz'] == actual['hz'] == req['hz']
    forward, reverse = {}, {}
    # The only new fields are metadata and integer instruction visits.
    actual['program'].pop('flow')
    frames = []
    for i, (e, a, f) in enumerate(zip(expected['frames'], actual['frames'], req['frames'], strict=True)):
        visits = a.pop('visits')
        compare(e, a, forward, reverse, '%s/%s/%d' % (req['mode'], req['hz'], i))
        counts['frames'] += 1
        counts['visits'] += len(visits)
        counts['empty'] += not visits
        counts['solved'] += 0 in visits
        counts['construction'] += 401 in visits
        counts['springs'] += sum(v in (242, 299, 311, 348, 371) for v in visits)
        counts['sweeps'] += sum('SphereTraceByTraceChannel::Execute' in program['instructions'][v]['text'] for v in visits)
        # Only terminal outputs of distinct single-executed predicate producers
        # are replayed by the bounded traversal test; no pose answer is injected.
        outputs = {}
        for v in set(visits):
            r = program['instructions'][v]
            if 'QuaternionToEuler::Execute' in r['text']:
                o = r['operands'][2]
                outputs[str(v)] = a['after']['work'][o['name']]
            elif 'SphereTraceByTraceChannel::Execute' in r['text']:
                o = r['operands'][4]
                outputs[str(v)] = a['after']['work'][o['name']]
        frames.append(dict(visits=visits, outputs=outputs, variables=a['updated']['variables'],
                           initialize=f['initialize'], solve=0 in visits))
    # Compare settings, initial program and every uninstrumented output exactly.
    compare(expected, actual, forward, reverse, req['mode'] + '/' + str(req['hz']))
    traces.append(dict(mode=req['mode'], hz=req['hz'], frames=frames))
assert counts['frames'] == 2520 and counts['solved'] > 1800 and counts['sweeps'] > 1000, counts
protect()
source = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
probe = {p: sha(source / p) for p in ('Private/AlsLyraRigTraversalLibrary.cpp', 'Public/AlsLyraRigTraversalLibrary.h')}
for tree in ('source', 'package'):
    for p, digest in probe.items():
        assert sha(repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter' / p) == digest

def save(kind, value):
    path = root / (prefix + '_' + kind + '.json')
    if path.exists(): assert load(path.name) == value, 'Immutable traversal capture differs: ' + kind
    else: path.write_text(json.dumps(value, separators=(',', ':'), allow_nan=False), encoding='utf-8')
    return sha(path)

program_sha = save('program', program)
save('native', dict(schemaVersion=1, traces=traces, counts=counts, programSha256=program_sha,
    dependencies=dependencies, probeSourceSha256=probe, assetSha256=packages,
    previousFixtureSha256=previous, originalGroundAllValuesExact=True,
    scope='Actual original VM visit order; traversal predicate-producer replay only; solver pending',
    fullRig=False, production=False))
protect()
unreal.log('LYRA_RIG_TRAVERSAL_NATIVE_OK frames=%d solved=%d visits=%d springs=%d sweeps=%d originalGroundAllValuesExact=true assets_saved=0' %
           (counts['frames'], counts['solved'], counts['visits'], counts['springs'], counts['sweeps']))
