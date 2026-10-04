"""Verify ALS-reference continuous native output and actual Godot Main physics."""
import hashlib
import json
import re
from pathlib import Path

from locomotion_paths import engine_path, project_path, rig_solver_path

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


def load(name):
    return json.loads((root / name).read_bytes())


pins = {
    'rig_target_v1_solver.json': '17e0561a6975eece54b179212d77271dc3342c4fb1dc87c918403c8141ad8bc5',
    'rig_target_v1_output.json': '961ee780e5e1d5b652d24885829922d6f6c4d7c21157a9524832d20f2f082f1c',
}
for path, digest in pins.items():
    assert sha(root / path) == digest, path
    for dependency, dependency_sha in load(path)['dependencies'].items():
        assert sha(root / dependency) == dependency_sha, dependency
fixture = load('rig_target_v1_native.json')
assert fixture['reference'] == 'AlsCompactReference'
assert len(fixture['previousFixtureSha256']) == 815 and len(fixture['assetSha256']) == 669
for path, digest in fixture['previousFixtureSha256'].items():
    assert sha(root / path) == digest, path
for path, digest in fixture['assetSha256'].items():
    assert sha(project_path('Content') / (path.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, path
for path, digest in fixture['dependencies'].items():
    assert sha(root / path) == digest, path
assert len(fixture['traces']) == 6
for trace in fixture['traces']:
    assert trace['settings']['bSetRefPoseFromSkeleton'] is True
    for frame in trace['frames']:
        assert frame['after']['variables']['ThighLength'] == 42.57203674316406
        assert frame['after']['variables']['CalfLength'] == 40.19668960571289
source = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
package_root = repo / 'artifacts/unreal/gasp58-lyra-rig-target'
for path, digest in fixture['probeSourceSha256'].items():
    for base in (source, package_root / 'source/AlsV4AssetExporter/Source/AlsV4AssetExporter',
                 package_root / 'package/Source/AlsV4AssetExporter',
                 rig_solver_path('Plugins/AlsV4AssetExporter/Source/AlsV4AssetExporter')):
        assert sha(base / path) == digest, (base, path)
for original, bases in (
    ('rig_reference_v1_native.json', (
        source, repo / 'artifacts/unreal/gasp58-lyra-rig-reference/source/AlsV4AssetExporter/Source/AlsV4AssetExporter',
        repo / 'artifacts/unreal/gasp58-lyra-rig-reference/package-ready/AlsV4AssetExporter/Source/AlsV4AssetExporter')),
    ('rig_traversal_v1_native.json', (
        source, repo / 'artifacts/unreal/gasp58-lyra-masks/source/AlsV4AssetExporter/Source/AlsV4AssetExporter',
        repo / 'artifacts/unreal/gasp58-lyra-masks/package/AlsV4AssetExporter/Source/AlsV4AssetExporter')),
):
    for path, digest in load(original)['probeSourceSha256'].items():
        for base in bases:
            assert sha(base / path) == digest, (base, path)
engine = engine_path('Engine')
modules = json.loads((package_root / 'package/Binaries/Win64/UnrealEditor.modules').read_bytes())
assert modules['BuildId'] == json.loads((engine / 'Binaries/Win64/UnrealEditor.modules').read_bytes())['BuildId']
binary = 'Binaries/Win64/UnrealEditor-AlsV4AssetExporter.dll'
assert sha(package_root / 'package' / binary) == sha(rig_solver_path('Plugins/AlsV4AssetExporter') / binary)
assert 'Result: Succeeded' in read(logs / 'rig-target-plugin-build.log')
native_marker = ('LYRA_RIG_TARGET_NATIVE_OK frames=2520 poses=2154 solves=2001 visits=683343 sweeps=16008 '
                 'leftHits=2313 rightHits=2303 protectedPackages=669 protectedJson=815 assets_saved=0')
for filename in ('rig-target-ue-first.log', 'rig-target-ue-repeat.log'):
    text = read(logs / filename)
    assert text.count(native_marker) == 1 and ': Error:' not in text
    assert text.rstrip().endswith('LYRA_TARGET_UE_EXIT code=0 projectUnchanged=True')
    assert 'gasp58-lyra-rig-target/package/Binaries/Win64/UnrealEditor-AlsV4AssetExporter.dll' in text
for filename in ('rig-target-debug-build-final.log', 'rig-target-optimize-build-final.log'):
    text = read(logs / filename)
    assert '0 个警告' in text and '0 个错误' in text
runs = []
for configuration, filename in (('Debug', 'rig-target-godot-debug-final.log'),
                                ('Optimize', 'rig-target-godot-optimize.log')):
    text = read(logs / filename)
    assert 'Godot Engine v4.7.2.stable.mono.official.ed1daf0bf' in text
    assert not any(line.lstrip().startswith(('ERROR:', 'WARNING:')) for line in text.splitlines()), filename
    scene_names = ['lyra_rig_target_output_smoke', 'lyra_rig_output_smoke', 'lyra_rig_reference_smoke']
    if configuration == 'Optimize':
        scene_names.append('lyra_rig_scene_collision_smoke')
    for scene in scene_names:
        assert text.count(f'LYRA_GODOT_PROCESS_EXIT scene={scene} code=0') == 1
    target = re.search(r'LYRA_RIG_TARGET_OUTPUT_GODOT_OK frames=2520 outputs=2154 bones=174474 partial=156 disabled=153 '
                       r'solves=2001 visits=683343 retries=2520 sweeps=32200 comparisons=14449584 channelChecks=43080 '
                       r'maxVector=(\S+) maxRotation=(\S+)', text)
    original = re.search(r'LYRA_RIG_OUTPUT_GODOT_OK frames=2520 outputs=2154 bones=174474 .*'
                         r'comparisons=10279440 channelChecks=43080 maxVector=(\S+) maxRotation=(\S+)', text)
    assert target and original
    for match in (target, original):
        assert float(match[1]) <= 1e-8 and float(match[2]) <= 1e-10
    assert text.count('LYRA_RIG_REFERENCE_GODOT_OK profiles=2 elements=98 transforms=4200 ') == 1
    if configuration == 'Optimize':
        assert text.rstrip().endswith('LYRA_DEBUG_ASSEMBLIES_RESTORED hashVerified=true')
        hashes = re.findall(r'LYRA_OPTIMIZED_ASSEMBLY (\S+) SHA256=([0-9A-F]{64})', text)
        assert len(hashes) == 3
        for name, digest in hashes:
            assert sha(repo / '.godot/mono/temp/bin/ExportRelease' / name) == digest.lower()
        for assembly in ('GodotALS', 'Als.Core', 'Als.Import'):
            for extension in ('.dll', '.pdb'):
                name = assembly + extension
                assert sha(repo / '.godot/mono/temp/bin/Debug' / name) == sha(logs / 'rig-target-final-debug-assemblies' / name)
    runs.append(dict(configuration=configuration, maxPositionCm=float(target[1]), maxQuaternion=float(target[2])))
physics = []
for configuration, logfile, reportfile in (
    ('Debug', 'rig-target-scene-debug-final.log', 'rig-target-scene-debug-final.json'),
    ('Optimize', 'rig-target-godot-optimize.log', 'rig-target-scene-optimize.json')):
    text = read(logs / logfile)
    assert not any(line.lstrip().startswith(('ERROR:', 'WARNING:')) for line in text.splitlines()), logfile
    result = json.loads((logs / reportfile).read_bytes())
    markers = re.findall(r'LYRA_RIG_SCENE_COLLISION_GODOT_OK (\{[^\r\n]+\})', text)
    assert len(markers) == 1 and json.loads(markers[0]) == result
    expected = dict(frames=2520, poses=2484, retry=2520, hits=30744, misses=2976, geometry=30798,
                    initialOverlaps=9, lateFailures=21, rejected=2716, covered=2358, partial=468,
                    disabled=387, opposingBoxNormals=30744, profiles=3, hz=[30, 60, 120],
                    actualGodotPhysics=True, reference='AlsCompactReference', initializations=18,
                    nativePhysicsParity=False, ordinaryDemo=False, production=False)
    for key, value in expected.items():
        assert result[key] == value, (configuration, key)
    assert result['maxPlaneCm'] <= .02 and result['maxNormal'] <= 1e-4
    if configuration == 'Debug':
        assert text.rstrip().endswith('LYRA_GODOT_PROCESS_EXIT code=0')
    physics.append(result)
assert physics[0] == physics[1]
report = dict(schemaVersion=1, targetReferenceFullForwardSolveNativeAccepted=True,
              originalOutputRegressionAccepted=True, targetMainGodotPhysicsAccepted=True,
              targetWholeMainNativeAccepted=False, nativePhysicsParity=False,
              ordinaryDemoAccepted=False, visualAccepted=False, production=False,
              protectedPackages=669, protectedJson=815, buildId=modules['BuildId'],
              nativeSha256=sha(root / 'rig_target_v1_native.json'), fixtureSha256=pins,
              runs=runs, physics=physics[0], debugAssembliesRestored=True)
(logs / 'lyra-rig-target-verification.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
print('LYRA_RIG_TARGET_VERIFIED frames=2520 outputs=2154 bones=174474 comparisons=14449584 '
      'configurations=Debug/Optimize physicsFrames=2520 initializations=18 '
      'protectedPackages=669 protectedJson=815 ordinaryDemo=false production=false')
