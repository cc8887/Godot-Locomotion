"""Audit the actual whole-Main action matrix, its processes and protected inputs."""
import hashlib
import json
import re
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
evidence = repo / 'artifacts/lyra-analysis'
assets = repo / 'assets/generated/lyra_als'
project = Path('../GASP58')
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
load = lambda p: json.loads(p.read_bytes())
output = evidence / 'whole-main-actions-final-integrity.json'
assert not output.exists(), 'Preserve the earlier audit'
reports = []
captures = []
protected = None
summary = []
for hz in (30, 60, 120):
    tag = f'actions{hz}-asc'
    request_path = evidence / f'whole-main-{tag}-request.json'
    native_path = evidence / f'whole-main-{tag}-native.json'
    request = load(request_path)
    closure = load(evidence / f'whole-main-{tag}-closure.json')
    native_log = evidence / f'whole-main-native-{tag}.log'
    text = native_log.read_text(encoding='utf-8-sig', errors='replace')
    assert f'LYRA_WHOLE_MAIN_CAPTURE_OK traces=3 frames={hz*36} assets_saved=0' in text
    assert 'WHOLE_MAIN_PROCESS_EXIT=0' in text
    assert not re.search(r'LogAbilitySystem: Error|Assertion failed|LogPython: Error', text)
    assert closure['scope']['case'] == 'actions'
    assert closure['scope']['originalFullRoot'] and closure['scope']['originalUnifiedSync']
    assert closure['scope']['originalTargetMasks'] and closure['scope']['finalControlRig']
    assert not closure['scope']['actualCharacterMovementSimulation']
    hashes = {k: closure[k] for k in ('previousFixtureSha256', 'protectedProject', 'assetSha256')}
    if protected is None:
        protected = hashes
    else:
        assert protected == hashes, 'Protected inputs drifted between captures'
    for rel, digest in closure['probeSourceSha256'].items():
        assert sha(repo / 'tools/unreal/LyraWholeMainOracle' / rel) == digest
    frames = sum(len(t['frames']) for t in request['traces'])
    commands = sum(len(f['commands']) for t in request['traces'] for f in t['frames'])
    assert frames == hz*36 and commands == 42
    assert [t['profile'] for t in request['traces']] == ['unarmed', 'pistol', 'rifle']
    summary.append(dict(hz=hz, frames=frames, commands=commands, sequences=len(request['sequencePaths']), montages=len(request['montagePaths'])))
    captures.append(dict(tag=tag, requestSha256=sha(request_path), nativeSha256=sha(native_path), logSha256=sha(native_log), closureSha256=sha(evidence / f'whole-main-{tag}-closure.json')))
    for configuration in ('debug', 'optimize'):
        p = evidence / f'whole-main-{configuration}-actions{hz}-final-verification.json'
        report = load(p)
        assert report['comparisonPassed'] and report['retry'] and not report['completeAcceptance']
        assert report['runTag'] == tag and len(report['runs']) == 3
        assert {r['boundary'] for r in report['runs']} == {'pre-inertia', 'pre-rig', 'final'}
        for run in report['runs']:
            log = Path(run['log'])
            log_text = log.read_text(encoding='utf-8-sig', errors='replace')
            assert run['exitCode'] == 0 and run['comparisonPassed']
            assert 'WHOLE_MAIN_PROCESS_EXIT=0' in log_text
            assert not re.search(r'^\s*(ERROR|WARNING):', log_text, re.M)
            marker = re.search(r'LYRA_WHOLE_MAIN_DIAGNOSTIC_OK frames=(\d+) profiles=3 .*commands=(\d+) frozenInstances=(\d+) covered=(\d+) overlap=(\d+) retry=(\d+)', log_text)
            assert marker and int(marker[1]) == frames and int(marker[2]) == commands and int(marker[6]) == frames
            assert all(int(marker[i]) > 0 for i in (3, 4, 5))
            run['logSha256'] = sha(log)
        if hz == 60:
            assert len(report['regressions']) == 7
            assert {r['name'] for r in report['regressions']} == {'aiming-layer', 'aiming-scope', 'lean-composition', 'als-main-linked', 'inertia-host', 'montage-notify', 'ordinary-ten'}
        for regression in report['regressions']:
            log = Path(regression['log'])
            log_text = log.read_text(encoding='utf-8-sig', errors='replace')
            assert regression['passed'] and regression['exitCode'] == 0
            assert not re.search(r'^\s*(ERROR|WARNING):', log_text, re.M)
            regression['logSha256'] = sha(log)
        if configuration == 'optimize':
            assert report['debugRestored']
            for name in report['assemblies']:
                backup = evidence / f'whole-main-{configuration}-actions{hz}-final-debug-backup' / name
                assert sha(backup) == sha(repo / '.godot/mono/temp/bin/Debug' / name)
        reports.append(dict(path=p.name, sha256=sha(p), result=report))
