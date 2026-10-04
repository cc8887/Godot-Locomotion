"""Check immutable oracle provenance and actual solver executions, not intent."""
import hashlib
import json
import re
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'

def sha(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        while block := f.read(1024 * 1024): h.update(block)
    return h.hexdigest()

def read(path):
    data = path.read_bytes()
    if data.startswith((b'\xff\xfe', b'\xfe\xff')): return data.decode('utf-16')
    try: return data.decode('utf-8-sig')
    except UnicodeDecodeError: return data.decode('gb18030')

fixture = json.loads((root / 'rig_solver_v1_native.json').read_bytes())
for p, digest in fixture['dependencies'].items(): assert sha(root / p) == digest, p
traversal = json.loads((root / 'rig_traversal_v1_native.json').read_bytes())
for p, digest in traversal['previousFixtureSha256'].items(): assert sha(root / p) == digest, p
for p, digest in traversal['assetSha256'].items():
    path = Path('../GASP58/Content') / (p.split('.')[0].removeprefix('/Game/') + '.uasset')
    assert sha(path) == digest, p
source = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
for p, digest in traversal['probeSourceSha256'].items():
    assert sha(source / p) == digest, p
    for tree in ('source', 'package'):
        assert sha(repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter' / p) == digest, p

report = dict(schemaVersion=1, fixtureSha256=sha(root / 'rig_solver_v1_native.json'),
    protectedPackages=len(traversal['assetSha256']), protectedJson=len(traversal['previousFixtureSha256']),
    acceptedImmediateUnits=True, inputPoseBoundary='recorded ALS81 input', collisionBoundary='recorded UE hits; computed queries asserted',
    finalPoseAdapterAccepted=False, realGodotCollisionAccepted=False, fullRigPoseAccepted=False, production=False, logs=[])
for name in ('rig-solver-debug-build-final.log', 'rig-solver-optimize-build.log'):
    text = read(logs / name)
    assert '0 个警告' in text and '0 个错误' in text, name
for name in ('rig-solver-godot-debug.log', 'rig-solver-godot-optimize.log'):
    text = read(logs / name)
    marker = 'LYRA_RIG_SOLVER_GODOT_OK frames=2520 solves=2001 visits=683343 retries=2520 sweeps=16008 comparisons=6552576 '
    assert text.count(marker) == 1 and 'LYRA_GODOT_PROCESS_EXIT code=0' in text, name
    assert 'rejected=6 collision=recorded fullRig=false production=false' in text, name
    assert not any(line.lstrip().startswith(('ERROR:', 'WARNING:')) for line in text.splitlines()), name
    match = re.search(r'maxVector=(\S+) maxRotation=(\S+)', text)
    assert match and float(match[1]) <= 1e-8 and float(match[2]) <= 1e-10
    if 'optimize' in name:
        assert text.count('LYRA_OPTIMIZED_ASSEMBLY ') == 3 and 'LYRA_DEBUG_ASSEMBLIES_RESTORED hashVerified=true' in text
        assert 'LYRA_RIG_DYNAMICS_GODOT_OK frames=3360 calls=19217 ' in text and 'LYRA_DYNAMICS_REGRESSION_PROCESS_EXIT code=0' in text
    report['logs'].append(dict(file=name, exit=0, comparisons=6552576, maxVector=float(match[1]), maxRotation=float(match[2])))

# The independent installed IK calculation used the captured Godot inputs
# before the normalization fix. It isolates the kernel from its upstream pose.
inputs = json.loads((logs / 'rig-solver-ik-input-before-retry.json').read_bytes())
outputs = json.loads((logs / 'rig-solver-ik-native-diagnostic.json').read_bytes())
for actual, native in zip(inputs, outputs['calls'], strict=True):
    for bone in 'ABC':
        for field, name, axes in [('p', 'Position', 'XYZ'), ('q', 'Rotation', 'XYZW'), ('s', 'Scale', 'XYZ')]:
            assert [actual['Solved' + bone][name][axis] for axis in axes] == native[bone][field]
text = read(logs / 'rig-solver-ik-native-ue.log')
assert text.count('LYRA_RIG_IK_MATH_DIAGNOSTIC_OK calls=2 protected_json=811 assets_saved=0') == 1
assert text.rstrip().endswith('LYRA_IK_DIAGNOSTIC_PROCESS_EXIT code=0') and 'Error:' not in text
assert 'BUILD SUCCESSFUL' in read(logs / 'rig-solver-ik-native-build.log')
report['ikDiagnostic'] = dict(calls=2, exact=True, inputsSha256=sha(logs / 'rig-solver-ik-input-before-retry.json'),
    outputSha256=sha(logs / 'rig-solver-ik-native-diagnostic.json'), protectedJson=811, sourceSha256={
        p: sha(source / p) for p in ('Private/AlsLyraRigIkMathLibrary.cpp', 'Public/AlsLyraRigIkMathLibrary.h')})
(logs / 'lyra-rig-solver-verification.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
print('LYRA_RIG_SOLVER_VERIFIED frames=2520 visits=683343 protectedPackages=%d protectedJson=%d fullRig=false production=false' %
      (report['protectedPackages'], report['protectedJson']))
