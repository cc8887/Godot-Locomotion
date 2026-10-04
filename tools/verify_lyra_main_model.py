"""Verify live Main-to-ALS model publication; whole-Main native acceptance stays open."""
import hashlib
import json
import re
from pathlib import Path

from PIL import Image, ImageStat

repo = Path(__file__).resolve().parents[1]
assets = repo / 'assets/generated/lyra_als'
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


def clean(text):
    assert 'Godot Engine v4.7.2.stable.mono.official.ed1daf0bf' in text
    assert not re.search(r'(?m)^\s*(ERROR|WARNING):', text)


def check(result, profile, hz, captures=0):
    assert result['profile'] == profile and result['hz'] == hz
    assert result['frames'] == result['published'] == result['retries'] == hz * 8
    assert result['skinBones'] == 68 and result['logicalBones'] == 81
    assert result['finalRig'] and result['actualCharacterMovement'] and result['actualGodotPhysics']
    assert result['states'] == [0, 1, 2, 3, 4, 6, 7, 8, 10, 11]
    assert result['queries'] == {30: 6656, 60: 13376, 120: 26784}[hz]
    assert result['lateFailures'] == {30: 3, 60: 5, 120: 10}[hz]
    assert result['air'] == {30: 33, 60: 67, 120: 133}[hz]
    assert result['crouch'] == hz * 6 // 5 and result['aim'] == hz
    assert result['maxSkinPositionM'] == result['maxSkinQuaternion'] == 0
    assert result['maxWorldPositionM'] <= 1e-4
    assert result['captures'] == captures
    assert result['nativeWholeMainParity'] is False and result['productionAccepted'] is False


pins = {
    'rig_reference_v1_native.json': 'feae2406be8327f7ce0bafe91aa1560583163ce7164cad51a9bae16b2e3654f3',
    'rig_reference_v1_policy.json': 'f617d206f5477dbbea4dc866b468c3adadd2e08778023846adb389b919bd52e8',
    'rig_target_v1_native.json': '667e163867ee5acbd473bd0b59f1b8f561518c3372de65fdfedb10be53c5c224',
    'rig_target_v1_solver.json': '17e0561a6975eece54b179212d77271dc3342c4fb1dc87c918403c8141ad8bc5',
    'rig_target_v1_output.json': '961ee780e5e1d5b652d24885829922d6f6c4d7c21157a9524832d20f2f082f1c',
}
for name, digest in pins.items():
    assert sha(assets / name) == digest, name
reference = json.loads((assets / 'rig_reference_v1_native.json').read_bytes())
assert len(reference['previousFixtureSha256']) == 813 and len(reference['assetSha256']) == 669
protected = reference['previousFixtureSha256'] | pins
assert len(protected) == 818
for name, digest in protected.items():
    assert sha(assets / name) == digest, name
for name, digest in reference['assetSha256'].items():
    path = Path('../GASP58/Content') / (name.split('.')[0].removeprefix('/Game/') + '.uasset')
    assert sha(path) == digest, name

for filename in ('main-model-demo-build-profile.log', 'main-model-optimize-build.log'):
    text = read(logs / filename)
    assert '0 个警告' in text and '0 个错误' in text, filename

optimized = read(logs / 'main-model-godot-optimize.log')
clean(optimized)
assert optimized.rstrip().endswith('LYRA_DEBUG_ASSEMBLIES_RESTORED hashVerified=true')
optimized_reports = [json.loads(value) for value in re.findall(r'LYRA_MAIN_MODEL_GODOT_OK (\{[^\r\n]+\})', optimized)]
assert len(optimized_reports) == 9
optimized_keys = {(r['profile'], r['hz']): r for r in optimized_reports}
assert len(optimized_keys) == 9
runs = []
for profile in ('unarmed', 'pistol', 'rifle'):
    for hz in (30, 60, 120):
        name = f'main-model-debug-{profile}-{hz}'
        text = read(logs / (name + '.log'))
        clean(text)
        assert text.rstrip().endswith('LYRA_GODOT_PROCESS_EXIT code=0')
        values = re.findall(r'LYRA_MAIN_MODEL_GODOT_OK (\{[^\r\n]+\})', text)
        assert len(values) == 1
        debug = json.loads((logs / (name + '.json')).read_bytes())
        assert debug == json.loads(values[0])
        check(debug, profile, hz)
        release = json.loads((logs / f'main-model-optimize-{profile}-{hz}.json').read_bytes())
        assert release == optimized_keys[(profile, hz)]
        check(release, profile, hz)
        assert optimized.count(f'LYRA_GODOT_PROCESS_EXIT profile={profile} hz={hz} code=0') == 1
        # Single-precision scene-world transforms can differ by a few ULPs.
        assert {k: v for k, v in debug.items() if k != 'maxWorldPositionM'} == {
            k: v for k, v in release.items() if k != 'maxWorldPositionM'}
        runs.extend((dict(configuration='Debug', **debug), dict(configuration='Optimize', **release)))

