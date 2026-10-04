"""Validate live role isolation, ordinary multi-role execution and resource protection."""
import hashlib
import json
import re
from pathlib import Path

from locomotion_paths import project_path

from PIL import Image, ImageStat

repo = Path(__file__).resolve().parents[1]
logs = repo / 'artifacts/lyra-analysis'
assets = repo / 'assets/generated/lyra_als'


def sha(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        while block := stream.read(1024 * 1024):
            h.update(block)
    return h.hexdigest()


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


def emitted(text, marker, result):
    values = [json.loads(v) for v in re.findall(re.escape(marker) + r' (\{[^\r\n]+\})', text)]
    assert result in values


def check_player(value, hz, profile='rifle', feedback=False, captures=0):
    m = value['model']
    assert m['hz'] == hz and m['profile'] == profile
    assert m['frames'] == m['published'] == m['retries'] == 8 * hz
    assert m['skinBones'] == 68 and m['logicalBones'] == 81 and m['finalRig']
    assert m['maxSkinPositionM'] == m['maxSkinQuaternion'] == 0
    assert m['maxWorldPositionM'] <= 1e-4 and m['captures'] == captures
    assert m['actualGodotPhysics'] and m['actualCharacterMovement']
    assert m['states'] == [0, 1, 2, 3, 4, 6, 7, 8, 10, 11]
    assert not m['nativeWholeMainParity'] and not m['productionAccepted']
    assert value['switches'] == value['sameClassReuse'] == 6 and len(value['bindings']) == 12
    assert value['rigCurveCarry'] == int(feedback)
    assert value['pendingRebindRejected'] == {30: 3, 60: 5, 120: 9}[hz]
    assert [b['epoch'] for b in value['bindings']] == [1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 7, 7]


runs, ordinary, collisions, assembly_hashes = [], [], [], {}
for config in ('debug-lifetime', 'optimize-lifetime'):
    build = read(logs / ('multi-character-' + config.replace('-lifetime', '-build-lifetime') + '.log'))
    assert '0 个警告' in build and '0 个错误' in build
    text = read(logs / f'multi-character-{config}-matrix.log')
    clean(text)
    assert len(re.findall(r'LYRA_MULTI_PROCESS_EXIT label=\S+ code=0', text)) == 11
    assert not re.search(r'LYRA_MULTI_PROCESS_EXIT .* code=[1-9]', text)
    assembly_hashes[config] = dict(re.findall(r'LYRA_MULTI_ASSEMBLY file=(\S+) sha256=([A-F0-9]{64})', text))
    assert len(assembly_hashes[config]) == 3
    for hz in (30, 60, 120):
        result = json.loads((logs / f'multi-character-{config}-{hz}.json').read_bytes())
        emitted(text, 'LYRA_MULTI_CHARACTER_GODOT_OK', result)
        assert f'LYRA_MULTI_PROCESS_EXIT label=pairs-{hz} code=0' in text
        assert result['hz'] == hz and result['physicsFrames'] == 8 * hz and result['roleFrames'] == 48 * hz
        assert result['activeRoles'] == 6 and result['createdLifetimes'] == 9
        assert result['switches'] == 24 and result['replacements'] == 2 and result['retries'] == 24 * hz
        assert result['rejected'] == {30: 8671, 60: 17323, 120: 34633}[hz]
        assert result['lateFailures'] == {30: 9, 60: 15, 120: 30}[hz]
        assert result['montageFrames'] == {30: 34, 60: 68, 120: 134}[hz]
        assert result['queries'] > 0 and result['samePoseAndHistory'] and result['actualGodotPhysics']
        assert result['states'] == [0, 1, 2, 3, 4, 6, 7, 8, 10, 11]
        assert result['profiles'] == ['pistol', 'rifle', 'unarmed']
        assert not result['nativeWholeMainParity'] and not result['productionAccepted']
        assert re.fullmatch(r'[A-F0-9]{64}', result['digest'])
        runs.append({'configuration': config, **result})

        demo = json.loads((logs / f'multi-demo-{config}-{hz}.json').read_bytes())
        emitted(text, 'LYRA_MAIN_MULTI_DEMO_GODOT_OK', demo)
        assert f'LYRA_MULTI_PROCESS_EXIT label=ordinary-ten-{hz} code=0' in text
        assert demo['characters'] == 10 and len(demo['companions']) == 9
        check_player(demo['player'], hz)
        assert [c['id'] for c in demo['companions']] == list(range(2, 11))
        assert all(c['published'] == 8 * hz and c['queries'] > 0 and c['epoch'] == 4 for c in demo['companions'])
        assert {c['profile'] for c in demo['companions']} == {'unarmed', 'pistol', 'rifle'}
        ordinary.append({'configuration': config, **demo})

    single = json.loads((logs / f'multi-single-rebind-{config}.json').read_bytes())
    emitted(text, 'LYRA_MAIN_REBIND_GODOT_OK', single)
    check_player(single, 60, 'unarmed', True)
    for marker in ('LYRA_MAIN_ALS_NATIVE_GODOT_OK frames=11340 poses=9762 ',
                   'LYRA_MAIN_RIG_HOST_GODOT_OK frames=7560 poses=7296 retry=7560 ',
                   'ALS_REFACTORED_STANCE_DEMO_OK hz=60 '):
        assert text.count(marker) == 1
    assert 'locomotion=1700 ' in text
    collision = json.loads((logs / f'multi-rig-collision-{config}.json').read_bytes())
    emitted(text, 'LYRA_RIG_SCENE_COLLISION_GODOT_OK', collision)
    assert collision['frames'] == collision['retry'] == 2520 and collision['actualGodotPhysics']
    collisions.append(collision)

for hz in (30, 60, 120):
    pair = [r for r in runs if r['hz'] == hz]
    a, b = [{k: v for k, v in r.items() if k != 'configuration'} for r in pair]
    assert a == b, f'Debug/Optimize role channels or histories differ at {hz}Hz'
    pair = [d for d in ordinary if d['player']['model']['hz'] == hz]
    a, b = [json.loads(json.dumps(d)) for d in pair]
    for value in (a, b):
        value.pop('configuration')
        value['player']['model'].pop('maxWorldPositionM')
    assert a == b

optimized = read(logs / 'multi-character-optimize-lifetime-matrix.log')
assert optimized.rstrip().endswith('LYRA_MULTI_DEBUG_RESTORED hashVerified=true')
files = ('GodotALS.dll', 'GodotALS.pdb', 'Als.Core.dll', 'Als.Core.pdb', 'Als.Import.dll', 'Als.Import.pdb')
for name in files:
    assert sha(repo / '.godot/mono/temp/bin/Debug' / name) == sha(logs / 'multi-character-optimize-lifetime-debug-assemblies' / name)
for name, digest in assembly_hashes['optimize-lifetime'].items():
    assert sha(repo / '.godot/mono/temp/bin/ExportRelease' / name).upper() == digest

for config in ('debug','optimize'):
    build=read(logs/f'multi-character-{config}-build-postdraw.log')
    assert '0 个警告' in build and '0 个错误' in build
render_text = read(logs / 'multi-demo-render-postdraw.log')
clean(render_text)
assert render_text.rstrip().endswith('LYRA_GODOT_PROCESS_EXIT code=0')
render = json.loads((logs / 'multi-demo-render-postdraw.json').read_bytes())
emitted(render_text, 'LYRA_MAIN_MULTI_DEMO_GODOT_OK', render)
assert render['characters'] == 10
check_player(render['player'], 60, captures=7)
captures = {}
for name in ('standing', 'movement', 'aim', 'crouching', 'jump', 'landing', 'reverse'):
    path = logs / 'multi-demo-render-postdraw' / (name + '.png')
    with Image.open(path) as img:
        assert img.size == (1280, 720) and max(ImageStat.Stat(img.convert('RGB')).stddev) > 5
    captures[name] = sha(path)
capture_frames=json.loads((logs/'multi-demo-render-postdraw/frames.json').read_bytes())
assert capture_frames['phase']=='FramePostDraw' and len(capture_frames['frames'])==7
assert [f['name'] for f in capture_frames['frames']]==list(captures)
assert all(f['drawnFrame']>0 for f in capture_frames['frames'])
assert all(b['drawnFrame']>a['drawnFrame'] for a,b in zip(capture_frames['frames'],capture_frames['frames'][1:]))
for frame in capture_frames['frames']:
    before=[b for b in render['player']['bindings'] if b['frame']<frame['physicsFrame']]
    expected=before[-1] if before else {'profile':'rifle','epoch':1}
    assert frame['profile']==expected['profile'] and frame['layerEpoch']==expected['epoch']

reference = json.loads((assets / 'rig_reference_v1_native.json').read_bytes())
pins = {
    'rig_reference_v1_native.json': 'feae2406be8327f7ce0bafe91aa1560583163ce7164cad51a9bae16b2e3654f3',
    'rig_reference_v1_policy.json': 'f617d206f5477dbbea4dc866b468c3adadd2e08778023846adb389b919bd52e8',
    'rig_target_v1_native.json': '667e163867ee5acbd473bd0b59f1b8f561518c3372de65fdfedb10be53c5c224',
    'rig_target_v1_solver.json': '17e0561a6975eece54b179212d77271dc3342c4fb1dc87c918403c8141ad8bc5',
    'rig_target_v1_output.json': '961ee780e5e1d5b652d24885829922d6f6c4d7c21157a9524832d20f2f082f1c',
}
protected = reference['previousFixtureSha256'] | pins
assert len(protected) == 818
for name, digest in protected.items():
    assert sha(assets / name) == digest, name
assert len(reference['assetSha256']) == 669
for name, digest in reference['assetSha256'].items():
    path = project_path('Content') / (name.split('.')[0].removeprefix('/Game/') + '.uasset')
    assert sha(path) == digest, name

report = dict(roleIsolationPassed=True, ordinaryTenRolesPassed=True, runs=runs, ordinary=ordinary,
              collisions=collisions, render=render, captureSha256=captures, captureFrames=capture_frames, assemblySha256=assembly_hashes,
              protectedJson=818, protectedUasset=669, debugAssembliesRestored=6,
              nativeWholeMainParity=False, productionAccepted=False, legacyP4SmokeAccepted=False)
with (logs / 'lyra-multi-character-verification.json').open('x', encoding='utf-8') as stream:
    json.dump(report, stream, ensure_ascii=False, indent=2)
print('LYRA_MULTI_CHARACTER_VERIFICATION_OK ' + json.dumps(dict(
    pairRuns=6, pairRoleFramesPerConfiguration=10080, ordinaryRuns=6,
    ordinaryRoleFramesPerConfiguration=16800, nonzeroRigFeedbackRuns=2, captures=7,
    protectedJson=818, protectedUasset=669, debugAssembliesRestored=6,
    nativeWholeMainParity=False, productionAccepted=False)))
