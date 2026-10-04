"""Check actual Godot collision runs, restored binaries and pinned provenance."""
import hashlib
import json
import re
from pathlib import Path

from locomotion_paths import engine_path, project_path

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


expected = dict(frames=2520, poses=2484, retry=2520, hits=30744, misses=2976,
                geometry=30798, initialOverlaps=9, lateFailures=21, rejected=2716,
                covered=2358, partial=468, disabled=387, opposingBoxNormals=30744,
                profiles=3, hz=[30, 60, 120], actualGodotPhysics=True,
                nativePhysicsParity=False, ordinaryDemo=False, production=False)
runs = []
for configuration, name in (('Debug', 'debug'), ('Optimize', 'optimize')):
    build = read(logs / f'rig-collision-{name}-build-final.log')
    assert '0 个警告' in build and '0 个错误' in build, configuration
    saved = json.loads((logs / f'rig-collision-scene-{name}-final.json').read_bytes())
    log_name = 'rig-collision-scene-debug-final.log' if name == 'debug' else 'rig-collision-godot-optimize.log'
    output = read(logs / log_name)
    assert 'Godot Engine v4.7.2.stable.mono.official.ed1daf0bf' in output, log_name
    assert not any(line.lstrip().startswith(('ERROR:', 'WARNING:')) for line in output.splitlines()), log_name
    matches = re.findall(r'LYRA_RIG_SCENE_COLLISION_GODOT_OK (\{[^\r\n]+\})', output)
    assert len(matches) == 1 and json.loads(matches[0]) == saved, log_name
    for key, value in expected.items():
        assert saved[key] == value, (configuration, key, saved[key])
    assert 0 <= saved['maxPlaneCm'] <= .02 and 0 <= saved['maxNormal'] <= 1e-4
    if name == 'debug':
        assert output.rstrip().endswith('LYRA_GODOT_PROCESS_EXIT code=0')
    else:
        for scene in ('lyra_rig_scene_collision_smoke', 'lyra_rig_output_smoke'):
            assert output.count(f'LYRA_GODOT_PROCESS_EXIT scene={scene} code=0') == 1
        assert output.count('LYRA_GODOT_PROCESS_EXIT scene=') == 2
        assert output.rstrip().endswith('LYRA_DEBUG_ASSEMBLIES_RESTORED hashVerified=true')
        assemblies = re.findall(r'LYRA_OPTIMIZED_ASSEMBLY (\S+) SHA256=([0-9A-F]{64})', output)
        assert {name for name, _ in assemblies} == {'GodotALS.dll', 'Als.Core.dll', 'Als.Import.dll'}
        assert len(assemblies) == 3
        for assembly, digest in assemblies:
            assert sha(repo / '.godot/mono/temp/bin/ExportRelease' / assembly) == digest.lower()
        for assembly in ('GodotALS', 'Als.Core', 'Als.Import'):
            for extension in ('.dll', '.pdb'):
                filename = assembly + extension
                assert sha(repo / '.godot/mono/temp/bin/Debug' / filename) == sha(logs / 'rig-collision-final-debug-assemblies' / filename)
        original = re.search(r'LYRA_RIG_OUTPUT_GODOT_OK frames=2520 outputs=2154 bones=174474 .*comparisons=10279440 channelChecks=43080 maxVector=(\S+) maxRotation=(\S+)', output)
        assert original and float(original[1]) <= 1e-8 and float(original[2]) <= 1e-10
    runs.append(dict(configuration=configuration, log=log_name, result=saved))
assert runs[0]['result'] == runs[1]['result']

for filename in ('rig_output_v1_native.json', 'rig_pose_v1_policy.json'):
    fixture = json.loads((root / filename).read_bytes())
    for path, digest in fixture['dependencies'].items():
        assert sha(root / path) == digest, path
policy = json.loads((root / 'rig_pose_v1_policy.json').read_bytes())
assert policy['descriptorSourceSha256'] == sha(root / 'footplant_rig_inputs_v1_input.json')
assert sha(root / 'footplant_rig_ground_v2_native.json') == '3ddc3ba4b9851a3e6cba3d5ae0b3c5ca64c30c98f75be86e4bb32417e5008ec9'
traversal = json.loads((root / 'rig_traversal_v1_native.json').read_bytes())
assert len(traversal['previousFixtureSha256']) == 808 and len(traversal['assetSha256']) == 669
for path, digest in traversal['previousFixtureSha256'].items():
    assert sha(root / path) == digest, path
for path, digest in traversal['assetSha256'].items():
    asset = project_path('Content') / (path.split('.')[0].removeprefix('/Game/') + '.uasset')
    assert sha(asset) == digest, path
source = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
for path, digest in traversal['probeSourceSha256'].items():
    assert sha(source / path) == digest, path
    for tree in ('source', 'package'):
        assert sha(repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter' / path) == digest, path

engine = engine_path('Engine')
sources = (
    ('Plugins/Animation/ControlRig/Source/ControlRig/Private/Units/Collision/RigUnit_WorldCollision.cpp', ('SweepSingleByChannel', 'InverseTransformVector(HitResult.ImpactNormal)', 'FCollisionShape::MakeSphere(Radius)')),
    ('Source/Runtime/Engine/Private/Collision/CollisionConversions.cpp', ('FindGeomOpposingNormal', 'FindGeometryOpposingNormal')),
    ('Source/Runtime/Experimental/ChaosCore/Public/Chaos/AABB.h', ('FindGeometryOpposingNormal', 'KINDA_SMALL_NUMBER')),
)
native_sources = []
for path, tokens in sources:
    text = read(engine / path)
    assert all(token in text for token in tokens), path
    native_sources.append(dict(path=str(engine / path), sha256=sha(engine / path)))
report = dict(schemaVersion=1, realFinalRigGodotPhysicsAccepted=True,
              nativePhysicsParity=False, providerAlternativeFootPlacementPhysicsAccepted=False,
              ordinaryDemoAccepted=False, targetProportionProfileAccepted=False,
              production=False, godotVersion='4.7.2.stable.mono.official.ed1daf0bf',
              physicsBackend='Jolt Physics', protectedJson=808, protectedPackages=669,
              originalOutputRegressionAccepted=True, debugAssembliesRestored=True,
              nativeSourceEvidence=native_sources, runs=runs)
assert '3d/physics_engine="Jolt Physics"' in read(repo / 'project.godot')
(logs / 'lyra-rig-collision-verification.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
print('LYRA_RIG_COLLISION_VERIFIED frames=2520 profiles=3 hz=30/60/120 configurations=Debug/Optimize protectedPackages=669 protectedJson=808 nativePhysicsParity=false ordinaryDemo=false production=false')
