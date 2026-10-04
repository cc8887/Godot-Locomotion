"""Audit authored idle/turn variation, real linked settings and live character gates."""
import gc
import json
from pathlib import Path

from verify_lyra_multi_layer_native import ASSETS, EVIDENCE, PROJECT, ROOT, package_file, read, sha
from verify_lyra_multi_owner_runtime import text


def main():
    tag = 'linked-idle-turn-v2'
    output = EVIDENCE / f'{tag}-integrity.json'
    assert not output.exists(), 'Preserve evidence.'
    evidence = {}

    def load(path):
        evidence[str(path.relative_to(ROOT))] = sha(path)
        return read(path)

    def log(path, godot=True):
        evidence[str(path.relative_to(ROOT))] = sha(path)
        value = text(path)
        if godot:
            assert not any(line.lstrip().startswith(('ERROR:', 'WARNING:')) for line in value.splitlines()), path
        return value

    sources = load(EVIDENCE / f'{tag}-frozen-sources.json')
    for name, digest in sources.items():
        assert sha(ROOT / name) == digest, name
    before = load(EVIDENCE / 'linked-graph-fields-v1-before/manifest.json')
    resources = {name: digest for name, digest in before.items() if name.startswith('assets/')}
    assert len(resources) == 869
    for name, digest in resources.items():
        assert sha(ROOT / name) == digest, name

    ref = f'{tag}-30-full'
    closure = load(EVIDENCE / f'whole-main-{ref}-closure.json')
    launch = load(EVIDENCE / f'whole-main-{ref}-launch.json')
    for name, digest in closure['assetSha256'].items():
        assert sha(package_file(name)) == digest, name
    for name, digest in closure['protectedProject'].items():
        assert sha(PROJECT / name) == digest, name
    for name, digest in closure['probeSourceSha256'].items():
        assert sha(ROOT / 'tools/unreal/LyraWholeMainOracle' / name) == digest, name
        assert sha(Path(launch['package']) / name) == digest, name
    for name, digest in closure['captureSourceSha256'].items():
        assert sha(ROOT / name) == digest, name
    native_log = log(EVIDENCE / f'whole-main-native-{ref}.log', godot=False)
    assert 'LYRA_WHOLE_MAIN_CAPTURE_OK traces=6 frames=4320 assets_saved=0' in native_log
    assert 'WHOLE_MAIN_PROCESS_EXIT=0' in native_log and 'LYRA_WHOLE_MAIN_FAILED' not in native_log
    assert not any(marker in native_log for marker in ('Error:', 'Fatal error:', 'Ensure condition failed'))
    provenance = load(EVIDENCE / f'whole-main-{ref}-idle-turn-provenance.json')
    assert provenance['authoredInputChangesOnly'] and not provenance['probeChanged']
    for key, name in (('originalCaptureSha256', 'tools/unreal/capture_lyra_whole_main.py'),
                      ('wrapperSha256', 'tools/unreal/capture_lyra_linked_idle_turn.py'),
                      ('runnerSha256', 'scripts/capture-lyra-linked-idle-turn.ps1')):
        assert sha(ROOT / name) == provenance[key]
    request = load(EVIDENCE / f'whole-main-{ref}-request.json')
    native = load(EVIDENCE / f'whole-main-{ref}-native.json')
    assert len(request['traces']) == len(native['traceFiles']) == len(provenance['coverage']) == 6
    assert {(c['profile'], c['layout']) for c in provenance['coverage']} == {
        (p, layout) for p in ('unarmed', 'pistol', 'rifle') for layout in ('single', 'per-call')}
    idle = load(ASSETS / 'idle_runtime_v2_requests.json')
    counts = {t['profile']: len(t['breaks']) for t in idle['traces']}
    witnesses = []
    for authored, entry, declared in zip(request['traces'], native['traceFiles'], provenance['coverage'], strict=True):
        path = EVIDENCE / entry['file']
        assert path.name == entry['file'] and sha(path) == entry['sha256']
        evidence[str(path.relative_to(ROOT))] = sha(path)
        raw = read(path)
        assert len(raw['frames']) == len(authored['frames']) == declared['frames'] == 720
        changes = {name: 0 for name in declared['changes']}
        values = {name: set() for name in changes}
        for frame in raw['frames']:
            for before, updated, after in zip(frame['instancesBefore'], frame['instancesUpdated'], frame['instancesAfter'], strict=True):
                assert before['owner'] == updated['owner'] == after['owner']
                for name in changes:
                    changes[name] += before['fields'][name] != updated['fields'][name]
                    values[name].update((before['fields'][name], updated['fields'][name], after['fields'][name]))
        assert changes == declared['changes'] and {n: sorted(v) for n, v in values.items()} == declared['values']
        assert all(n > 0 for name, n in changes.items() if name != 'CurrentIdleBreakIndex')
        assert declared['idleBreakCount'] == counts[authored['profile']]
        assert changes['CurrentIdleBreakIndex'] > 0 if counts[authored['profile']] > 1 else values['CurrentIdleBreakIndex'] == {0}
        for name in ('TurnInPlaceRotationDirection', 'TurnInPlaceRecoveryDirection'):
            assert {-1, 1}.issubset(values[name])
        for index, frame in enumerate(authored['frames']):
            time = index / authored['hz']
            expected = 3 <= time < 4.5 or 10 <= time < 12 or 19 <= time < 22
            assert frame['layerProperties'] == {'EnableLeftHandPoseOverride': expected}
            assert frame['relink'] == (index % 37 == 19)
        assert declared['enabledFrames'] == 195
        witnesses.append(declared)
        del raw
        gc.collect()

    refs = {30: 'multi-layer-v3-30-full', 60: 'multi-layer-v3-60-repeat-full', 120: 'multi-layer-v3-120-full'}
    for name in ('linked-private-v2-30-full', *refs.values()):
        old_native = load(EVIDENCE / f'whole-main-{name}-native.json')
        load(EVIDENCE / f'whole-main-{name}-request.json')
        for entry in old_native['traceFiles']:
            assert sha(EVIDENCE / entry['file']) == entry['sha256']

    previous_graph = load(EVIDENCE / 'linked-graph-fields-v1-integrity.json')
    assert previous_graph['auditPassed'] and previous_graph['totalComparedPrivateFields'] == 32
    graph_changes = dict(previous_graph['graphFieldChangeWitnesses'])
    for witness in witnesses:
        for name, count in witness['changes'].items():
            graph_changes[name] += count
    assert len(graph_changes) == 12 and all(count > 0 for count in graph_changes.values())

    owners = {'single': 1, 'three-groups': 3, 'mixed': 4, 'per-call': 14}
    processes = frames = graph_comparisons = setting_comparisons = pending_rejected = restores = 0
    live_frames = live_enabled = live_disabled = live_retries = 0
    configurations = []
    for configuration, directory in (('debug', 'Debug'), ('optimize', 'ExportRelease')):
        build = log(EVIDENCE / f'{tag}-build-{"debug" if configuration == "debug" else "export-release"}.log')
        assert '0 个警告' in build and '0 个错误' in build
        reports = []
        for suffix, reference, layouts, boundaries in (
            ('native', ref, ('single', 'per-call'), ('pre-rig', 'final')),
            ('authored', 'linked-private-v2-30-full', tuple(owners), ('pre-rig',)),
            *((f'matrix-{hz}', reference, ('per-call',), ('pre-rig',)) for hz, reference in refs.items()),
        ):
            report = load(EVIDENCE / f'{tag}-{suffix}-{configuration}-verification.json')
            assert report['runTag'] == reference
            assert {(r['layout'], r['boundary']) for r in report['runs']} == {(l, b) for l in layouts for b in boundaries}
            reports.append(report)
        assemblies = reports[0]['assemblies']
        for name, digest in assemblies.items():
            assert sha(ROOT / '.godot/mono/temp/bin' / directory / name) == digest.lower(), name
        for report in reports:
            assert report['passed'] and report['assemblies'] == assemblies
            assert not report['fullPrivateFieldParity'] and not report['goalComplete']
            for flag, count in (('worker', 8), ('preUpdate', 3), ('movement', 9), ('graph', 12)):
                assert report[flag + 'Fields'] and report[flag + 'FieldCount'] == count
            assert report['leftSettings'] and report['leftSettingFieldCount'] == 1
            if configuration == 'optimize':
                assert report['debugRestored']
                restores += 1
            for run in report['runs']:
                assert run['passed'] and run['exitCode'] == 0
                path = Path(run['log'])
                assert sha(path) == run['logSha256'].lower()
                value = log(path)
                n = int(run['frames'])
                comparisons = n * owners[run['layout']] * 5
                for marker, count in (('WORKER', 8), ('PREUPDATE', 3), ('MOVEMENT', 9), ('GRAPH', 12)):
                    assert f'LYRA_LINKED_{marker}_FIELDS_OK layout={run["layout"]} frames={n} fields={count} comparisons={comparisons * count}' in value
                assert f'LYRA_LINKED_LEFT_SETTINGS_OK layout={run["layout"]} frames={n} fields=1 comparisons={comparisons} pendingRejected={n * 2}' in value
                assert f'retry={n} controlledPhysicalInputs=true' in value and 'MULTI_OWNER_PROCESS_EXIT=0' in value
                processes += 1
                frames += n
                graph_comparisons += comparisons * 12
                setting_comparisons += comparisons
                pending_rejected += n * 2
        components = load(EVIDENCE / f'{tag}-components-{configuration}-verification.json')
        assert components['passed'] and components['assemblies'] == assemblies and len(components['runs']) == 4
        current = []
        for run in components['runs']:
            assert run['passed'] and run['exitCode'] == 0
            path = ROOT / run['log']
            assert sha(path) == run['logSha256'].lower()
            value = log(path)
            assert 'IDLE_TURN_PROCESS_EXIT=0' in value and 'LYRA_MAIN_MULTI_DEMO_GODOT_OK' in value
            path = Path(run['report'])
            assert sha(path) == run['reportSha256'].lower()
            saved = load(path)
            if run['leftSettings']:
                settings, saved = saved['leftSettings'], saved['model']
                hz = run['hz']
                assert hz in (30, 60, 120) and saved['characters'] == 10
                assert settings['pendingRejected'] == hz * 8
                assert settings['retiredRejected'] == 6 * 14 and settings['sameClassPreserved'] == 6
                assert settings['nullSequence'] and settings['enabledFrames'] > 0 and settings['disabledFrames'] > 0
                assert settings['enabledFrames'] + settings['disabledFrames'] == hz * 8 * 10
                assert saved['player']['model']['retries'] == hz * 8
                assert saved['player']['switches'] == saved['player']['sameClassReuse'] == 6
                assert all(c['published'] == hz * 8 for c in saved['companions'])
                assert 'LYRA_LINKED_LEFT_SETTINGS_DEMO_OK' in value
                live_frames += hz * 8 * 10
                live_enabled += settings['enabledFrames']
                live_disabled += settings['disabledFrames']
                live_retries += hz * 8
            else:
                assert saved == load(EVIDENCE / f'linked-graph-fields-v1-components-{configuration}-per-call-ten.json')
            current.append(read(path))
            processes += 1
        configurations.append((assemblies, current))
        if configuration == 'optimize':
            assert components['debugRestored']
            restores += 1
    assert configurations[0][1] == configurations[1][1]
    for backup in (f'{tag}-native-optimize-debug-backup', f'{tag}-authored-optimize-debug-backup',
                   *(f'{tag}-matrix-{hz}-optimize-debug-backup' for hz in refs), f'{tag}-components-optimize-debug-backup'):
        for name, digest in configurations[0][0].items():
            assert sha(EVIDENCE / backup / name) == digest.lower(), (backup, name)
    assert processes == 30 and frames == 41040 and graph_comparisons == 23328000 and restores == 6
    assert setting_comparisons == 1944000 and pending_rejected == 82080
    assert live_frames == 33600 and live_retries == 3360
    result = dict(auditPassed=True, finalGodotProcesses=processes, nativeSubmittedFrames=frames,
                  graphFieldComparisons=graph_comparisons, settingFieldComparisons=setting_comparisons,
                  pendingSettingWritesRejected=pending_rejected, totalComparedPrivateFields=33,
                  authoredNativeFrames=4320, coverage=witnesses,
                  combinedGraphFieldChangeWitnesses=graph_changes, constantGraphFields=[],
                  liveSettingCharacterFrames=live_frames, liveSettingEnabledFrames=live_enabled,
                  liveSettingDisabledFrames=live_disabled, livePlayerRetries=live_retries,
                  fullPrivateFieldParity=False, fullPhysicalParity=False, nonNullLeftHandSequence=False,
                  goalComplete=False, newUeCapture=True, ueAssetsSaved=0,
                  unchangedLyraJson=len(resources), unchangedOriginalPackages=len(closure['assetSha256']),
                  unchangedProjectConfiguration=len(closure['protectedProject']), assemblyRestoreRounds=restores,
                  perCallTenDefaultReportsUnchanged=True, sources=sources, evidence=evidence,
                  verifierSha256=sha(Path(__file__)))
    with output.open('x', encoding='utf-8', newline='\n') as stream:
        json.dump(result, stream, ensure_ascii=False, indent=2, allow_nan=False)
        stream.write('\n')
    print(output)


if __name__ == '__main__':
    main()
