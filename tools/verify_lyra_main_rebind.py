"""Verify live provider replacement and preserve the broader native acceptance boundary."""
import hashlib
import json
import re
from pathlib import Path

from PIL import Image, ImageStat

repo = Path(__file__).resolve().parents[1]
logs = repo / 'artifacts/lyra-analysis'
assets = repo / 'assets/generated/lyra_als'


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


native = json.loads((assets / 'linked_layer_binding_native.json').read_bytes())
assert sha(assets / 'linked_layer_binding_native.json') == 'bd3f17e33883d1894b3dc1ddc81b96c84f49870b6bf035562578a96666c1464b'
assert sha(assets / 'linked_layer_contracts.json') == native['contractsSha256']
assert native['profiles'] == ['unarmed', 'unarmed', 'pistol', 'pistol', 'rifle', 'rifle', 'unarmed', 'unarmed']
native_epochs = []
for step in native['result']['steps']:
    assert step['linkedInstances'] == 1 and len(step['nodes']) == 14
    owners = {node['owner'] for node in step['nodes']}
    assert len(owners) == 1
    native_epochs.append(next(iter(owners)) + 1)
assert native_epochs == [1, 1, 2, 2, 3, 3, 4, 4]


def check(result, profile, hz, feedback=False, captures=0):
    model = result['model']
    assert model['profile'] == profile and model['hz'] == hz
    assert model['frames'] == model['published'] == model['retries'] == hz * 8
    assert model['skinBones'] == 68 and model['logicalBones'] == 81
    assert model['finalRig'] and model['actualCharacterMovement'] and model['actualGodotPhysics']
    assert model['queries'] == {30: 6656, 60: 13376, 120: 26784}[hz]
    assert model['lateFailures'] == {30: 3, 60: 5, 120: 10}[hz]
    assert model['states'] == [0, 1, 2, 3, 4, 6, 7, 8, 10, 11]
    assert model['air'] == {30: 33, 60: 67, 120: 133}[hz]
    assert model['crouch'] == hz * 6 // 5 and model['aim'] == hz
    assert model['maxSkinPositionM'] == model['maxSkinQuaternion'] == 0 and model['maxWorldPositionM'] <= 1e-4
    assert model['captures'] == captures
    assert model['nativeWholeMainParity'] is False and model['productionAccepted'] is False
    assert result['switches'] == result['sameClassReuse'] == 6
    assert result['pendingRebindRejected'] == {30: 3, 60: 5, 120: 9}[hz]
    assert result['rigCurveCarry'] == int(feedback)
    bindings = result['bindings']
    assert len(bindings) == 12
    assert [b['epoch'] for b in bindings] == [1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 7, 7]
    assert [b['changed'] for b in bindings] == [False, True, False, True, False, True, False, True, False, True, True, False]
    profiles = ['unarmed', 'pistol', 'rifle']
    offset = profiles.index(profile)
    expected = [profiles[(offset + n) % 3] for n in [0, 1, 1, 2, 2, 0, 0, 1, 1, 2, 0, 0]]
    assert [b['profile'] for b in bindings] == expected
    # Input.ActionPress reaches the ordinary input handler on the next tick.
    expected_frames = [hz * 4 // 5, hz + 1, hz * 6 // 5, hz * 21 // 10 + 1, hz * 23 // 10,
                       hz * 3 + 1, hz * 31 // 10, hz * 43 // 10 + 1, hz * 46 // 10,
                       hz * 64 // 10 + 1, hz * 69 // 10 + 1, hz * 71 // 10]
    assert [b['frame'] for b in bindings] == expected_frames
    assert bindings[1]['state'] == 1 and bindings[3]['ads'] and bindings[5]['crouch'] and bindings[7]['air'] and bindings[10]['state'] == 4
    if profile == 'unarmed':
        assert [1] + [b['epoch'] for b in bindings[:7]] == native_epochs
        assert [profile] + [b['profile'] for b in bindings[:7]] == native['profiles']


for name in ('main-rebind-debug-build-guard.log', 'main-rebind-optimize-build-guard.log'):
    text = read(logs / name)
    assert '0 个警告' in text and '0 个错误' in text
optimized = read(logs / 'main-rebind-godot-optimize.log')
clean(optimized)
assert optimized.rstrip().endswith('LYRA_DEBUG_ASSEMBLIES_RESTORED hashVerified=true')
optimized_reports = [json.loads(v) for v in re.findall(r'LYRA_MAIN_REBIND_GODOT_OK (\{[^\r\n]+\})', optimized)]
assert len(optimized_reports) == 12
runs = []
for profile in ('unarmed', 'pistol', 'rifle'):
    for hz in (30, 60, 120):
        base = f'main-rebind-guard-debug-{profile}-{hz}'
        text = read(logs / (base + '.log'))
        clean(text)
        assert text.rstrip().endswith('LYRA_GODOT_PROCESS_EXIT code=0')
        report = json.loads((logs / (base + '.json')).read_bytes())
        values = re.findall(r'LYRA_MAIN_REBIND_GODOT_OK (\{[^\r\n]+\})', text)
        assert len(values) == 1 and json.loads(values[0]) == report
        release = json.loads((logs / f'main-rebind-optimize-{profile}-{hz}.json').read_bytes())
        assert release in optimized_reports
        assert optimized.count(f'LYRA_GODOT_PROCESS_EXIT profile={profile} hz={hz} code=0') == 1
        for configuration, result in (('Debug', report), ('Optimize', release)):
            check(result, profile, hz)
            runs.append(dict(configuration=configuration, **result))
        # The live Skeleton3D world result is a single-precision transform.
        left, right = json.loads(json.dumps(report)), json.loads(json.dumps(release))
        left['model'].pop('maxWorldPositionM'); right['model'].pop('maxWorldPositionM')
        assert left == right

feedback_runs = []
for hz in (30, 60, 120):
    base = f'main-rebind-feedback-debug-{hz}'
    text = read(logs / (base + '.log'))
    clean(text)
    assert text.rstrip().endswith('LYRA_GODOT_PROCESS_EXIT code=0')
    debug = json.loads((logs / (base + '.json')).read_bytes())
    assert json.loads(re.findall(r'LYRA_MAIN_REBIND_GODOT_OK (\{[^\r\n]+\})', text)[0]) == debug
    release = json.loads((logs / f'main-rebind-feedback-optimize-{hz}.json').read_bytes())
    assert release in optimized_reports and optimized.count(f'LYRA_GODOT_PROCESS_EXIT feedbackHz={hz} code=0') == 1
    for configuration, result in (('Debug', debug), ('Optimize', release)):
        check(result, 'unarmed', hz, True)
        feedback_runs.append(dict(configuration=configuration, **result))

regression_markers = [
    'LYRA_MAIN_ALS_NATIVE_GODOT_OK frames=11340 poses=9762 ',
    'LYRA_MAIN_RIG_HOST_GODOT_OK frames=7560 poses=7296 retry=7560 ',
    'LYRA_MAIN_INERTIA_GODOT_OK frames=40320 poses=11250 ',
]
for text in (read(logs / 'main-rebind-native-regressions.log'), optimized):
    clean(text)
    for marker in regression_markers:
        assert text.count(marker) == 1
    for scene in ('lyra_main_als_native_smoke', 'lyra_main_rig_pose_host_smoke', 'lyra_main_inertia_smoke'):
        assert text.count(f'LYRA_GODOT_PROCESS_EXIT scene={scene} code=0') == 1
assert 'ALS_REFACTORED_STANCE_DEMO_OK hz=60 ' in optimized and 'locomotion=1700 ' in optimized
assert 'LYRA_GODOT_PROCESS_EXIT scene=refactored_stance_demo_smoke code=0' in optimized
fixed = json.loads((logs / 'main-rebind-optimize-fixed.json').read_bytes())
assert fixed['frames'] == fixed['published'] == fixed['retries'] == 480
assert fixed['maxSkinPositionM'] == fixed['maxSkinQuaternion'] == 0 and fixed['maxWorldPositionM'] <= 1e-4
assert 'LYRA_GODOT_PROCESS_EXIT fixedProfile=rifle code=0' in optimized

render_text = read(logs / 'main-rebind-render.log')
clean(render_text)
assert render_text.rstrip().endswith('LYRA_GODOT_PROCESS_EXIT code=0')
assert 'OpenGL API 3.3.0 NVIDIA 610.62' in render_text and 'GeForce RTX 5080' in render_text
assert not read(logs / 'main-rebind-render.stderr.log').strip()
render = json.loads((logs / 'main-rebind-render.json').read_bytes())
assert json.loads(re.findall(r'LYRA_MAIN_REBIND_GODOT_OK (\{[^\r\n]+\})', render_text)[0]) == render
check(render, 'rifle', 60, captures=7)
captures = {}
for name in ('standing', 'movement', 'aim', 'crouching', 'jump', 'landing', 'reverse'):
    path = logs / 'main-rebind-render' / (name + '.png')
    with Image.open(path) as image:
        assert image.size == (1280, 720) and sum(ImageStat.Stat(image.convert('RGB')).var) > 100
    captures[name] = sha(path)

assembly_hashes = dict(re.findall(r'LYRA_OPTIMIZED_ASSEMBLY (\S+) SHA256=([0-9A-F]{64})', optimized))
assert len(assembly_hashes) == 3
for name, digest in assembly_hashes.items():
    assert sha(repo / '.godot/mono/temp/bin/ExportRelease' / name).upper() == digest
for name in ('GodotALS.dll', 'GodotALS.pdb', 'Als.Core.dll', 'Als.Core.pdb', 'Als.Import.dll', 'Als.Import.pdb'):
    assert sha(repo / '.godot/mono/temp/bin/Debug' / name) == sha(logs / 'main-rebind-final-debug-assemblies' / name)

previous = json.loads((logs / 'lyra-main-model-verification.json').read_bytes())
reference = json.loads((assets / 'rig_reference_v1_native.json').read_bytes())
pins = {
    'rig_reference_v1_native.json': 'feae2406be8327f7ce0bafe91aa1560583163ce7164cad51a9bae16b2e3654f3',
    'rig_reference_v1_policy.json': 'f617d206f5477dbbea4dc866b468c3adadd2e08778023846adb389b919bd52e8',
    'rig_target_v1_native.json': '667e163867ee5acbd473bd0b59f1b8f561518c3372de65fdfedb10be53c5c224',
    'rig_target_v1_solver.json': '17e0561a6975eece54b179212d77271dc3342c4fb1dc87c918403c8141ad8bc5',
    'rig_target_v1_output.json': '961ee780e5e1d5b652d24885829922d6f6c4d7c21157a9524832d20f2f082f1c',
}
protected = reference['previousFixtureSha256'] | pins
assert len(protected) == previous['protectedJson'] == 818
for name, digest in protected.items():
    assert sha(assets / name) == digest, name
assert len(reference['assetSha256']) == previous['protectedUasset'] == 669
for name, digest in reference['assetSha256'].items():
    assert sha(Path('../GASP58/Content') / (name.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, name

report = {
    'liveRebindPassed': True,
    'nativeBindingOwnerProtocolMatched': 'Unarmed first eight steps only; no new continuous Main oracle',
    'nativeWholeMainParity': False, 'productionAccepted': False,
    'runs': runs, 'nonzeroRigFeedbackRuns': feedback_runs, 'render': render,
    'renderCaptureSha256': captures, 'optimizedAssemblySha256': assembly_hashes,
    'protectedJson': 818, 'protectedUasset': 669, 'debugAssembliesRestored': 6,
    'alsOrdinaryEntryRegressionPassed': True,
    'legacyP4SmokeAccepted': False,
    'legacyP4Failure': previous['legacyP4Failure'],
}
with (logs / 'lyra-main-rebind-verification.json').open('x', encoding='utf-8') as stream:
    json.dump(report, stream, ensure_ascii=False, indent=2)
print('LYRA_MAIN_REBIND_VERIFICATION_OK ' + json.dumps({
    'runs': len(runs), 'framesPerConfiguration': 5040, 'switchesPerConfiguration': 54, 'sameClassPerConfiguration': 54,
    'nonzeroRigFeedbackRuns': len(feedback_runs), 'captures': 7, 'debugAssembliesRestored': 6,
    'protectedJson': 818, 'protectedUasset': 669, 'nativeWholeMainParity': False, 'productionAccepted': False,
}))
