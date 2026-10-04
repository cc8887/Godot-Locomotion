"""Audit complete native multi-owner graph traces without claiming Godot parity."""
import argparse
import gc
import hashlib
import json
import math
from pathlib import Path

from locomotion_paths import project_path

ROOT = Path(__file__).resolve().parents[1]
EVIDENCE = ROOT / 'artifacts/lyra-analysis'
ASSETS = ROOT / 'assets/generated/lyra_als'
PROJECT = project_path()
COUNTS = {'single': 1, 'three-groups': 3, 'mixed': 4, 'per-call': 14}
read = lambda p: json.loads(p.read_bytes())
DIAGNOSTICS = {'Main_InertiaInput', 'Main_InertiaOutput', 'Main_Dynamic', 'Main_Lower',
               'Main_Upper', 'Main_UpperSource', 'Aim_Relaxed', 'Aim_Ready',
               'Aim_RelaxedInput', 'Aim_ReadyInput', 'Aim_Relaxed_Additive', 'Aim_Ready_Additive'}


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def package_file(name):
    name = name.split('.')[0]
    if name.startswith('/Game/'):
        return PROJECT / 'Content' / (name.removeprefix('/Game/') + '.uasset')
    if name.startswith('/ShooterCore/'):
        return PROJECT / 'Plugins/GameFeatures/ShooterCore/Content' / (name.removeprefix('/ShooterCore/') + '.uasset')
    raise AssertionError(name)


def finite(value):
    if isinstance(value, float):
        assert math.isfinite(value)
    elif isinstance(value, dict):
        for item in value.values():
            finite(item)
    elif isinstance(value, list):
        for item in value:
            finite(item)


