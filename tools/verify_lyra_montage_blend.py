"""Audit immutable native profile data, protected assets, builds and real exits."""
import hashlib
import json
import re
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
native = json.loads((root / 'montage_blend_v1_native.json').read_bytes())
policy = json.loads((root / 'montage_blend_v1_policy.json').read_bytes())
requests = json.loads((root / 'montage_blend_v1_requests.json').read_bytes())
assert native['schemaVersion'] == policy['schemaVersion'] == 1
assert native['requestSha256'] == sha(root / 'montage_blend_v1_requests.json')
for key in ('profiles', 'bindings', 'dependencies'):
    assert native[key] == policy[key], key
for p, digest in native['dependencies'].items():
    assert sha(root / p) == digest, p
for p, digest in native['previousFixtureSha256'].items():
    assert sha(root / p) == digest, p
content = Path('../GASP58/Content')
for p, digest in native['assetSha256'].items():
    assert sha(content / (p.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, p
source = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
for p, digest in native['probeSourceSha256'].items():
    assert sha(source / p) == digest, p
    for tree in ('source', 'package'):
        assert sha(repo / 'artifacts/unreal/gasp58-lyra-masks' / tree /
                   'AlsV4AssetExporter/Source/AlsV4AssetExporter' / p) == digest, (tree, p)
counts = dict(frames=0, frozen=0, profileFrames=0, boneWeights=0, earlyReversal=0)
played = set()
for t, q in zip(native['traces'], requests['traces'], strict=True):
    assert t['hz'] == q['hz'] and len(t['frames']) == len(q['frames'])
    for f in t['frames']:
        counts['frames'] += 1
        for r in f['frozen']:
            played.add(r['asset']); counts['frozen'] += 1
            if r['profile'] >= 0:
                assert len(r['boneWeights']) == 81
                counts['profileFrames'] += 1; counts['boneWeights'] += 81
                counts['earlyReversal'] += 0 < r['startAlpha'] < 1
assert counts == native['counts'] and counts['earlyReversal'] > 0 and played == set(range(45))
source_layout = json.loads((root / 'skeletal_control_defaults.json').read_bytes())['skeletons']['source']['layout']
source_names = source_layout['logicalBoneNames']; source_parents = source_layout['logicalParents']
target_names = policy['boneNames']; target_indices = {n.lower(): i for i, n in enumerate(target_names)}
for profile in policy['profiles']:
    factors = [1] * 81
    omitted = {e['bone'].lower(): e for e in profile['omittedTargetBones']}
    for entry in profile['entries']:
        bone = entry['bone']; scale = entry['scale']; index = target_indices.get(bone.lower())
        if index is not None:
            factors[index] = scale
        else:
            mapping = omitted.pop(bone.lower())
            si = source_names.index(bone)
            while si >= 0 and source_names[si].lower() not in target_indices:
                si = source_parents[si]
            assert si >= 0 and mapping['ancestor'] == source_names[si]
            ti = target_indices[source_names[si].lower()]
            assert mapping['targetIndex'] == ti and mapping['scale'] == scale == profile['factors'][ti]
    assert not omitted and factors == profile['factors']
checks = {}
for name in ('lyra-montage-blend-ue.log', 'lyra-montage-blend-repeat-ue.log'):
    text = (logs / name).read_text(encoding='utf-8-sig', errors='replace')
    assert text.count('LYRA_MONTAGE_BLEND_NATIVE_OK frames=') == 1
    assert 'LYRA_EXPORT_PROCESS_EXIT_OK mode=montage-blend code=0' in text
    assert not re.search(r'LogPython: Error|Assertion failed|Ensure condition failed|LYRA_MONTAGE_BLEND_FAILED', text)
    checks[name] = dict(warnings=len(re.findall(r'Warning:', text)), exit=0)
for name in ('montage-blend-debug.log', 'montage-blend-optimize.log'):
    text = (logs / name).read_text(encoding='utf-8-sig', errors='replace')
    assert re.search(r'0\s*(个警告|Warning)', text) and re.search(r'0\s*(个错误|Error)', text), name
    checks[name] = dict(warnings=0, errors=0)
for name in ('montage-blend-godot.log', 'montage-blend-godot-optimize.log'):
    text = (logs / name).read_text(encoding='utf-8-sig', errors='replace')
    assert text.count('LYRA_MONTAGE_BLEND_GODOT_OK frames=13440') == 1 and 'LYRA_GODOT_PROCESS_EXIT_OK code=0' in text
    assert not re.search(r'ERROR:|WARNING:', text)
    checks[name] = dict(exit=0, exact=True)
optimized = (logs / 'montage-blend-godot-optimize.log').read_text(encoding='utf-8-sig')
for assembly in ('GodotALS.dll', 'Als.Core.dll', 'Als.Import.dll'):
    digest = sha(repo / '.godot/mono/temp/bin/ExportRelease' / assembly).upper()
    assert f'LYRA_OPTIMIZED_ASSEMBLY {assembly} SHA256={digest}' in optimized
    assert sha(repo / '.godot/mono/temp/bin/Debug' / assembly) == sha(logs / 'montage-blend-debug-assemblies' / assembly)
for name in ('montage-blend-slots-regression.log', 'montage-blend-slots-v1-regression.log', 'montage-blend-resources-regression.log'):
    text = (logs / name).read_text(encoding='utf-8-sig', errors='replace')
    marker = 'LYRA_MONTAGE_SLOTS_GODOT_OK' if 'slots' in name else 'LYRA_MONTAGE_RESOURCES_GODOT_OK'
    assert marker in text and 'LYRA_GODOT_PROCESS_EXIT_OK code=0' in text and not re.search(r'ERROR:|WARNING:', text)
    checks[name] = dict(exit=0)
trx = (logs / 'montage-blend-core.trx').read_text(encoding='utf-8-sig')
assert 'failed="0"' in trx and 'passed="214"' in trx
build = (logs / 'lyra-montage-blend-build-complete.log').read_text(encoding='utf-8-sig', errors='replace')
assert 'BUILD SUCCESSFUL' in build and 'GODOT_ALS_EXTERNAL_EXPORTER_OK' in build and 'error C' not in build
report = dict(counts=counts, mathCases=len(requests['mathCases']), assets=len(native['assetSha256']),
    previous=len(native['previousFixtureSha256']), profiles=policy['profiles'], checks=checks, corePassed=214,
    slotPose=False, production=False, wholeGoal=False)
(logs / 'lyra-montage-blend-verification.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print('LYRA_MONTAGE_BLEND_FINAL_VERIFIED', json.dumps(counts), 'slotPose=false production=false whole_goal=false')
