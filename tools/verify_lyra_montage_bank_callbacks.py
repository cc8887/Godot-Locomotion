"""Audit captured delegates and committed callback mutation; keep pre-Advance work open."""
import collections
import json
import xml.etree.ElementTree as ET
from pathlib import Path
from verify_lyra_multi_layer_native import ROOT, EVIDENCE, ASSETS, PROJECT, read, sha, package_file


def main():
    tag = 'montage-bank-callbacks-v1-final'
    native_tag = 'montage-bank-callbacks-v1'
    target = EVIDENCE / f'{tag}-integrity.json'
    assert not target.exists(), 'Preserve audit.'
    evidence = {}

    def load(path):
        evidence[str(path.relative_to(ROOT))] = sha(path)
        return read(path)

    def log(path, native=False):
        evidence[str(path.relative_to(ROOT))] = sha(path)
        value = path.read_text(encoding='utf-8-sig')
        if native:
            assert 'LYRA_MONTAGE_BANK_NATIVE_OK traces=12 frames=5880 assets_saved=0' in value
            assert 'MONTAGE_DELEGATES_PROCESS_EXIT=0' in value
            assert not any(x in value for x in ('Error:', 'Fatal error:', 'Ensure condition failed'))
        else:
            assert not any(line.lstrip().startswith(('ERROR:', 'WARNING:')) for line in value.splitlines()), path
            assert 'MONTAGE_DELEGATES_GODOT_EXIT=0' in value or 'MULTI_OWNER_PROCESS_EXIT=0' in value
        return value

    sources = load(EVIDENCE / f'{tag}-frozen-sources.json')
    for name, digest in sources.items():
        assert sha(ROOT / name) == digest, name
    native = load(EVIDENCE / f'{native_tag}-native.json')
    repeat = load(EVIDENCE / f'{native_tag}-repeat-native.json')
    request = load(EVIDENCE / f'{native_tag}-requests.json')
    assert native == repeat and sha(EVIDENCE / f'{native_tag}-native.json') == sha(EVIDENCE / f'{native_tag}-repeat-native.json')
    assert request == load(EVIDENCE / f'{native_tag}-repeat-requests.json')
    assert native['requestSha256'] == sha(EVIDENCE / f'{native_tag}-requests.json')
    assert native['catalogSha256'] == sha(ASSETS / 'montage_catalog_v2.json')
    for suffix in ('', '-repeat'):
        log(EVIDENCE / f'{native_tag}{suffix}-native.log', True)
    assert len(native['previousFixtureSha256']) == 869
    assert len(native['assetSha256']) == 710 and len(native['protectedProject']) == 9
    for name, digest in native['previousFixtureSha256'].items():
        assert sha(ASSETS / name) == digest, name
    for name, digest in native['assetSha256'].items():
        assert sha(package_file(name)) == digest, name
    for name, digest in native['protectedProject'].items():
        assert sha(PROJECT / name) == digest, name
    package = ROOT / 'artifacts/unreal/lyra-whole-main-oracle/package-montage-bank-callbacks-v1-final-v2'
    for name, digest in native['probeSourceSha256'].items():
        assert sha(ROOT / 'tools/unreal/LyraMontageBankOracle' / name) == digest
        assert sha(package / name) == digest
    old_native = load(EVIDENCE / 'montage-delegates-v1-native.json')
    for name, digest in old_native['probeSourceSha256'].items():
        assert sha(ROOT / 'tools/unreal/LyraMontageDelegateOracle' / name) == digest
    copied = 'Source/LyraWholeMainOracle/Private/LyraWholeMainOracleLibrary.cpp'
    assert sha(package / copied) == old_native['probeSourceSha256'][copied]
    assert 'Result: Succeeded' in (EVIDENCE / 'whole-main-build-package-montage-bank-callbacks-v1-final-v2.log').read_text(encoding='utf-8-sig')

    events = collections.Counter()
    callbacks = frames = 0
    for q, t in zip(request['traces'], native['trace']['traces'], strict=True):
        assert q['name'] == t['name'] and len(q['frames']) == len(t['frames']) == q['hz'] * 7
        seen = []
        for row in t['frames']:
            assert not row['queuing']
            for key in ('immediate', 'calls'):
                for call in row[key]:
                    assert not call['queuing']
                    assert call['listener'] != 'rebound', 'Already queued event changed its captured delegate.'
                    if call['listener'] == 'instance':
                        events[call['kind']] += 1
                    seen.append(call)
                    callbacks += 1
            frames += 1
        assert any(c['listener'] == 'instance' and c['kind'] == 0 for c in seen)
        if q['mode'] in ('ended-play', 'out-play'):
            child = 2 if q['mode'] == 'ended-play' else 3
            assert any(c['listener'] == 'return' and any(s['instance'] == child and s['position'] == s['weight'] == 0 for s in c['live']) for c in seen)
        if q['mode'] in ('out-stop', 'out-play'):
            dispatch = [c for c in seen if c['stage'] == 'dispatch']
            first = next(i for i, c in enumerate(dispatch) if c['kind'] == 0 and c['instance'] == 1)
            assert dispatch[first + 1]['kind'] == 0 and dispatch[first + 1]['instance'] == 2
            assert dispatch[first + 2]['listener'] == 'global'
            assert dispatch[first + 3]['listener'] == 'return'
    assert frames == 5880 and callbacks == 171 and sum(events.values()) == 81

    run_counts = {}
    for config in ('debug', 'optimize'):
        summary = load(EVIDENCE / f'{tag}-{config}-verification.json')
        assert summary['passed'] and len(summary['runs']) == 11
        build = EVIDENCE / f'{tag}-build-{config}-final-v2.log'
        evidence[str(build.relative_to(ROOT))] = sha(build)
        assert '0 个错误' in build.read_text(encoding='utf-8-sig') and '0 个警告' in build.read_text(encoding='utf-8-sig')
        for run in summary['runs']:
            assert run['passed'] and run['exitCode'] == 0
            path = Path(run['log'])
            assert sha(path) == run['logSha256'].lower()
            text = log(path)
            if run['name'] == 'native-bank-callbacks':
                assert 'traces=12 frames=5880 retries=5880 callbacks=171 checks=33021 immediateMutation=false' in text
            if 'report' in run:
                report = load(Path(run['report']))
                assert sha(Path(run['report'])) == run['reportSha256'].lower()
                assert report == load(EVIDENCE / f'{tag}-{("optimize" if config == "debug" else "debug")}-{run["name"]}.json')
                if run['name'].startswith('physics'):
                    assert report['moves'] == report['animationRetries'] == report['preRetries'] == report['hz'] * 8 * 6
                else:
                    baseline = 'per-call-ten' if run['name'] == 'ordinary-ten' else 'ordinary-emote'
                    assert report == load(EVIDENCE / f'linked-montage-events-v1-components-{config}-{baseline}.json')
        whole = load(EVIDENCE / f'{tag}-whole-main-{config}-verification.json')
        assert whole['passed'] and len(whole['runs']) == 1
        assert whole['workerFields'] and whole['preUpdateFields'] and whole['movementFields'] and whole['graphFields'] and whole['leftSettings'] and whole['montageEventFields']
        for name, digest in whole['assemblies'].items():
            assert digest == summary['assemblies'][name]
        for run in whole['runs']:
            assert run['passed'] and run['boundary'] == 'final' and run['layout'] == 'per-call' and run['frames'] == 1080
            assert sha(Path(run['log'])) == run['logSha256'].lower()
            assert 'retry=1080 controlledPhysicalInputs=true' in log(Path(run['log']))
        run_counts[config] = len(summary['runs']) + len(whole['runs'])
    for suffix in ('', '-whole-main'):
        summary = read(EVIDENCE / f'{tag}{suffix}-optimize-verification.json')
        assert summary['debugRestored']
        for name in summary['assemblies']:
            assert sha(EVIDENCE / f'{tag}{suffix}-optimize-debug-backup' / name) == sha(ROOT / '.godot/mono/temp/bin/Debug' / name)
    for config, directory in (('debug', 'Debug'), ('optimize', 'ExportRelease')):
        for name, digest in read(EVIDENCE / f'{tag}-{config}-verification.json')['assemblies'].items():
            assert sha(ROOT / '.godot/mono/temp/bin' / directory / name) == digest.lower()
    trx = EVIDENCE / f'{tag}-core-final-v2.trx'
    evidence[str(trx.relative_to(ROOT))] = sha(trx)
    counters = ET.parse(trx).find('.//{*}Counters')
    assert counters is not None and counters.attrib['passed'] == counters.attrib['total'] == '540' and counters.attrib['failed'] == '0'
    runtime = (ROOT / 'src/Als.Godot/Animation/Lyra/LyraCharacterAnimation.cs').read_text()
    assert 'DeliverImmediateMontageEvents(Emote.HandleMontageEvent)' not in runtime
    assert 'DispatchMontageEventCallbacks(Emote.HandleMontageEvent)' not in runtime
    assert runtime.count('Emote.BindMontageCallbacks') == 3
    result = dict(auditPassed=True, nativeTraces=12, nativeFrames=5880, callbacks=callbacks, events=dict(events),
        retryFramesPerConfiguration=5880, godotRuns=run_counts, corePassed=540, frozenSources=len(sources),
        protectedJson=869, protectedPackages=710, protectedProject=9, capturedInstanceDelegates=True,
        committedCallbackBankMutation=True, immediateCallbackBankMutation=False,
        fullMontageEventDispatch=False, resourceNotifyTermination=False, goalComplete=False, evidenceSha256=evidence)
    with target.open('x', encoding='utf-8', newline='\n') as stream:
        json.dump(result, stream, indent=2)
    print(json.dumps({k: v for k, v in result.items() if k != 'evidenceSha256'}))


if __name__ == '__main__':
    main()
