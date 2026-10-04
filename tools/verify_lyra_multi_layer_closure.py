"""Combine audited full trajectories and check an independent process repeat."""
import argparse
import json
from pathlib import Path
from verify_lyra_multi_layer_native import ASSETS, EVIDENCE, PROJECT, ROOT, COUNTS, package_file, read, sha


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--baseline', required=True)
    parser.add_argument('--matrix', nargs='+', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    assert all(value.replace('-', '').replace('_', '').isalnum() for value in (args.baseline, *args.matrix, args.output))
    output = EVIDENCE / f'{args.output}-integrity.json'
    assert not output.exists()
    reports = [EVIDENCE / f'{name}-integrity.json' for name in (args.baseline, *args.matrix)]
    captures = []
    for path in reports:
        report = read(path)
        assert report['auditPassed'] and report['fullTwelveSecondTrajectories']
        assert report['verifierSha256'] == sha(ROOT / 'tools/verify_lyra_multi_layer_native.py')
        assert not any(report[key] for key in ('godotMultipleOwnerPoseParity', 'newBlueprintCompiled',
                                              'sharedPersistentSubsystem', 'fullPhysicalParity', 'goalComplete'))
        captures.extend(report['captures'])
    assert len(captures) == 4
    assert sorted(c['traces'][0]['hz'] for c in captures) == [30, 60, 60, 120]
    closures = []
    for capture in captures:
        tag = capture['tag']
        for kind, digest in capture['files'].items():
            assert sha(EVIDENCE / f'whole-main-{tag}-{kind}.json') == digest
        native = read(EVIDENCE / f'whole-main-{tag}-native.json') if tag != captures[0]['tag'] else None
        # The monolithic baseline is already covered by its complete byte hash;
        # the primary verifier calculated each canonical trajectory digest.
        if native is not None:
            assert [e['sha256'] for e in native['traceFiles']] == capture['nativeTraceSha256']
            for entry in native['traceFiles']:
                assert Path(entry['file']).name == entry['file']
                assert sha(EVIDENCE / entry['file']) == entry['sha256']
        closure = read(EVIDENCE / f'whole-main-{tag}-closure.json')
        closures.append(closure)
        for name, digest in closure['previousFixtureSha256'].items():
            assert sha(ASSETS / name) == digest, name
        for name, digest in closure['protectedProject'].items():
            assert sha(PROJECT / name) == digest, name
        for name, digest in closure['assetSha256'].items():
            assert sha(package_file(name)) == digest, name
        for name, digest in closure.get('captureSourceSha256', {}).items():
            assert sha(ROOT / name) == digest, name
        assert len(capture['traces']) == 12 and len(capture['nativeTraceSha256']) == 12
        assert {(t['profile'],t['layout']) for t in capture['traces']} == {
            (profile,layout) for profile in ('unarmed','pistol','rifle') for layout in COUNTS}
        for trace in capture['traces']:
            assert trace['frames'] == trace['hz'] * 12
            assert len(trace['visitedHooks']) == len(trace['activeHooks']) == 14
            assert trace['visitedHooks'] == trace['activeHooks'] and all(trace['coverage'].values())
            assert trace['instances'] == COUNTS[trace['layout']]
            if trace['layout'] != 'single':
                assert trace['framesWithDistinctInstanceFields'] == trace['frames']
                assert trace['framesWithDistinctInstancePlayers'] == trace['frames']
    baseline = captures[0]
    assert baseline['traces'][0]['hz'] == 60
    repeat = next(c for c in captures[1:] if c['traces'][0]['hz'] == 60)
    assert baseline['files']['request'] == repeat['files']['request']
    assert baseline['nativeTraceSha256'] == repeat['nativeTraceSha256']
    for closure in closures[1:]:
        for key in ('previousFixtureSha256','protectedProject','movementSourceSha256','probeSourceSha256','scope'):
            assert closure[key] == closures[0][key], key
    result = dict(auditPassed=True, independentlyRepeated=True,
                  primaryVerifierSha256=sha(ROOT / 'tools/verify_lyra_multi_layer_native.py'),
                  closureVerifierSha256=sha(Path(__file__)),
                  reportSha256={p.name:sha(p) for p in reports},
                  captures=captures, totalTrajectories=sum(len(c['traces']) for c in captures),
                  totalFrames=sum(t['frames'] for c in captures for t in c['traces']),
                  totalSameClassRelinks=sum(t['relinks'] for c in captures for t in c['traces']),
                  godotMultipleOwnerPoseParity=False, newBlueprintCompiled=False,
                  fullPhysicalParity=False, sharedPersistentSubsystem=False, goalComplete=False)
    with output.open('x', encoding='utf-8', newline='\n') as stream:
        json.dump(result, stream, ensure_ascii=False, indent=2, allow_nan=False)
        stream.write('\n')
    print(output)


if __name__ == '__main__':
    main()
