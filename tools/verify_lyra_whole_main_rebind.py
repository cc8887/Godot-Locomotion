"""Audit native layer replacement, actual comparison processes and input integrity."""
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

from locomotion_paths import project_path

repo = Path(__file__).resolve().parents[1]
evidence = repo / 'artifacts/lyra-analysis'
assets = repo / 'assets/generated/lyra_als'
project = project_path()


def sha(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()


def load(path):
    return json.loads(path.read_bytes())


def log_text(path):
    return path.read_text(encoding='utf-8-sig', errors='replace')


def clean_godot(path):
    text = log_text(path)
    assert 'WHOLE_MAIN_PROCESS_EXIT=0' in text, path
    assert not re.search(r'^\s*(ERROR|WARNING):', text, re.M), path
    return text


output = evidence / 'whole-main-rebind-final-integrity.json'
assert not output.exists(), 'Preserve earlier audit'
control = load(evidence / 'whole-main-rebind60-probe-control-integrity.json')
for name, digest in control['files'].items():
    assert sha(evidence / name) == digest, name
assert all(p['frames'] == 80 for p in control['oldControlPrefixExact'])
assert all(p['framesThroughFirstRebind'] == 49 for p in control['rebindPrefixThroughFirstChangeExact'])
captures, reports, summary = [], [], []
protected = None
locomotion_hooks = {
    'FullBody_IdleState', 'FullBody_CycleState', 'FullBody_StartState', 'FullBody_StopState',
    'FullBody_PivotState', 'FullBody_JumpStartState', 'FullBody_JumpApexState',
    'FullBody_FallLoopState', 'FullBody_FallLandState', 'FullBody_JumpStartLoopState'}
for hz, tag in ((30, 'rebind30-final'), (60, 'rebind60-first'), (120, 'rebind120-final')):
    request_path = evidence / f'whole-main-{tag}-request.json'
    native_path = evidence / f'whole-main-{tag}-native.json'
    closure_path = evidence / f'whole-main-{tag}-closure.json'
    request = load(request_path)
    closure = load(closure_path)
    native_log = evidence / f'whole-main-native-{tag}.log'
    text = log_text(native_log)
    assert f'LYRA_WHOLE_MAIN_CAPTURE_OK traces=3 frames={hz * 36} assets_saved=0' in text
    assert 'WHOLE_MAIN_PROCESS_EXIT=0' in text
    assert not re.search(r'LogAbilitySystem: Error|Assertion failed|LogPython: Error', text)
    scope = closure['scope']
    assert scope['case'] == 'rebind' and scope['originalFullRoot'] and scope['originalUnifiedSync']
    assert scope['originalTargetMasks'] and scope['finalControlRig'] and scope['als81']
    assert scope['controlledPhysicalInputs'] and not scope['actualCharacterMovementSimulation']
    hashes = {k: closure[k] for k in ('previousFixtureSha256', 'protectedProject', 'assetSha256')}
    if protected is None:
        protected = hashes
    else:
        assert protected == hashes, 'Protected capture inputs drifted'
    for rel, digest in closure['probeSourceSha256'].items():
        assert sha(repo / 'tools/unreal/LyraWholeMainOracle' / rel) == digest, rel
    frames = sum(len(t['frames']) for t in request['traces'])
    commands = sum(len(f['commands']) for t in request['traces'] for f in t['frames'])
    assert frames == hz * 36 and commands == 42
    assert len(request['sequencePaths']) == 300 and len(request['montagePaths']) == 45
    native = load(native_path)
    assert len(native['traces']) == len(request['traces']) == 3
    changed = same = live_changes = hidden_changes = 0
    for expected, actual in zip(request['traces'], native['traces'], strict=True):
        assert actual['profile'] == expected['profile'] and actual['hz'] == hz
        assert len(actual['frames']) == len(expected['frames']) == hz * 12
        profile = expected['profile']
        owner = 0
        for index, (f, row) in enumerate(zip(expected['frames'], actual['frames'], strict=True)):
            binding = row['binding']
            assert binding['profile'] == profile and binding['owner'] == owner
            assert binding['nodes'] == 14 and binding['activeInstances'] == 1
            if 'rebind' not in f:
                assert 'rebind' not in row
                continue
            op = row['rebind']
            requested = f['rebind']
            assert op['changed'] == requested['changed'] == (profile != requested['profile'])
            assert op['mainBefore'] == op['mainAfter'], (hz, profile, index, 'Main reset')
            assert op['frozenBefore'] == op['frozenAfter'], (hz, profile, index, 'Montage reset')
            before, after = op['before'], op['after']
            assert before == binding
            if op['changed']:
                changed += 1
                live_changes += bool(op['frozenBefore'])
                hidden_changes += not any(x['hook'] in locomotion_hooks for x in row['updates'])
                owner += 1
            else:
                same += 1
            profile = requested['profile']
            assert after['profile'] == profile and after['owner'] == owner
            assert after['class'] == requested['class']
            assert after['nodes'] == 14 and after['activeInstances'] == 1
    assert changed == same == 24 and live_changes > 0 and hidden_changes > 0
    del native
    summary.append(dict(hz=hz, frames=frames, commands=commands, rebinds=changed,
                        sameClass=same, liveChanges=live_changes, hiddenChanges=hidden_changes))
    captures.append(dict(tag=tag, requestSha256=sha(request_path), nativeSha256=sha(native_path),
                         closureSha256=sha(closure_path), logSha256=sha(native_log)))
    for configuration in ('debug', 'optimize'):
        prefix = f'whole-main-{configuration}-rebind{hz}-alpha'
        path = evidence / (prefix + '-verification.json')
        report = load(path)
        assert report['comparisonPassed'] and report['retry'] and not report['completeAcceptance']
        assert report['runTag'] == tag and len(report['runs']) == 3
        assert {r['boundary'] for r in report['runs']} == {'pre-inertia', 'pre-rig', 'final'}
        for run in report['runs']:
            assert run['comparisonPassed'] and run['exitCode'] == 0
            text = clean_godot(Path(run['log']))
            assert f'LYRA_WHOLE_MAIN_DIAGNOSTIC_OK frames={frames} profiles=3' in text
            assert f'commands={commands} ' in text and 'rebinds=24 sameClass=24 ' in text
            assert 'rejectedRebinds=48 ' in text and f'retry={frames} controlledPhysicalInputs=true' in text
            run['logSha256'] = sha(Path(run['log']))
        if hz == 60:
            assert {r['name'] for r in report['regressions']} == {
                'aiming-layer', 'aiming-scope', 'lean-composition', 'als-main-linked',
                'inertia-host', 'montage-notify', 'ordinary-ten'}
        for regression in report['regressions']:
            assert regression['passed'] and regression['exitCode'] == 0
            text = clean_godot(Path(regression['log']))
            if regression['name'] == 'montage-notify':
                assert 'LYRA_MONTAGE_NOTIFY_OK ' in text and 'native=True' in text
            regression['logSha256'] = sha(Path(regression['log']))
        assembly_dir = repo / '.godot/mono/temp/bin' / ('Debug' if configuration == 'debug' else 'ExportRelease')
        for name, digest in report['assemblies'].items():
            assert sha(assembly_dir / name).upper() == digest.upper(), (configuration, name)
            if configuration == 'optimize':
                assert report['debugRestored']
                assert sha(evidence / (prefix + '-debug-backup') / name) == sha(repo / '.godot/mono/temp/bin/Debug' / name)
        reports.append(dict(path=path.name, sha256=sha(path), result=report))
for rel, digest in protected['previousFixtureSha256'].items():
    assert sha(assets / rel) == digest, rel
for rel, digest in protected['protectedProject'].items():
    assert sha(project / rel) == digest, rel
for name, digest in protected['assetSha256'].items():
    package = name.split('.')[0]
    if package.startswith('/Game/'):
        path = project / 'Content' / (package.removeprefix('/Game/') + '.uasset')
    else:
        assert package.startswith('/ShooterCore/')
        path = project / 'Plugins/GameFeatures/ShooterCore/Content' / (package.removeprefix('/ShooterCore/') + '.uasset')
    assert sha(path) == digest, name
for configuration in ('debug', 'optimize'):
    text = log_text(evidence / f'whole-main-rebind-{configuration}-alpha-build.log')
    assert '0 个警告' in text and '0 个错误' in text
ordinary = [load(evidence / f'whole-main-{c}-rebind60-alpha-ordinary-ten.json') for c in ('debug', 'optimize')]
assert ordinary[0] == ordinary[1], 'Ordinary character reports differ by build'
component_reports = []
for configuration in ('debug', 'optimize'):
    prefix = f'rebind-components-{configuration}-alpha'
    path = evidence / (prefix + '-verification.json')
    report = load(path)
    assert {r['name'] for r in report['runs']} == {'fixed-actions', 'stride-native', 'skeletal-native', 'als-ordinary'}
    for run in report['runs']:
        assert run['passed'] and run['exitCode'] == 0
        text = clean_godot(Path(run['log']))
        if run['name'] == 'fixed-actions':
            assert 'frames=2160 profiles=3' in text and 'retry=2160 controlledPhysicalInputs=true' in text
        if run['name'] == 'als-ordinary':
            assert 'ALS_REFACTORED_REST_FEEDBACK_OK frames=1700 ' in text
        run['logSha256'] = sha(Path(run['log']))
    for name, digest in report['assemblies'].items():
        directory = 'Debug' if configuration == 'debug' else 'ExportRelease'
        assert sha(repo / '.godot/mono/temp/bin' / directory / name).upper() == digest.upper()
        if configuration == 'optimize':
            assert report['debugRestored']
            assert sha(evidence / (prefix + '-debug-backup') / name) == sha(repo / '.godot/mono/temp/bin/Debug' / name)
    component_reports.append(dict(path=path.name, sha256=sha(path), result=report))
test_reports = []
for name, expected in (('component-blend-native-paths.trx', 93), ('component-blend-standing-native.trx', 6)):
    path = evidence / 'component-blend-tests' / name
    tree = ET.parse(path)
    counters = tree.find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters').attrib
    assert int(counters['total']) == int(counters['passed']) == expected and int(counters['failed']) == 0
    test_reports.append(dict(path=str(path.relative_to(evidence)), sha256=sha(path), passed=expected))
queue_native = load(assets / 'montage_notify_queue_v2_native.json')
assert queue_native['requestSha256'] == sha(assets / 'montage_notify_queue_v2_requests.json')
assert queue_native['scope']['nativeHandleEventsAndPostUpdateAccepted']
assert not queue_native['scope']['originalClassGraphDispatchAccepted']
queue_log = log_text(evidence / 'whole-main-rebind-notify-v2-ue.log')
assert 'LYRA_EXPORT_PROCESS_EXIT_OK mode=montage-notify-queue code=0' in queue_log
native_math = sha(assets / 'native_win64/LyraNativeMath.dll')
assert native_math == 'f9af2ea6c276a87d5d769d5eb84db1019b77ca780bcfd63a9c43b9dc9fa0170b'
assert all(r['result']['nativeMathSha256'].lower() == native_math for r in reports)
sources = ['src/Als.Core/Locomotion/AlsComponentPose.cs', 'src/Als.Godot/Animation/Lyra/LyraMainLocomotionHost.cs',
           'src/Als.Godot/Animation/Lyra/LyraMainPoseHost.cs', 'src/Als.Godot/Animation/Lyra/LyraCycleLayerPoseHost.cs', 'src/Als.Godot/Animation/Lyra/LyraWholeMainDiagnosticSmoke.cs',
           'src/Als.Godot/Animation/Lyra/LyraMontageNotifySmoke.cs', 'scripts/capture-lyra-whole-main.ps1',
           'scripts/verify-lyra-whole-main-diagnostic.ps1', 'scripts/export-lyra-montage-notify-queue.ps1',
           'scripts/verify-lyra-rebind-component-regressions.ps1',
           'tools/unreal/capture_lyra_whole_main.py', 'tools/unreal/export_lyra_montage_notify_queue.py',
           'tools/verify_lyra_whole_main_rebind.py']
baseline_failure = log_text(evidence / 'rebind-stride-before-change.log')
assert 'quaternion=0.02065841997227029' in baseline_failure and 'STRIDE_BASELINE_DEBUG_RESTORED hashVerified=true' in baseline_failure
assert 'LYRA_ORIENTATION_GODOT_OK frames=3528' in log_text(evidence / 'rebind-independent-orientation-alpha.log')
retained_failures = {name: sha(evidence / name) for name in ('rebind-components-debug-final-verification.json', 'rebind-stride-before-change.log', 'rebind-independent-orientation-baseline.log')}
result = dict(schemaVersion=1, comparisonPassed=True, completeAcceptance=False, goalComplete=False,
              scope='Original Main/Linked/Montage with actual LinkAnimClassLayers; controlled observations and static plane; no full equipment Ability, native CharacterMovement and Jolt joint parity, GPU or general layer acceptance; ordinary Jolt regression included',
              summary=summary, captures=captures, reports=reports, componentReports=component_reports,
              testReports=test_reports, nativeMathSha256=native_math, retainedFailures=retained_failures,
              sourceSha256={p: sha(repo / p) for p in sources}, protectedCounts={k: len(v) for k, v in protected.items()},
              queueRequestSha256=queue_native['requestSha256'], ordinaryReportsExactlyEqual=True,
              controlPrefixAuditSha256=sha(evidence / 'whole-main-rebind60-probe-control-integrity.json'))
with output.open('x', encoding='utf-8', newline='\n') as stream:
    json.dump(result, stream, separators=(',', ':'), allow_nan=False)
    stream.write('\n')
print('LYRA_WHOLE_MAIN_REBIND_INTEGRITY_OK framesPerConfiguration=7560 reports=6 boundaries=18 regressions=14 componentRegressions=8 coreTests=99 goalComplete=false')