for rel, digest in protected['previousFixtureSha256'].items():
    assert sha(assets / rel) == digest, rel
for rel, digest in protected['protectedProject'].items():
    assert sha(project / rel) == digest, rel
for package, digest in protected['assetSha256'].items():
    name = package.split('.')[0]
    if name.startswith('/Game/'):
        path = project / 'Content' / (name.removeprefix('/Game/') + '.uasset')
    else:
        assert name.startswith('/ShooterCore/')
        path = project / 'Plugins/GameFeatures/ShooterCore/Content' / (name.removeprefix('/ShooterCore/') + '.uasset')
    assert sha(path) == digest, package
for configuration in ('debug', 'optimize'):
    log = evidence / f'whole-main-actions-final-{configuration}-build.log'
    text = log.read_text(encoding='utf-8-sig')
    assert '0 个警告' in text and '0 个错误' in text
ordinary = [load(evidence / f'whole-main-{c}-actions60-final-ordinary-ten.json') for c in ('debug', 'optimize')]
assert ordinary[0] == ordinary[1], 'Ordinary character reports differ by configuration'
native_math = sha(assets / 'native_win64/LyraNativeMath.dll')
assert all(r['result']['nativeMathSha256'].lower() == native_math for r in reports)
assert all(sha(repo / f'.godot/mono/temp/bin/{c}/LyraNativeMath.dll') == native_math for c in ('Debug', 'ExportRelease'))
sources = ['src/Als.Core/Actions/AlsDynamicMontageRuntime.cs', 'src/Als.Godot/Animation/Lyra/LyraCharacterAnimation.cs',
           'src/Als.Godot/Animation/Lyra/LyraWholeMainDiagnosticSmoke.cs', 'src/Als.Godot/Animation/Lyra/LyraMontageNotifySmoke.cs',
           'src/Als.Godot/Animation/Lyra/LyraMainPoseHost.cs', 'src/Als.Godot/Animation/Lyra/LyraMainLocomotionHost.cs',
           'src/Als.Godot/Animation/Lyra/LyraMontageSlotPose.cs', 'src/Als.Godot/Animation/Lyra/LyraMontageTrackSampler.cs',
           'src/Als.Godot/Animation/Lyra/LyraMainSlotsTraversal.cs', 'src/Als.Godot/Animation/Lyra/LyraItemLayerGraphInstance.cs',
           'scripts/capture-lyra-whole-main.ps1', 'scripts/verify-lyra-whole-main-diagnostic.ps1',
           'tools/unreal/capture_lyra_whole_main.py', 'tools/verify_lyra_whole_main_actions.py']
result = dict(schemaVersion=1, comparisonPassed=True, completeAcceptance=False, goalComplete=False,
              scope='Original complete Main/Linked graph and real Montage commands; controlled observations and static plane; no gameplay abilities, native rebind, CharacterMovement/Jolt/GPU acceptance',
              summary=summary, captures=captures, reports=reports, nativeMathSha256=native_math,
              sourceSha256={p: sha(repo / p) for p in sources},
              protectedCounts={k: len(v) for k, v in protected.items()}, ordinaryReportsExactlyEqual=True)
with output.open('x', encoding='utf-8', newline='\n') as stream:
    json.dump(result, stream, separators=(',', ':'), allow_nan=False)
    stream.write('\n')
print('LYRA_WHOLE_MAIN_ACTIONS_INTEGRITY_OK framesPerConfiguration=7560 reports=6 boundaries=18 regressions=14 goalComplete=false')
