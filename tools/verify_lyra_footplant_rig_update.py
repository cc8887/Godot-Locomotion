"""Verify Main73 update/binding evidence. This does not accept Rig pose or Demo."""
import hashlib
import json
import re
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
load = lambda name: json.loads((root / name).read_bytes())
def read(name):
    raw = (logs / name).read_bytes()
    if raw.startswith((b'\xff\xfe', b'\xfe\xff')):
        return raw.decode('utf-16')
    try:
        return raw.decode('utf-8-sig')
    except UnicodeDecodeError:
        return raw.decode('gb18030')

native = load('footplant_rig_v1_native.json')
update = load('footplant_rig_v1_update.json')
requests = load('footplant_rig_v1_requests.json')
program = load('footplant_rig_v1_program.json')
binding = load('footplant_binding_v1.json')
for kind in ('native', 'request', 'program', 'policy'):
    file_kind = 'requests' if kind == 'request' else kind
    assert update[kind + 'Sha256'] == sha(root / ('footplant_rig_v1_' + file_kind + '.json'))
for fixture in (native, binding):
    for field in ('dependencies', 'previousFixtureSha256'):
        for p, h in fixture.get(field, {}).items():
            assert sha(root / p) == h, p
    for p, h in fixture['assetSha256'].items():
        assert sha(Path('../GASP58/Content') / (p.split('.')[0].removeprefix('/Game/') + '.uasset')) == h, p
    for p, h in fixture['probeSourceSha256'].items():
        assert sha(repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter' / p) == h, p
        for tree in ('source', 'package'):
            assert sha(repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter' / p) == h, p
assert len(program['functions']) == 40 and len(program['instructions']) == 436
assert program['entries'] == [{'name': 'Forwards Solve', 'instruction': 0}, {'name': 'Construction', 'instruction': 401}]
assert not any('.FootTrace.' in i['subject'] for i in program['instructions'])
assert len(native['traces']) == len(update['traces']) == len(requests['traces']) == 6
names = load('logical_controls/calibration.json')['layout']['logicalBoneNames']
frames = poses = 0
for t, u, q in zip(native['traces'], update['traces'], requests['traces'], strict=True):
    assert (t['mode'], t['hz']) == (u['mode'], u['hz']) == (q['mode'], q['hz'])
    assert t['skeletonNames'] == names and len(names) == 81
    assert len(t['program']['initial']['hierarchy']) == 98 and 'ik_ball_r' in t['program']['initial']['hierarchy']
    assert len(t['program']['workTypes']) == 345
    for row, filtered, request in zip(t['frames'], u['frames'], q['frames'], strict=True):
        frames += 1
        assert row['alpha'] == filtered['alpha'] and row['boolUpdated'] == filtered['boolUpdated']
        assert row['updated']['delta'] == filtered['updatedDelta'] and row['after']['delta'] == filtered['afterDelta']
        springs = {n: v for n, v in row['after']['work'].items() if n.endswith('_SpringState')}
        assert len(springs) == 5 and all(v and all(set(s) == {'velocity', 'target', 'valid'} for s in v) for v in springs.values())
        if 'output' in row:
            poses += 1
            assert len(row['input']['pose']) == len(row['output']['pose']) == 81
            if t['mode'] == 'OriginalMain':
                assert row['alpha'] == 1 and row['nodeUpdated']['bAlphaBoolEnabled']
assert frames == 2520 and poses == 2154 and native['counts'] == update['counts']
assert len(binding['frames']) == len(binding['requests']) == 96
for row, q in zip(binding['frames'], binding['requests'], strict=True):
    assert row['enabled'] == (row['curve'] <= 0 and not q['useFootPlacement'])
    assert (row['crouching'], row['moving']) == (q['crouching'], q['moving'])

checks = {}
for name, marker, mode in (
    ('lyra-footplant-rig-trace-ue.log', 'LYRA_FOOTPLANT_RIG_NATIVE_OK frames=2520 poses=2154 partial=164 changed=2001 assets_saved=0', 'footplant-rig-trace'),
    ('lyra-footplant-rig-trace-repeat-ue.log', 'LYRA_FOOTPLANT_RIG_NATIVE_OK frames=2520 poses=2154 partial=164 changed=2001 assets_saved=0', 'footplant-rig-trace'),
    ('lyra-footplant-bindings-ue.log', 'LYRA_FOOTPLANT_BINDINGS_NATIVE_OK cases=96 enabled=24 assets_saved=0', 'footplant-bindings'),
    ('lyra-footplant-bindings-repeat-ue.log', 'LYRA_FOOTPLANT_BINDINGS_NATIVE_OK cases=96 enabled=24 assets_saved=0', 'footplant-bindings')):
    text = read(name)
    assert text.count(marker) == 1 and f'LYRA_EXPORT_PROCESS_EXIT_OK mode={mode} code=0' in text, name
    assert ': Error:' not in text, name
    checks[name] = sha(logs / name)
repeat = read('lyra-footplant-rig-trace-repeat-ue.log')
assert re.search(r'LYRA_FOOTPLANT_CACHE_IDENTITY_COMPARE_OK remappedOccurrences=\d+ allOtherValuesExact=true originalBytesPreserved=true', repeat)
for name in ('footplant-rig-update-godot-debug.log', 'footplant-rig-update-godot-optimize.log'):
    text = read(name)
    assert text.count('LYRA_FOOTPLANT_RIG_UPDATE_GODOT_OK frames=2520 poses=2154 partial=164 disabled=149 hidden=210 updateOnly=156 initialize=12 retries=2520 rejected=32448 bindingCases=96 stateBitExact=true rigPose=false production=false') == 1, name
    assert 'LYRA_GODOT_PROCESS_EXIT code=0' in text and not re.search(r'^\s*(ERROR|WARNING):', text, re.M), name
    checks[name] = sha(logs / name)
optimized = read('footplant-rig-update-godot-optimize.log')
assert optimized.count('LYRA_OPTIMIZED_ASSEMBLY') == 3 and 'LYRA_DEBUG_ASSEMBLIES_RESTORED hashVerified=true' in optimized
for name in ('footplant-rig-update-debug-build.log', 'footplant-rig-update-optimize-build.log'):
    text = read(name)
    assert re.search(r'0\s*(个警告|Warning)', text) and re.search(r'0\s*(个错误|Error)', text), name
    checks[name] = sha(logs / name)
report = dict(stage='Main73UpdateAndBindingOnly', counts=native['counts'], bindingCases=96,
              rigInstructions=436, nativeFunctions=40, workRegisters=345,
              protectedPackages=len(native['assetSha256']), protectedPreviousJson=len(native['previousFixtureSha256']),
              nativeSha256=sha(root / 'footplant_rig_v1_native.json'), checks=checks,
              cacheIdentityComparison='Per Rig bijection for address-derived ContainerVersion; validity, key, index and all other values exact',
              stateBitExact=True, rigPose=False, production=False)
(logs / 'lyra-footplant-rig-update-verification.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print('LYRA_FOOTPLANT_RIG_UPDATE_VERIFIED frames=2520 bindingCases=96 rigPose=false production=false')
