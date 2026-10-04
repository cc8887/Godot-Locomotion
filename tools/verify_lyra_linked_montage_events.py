"""Audit actual linked empty banks and dispatch phases without claiming generic events."""
import gc
import json
import xml.etree.ElementTree as ET
from pathlib import Path

from verify_lyra_multi_layer_native import ASSETS, EVIDENCE, PROJECT, ROOT, package_file, read, sha
from verify_lyra_multi_owner_runtime import text


def main():
    tag = 'linked-montage-events-v1'
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
    previous = load(EVIDENCE / 'linked-idle-turn-v2-integrity.json')
    assert previous['auditPassed'] and previous['totalComparedPrivateFields'] == 33

    provenance = load(EVIDENCE / f'{tag}-before/probe-provenance.json')
    for name, digest in provenance['originalProbeSha256'].items():
        assert sha(ROOT / 'tools/unreal/LyraWholeMainOracle' / name) == digest, name
    cpp = 'Source/LyraWholeMainOracle/Private/LyraWholeMainOracleLibrary.cpp'
    old = (ROOT / 'tools/unreal/LyraWholeMainOracle' / cpp).read_bytes()
    new = (ROOT / 'tools/unreal/LyraLinkedMontageOracle' / cpp).read_bytes()
    anchor = b'            Main->DispatchQueuedAnimEvents();Rows.Add(MakeShared<FJsonValueObject>(Row));\n'
    assert old.count(anchor) == 1
    start = old.index(anchor)
    tail = old[start + len(anchor):]
    assert new[:start] == old[:start] and new.endswith(tail)
    delta = new[start:len(new) - len(tail)]
    assert b'GetLinkedAnimInstances()' in delta and delta.count(b'A->DispatchQueuedAnimEvents();') == 1
    assert delta.index(b'A->DispatchQueuedAnimEvents();') < delta.index(b'Main->DispatchQueuedAnimEvents();')
    assert b'mainDispatched' in delta and b'instancesDispatched' in delta
    assert provenance['addedDispatchSnapshots'] and provenance['graphAndUpdateUnchanged']

    reference = f'{tag}-30-full'
    closure = load(EVIDENCE / f'whole-main-{reference}-closure.json')
    launch = load(EVIDENCE / f'whole-main-{reference}-launch.json')
    for name, digest in closure['assetSha256'].items():
        assert sha(package_file(name)) == digest, name
    for name, digest in closure['protectedProject'].items():
        assert sha(PROJECT / name) == digest, name
    for name, digest in closure['probeSourceSha256'].items():
        assert provenance['newProbeSha256'][name] == digest
        assert sha(ROOT / 'tools/unreal/LyraLinkedMontageOracle' / name) == digest, name
        assert sha(Path(launch['package']) / name) == digest, name
        if name != cpp:
            assert provenance['originalProbeSha256'][name] == digest
    for name, digest in closure['captureSourceSha256'].items():
        assert sha(ROOT / name) == digest, name
    capture = log(EVIDENCE / f'whole-main-native-{reference}.log', godot=False)
    assert 'LYRA_WHOLE_MAIN_CAPTURE_OK traces=6 frames=2160 assets_saved=0' in capture
    assert 'WHOLE_MAIN_PROCESS_EXIT=0' in capture
    assert not any(marker in capture for marker in ('Error:', 'Fatal error:', 'Ensure condition failed', 'LYRA_WHOLE_MAIN_FAILED'))
    ue_build = log(EVIDENCE / 'whole-main-build-package-linked-montage-events-v1.log', godot=False)
    assert 'Result: Succeeded' in ue_build

    request = load(EVIDENCE / f'whole-main-{reference}-request.json')
    native = load(EVIDENCE / f'whole-main-{reference}-native.json')
    assert len(request['traces']) == len(native['traceFiles']) == 6
    assert {(t['profile'], t['layout']) for t in request['traces']} == {
        (p, l) for p in ('unarmed', 'pistol', 'rifle') for l in ('single', 'per-call')}
    witnesses = []
    counts = {'single': 1, 'three-groups': 3, 'mixed': 4, 'per-call': 14}
    for authored, entry in zip(request['traces'], native['traceFiles'], strict=True):
        path = EVIDENCE / entry['file']
        assert path.name == entry['file'] and sha(path) == entry['sha256']
        raw = load(path)
        assert len(raw['frames']) == len(authored['frames']) == 360
        owners = counts[authored['layout']]
        hidden_updates = held_before = component_frames = 0
        for index, (frame, row) in enumerate(zip(authored['frames'], raw['frames'], strict=True)):
            dispatch = not 4 <= index / authored['hz'] < 5
            assert frame['dispatchLinked'] == dispatch and frame['relink'] == (index % 37 == 19)
            assert row['dispatchOrder'] == (list(range(owners)) if dispatch else [])
            assert not row['mainBefore']['bQueueMontageEvents']
            assert row['mainUpdated']['bQueueMontageEvents'] and row['mainAfter']['bQueueMontageEvents']
            assert not row['mainDispatched']['bQueueMontageEvents']
            expected_before = index > 0 and not authored['frames'][index - 1]['dispatchLinked']
            active_hooks = {u['hook'] for u in row['updates'] if u['weight'] > 0}
            visited = {c['owner'] for c in row['calls'] if c['function'] in active_hooks}
            snapshots = [row[k] for k in ('instancesBefore', 'instancesUpdated', 'instancesAfter', 'instancesDispatched')]
            assert all(len(s) == owners for s in snapshots)
            for before_row, updated_row, after_row, dispatched_row in zip(*snapshots, strict=True):
                owner = before_row['owner']
                assert owner == updated_row['owner'] == after_row['owner'] == dispatched_row['owner']
                assert before_row['fields']['bQueueMontageEvents'] == expected_before
                assert updated_row['fields']['bQueueMontageEvents'] and after_row['fields']['bQueueMontageEvents']
                assert dispatched_row['fields']['bQueueMontageEvents'] == (not dispatch)
                hidden_updates += owner not in visited
                held_before += expected_before
            component_frames += dispatch
        # The single grouped instance remains visited through postprocessing.
        # Dormant instance coverage belongs to the per-call allocation.
        assert (hidden_updates > 0 if owners > 1 else hidden_updates == 0)
        assert held_before == 30 * owners and component_frames == 330
        commands = [(i, c) for i, f in enumerate(authored['frames']) for c in f['commands']]
        assert len(commands) == 4 and [i for i, _ in commands] == [18, 120, 129, 180]
        assert [c['stop'] for _, c in commands] == [False, False, False, True]
        assert any(row['frozen'] for row in raw['frames'])
        witnesses.append(dict(profile=authored['profile'], layout=authored['layout'], owners=owners, frames=360,
                              componentDispatchFrames=component_frames, mainOnlyFrames=30,
                              hiddenOwnerUpdates=hidden_updates, retainedBeforeOwnerFrames=held_before))
        del raw
        gc.collect()

    refs = {30: 'multi-layer-v3-30-full', 60: 'multi-layer-v3-60-repeat-full', 120: 'multi-layer-v3-120-full'}
    for ref in ('linked-private-v2-30-full', 'linked-idle-turn-v2-30-full', *refs.values()):
        old_native = load(EVIDENCE / f'whole-main-{ref}-native.json')
        load(EVIDENCE / f'whole-main-{ref}-request.json')
        for entry in old_native['traceFiles']:
            assert sha(EVIDENCE / entry['file']) == entry['sha256']

    processes = frames = linked_comparisons = main_comparisons = restores = 0
    live_frames = pending = foreign = late = retired = 0
    configurations = []
    suffixes = [('native', reference, ('single', 'per-call'), ('pre-rig', 'final')),
                ('authored', 'linked-private-v2-30-full', ('three-groups', 'mixed'), ('pre-rig',)),
                *((f'matrix-{hz}', ref, ('per-call',), ('pre-rig',)) for hz, ref in refs.items()),
                ('idle', 'linked-idle-turn-v2-30-full', ('per-call',), ('pre-rig',))]
    for configuration, directory in (('debug', 'Debug'), ('optimize', 'ExportRelease')):
        build = log(EVIDENCE / f'{tag}-build-{"debug" if configuration == "debug" else "export-release"}-final.log')
        assert '0 个警告' in build and '0 个错误' in build
        assemblies = None
        for suffix, ref, layouts, boundaries in suffixes:
            report = load(EVIDENCE / f'{tag}-{suffix}-{configuration}-verification.json')
            assert report['runTag'] == ref
            assert {(r['layout'], r['boundary']) for r in report['runs']} == {(l, b) for l in layouts for b in boundaries}
            assemblies = assemblies or report['assemblies']
            assert report['assemblies'] == assemblies and report['passed']
            assert not report['fullPrivateFieldParity'] and not report['goalComplete']
            for flag, count in (('worker', 8), ('preUpdate', 3), ('movement', 9), ('graph', 12), ('montageEvent', 1)):
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
                snapshots = n if suffix == 'native' else 0
                component = n * 11 // 12 if suffix == 'native' else 0
                linked = (n * 5 + snapshots) * counts[run['layout']]
                main = n * 5 + snapshots
                assert (f'LYRA_LINKED_MONTAGE_EVENT_FIELDS_OK layout={run["layout"]} frames={n} fields=1 comparisons={linked}'
                        f' mainComparisons={main} componentDispatchFrames={component} dispatchedSnapshots={snapshots}') in value
                for marker, count in (('WORKER', 8), ('PREUPDATE', 3), ('MOVEMENT', 9), ('GRAPH', 12)):
                    assert f'LYRA_LINKED_{marker}_FIELDS_OK layout={run["layout"]} frames={n} fields={count} comparisons={n * counts[run["layout"]] * count * 5}' in value
                assert f'retry={n} controlledPhysicalInputs=true' in value and 'MULTI_OWNER_PROCESS_EXIT=0' in value
                processes += 1
                frames += n
                linked_comparisons += linked
                main_comparisons += main
        for name, digest in assemblies.items():
            assert sha(ROOT / '.godot/mono/temp/bin' / directory / name) == digest.lower(), name
        components = load(EVIDENCE / f'{tag}-components-{configuration}-verification.json')
        assert components['passed'] and components['assemblies'] == assemblies and len(components['runs']) == 4
        assert not components['fullMontageEventDispatch'] and not components['goalComplete']
        current = []
        for run in components['runs']:
            assert run['passed'] and run['exitCode'] == 0
            path = ROOT / run['log']
            assert sha(path) == run['logSha256'].lower()
            value = log(path)
            assert 'MONTAGE_PHASE_PROCESS_EXIT=0' in value
            if 'report' in run:
                saved_path = Path(run['report'])
                assert sha(saved_path) == run['reportSha256'].lower()
                saved = load(saved_path)
                p = saved['montagePhases']
                roles = 10 if run['name'] == 'per-call-ten' else 1
                assert p['publishedFrames'] == 480 * roles and p['preparedFrames'] == 960 and p['cancelledFrames'] == 480
                assert p['pendingRejected'] == 14400 and p['foreignRejected'] == p['lateRejected'] == 480 * roles * 14
                assert p['retiredRejected'] == 84 and p['linkedBanksEmpty'] and not p['fullMontageEventDispatch']
                assert 'LYRA_LINKED_MONTAGE_PHASE_DEMO_OK' in value
                if roles == 10:
                    assert saved['model'] == load(EVIDENCE / f'linked-idle-turn-v2-components-{configuration}-per-call-ten.json')
                else:
                    assert saved['model']['emote'] == dict(Activations=1, Ends=1, MovementClears=1)
                current.append(saved)
                live_frames += p['publishedFrames']
                pending += p['pendingRejected']
                foreign += p['foreignRejected']
                late += p['lateRejected']
                retired += p['retiredRejected']
            elif run['name'] == 'original-emote':
                assert 'traces=54 frames=27720' in value
            else:
                assert 'ALS_REFACTORED_STANCE_DEMO_OK hz=60' in value
            processes += 1
        configurations.append((assemblies, current))
        if configuration == 'optimize':
            assert components['debugRestored']
            restores += 1
    assert configurations[0][1] == configurations[1][1]
    for suffix in [s[0] for s in suffixes] + ['components']:
        backup = EVIDENCE / f'{tag}-{suffix}-optimize-debug-backup'
        for name, digest in configurations[0][0].items():
            assert sha(backup / name) == digest.lower(), (backup, name)
    trx = EVIDENCE / f'{tag}-core-2.trx'
    evidence[str(trx.relative_to(ROOT))] = sha(trx)
    counters = ET.parse(trx).getroot().find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters').attrib
    assert (counters['total'], counters['passed'], counters['failed'], counters['notExecuted']) == ('56', '56', '0', '0')
    assert processes == 28 and frames == 32400 and restores == 7
    assert linked_comparisons == 1825200 and main_comparisons == 170640 and live_frames == 10560
    result = dict(auditPassed=True, finalGodotProcesses=processes, nativeSubmittedFrames=frames,
                  linkedQueuePhaseComparisons=linked_comparisons, mainQueuePhaseComparisons=main_comparisons,
                  totalComparedPrivateFields=34, totalExportedPrivateFields=47, authoredNativeFrames=2160,
                  nativePhaseWitnesses=witnesses, liveCharacterFrames=live_frames,
                  livePendingDispatchRejected=pending, foreignDispatchRejected=foreign,
                  lateDispatchRejected=late, retiredDispatchRejected=retired, coreTestsPassed=56,
                  fullPrivateFieldParity=False, fullMontageEventDispatch=False, fullPhysicalParity=False,
                  nonNullLeftHandSequence=False, sharedMainMontageModeValidated=False,
                  goalComplete=False, newUeCapture=True, ueAssetsSaved=0,
                  unchangedLyraJson=len(resources), unchangedOriginalPackages=len(closure['assetSha256']),
                  unchangedProjectConfiguration=len(closure['protectedProject']), assemblyRestoreRounds=restores,
                  originalProbeUnchanged=True, originalGraphUpdateUnchanged=True,
                  perCallTenDefaultReportsUnchanged=True, sources=sources, evidence=evidence,
                  verifierSha256=sha(Path(__file__)))
    with output.open('x', encoding='utf-8', newline='\n') as stream:
        json.dump(result, stream, ensure_ascii=False, indent=2, allow_nan=False)
        stream.write('\n')
    print(output)


if __name__ == '__main__':
    main()