assemblies = {}
for name, digest in re.findall(r'LYRA_OPTIMIZED_ASSEMBLY (\S+) SHA256=([0-9A-F]{64})', optimized):
    assert sha(repo / '.godot/mono/temp/bin/ExportRelease' / name).upper() == digest
    assemblies[name] = digest.lower()
assert len(assemblies) == 3
for name in ('GodotALS.dll', 'GodotALS.pdb', 'Als.Core.dll', 'Als.Core.pdb', 'Als.Import.dll', 'Als.Import.pdb'):
    assert sha(repo / '.godot/mono/temp/bin/Debug' / name) == sha(logs / 'main-model-final-debug-assemblies' / name)

captures = {}
render_runs = []
for profile in ('unarmed', 'pistol', 'rifle'):
    name = f'main-model-render-{profile}'
    text = read(logs / (name + '.log'))
    clean(text)
    assert 'OpenGL API 3.3.0 NVIDIA 610.62' in text and 'GeForce RTX 5080' in text
    assert text.rstrip().endswith('LYRA_GODOT_PROCESS_EXIT code=0')
    assert not read(logs / (name + '.stderr.log')).strip()
    result = json.loads((logs / (name + '.json')).read_bytes())
    values = re.findall(r'LYRA_MAIN_MODEL_GODOT_OK (\{[^\r\n]+\})', text)
    assert len(values) == 1 and json.loads(values[0]) == result
    check(result, profile, 60, 7)
    render_runs.append(result)
    for frame in ('standing', 'movement', 'aim', 'crouching', 'jump', 'landing', 'reverse'):
        path = logs / name / (frame + '.png')
        with Image.open(path) as image:
            assert image.size == (1280, 720) and sum(ImageStat.Stat(image.convert('RGB')).var) > 100
        captures[f'{profile}/{frame}'] = sha(path)

ordinary = read(logs / 'main-model-als-ordinary-regression.log')
clean(ordinary)
assert ordinary.rstrip().endswith('LYRA_GODOT_PROCESS_EXIT code=0')
assert 'ALS_REFACTORED_STANCE_DEMO_OK hz=60 ' in ordinary
assert 'locomotion=1700 ' in ordinary and 'ALS_REFACTORED_REST_FEEDBACK_OK frames=1700 dynamic_requests=4' in ordinary
legacy = read(logs / 'main-model-als-entry-regression.log')
assert 'P4_DEMO_FAIL code=physics' in legacy and 'AimOffset did not publish positive/negative yaw/pitch evidence' in legacy
assert legacy.rstrip().endswith('LYRA_GODOT_PROCESS_EXIT code=1')
legacy_source = (repo / 'src/Als.Godot/Locomotion/P4DemoSmoke.cs').read_text(encoding='utf-8-sig')
assert '"res://scenes/demo/p4_locomotion_demo.tscn"' in legacy_source and 'AlsDemoEntry' not in legacy_source

report = {
    'verifiedScope': 'Live fixed-provider complete Main and final Rig to ALS skin, ordinary explicit entry',
    'mainModelPublicationPassed': True,
    'nativeWholeMainParity': False,
    'productionAccepted': False,
    'headlessRuns': runs,
    'renderRuns': render_runs,
    'renderCaptureSha256': captures,
    'optimizedAssemblySha256': assemblies,
    'debugAssembliesRestored': 6,
    'protectedJson': len(protected),
    'protectedUasset': len(reference['assetSha256']),
    'alsOrdinaryEntryRegressionPassed': True,
    'legacyP4SmokeAccepted': False,
    'legacyP4Failure': 'Direct P4 scene, bypasses AlsDemoEntry: aim yaw sign coverage missing; unresolved',
}
destination = logs / 'lyra-main-model-verification.json'
with destination.open('x', encoding='utf-8') as stream:
    json.dump(report, stream, ensure_ascii=False, indent=2)
print('LYRA_MAIN_MODEL_VERIFICATION_OK ' + json.dumps({
    'headlessRuns': len(runs), 'framesPerConfiguration': sum(r['frames'] for r in runs if r['configuration'] == 'Debug'),
    'renderRuns': len(render_runs), 'captures': len(captures),
    'maxWorldPositionM': max(r['maxWorldPositionM'] for r in runs + render_runs),
    'protectedJson': len(protected), 'protectedUasset': 669,
    'legacyP4SmokeAccepted': False, 'nativeWholeMainParity': False, 'productionAccepted': False,
}))
