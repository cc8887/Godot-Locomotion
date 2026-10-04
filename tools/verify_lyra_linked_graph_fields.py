"""Audit real linked graph fields at update/commit/retry boundaries."""
import json
from pathlib import Path

from verify_lyra_multi_layer_native import ASSETS, EVIDENCE, PROJECT, ROOT, package_file, read, sha
from verify_lyra_multi_owner_runtime import text


GRAPH_FIELDS = (
    'IdleBreakDelayTime', 'TimeUntilNextIdleBreak', 'CurrentIdleBreakIndex',
    'TurnInPlaceRotationDirection', 'TurnInPlaceRecoveryDirection', 'TurnInPlaceAnimTime',
    'PivotStartingAcceleration', 'TimeAtPivotStop', 'StrideWarpingStartAlpha',
    'StrideWarpingCycleAlpha', 'StrideWarpingPivotAlpha', 'LeftHandPoseOverrideWeight',
)


def main():
    tag = 'linked-graph-fields-v1'
    output = EVIDENCE / f'{tag}-integrity.json'
    assert not output.exists(), 'Preserve evidence.'
    evidence = {}

    def load(path):
        evidence[str(path.relative_to(ROOT))] = sha(path)
        return read(path)

    def clean_log(path):
        evidence[str(path.relative_to(ROOT))] = sha(path)
        value = text(path)
        assert not any(line.lstrip().startswith(('ERROR:', 'WARNING:')) for line in value.splitlines()), path
        return value

    sources = load(EVIDENCE / f'{tag}-frozen-sources.json')
    for name, digest in sources.items():
        assert sha(ROOT / name) == digest, name
    before = load(EVIDENCE / f'{tag}-before/manifest.json')
    resources = {name: digest for name, digest in before.items() if name.startswith('assets/')}
    assert len(resources) == 869
    for name, digest in resources.items():
        assert sha(ROOT / name) == digest, name

    fresh = 'linked-private-v2-30-full'
    refs = {30: 'multi-layer-v3-30-full', 60: 'multi-layer-v3-60-repeat-full', 120: 'multi-layer-v3-120-full'}
    closure = load(EVIDENCE / f'whole-main-{fresh}-closure.json')
    for name, digest in closure['assetSha256'].items():
        assert sha(package_file(name)) == digest, name
    for name, digest in closure['protectedProject'].items():
        assert sha(PROJECT / name) == digest, name
    for name, digest in closure['probeSourceSha256'].items():
        assert sha(ROOT / 'tools/unreal/LyraWholeMainOracle' / name) == digest, name
        assert sha(ROOT / 'artifacts/unreal/lyra-whole-main-oracle/package-multi-layer-v1' / name) == digest, name
    for name, digest in closure['captureSourceSha256'].items():
        assert sha(ROOT / name) == digest, name
    for ref in (*refs.values(), fresh):
        native = load(EVIDENCE / f'whole-main-{ref}-native.json')
        load(EVIDENCE / f'whole-main-{ref}-request.json')
        for entry in native['traceFiles']:
            assert Path(entry['file']).name == entry['file']
            assert sha(EVIDENCE / entry['file']) == entry['sha256']

    counts = {'single': 1, 'three-groups': 3, 'mixed': 4, 'per-call': 14}
    processes = frames = graph_comparisons = restores = 0
    configurations = []
    for configuration, directory in (('debug', 'Debug'), ('optimize', 'ExportRelease')):
        build_name = 'debug' if configuration == 'debug' else 'export-release'
        build = clean_log(EVIDENCE / f'{tag}-build-{build_name}.log')
        assert '0 个警告' in build and '0 个错误' in build
        authored = load(EVIDENCE / f'{tag}-authored-30-{configuration}-verification.json')
        reports = [authored]
        assert {(run['layout'], run['boundary']) for run in authored['runs']} == {
            (layout, boundary) for layout in counts for boundary in ('pre-rig', 'final')}
        assert authored['runTag'] == fresh
        for hz, ref in refs.items():
            report = load(EVIDENCE / f'{tag}-matrix-{hz}-{configuration}-verification.json')
            assert report['runTag'] == ref
            assert [(run['layout'], run['boundary']) for run in report['runs']] == [('per-call', 'pre-rig')]
            reports.append(report)
        assemblies = authored['assemblies']
        for name, digest in assemblies.items():
            assert sha(ROOT / '.godot/mono/temp/bin' / directory / name) == digest.lower(), name
        for report in reports:
            assert report['passed'] and report['assemblies'] == assemblies
            assert not report['fullPrivateFieldParity'] and not report['goalComplete']
            for flag, number in (('worker', 8), ('preUpdate', 3), ('movement', 9), ('graph', 12)):
                assert report[flag + 'Fields'] and report[flag + 'FieldCount'] == number
            if configuration == 'optimize':
                assert report['debugRestored']
                restores += 1
            for run in report['runs']:
                assert run['passed'] and run['exitCode'] == 0
                path = Path(run['log'])
                assert sha(path) == run['logSha256'].lower()
                value = clean_log(path)
                n = int(run['frames'])
                comparisons = n * counts[run['layout']] * 5
                for marker, number in (('WORKER', 8), ('PREUPDATE', 3), ('MOVEMENT', 9), ('GRAPH', 12)):
                    assert f'LYRA_LINKED_{marker}_FIELDS_OK layout={run["layout"]} frames={n} fields={number} comparisons={comparisons * number}' in value
                assert f'retry={n} controlledPhysicalInputs=true' in value and 'MULTI_OWNER_PROCESS_EXIT=0' in value
                processes += 1
                frames += n
                graph_comparisons += comparisons * 12
        components = load(EVIDENCE / f'{tag}-components-{configuration}-verification.json')
        assert components['passed'] and len(components['runs']) == 1
        run = components['runs'][0]
        assert run['name'] == 'per-call-ten' and run['passed'] and run['exitCode'] == 0
        assert 'WORKER_PROCESS_EXIT=0' in clean_log(ROOT / run['log'])
        ten = load(EVIDENCE / f'{tag}-components-{configuration}-per-call-ten.json')
        assert ten == load(EVIDENCE / f'linked-movement-v1-components-{configuration}-per-call-ten.json')
        processes += 1
        configurations.append((assemblies, ten))

    assert configurations[0][1] == configurations[1][1]
    for backup in [f'{tag}-authored-30-optimize-debug-backup',
                   *(f'{tag}-matrix-{hz}-optimize-debug-backup' for hz in refs),
                   f'{tag}-components-optimize-debug-backup']:
        for name, digest in configurations[0][0].items():
            assert sha(EVIDENCE / backup / name) == digest.lower(), (backup, name)
    restores += 1
    assert processes == 24 and frames == 32400 and graph_comparisons == 18403200 and restores == 5

    # Coverage is taken from captured changing values, not from a green marker.
    inventories = [load(EVIDENCE / name) for name in
                   ('linked-private-v1-private-fields.json', 'linked-private-v2-authored-private-fields.json')]
    changed = {}
    for field in GRAPH_FIELDS:
        changed[field] = sum(sum(t['fields'][field].get(key, 0) for key in
                                 ('visitedUpdateChanges', 'dormantUpdateChanges', 'evaluateChanges'))
                             for inventory in inventories for t in inventory['trajectories'])
    assert len(changed) == 12 and sum(n > 0 for n in changed.values()) == 7
    result = dict(auditPassed=True, finalGodotProcesses=processes, nativeSubmittedFrames=frames,
                  graphFields=12, graphFieldComparisons=graph_comparisons, totalComparedPrivateFields=32,
                  graphFieldChangeWitnesses=changed, constantGraphFields=[name for name, n in changed.items() if not n],
                  fullPrivateFieldParity=False, fullPhysicalParity=False, goalComplete=False, newUeCapture=False,
                  unchangedLyraJson=len(resources), unchangedOriginalPackages=len(closure['assetSha256']),
                  unchangedProjectConfiguration=len(closure['protectedProject']), assemblyRestoreRounds=restores,
                  perCallTenReportsUnchanged=True, sources=sources, evidence=evidence,
                  verifierSha256=sha(Path(__file__)))
    with output.open('x', encoding='utf-8', newline='\n') as stream:
        json.dump(result, stream, ensure_ascii=False, indent=2, allow_nan=False)
        stream.write('\n')
    print(output)


if __name__ == '__main__':
    main()