def verify(tag, full):
    paths = {kind: EVIDENCE / f'whole-main-{tag}-{kind}.json'
             for kind in ('request', 'native', 'closure', 'launch')}
    request, native, closure, launch = (read(paths[k]) for k in paths)
    assert launch['runTag'] == tag and launch['case'] == 'multi-layer'
    assert closure['scope']['multipleOriginalLayerGraphs']
    assert closure['scope']['originalFullRoot'] and closure['scope']['originalUnifiedSync']
    assert not closure['scope']['actualCharacterMovementSimulation']
    assert not closure['scope']['comparisonPassed'] and not closure['scope']['goalComplete']
    log = (EVIDENCE / f'whole-main-native-{tag}.log').read_text(encoding='utf-8-sig', errors='replace')
    assert 'LYRA_WHOLE_MAIN_CAPTURE_OK' in log and 'WHOLE_MAIN_PROCESS_EXIT=0' in log
    assert 'LYRA_WHOLE_MAIN_FAILED' not in log
    assert not any(marker in log for marker in ('Error:', 'Fatal error:', 'Ensure condition failed'))
    for name, digest in closure['previousFixtureSha256'].items():
        assert sha(ASSETS / name) == digest, name
    for name, digest in closure['protectedProject'].items():
        assert sha(PROJECT / name) == digest, name
    for name, digest in closure.get('captureSourceSha256', {}).items():
        assert sha(ROOT / name) == digest, name
    packages = dict(closure['assetSha256'])
    contracts = read(ASSETS / 'linked_layer_contracts.json')
    for alias, digest in contracts['assetSha256'].items():
        path = contracts['classes'][alias]['class']
        assert packages.get(path, digest) == digest
        packages[path] = digest
    for name, digest in packages.items():
        assert sha(package_file(name)) == digest, name
    package = Path(launch['package'])
    for name, digest in closure['probeSourceSha256'].items():
        assert sha(ROOT / 'tools/unreal/LyraWholeMainOracle' / name) == digest, name
        assert sha(package / name) == digest, name
    assert len(native['traces']) == len(request['traces']) == 12
    masks = read(ASSETS / 'main_composition_v2_policy.json')['policies']
    def captured_traces():
        if 'traceFiles' in native:
            assert len(native['traceFiles']) == 12
            for entry, metadata in zip(native['traceFiles'], native['traces'], strict=True):
                assert Path(entry['file']).name == entry['file'] and entry['file'].startswith(f'whole-main-{tag}-native-')
                path = EVIDENCE / entry['file']
                assert sha(path) == entry['sha256']
                trace = read(path)
                assert {k:v for k,v in trace.items() if k!='frames'} == metadata
                yield trace
        else:
            yield from native['traces']
    summary = []
    baselines = {}
    trace_digests = []
    for actual, authored in zip(captured_traces(), request['traces'], strict=True):
        layout, profile, hz = actual['layout'], actual['profile'], actual['hz']
        assert (layout, profile, hz) == (authored['layout'], authored['profile'], authored['hz'])
        assert hz == launch['hz'] and authored['case'] == 'multi-layer' and authored['alsReference']
        count = COUNTS[layout]
        assert actual['instanceCount'] == count and actual['multipleOriginalGraphInstances']
        assert len(actual['frames']) == len(authored['frames']) > 0
        if full:
            assert len(actual['frames']) == 12 * hz, (tag, layout, profile)
        calls = actual['calls']
        assert len(calls) == len({c['node'] for c in calls}) == len({c['function'] for c in calls}) == 14
        assert {c['owner'] for c in calls} == set(range(count))
        for call in calls:
            assert call['group'] == (authored['functionGroups'][call['function']] or 'None')
        for left in calls:
            for right in calls:
                shared = left['node'] == right['node'] or left['group'] != 'None' and left['group'] == right['group']
                assert (left['owner'] == right['owner']) == shared
        relinks = fields_split = clocks_split = output_differences = 0
        max_position_difference = 0.0
        hooks = set()
        active_hooks = set()
        functions = {call['function'] for call in calls}
        coverage = dict(idle=False, moving=False, crouching=False, jumping=False, falling=False)
        inventory = None
        baseline = baselines.get(profile)
        for index, (frame, source) in enumerate(zip(actual['frames'], authored['frames'], strict=True)):
            finite(frame)
            assert frame['calls'] == calls
            assert len(frame['output']['pose']) == 81
            assert frame['upperWeights'] == masks[profile]['mask']
            assert frame['updates'] and frame['layerOutputs']
            hooks.update(item['hook'] for item in frame['updates'] if item['hook'] in functions)
            active_hooks.update(item['hook'] for item in frame['updates']
                                if item['hook'] in functions and item['active'] and item['weight'] > 0)
            for layer in frame['layerOutputs']:
                assert layer['hook'] in {c['function'] for c in calls} | DIAGNOSTICS
                assert len(layer['output']['pose']) == 81
            for stage in ('Before', 'Updated', 'After'):
                rows = frame['instances' + stage]
                assert len(rows) == count and [row['owner'] for row in rows] == list(range(count))
                assert all(row['class'] == authored['class'] for row in rows)
                current = [[p['node'] for p in row['players']] for row in rows]
                assert all(nodes == current[0] for nodes in current)
                assert len(current[0]) == len(set(current[0]))
                if inventory is None:
                    inventory = current
                assert current == inventory
            rows = frame['instancesAfter']
            fields_split += any(row['fields'] != rows[0]['fields'] for row in rows[1:])
            clocks_split += any(row['players'] != rows[0]['players'] for row in rows[1:])
            if source.get('relink'):
                relinks += 1
                assert frame['instancesRelinked'] == rows, (tag, layout, profile, index)
            else:
                assert 'instancesRelinked' not in frame
            main = frame['mainUpdated']
            coverage['idle'] |= main['IsOnGround'] and not main['HasVelocity']
            coverage['moving'] |= main['IsOnGround'] and main['HasVelocity']
            for flag, field in (('crouching', 'IsCrouching'), ('jumping', 'IsJumping'), ('falling', 'IsFalling')):
                coverage[flag] |= main[field]
            if layout != 'single':
                original = baseline[index]
                output_differences += original != frame['output']
                for p, q in zip(original['pose'], frame['output']['pose'], strict=True):
                    max_position_difference = max(max_position_difference, *(abs(x-y) for x,y in zip(p['position'], q['position'], strict=True)))
        if layout == 'single':
            baselines[profile] = [frame['output'] for frame in actual['frames']]
        else:
            assert fields_split > 0 and clocks_split > 0
        assert relinks > 0
        if full:
            assert all(coverage.values()), (tag, layout, profile, coverage)
            assert hooks == active_hooks == functions, (tag, layout, profile, hooks, active_hooks)
        summary.append(dict(profile=profile, layout=layout, hz=hz, frames=len(actual['frames']),
                            instances=count, relinks=relinks, framesWithDistinctInstanceFields=fields_split,
                            framesWithDistinctInstancePlayers=clocks_split, visitedHooks=sorted(hooks),
                            activeHooks=sorted(active_hooks),
                            coverage=coverage, framesDifferentFromSingleOutput=output_differences,
                            maxPositionDifferenceFromSingleCm=max_position_difference))
        if 'traceFiles' in native:
            trace_digests.append(native['traceFiles'][len(summary)-1]['sha256'])
        else:
            serialized = (json.dumps(actual, separators=(',', ':'), allow_nan=False)+'\n').encode('utf-8')
            trace_digests.append(hashlib.sha256(serialized).hexdigest())
            del serialized
    result = dict(tag=tag, files={k:sha(p) for k,p in paths.items()}, traces=summary,
                  nativeTraceSha256=trace_digests,
                  protectedJson=len(closure['previousFixtureSha256']), protectedPackages=len(packages),
                  protectedConfiguration=len(closure['protectedProject']),
                  originalUeWarningCount=sum('Warning:' in line for line in log.splitlines()))
    del native, request, baselines
    gc.collect()
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--tags', nargs='+', required=True)
    parser.add_argument('--repeat-tag')
    parser.add_argument('--output', required=True)
    parser.add_argument('--smoke', action='store_true')
    args = parser.parse_args()
    output = EVIDENCE / f'{args.output}-integrity.json'
    assert args.output.replace('-', '').replace('_', '').isalnum() and not output.exists()
    results = [verify(tag, not args.smoke) for tag in args.tags]
    if args.repeat_tag:
        repeat = verify(args.repeat_tag, not args.smoke)
        original = next(r for r in results if r['traces'][0]['hz'] == repeat['traces'][0]['hz'])
        assert original['files']['request'] == repeat['files']['request']
        assert original['nativeTraceSha256'] == repeat['nativeTraceSha256']
        a = read(EVIDENCE / f"whole-main-{original['tag']}-closure.json")
        b = read(EVIDENCE / f"whole-main-{repeat['tag']}-closure.json")
        for key in ('previousFixtureSha256', 'protectedProject', 'movementSourceSha256', 'probeSourceSha256', 'scope'):
            assert a[key] == b[key], key
        for name in a['assetSha256'].keys() & b['assetSha256'].keys():
            assert a['assetSha256'][name] == b['assetSha256'][name], name
        results.append(repeat)
    report = dict(auditPassed=True, captures=results, independentlyRepeated=bool(args.repeat_tag),
                  fullTwelveSecondTrajectories=not args.smoke,
                  totalFrames=sum(sum(t['frames'] for t in r['traces']) for r in results),
                  verifierSha256=sha(Path(__file__)),
                  godotMultipleOwnerPoseParity=False, newBlueprintCompiled=False,
                  sharedPersistentSubsystem=False, fullPhysicalParity=False, goalComplete=False)
    with output.open('x', encoding='utf-8', newline='\n') as stream:
        json.dump(report, stream, ensure_ascii=False, indent=2, allow_nan=False)
        stream.write('\n')
    print(output)


if __name__ == '__main__':
    main()
