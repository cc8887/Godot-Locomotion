"""Audit the real Main W-to-D reproduction, model mount and protected resources."""
from pathlib import Path
import hashlib
import json
import math
import re

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts/lyra-analysis'
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()

before = read(OUT / 'direction-change-v2-before.json')['directionChanges']
assert before[119]['asset'] == 69 and before[119]['direction'] == 3
assert abs(math.degrees(before[119]['warpAngle'])) > 85
assert abs(before[0]['componentForwardX'] + 1) < 1e-6
assert abs(before[0]['componentForwardY']) < 1e-6

def verify_trace(data, label, steady=False):
    model = data['model']; rows = data['directionChanges']; hz = model['hz']
    assert len(rows) == model['frames'] == model['published'] == model['retries'] == hz * 8, label
    assert model['actualGodotPhysics'] and model['finalRig'] and model['skinBones'] == 68 and model['logicalBones'] == 81
    assert model['maxSkinPositionM'] == 0 and model['maxSkinQuaternion'] <= 2e-7 and model['maxWorldPositionM'] <= 1e-4
    for row in rows:
        assert abs(row['relativeForwardX']) < 1e-5 and abs(row['relativeForwardY'] + 1) < 1e-5, (label, row['frame'])
    index = math.ceil(hz * 3.8) - 1 if steady else hz * 2 - 1
    right = rows[index]
    assert right['state'] == 2 and right['direction'] == 3
    warp = abs(math.degrees(right['warpAngle']))
    if model['profile'] in ('rifle', 'pistol'):
        assert warp < 5, (label, warp)
    if steady:
        forward = rows[hz * 3 - 1]
        assert forward['state'] == 2 and forward['direction'] == 0 and forward['asset'] != right['asset']
    return warp

results = {}; report_hashes = {}; log_hashes = {}
for config in ('debug', 'optimize'):
    for case in ('rifle-30', 'rifle-60', 'rifle-120', 'pistol-60', 'unarmed-60', 'ten-60', 'steady-30', 'steady-60', 'steady-120'):
        # Debug runs retain all successful process evidence, including the two
        # wrapper checks that initially assumed the wrong Unarmed angle/marker.
        tag = 'direction-change-v5' if config == 'debug' and case not in ('ten-60', 'steady-30', 'steady-60', 'steady-120') else 'direction-change-v6' if config == 'debug' and case == 'ten-60' else 'direction-change-v8'
        path = OUT / f'{tag}-{config}-{case}.json'
        log_path = path.with_suffix('.log')
        data = read(path); log = log_path.read_text(encoding='utf-8-sig')
        assert not re.search(r'^\s*(ERROR|WARNING):', log, re.M), str(log_path)
        assert re.search(r'LYRA_MAIN_(MODEL|MULTI_DEMO)_GODOT_OK', log)
        if case == 'ten-60':
            assert data['characters'] == 10 and len(data['companions']) == 9
            assert all(c['published'] == 480 and c['weaponMissing'] == 0 and c['queries'] > 0 for c in data['companions'])
            assert {c['profile'] for c in data['companions']} == {'rifle', 'pistol', 'unarmed'}
            assert data['player']['switches'] == 6 and data['player']['sameClassReuse'] == 6
            model = data['player']['model']
            assert model['published'] == model['retries'] == 480 and model['weaponMissing'] == 0 and model['weaponPlayed'] > 0
            result = None
        else:
            result = verify_trace(data, f'{config}/{case}', case.startswith('steady'))
        results[config, case] = data
        report_hashes[f'{config}/{case}'] = sha(path)
        log_hashes[f'{config}/{case}'] = sha(log_path)
for case in ('rifle-30', 'rifle-60', 'rifle-120', 'pistol-60', 'unarmed-60', 'ten-60', 'steady-30', 'steady-60', 'steady-120'):
    assert results['debug', case] == results['optimize', case], case

summary = read(OUT / 'direction-change-v8-optimize-verification.json')
assert summary['passed'] and summary['debugRestored'] and len(summary['runs']) == 9
for backup in (OUT / 'direction-change-v8-optimize-debug-backup').iterdir():
    assert sha(backup) == sha(ROOT / '.godot/mono/temp/bin/Debug' / backup.name)
for name in ('direction-change-v8-build.log', 'direction-change-v8-optimize-build.log'):
    log = (OUT / name).read_text(encoding='utf-8-sig')
    assert re.search(r'0\s*(个警告|Warning)', log) and re.search(r'0\s*(个错误|Error)', log)

previous = read(OUT / 'contact-input-core-v1-acceptance-audit.json')
for name, h in previous['coreMechanismSources'].items():
    assert sha(ROOT / 'src/Als.Core' / name) == h, name
frozen = read(OUT / 'contact-input-core-v1-frozen.json')
assets = {name: h for name, h in frozen['protected'].items() if name.startswith('assets/generated/lyra_als/') and name.endswith('.json')}
assert len(assets) == 870
for name, h in assets.items():
    assert sha(ROOT / name) == h, name
model = read(OUT / 'contact-input-core-v1-als-model-check.json')
for name, h in model['checkedFiles'].items():
    assert sha(ROOT / name) == h, name

frames = read(OUT / 'direction-change-v3-after-frames/frames.json')
assert frames['phase'] == 'FramePostDraw' and len(frames['frames']) == 7
images = {p.name: sha(p) for p in (OUT / 'direction-change-v3-after-frames').glob('*.png')}
assert len(images) == 7
sources = ['src/Als.Godot/Animation/Lyra/LyraAlsCharacterBinding.cs', 'src/Als.Godot/Locomotion/LyraLocomotionDemo.cs', 'scripts/verify-lyra-direction-change.ps1', 'tools/verify_lyra_direction_change.py']
result = dict(passed=True, beforeRightWarpDegrees=abs(math.degrees(before[119]['warpAngle'])),
    rifleAfterRightWarpDegrees={str(hz): verify_trace(results['debug', f'rifle-{hz}'], f'rifle-{hz}') for hz in (30,60,120)},
    rifleSteadyAfterRightWarpDegrees={str(hz): verify_trace(results['debug', f'steady-{hz}'], f'steady-{hz}', True) for hz in (30,60,120)},
    currentSourceHashes={n: sha(ROOT/n) for n in sources}, reportHashes=report_hashes, logHashes=log_hashes,
    successfulHeadlessProcesses=18, realPostDrawRenderSamples=images, renderImagesVisuallyChecked=True,
    unchangedCoreMechanisms=len(previous['coreMechanismSources']), unchangedLyraAssetJson=len(assets),
    unchangedAlsModelFiles=len(model['checkedFiles']), debugOptimizeReportsExact=True, debugAssembliesRestored=True,
    actualOsKeyboardReplay=False, goalComplete=False,
    scope='Rifle W-to-D model/component mount fix; short and settled Cycle changes; two weapons and fallback runtime regression.')
with (OUT / 'direction-change-v8-audit.json').open('x', encoding='utf-8') as f:
    json.dump(result, f, indent=2)
print('LYRA_DIRECTION_CHANGE_AUDIT_OK processes=18 assets=870 core=18 model=unchanged debugOptimize=exact')
