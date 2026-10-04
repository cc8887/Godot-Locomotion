"""Verify native target binding, full snapshots and actual Main/Jolt runs."""
import hashlib
import json
import re
from pathlib import Path

from locomotion_paths import engine_path, project_path, rig_reference_path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'


def sha(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        while block := stream.read(1024 * 1024):
            digest.update(block)
    return digest.hexdigest()


def read(path):
    data = path.read_bytes()
    if data.startswith((b'\xff\xfe', b'\xfe\xff')):
        return data.decode('utf-16')
    try:
        return data.decode('utf-8-sig')
    except UnicodeDecodeError:
        return data.decode('gb18030')


fixture = json.loads((root / 'rig_reference_v1_native.json').read_bytes())
policy = json.loads((root / 'rig_reference_v1_policy.json').read_bytes())
assert sha(root / 'rig_reference_v1_native.json') == 'feae2406be8327f7ce0bafe91aa1560583163ce7164cad51a9bae16b2e3654f3'
assert sha(root / 'rig_reference_v1_policy.json') == 'f617d206f5477dbbea4dc866b468c3adadd2e08778023846adb389b919bd52e8'
assert len(fixture['previousFixtureSha256']) == 813 and len(fixture['assetSha256']) == 669
for path, digest in fixture['previousFixtureSha256'].items():
    assert sha(root / path) == digest, path
for path, digest in fixture['assetSha256'].items():
    assert sha(project_path('Content') / (path.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, path
for path, digest in policy['dependencies'].items():
    assert sha(root / path) == digest, path
assert policy['mapping'] == fixture['profiles'][1]['mapping']
assert sum(row['imported'] for row in policy['mapping']) == 89
assert sum(row['targetIndex'] >= 0 for row in policy['mapping']) == 69
assert {row['name'] for row in policy['mapping'] if not row['imported']} == {'ik_ball_l', 'ik_ball_r'}
assert fixture['profiles'][1]['variables']['ThighLength'] == 42.57203674316406
assert fixture['profiles'][1]['variables']['CalfLength'] == 40.19668960571289
source = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
package_root = repo / 'artifacts/unreal/gasp58-lyra-rig-reference'
for path, digest in fixture['probeSourceSha256'].items():
    for base in (source, package_root / 'source/AlsV4AssetExporter/Source/AlsV4AssetExporter',
                 package_root / 'package-ready/AlsV4AssetExporter/Source/AlsV4AssetExporter',
                 rig_reference_path('Plugins/AlsV4AssetExporter/Source/AlsV4AssetExporter')):
        assert sha(base / path) == digest, (base, path)
traversal = json.loads((root / 'rig_traversal_v1_native.json').read_bytes())
for path, digest in traversal['probeSourceSha256'].items():
    for base in (source, repo / 'artifacts/unreal/gasp58-lyra-masks/source/AlsV4AssetExporter/Source/AlsV4AssetExporter',
                 repo / 'artifacts/unreal/gasp58-lyra-masks/package/AlsV4AssetExporter/Source/AlsV4AssetExporter'):
        assert sha(base / path) == digest, (base, path)
engine = engine_path('Engine')
modules = json.loads((package_root / 'package-ready/AlsV4AssetExporter/Binaries/Win64/UnrealEditor.modules').read_bytes())
assert modules['BuildId'] == json.loads((engine / 'Binaries/Win64/UnrealEditor.modules').read_bytes())['BuildId']
binary = 'Binaries/Win64/UnrealEditor-AlsV4AssetExporter.dll'
assert sha(package_root / 'package-ready/AlsV4AssetExporter' / binary) == sha(rig_reference_path('Plugins/AlsV4AssetExporter') / binary)
assert 'Result: Succeeded' in read(logs / 'rig-reference-plugin-build-bridge.log')
for filename in ('rig-reference-ue-fixed.log', 'rig-reference-ue-repeat.log'):
    text = read(logs / filename)
    assert text.count('LYRA_RIG_REFERENCE_NATIVE_OK profiles=2 bones=91 mapped=69 elements=98 protectedPackages=669 protectedJson=813 assets_saved=0') == 1
    assert ': Error:' not in text and 'LYRA_REFERENCE_UE_EXIT code=0 projectUnchanged=True' in text
    assert 'gasp58-lyra-rig-reference/package-ready/AlsV4AssetExporter/Binaries/Win64/UnrealEditor-AlsV4AssetExporter.dll' in text

for filename in ('rig-reference-debug-build-final.log', 'rig-reference-optimize-build-final.log'):
    text = read(logs / filename)
    assert '0 个警告' in text and '0 个错误' in text
snapshot_marker = 'LYRA_RIG_REFERENCE_GODOT_OK profiles=2 elements=98 transforms=4200 retries=2 rejected=2 maxVector=0 maxQuaternion=0 targetReference=true production=false'
expected = dict(frames=2520, poses=2484, retry=2520, hits=30744, misses=2976, geometry=30798,
                initialOverlaps=9, lateFailures=21, rejected=2716, covered=2358,
                partial=468, disabled=387, opposingBoxNormals=30744, profiles=3, hz=[30, 60, 120],
                actualGodotPhysics=True, reference='AlsCompactReference', initializations=18,
                nativePhysicsParity=False, ordinaryDemo=False, production=False)
runs = []
for configuration, logfile, reportfile in (
    ('Debug', 'rig-reference-scene-debug-final.log', 'rig-reference-scene-debug-final.json'),
    ('Optimize', 'rig-reference-godot-optimize.log', 'rig-reference-scene-optimize.json')):
    text = read(logs / logfile)
    assert 'Godot Engine v4.7.2.stable.mono.official.ed1daf0bf' in text
    assert not any(line.lstrip().startswith(('ERROR:', 'WARNING:')) for line in text.splitlines()), logfile
    result = json.loads((logs / reportfile).read_bytes())
    markers = re.findall(r'LYRA_RIG_SCENE_COLLISION_GODOT_OK (\{[^\r\n]+\})', text)
    assert len(markers) == 1 and json.loads(markers[0]) == result
    for key, value in expected.items():
        assert result[key] == value, (configuration, key)
    assert result['maxPlaneCm'] <= .02 and result['maxNormal'] <= 1e-4
    if configuration == 'Debug':
        assert text.rstrip().endswith('LYRA_GODOT_PROCESS_EXIT code=0')
        reference_text = read(logs / 'rig-reference-godot-debug-final.log')
        assert reference_text.count(snapshot_marker) == 1 and reference_text.rstrip().endswith('LYRA_GODOT_PROCESS_EXIT code=0')
    else:
        assert text.count(snapshot_marker) == 1
        for scene in ('lyra_rig_reference_smoke', 'lyra_rig_scene_collision_smoke', 'lyra_rig_output_smoke'):
            assert text.count(f'LYRA_GODOT_PROCESS_EXIT scene={scene} code=0') == 1
        assert text.count('LYRA_GODOT_PROCESS_EXIT scene=') == 3
        assert text.rstrip().endswith('LYRA_DEBUG_ASSEMBLIES_RESTORED hashVerified=true')
        hashes = re.findall(r'LYRA_OPTIMIZED_ASSEMBLY (\S+) SHA256=([0-9A-F]{64})', text)
        assert len(hashes) == 3
        for name, digest in hashes:
            assert sha(repo / '.godot/mono/temp/bin/ExportRelease' / name) == digest.lower()
        for assembly in ('GodotALS', 'Als.Core', 'Als.Import'):
            for extension in ('.dll', '.pdb'):
                filename = assembly + extension
                assert sha(repo / '.godot/mono/temp/bin/Debug' / filename) == sha(logs / 'rig-reference-final-debug-assemblies' / filename)
        regression = re.search(r'LYRA_RIG_OUTPUT_GODOT_OK frames=2520 outputs=2154 bones=174474 .*comparisons=10279440 channelChecks=43080 maxVector=(\S+) maxRotation=(\S+)', text)
        assert regression and float(regression[1]) <= 1e-8 and float(regression[2]) <= 1e-10
    runs.append(dict(configuration=configuration, result=result))
assert runs[0]['result'] == runs[1]['result']
report = dict(schemaVersion=1, targetReferenceBindingNativeAccepted=True,
              fullNativeConstructionSnapshotsAccepted=True, targetMainGodotPhysicsAccepted=True,
              targetFullForwardSolveNativeAccepted=False, targetWholeMainNativeAccepted=False,
              fullSkeletonProportionVisualAccepted=False, ordinaryDemoAccepted=False, production=False,
              rigImportedBones=89, rigCreatedBones=2, mappedTargetBones=69,
              protectedPackages=669, protectedJson=813, buildId=modules['BuildId'],
              originalOutputRegressionAccepted=True, debugAssembliesRestored=True,
              nativeSha256=sha(root / 'rig_reference_v1_native.json'),
              policySha256=sha(root / 'rig_reference_v1_policy.json'), variables=fixture['profiles'][1]['variables'], runs=runs)
(logs / 'lyra-rig-reference-verification.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
print('LYRA_RIG_REFERENCE_VERIFIED nativeSnapshots=4200 frames=2520 profiles=3 hz=30/60/120 initializations=18 configurations=Debug/Optimize protectedPackages=669 protectedJson=813 ordinaryDemo=false production=false')
