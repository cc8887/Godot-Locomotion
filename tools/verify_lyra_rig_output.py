"""Verify original full Rig outputs and the enclosing host's actual runs."""
import hashlib
import json
import re
from pathlib import Path

from locomotion_paths import project_path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'

def sha(path):
    digest = hashlib.sha256()
    with path.open('rb') as f:
        while block := f.read(1024 * 1024): digest.update(block)
    return digest.hexdigest()

def read(path):
    data = path.read_bytes()
    if data.startswith((b'\xff\xfe', b'\xfe\xff')): return data.decode('utf-16')
    try: return data.decode('utf-8-sig')
    except UnicodeDecodeError: return data.decode('gb18030')

for name in ('rig_output_v1_native.json', 'rig_pose_v1_policy.json'):
    fixture = json.loads((root / name).read_bytes())
    for p, digest in fixture['dependencies'].items(): assert sha(root / p) == digest, p
policy = json.loads((root / 'rig_pose_v1_policy.json').read_bytes())
assert policy['descriptorSourceSha256'] == sha(root / 'footplant_rig_inputs_v1_input.json')
assert sha(root / 'footplant_rig_ground_v2_native.json') == '3ddc3ba4b9851a3e6cba3d5ae0b3c5ca64c30c98f75be86e4bb32417e5008ec9'
traversal = json.loads((root / 'rig_traversal_v1_native.json').read_bytes())
for p, digest in traversal['previousFixtureSha256'].items(): assert sha(root / p) == digest, p
for p, digest in traversal['assetSha256'].items():
    path = project_path('Content') / (p.split('.')[0].removeprefix('/Game/') + '.uasset')
    assert sha(path) == digest, p
source = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
for p, digest in traversal['probeSourceSha256'].items():
    assert sha(source / p) == digest, p
    for tree in ('source', 'package'):
        assert sha(repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter' / p) == digest, p

for name in ('rig-output-debug-build-main-fixed.log', 'rig-output-optimize-build.log'):
    text = read(logs / name)
    assert '0 个警告' in text and '0 个错误' in text, name
report = dict(schemaVersion=1, protectedJson=len(traversal['previousFixtureSha256']), protectedPackages=len(traversal['assetSha256']),
    originalRigOutputAccepted=True, inputPoseBoundary='recorded original ALS81 source input',
    rigCollisionBoundary='recorded UE hits; computed requests asserted',
    mainHostIntegrated=True, mainCollisionBoundary='analytic plane', nativeWholeMainAccepted=False,
    realGodotCollisionAccepted=False, ordinaryDemoAccepted=False, targetProportionProfileAccepted=False, production=False,
    fixtureSha256=sha(root / 'rig_output_v1_native.json'), policySha256=sha(root / 'rig_pose_v1_policy.json'), runs=[])
marker = 'LYRA_RIG_OUTPUT_GODOT_OK frames=2520 outputs=2154 bones=174474 partial=156 disabled=153 solves=2001 visits=683343 retries=2520 '
main = 'LYRA_MAIN_RIG_HOST_GODOT_OK frames=7560 poses=7296 retry=7560 changed=5817 partial=2097 disabled=1479 hidden=264 coveredLocomotion=4689 faults=63 rejected=891 repeated=75 sweeps=188184 '
for name in ('rig-output-godot-host-fixed.log', 'rig-output-main-godot-fixed.log', 'rig-output-godot-optimize.log'):
    text = read(logs / name)
    assert not any(line.lstrip().startswith(('ERROR:', 'WARNING:')) for line in text.splitlines()), name
    if 'main-godot' not in name:
        assert text.count(marker) == 1, name
        match = re.search(r'LYRA_RIG_OUTPUT_GODOT_OK .*maxVector=(\S+) maxRotation=(\S+)', text)
        assert match and float(match[1]) <= 1e-8 and float(match[2]) <= 1e-10, name
        assert 'comparisons=10279440 channelChecks=43080' in text, name
        report['runs'].append(dict(file=name, rigFrames=2520, poses=2154, bones=174474,
                                  maxVector=float(match[1]), maxRotation=float(match[2])))
    if 'host-fixed' not in name:
        assert text.count(main) == 1 and 'collision=analytic nativeWholeMain=false production=false' in text, name
    if 'optimize' in name:
        assert text.count('LYRA_OPTIMIZED_ASSEMBLY ') == 3
        assert text.count('LYRA_GODOT_PROCESS_EXIT scene=') == 3 and 'code=1' not in text
        assert 'LYRA_GODOT_PROCESS_EXIT scene=lyra_rig_output_smoke code=0' in text
        assert 'LYRA_GODOT_PROCESS_EXIT scene=lyra_main_rig_pose_host_smoke code=0' in text
        assert 'LYRA_GODOT_PROCESS_EXIT scene=lyra_rig_solver_smoke code=0' in text
        assert 'LYRA_RIG_SOLVER_GODOT_OK frames=2520 solves=2001 visits=683343 retries=2520 sweeps=16008 comparisons=6552576 ' in text
        assert text.rstrip().endswith('LYRA_DEBUG_ASSEMBLIES_RESTORED hashVerified=true')
    else:
        assert text.rstrip().endswith('LYRA_GODOT_PROCESS_EXIT code=0'), name
report['mainRuns'] = dict(configurations=['Debug', 'Optimize'], frames=7560, poses=7296,
                         retry=7560, lateCollisionFailures=63, profiles=3, hz=[30,60,120], repeatedEnclosingEvaluations=75)
(logs / 'lyra-rig-output-verification.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
print('LYRA_RIG_OUTPUT_VERIFIED frames=2520 poses=2154 mainFrames=7560 protectedPackages=%d protectedJson=%d realCollision=false production=false' %
      (report['protectedPackages'], report['protectedJson']))
