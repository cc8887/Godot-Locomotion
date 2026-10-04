"""Read target reference binding and Construction on transient native Rigs."""
import hashlib
import json
import os
from pathlib import Path
import unreal

repo = Path(__file__).resolve().parents[2]
root = Path(os.environ['LYRA_OUTPUT_ROOT'])
prefix = 'rig_reference_v1'
sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
load = lambda name: json.loads((root / name).read_bytes())
previous = {str(path.relative_to(root)).replace('\\', '/'): sha(path)
            for path in root.rglob('*.json') if not path.name.startswith(prefix)}
destination = root / (prefix + '_native.json')
if destination.exists():
    previous = load(destination.name)['previousFixtureSha256']
graph = load('footplant_rig_graph_v1.json')
packages = dict(load('rig_traversal_v1_native.json')['assetSha256'])
content = Path(unreal.Paths.project_content_dir())


def protect():
    for name, digest in previous.items():
        assert sha(root / name) == digest, name
    for path, digest in packages.items():
        assert sha(content / (path.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, path


protect()
calibration = load('logical_controls/calibration.json')
cal = calibration['calibration']
source = unreal.load_asset(cal['sourceMesh'])
target = unreal.load_asset(cal['targetMesh'])
skeleton = unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(
    source.get_editor_property('skeleton'), target.get_editor_property('skeleton'),
    unreal.Quat(*cal['handBasis']['rotation']))
assert skeleton is not None
text = unreal.AlsLyraRigReferenceLibrary.read_reference(unreal.load_class(None, graph['rig'] + '_C'), skeleton)
assert text
result = json.loads(text)
assert len(result['profiles']) == 2
for profile in result['profiles']:
    assert len(profile['mapping']) == 91 and len(profile['afterConstruction']) == 98
    assert profile['variables'] == profile['repeatedVariables']
    for row in profile['mapping']:
        index = row['targetIndex']
        expected = next((i for i, name in enumerate(calibration['layout']['logicalBoneNames'])
                         if name.lower() == row['name'].lower()), -1)
        assert index == expected, row
    assert sum(row['targetIndex'] >= 0 for row in profile['mapping']) == 69
    assert {row['name'] for row in profile['mapping'] if not row['imported']} == {'ik_ball_l', 'ik_ball_r'}
    assert all(row['imported'] or row['targetIndex'] == -1 for row in profile['mapping'])
assert result['profiles'][0]['variables']['ThighLength'] == 45.752037048339844
assert result['profiles'][1]['variables']['ThighLength'] == 42.57203674316406
assert result['profiles'][1]['variables']['CalfLength'] == 40.19668960571289
probe_root = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
probe_paths = ['Private/AlsLyraRigReferenceLibrary.cpp', 'Public/AlsLyraRigReferenceLibrary.h']
result.update(schemaVersion=1, previousFixtureSha256=previous, assetSha256=packages,
              probeSourceSha256={name: sha(probe_root / name) for name in probe_paths})
if destination.exists():
    assert json.loads(destination.read_bytes()) == result, 'Preserve immutable reference capture.'
else:
    destination.write_text(json.dumps(result, separators=(',', ':'), allow_nan=False), encoding='utf-8')
policy = dict(schemaVersion=1, name='ALS81CompactReference-v1',
              binding='SetBoneInitialTransformsFromCompactPose',
              dependencies={name: sha(root / name) for name in (
                  'logical_controls/calibration.json', 'footplant_rig_graph_v1.json',
                  'rig_traversal_v1_program.json', 'rig_control_settings_v1.json')},
              mapping=result['profiles'][1]['mapping'])
policy_path = root / (prefix + '_policy.json')
if policy_path.exists():
    assert json.loads(policy_path.read_bytes()) == policy
else:
    policy_path.write_text(json.dumps(policy, separators=(',', ':')), encoding='utf-8')
protect()
unreal.log('LYRA_RIG_REFERENCE_NATIVE_OK profiles=2 bones=91 mapped=69 elements=98 protectedPackages=%d protectedJson=%d assets_saved=0' % (len(packages), len(previous)))
