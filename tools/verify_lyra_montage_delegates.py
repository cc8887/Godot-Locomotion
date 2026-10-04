"""Independent evidence audit; retains open generic bank callbacks and NotifyState teardown."""
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path
from verify_lyra_multi_layer_native import ROOT, EVIDENCE, ASSETS, PROJECT, read, sha, package_file


def main():
    tag = 'montage-delegates-v1'
    target = EVIDENCE/f'{tag}-integrity.json'
    assert not target.exists(), 'Preserve audit.'
    evidence = {}

    def load(path):
        evidence[str(path.relative_to(ROOT))] = sha(path)
        return read(path)

    def log(path, godot=True):
        evidence[str(path.relative_to(ROOT))] = sha(path)
        value = path.read_text(encoding='utf-8-sig')
        if godot:
            assert not any(line.lstrip().startswith(('ERROR:', 'WARNING:')) for line in value.splitlines()), path
            assert 'MONTAGE_DELEGATES_GODOT_EXIT=0' in value or 'MULTI_OWNER_PROCESS_EXIT=0' in value, path
        return value

    sources = load(EVIDENCE/f'{tag}-frozen-sources-final-v2.json')
    for name, digest in sources.items(): assert sha(ROOT/name) == digest, name
    native = load(EVIDENCE/f'{tag}-native.json')
    repeat = load(EVIDENCE/f'{tag}-repeat-native.json')
    request = load(EVIDENCE/f'{tag}-requests.json')
    assert request == load(EVIDENCE/f'{tag}-repeat-requests.json')
    assert native['trace'] == repeat['trace']
    assert native['requestSha256'] == repeat['requestSha256'] == sha(EVIDENCE/f'{tag}-requests.json')
    assert native['catalogSha256'] == sha(ASSETS/'montage_catalog_v2.json')
    capture = log(EVIDENCE/f'{tag}-repeat-native.log', False)
    assert 'LYRA_MONTAGE_DELEGATES_NATIVE_OK traces=21 frames=8823 assets_saved=0' in capture
    assert 'MONTAGE_DELEGATES_PROCESS_EXIT=0' in capture
    assert not any(marker in capture for marker in ('Error:', 'Fatal error:', 'Ensure condition failed', 'LYRA_MONTAGE_DELEGATES_FAILED'))
    # The first process succeeded, but its initial launcher omitted stdout flags
    # and failed its success-marker gate. It remains evidence, not a passed gate.
    first = log(EVIDENCE/f'{tag}-native.log', False)
    assert 'Python script executed successfully' in first and 'MONTAGE_DELEGATES_PROCESS_EXIT=0' in first
    assert 'Result: Succeeded' in log(EVIDENCE/'whole-main-build-package-montage-delegates-v1-final.log', False)
    assert len(native['previousFixtureSha256']) == 869
    assert len(native['assetSha256']) == 710 and len(native['protectedProject']) == 9
    for name, digest in native['previousFixtureSha256'].items(): assert sha(ASSETS/name) == digest, name
    for name, digest in native['assetSha256'].items(): assert sha(package_file(name)) == digest, name
    for name, digest in native['protectedProject'].items(): assert sha(PROJECT/name) == digest, name
    package = ROOT/'artifacts/unreal/lyra-whole-main-oracle/package-montage-delegates-v1-final'
    for name, digest in native['probeSourceSha256'].items():
        assert sha(ROOT/'tools/unreal/LyraMontageDelegateOracle'/name) == digest
        assert sha(package/name) == digest
    original = load(EVIDENCE/'linked-montage-events-v1-before/probe-provenance.json')
    for name, digest in original['originalProbeSha256'].items(): assert sha(ROOT/'tools/unreal/LyraWholeMainOracle'/name) == digest
    for name, digest in original['newProbeSha256'].items(): assert sha(ROOT/'tools/unreal/LyraLinkedMontageOracle'/name) == digest
    cpp = 'Source/LyraWholeMainOracle/Private/LyraWholeMainOracleLibrary.cpp'
    assert sha(ROOT/'tools/unreal/LyraMontageDelegateOracle'/cpp) == original['newProbeSha256'][cpp]

    traces = native['trace']['traces']
    assert len(traces) == 21 and len(request['traces']) == 21
    stages = set()
    producers = {kind: 0 for kind in range(4)}
    for authored, actual in zip(request['traces'], traces, strict=True):
        assert authored['name'] == actual['name']
        if authored['kernel'] >= 0:
            row = actual['frames'][0]
            assert row['callsBefore'] == 0 and not row['phaseAfterSecond']
            assert row['phaseAfterFirst'] == (authored['kernel'] == 2)
            assert bool(row['second']) == (authored['kernel'] == 2)
            if authored['kernel'] == 0:
                assert [e['kind'] for e in row['first'] if e['listener'] == 'instance'] == [0, 1, 2, 3]
                assert row['first'][-1]['listener'] == 'new-global'
            if authored['kernel'] == 1:
                assert [(e['kind'], e['listener']) for e in row['first'][:4]] == [(0, 'instance'), (3, 'instance'), (3, 'global'), (0, 'return')]
            if authored['kernel'] == 2:
                assert row['second'][0]['instance'] == 2 and row['second'][0]['kind'] == 0
            continue
        assert authored['hz'] in (30, 60, 120)
        assert len(authored['frames']) == len(actual['frames']) == authored['hz']*7
        for f, row in zip(authored['frames'], actual['frames'], strict=True):
            assert row['afterWeight'] == row['before'] and row['afterAdvance']
            assert row['afterDispatch'] == (not f['dispatch'])
            for key in ('beforeDispatch', 'dispatched'):
                calls = row[key]
                assert len(calls) % 2 == 0
                for instance, global_call in zip(calls[::2], calls[1::2], strict=True):
                    assert instance['listener'] == 'instance' and global_call['listener'] == 'global'
                    assert instance['instance'] > 0 and global_call['instance'] == 0
                    assert all(instance[k] == global_call[k] for k in ('kind', 'asset', 'interrupted', 'section', 'looped', 'stage', 'queuing'))
                    assert not instance['queuing']
                    stages.add(instance['stage']); producers[instance['kind']] += 1
            kinds = [e['kind'] for e in row['dispatched'] if e['listener'] == 'instance']
            assert kinds == sorted(kinds)
    assert stages == {'requests', 'weight', 'dispatch'}
    assert producers[0] and producers[1] and producers[3] and producers[2] == 0
    # Original single Default sections produce no SectionChanged. Only the
    # controlled native container test proves that container and its payload.
    for index in request['catalogIndices']:
        section = read(ASSETS/'montage_catalog_v2.json')['assets'][index]['sections']
        assert len(section) == 1 and section[0]['name'] == 'Default' and section[0]['next'] == 'None'

    report_counts = {}
    for configuration, directory in (('debug', 'Debug'), ('optimize', 'ExportRelease')):
        build = log(EVIDENCE/f'{tag}-build-{configuration}-final.log', False)
        assert re.search(r'^\s*0\s*(个警告|Warning)', build, re.M) and re.search(r'^\s*0\s*(个错误|Error)', build, re.M)
        summary = load(EVIDENCE/f'{tag}-{configuration}-verification.json')
        assert summary['passed'] and len(summary['runs']) == 10
        assert not summary['genericCallbackBankMutation'] and not summary['resourceNotifyTermination'] and not summary['goalComplete']
        for name, digest in summary['assemblies'].items(): assert sha(ROOT/f'.godot/mono/temp/bin/{directory}'/name) == digest.lower()
        for run in summary['runs']:
            assert run['passed'] and run['exitCode'] == 0
            path = Path(run['log']); assert sha(path) == run['logSha256'].lower(); value = log(path)
            if run['name'] == 'native-events': assert 'frames=8823 retries=8820 events=95 checks=855' in value
            if run['name'] == 'original-emote': assert 'traces=54 frames=27720 retries=27720 checks=1774080 activations=57 ends=51 clears=27' in value
            if 'report' in run:
                path = Path(run['report']); assert sha(path) == run['reportSha256'].lower(); report = load(path)
                other = load(EVIDENCE/f'{tag}-{"optimize" if configuration == "debug" else "debug"}-{run["name"]}.json')
                assert report == other, run['name']
                if run['name'].startswith('physics'):
                    hz = int(run['name'].split('-')[-1]); assert report['hz'] == hz
                    assert report['moves'] == report['animationRetries'] == report['preRetries'] == hz*8*6
                else:
                    baseline_name = 'per-call-ten' if run['name'] == 'ordinary-ten' else 'ordinary-emote'
                    assert report == load(EVIDENCE/f'linked-montage-events-v1-components-{configuration}-{baseline_name}.json'), run['name']
        whole = load(EVIDENCE/f'{tag}-whole-main-{configuration}-verification.json')
        assert whole['passed'] and len(whole['runs']) == 2
        for name, digest in whole['assemblies'].items(): assert summary['assemblies'][name] == digest
        for run in whole['runs']:
            assert run['passed'] and run['boundary'] == 'final' and run['frames'] == 1080
            path = Path(run['log']); assert sha(path) == run['logSha256'].lower(); value=log(path)
            assert 'retry=1080 controlledPhysicalInputs=true' in value
        report_counts[configuration] = len(summary['runs'])+len(whole['runs'])
    for configuration in ('optimize',):
        for suffix in ('', '-whole-main'):
            summary = read(EVIDENCE/f'{tag}{suffix}-{configuration}-verification.json')
            assert summary['debugRestored']
            for name in summary['assemblies']:
                assert sha(EVIDENCE/f'{tag}{suffix}-{configuration}-debug-backup'/name) == sha(ROOT/'.godot/mono/temp/bin/Debug'/name)
    trx = EVIDENCE/f'{tag}-core-final.trx'; evidence[str(trx.relative_to(ROOT))] = sha(trx)
    counters = ET.parse(trx).find('.//{*}Counters')
    assert counters is not None and counters.attrib['total'] == counters.attrib['passed'] == '200' and counters.attrib['failed'] == '0'
    runtime = (ROOT/'src/Als.Godot/Animation/Lyra/LyraEmoteAbility.cs').read_text()
    assert all(old not in runtime for old in ('CaptureAdvance', 'ApplyAdvance', 'MovementStopped', '_blendEvent', '_interruptEvent'))
    result = dict(auditPassed=True, nativeTraces=21, nativeFrames=8823, retryFramesPerConfiguration=8820,
        productionEventCounts=producers, godotRuns=report_counts, corePassed=200, protectedJson=869, protectedPackages=710,
        protectedProject=9, frozenSources=len(sources), fullMontageEventDispatch=False, genericCallbackBankMutation=False,
        resourceNotifyTermination=False, goalComplete=False, evidenceSha256=evidence)
    with target.open('x', encoding='utf-8', newline='\n') as f: json.dump(result, f, indent=2)
    print(json.dumps({k: v for k, v in result.items() if k != 'evidenceSha256'}))


if __name__ == '__main__': main()
