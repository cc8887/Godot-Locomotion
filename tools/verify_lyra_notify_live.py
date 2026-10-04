"""Audit live callbacks, native inventory snapshots, final runs and protected resources."""
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path
from verify_lyra_multi_layer_native import ROOT, EVIDENCE, ASSETS, PROJECT, sha, read, package_file


def main():
    tag = 'notify-live-v2'
    capture = 'notify-live-v1'
    destination = EVIDENCE / f'{tag}-integrity.json'
    assert not destination.exists(), 'Preserve audit.'
    evidence = {}

    def load(path):
        evidence[str(path.relative_to(ROOT))] = sha(path)
        return read(path)

    def log_text(path):
        data = path.read_bytes()
        return data.decode('utf-16' if data.startswith((b'\xff\xfe', b'\xfe\xff')) else 'utf-8-sig')

    def clean(path, native=False):
        evidence[str(path.relative_to(ROOT))] = sha(path)
        text = log_text(path)
        if native:
            assert 'LYRA_NOTIFY_LIVE_NATIVE_OK cases=13 assets_saved=0' in text
            assert 'MONTAGE_DELEGATES_PROCESS_EXIT=0' in text
            assert not any(t in text for t in ('Error:', 'Fatal error:', 'Ensure condition failed'))
        else:
            assert not any(l.lstrip().startswith(('ERROR:', 'WARNING:')) for l in text.splitlines()), path
            assert 'MONTAGE_DELEGATES_GODOT_EXIT=0' in text or 'MULTI_OWNER_PROCESS_EXIT=0' in text
        return text

    frozen = load(EVIDENCE / f'{tag}-frozen-sources-final.json')
    for name, digest in frozen.items():
        assert sha(ROOT / name) == digest, name
    native = load(EVIDENCE / f'{capture}-native.json')
    repeat = load(EVIDENCE / f'{capture}-repeat-native.json')
    assert native == repeat
    assert sha(EVIDENCE / f'{capture}-native.json') == sha(EVIDENCE / f'{capture}-repeat-native.json')
    request = load(EVIDENCE / f'{capture}-requests.json')
    assert request == load(EVIDENCE / f'{capture}-repeat-requests.json')
    assert sha(EVIDENCE / f'{capture}-requests.json') == sha(EVIDENCE / f'{capture}-repeat-requests.json') == native['requestSha256']
    for suffix in ('', '-repeat'):
        clean(EVIDENCE / f'{capture}{suffix}-native.log', True)
    assert len(native['previousFixtureSha256']) == 869
    assert len(native['assetSha256']) == 710 and len(native['protectedProject']) == 9
    for name, digest in native['previousFixtureSha256'].items():
        assert sha(ASSETS / name) == digest, name
    for name, digest in native['assetSha256'].items():
        assert sha(package_file(name)) == digest, name
    for name, digest in native['protectedProject'].items():
        assert sha(PROJECT / name) == digest, name
    package = ROOT / 'artifacts/unreal/lyra-whole-main-oracle/package-notify-live-v3'
    for name, digest in native['probeSourceSha256'].items():
        assert sha(ROOT / 'tools/unreal/LyraNotifyLiveOracle' / name) == sha(package / name) == digest, name
    build = EVIDENCE / 'whole-main-build-package-notify-live-v3.log'
    evidence[str(build.relative_to(ROOT))] = sha(build)
    assert 'Result: Succeeded' in log_text(build)
    assert native['scope'] == dict(originalTriggerAnimNotifies=True, callbackTimeInventory=True,
        controlledStates=True, originalBlueprintStateBodies=False, worldTeardownAccepted=False, assetsSaved=0, goalComplete=False)
    expected = {
        'ordinary': ([4, 2, 1, 3, 9, 3], [9, 3]), 'begin-clear': ([0, 1, 1], [1]),
        'tick-clear': ([0, 1, 0], []), 'tick-append': ([0, 0, 2], [0, 2]),
        'end-append': ([0, 2, 1, 1], [1]), 'instant-clear': ([5, 4, 9, 9], [9]),
        'nested-tick': ([0, 0, 0, 1, 1], [1]), 'nested-begin': ([0, 1, 0, 2, 2, 1], [1]),
        'filtered': ([1, 1], [0, 1]), 'skip-graph': ([1, 0], [0]),
        'forced-montage': ([0, 8], [8]), 'concurrent': ([2, 9, 8, 9, 8], [9, 8]), 'end-all': ([0, 1], [])}
    calls = 0
    for q, n in zip(request['cases'], native['trace']['cases'], strict=True):
        handles, after = expected[q['action']]
        assert [c['state']['handle'] for c in n['calls']] == handles
        assert [s['handle'] for s in n['after']] == after
        if q['action'] == 'ordinary':
            assert [[s['handle'] for s in c['active']] for c in n['calls']] == [[2, 1]] * 4 + [[9, 3]] * 2
        if q['action'] == 'begin-clear':
            assert [[s['handle'] for s in c['active']] for c in n['calls']] == [[0], [0], [1]]
        if q['action'] == 'instant-clear':
            assert n['calls'][0]['state']['instance'] == -1
            assert n['calls'][2]['state']['instance'] != q['before'][0]['instance']
        calls += len(n['calls'])
    assert len(expected) == 13 and calls == 47
    xml = ET.parse(EVIDENCE / f'{tag}-core-final.trx')
    counter = xml.find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters')
    assert counter is not None and int(counter.attrib['passed']) == 607
    assert counter.attrib['failed'] == '0' and counter.attrib['notExecuted'] == '0'
    evidence[f'artifacts/lyra-analysis/{tag}-core-final.trx'] = sha(EVIDENCE / f'{tag}-core-final.trx')
    summaries = {}
    for configuration in ('debug', 'optimize'):
        summary = load(EVIDENCE / f'{tag}-{configuration}-verification.json')
        assert summary['passed'] and len(summary['runs']) == 22 and not summary['goalComplete']
        assert summary['liveNotifyInventory'] and not summary['originalBlueprintStateBodies']
        assert not summary['worldTeardownAccepted']
        for run in summary['runs']:
            assert run['passed'] and run['exitCode'] == 0
            path = Path(run['log'])
            assert sha(path).upper() == run['logSha256']
            text = clean(path)
            if run['name'].startswith('live-'):
                hz = int(run['name'].split('-')[1])
                assert f'cases=13 callbacks=47 hz={hz} roles=2 frames={hz*6} retries={hz*6} begin=1,2' in text
            if 'report' in run:
                assert sha(Path(run['report'])).upper() == run['reportSha256']
                evidence[str(Path(run['report']).relative_to(ROOT))] = sha(Path(run['report']))
        full = load(EVIDENCE / f'{tag}-whole-main-{configuration}-verification.json')
        assert full['passed'] and len(full['runs']) == 1 and not full['fullPrivateFieldParity']
        assert sum(full[k] for k in ('workerFieldCount', 'preUpdateFieldCount', 'movementFieldCount',
            'graphFieldCount', 'leftSettingFieldCount', 'montageEventFieldCount')) == 34
        for run in full['runs']:
            assert run['passed'] and run['exitCode'] == 0 and run['frames'] == 1080
            assert sha(Path(run['log'])).upper() == run['logSha256']
            clean(Path(run['log']))
        assert full['assemblies'] == summary['assemblies']
        if configuration == 'optimize':
            assert summary['debugRestored'] and full['debugRestored']
        log = EVIDENCE / f'{tag}-build-{configuration}-final.log'
        evidence[str(log.relative_to(ROOT))] = sha(log)
        text = log_text(log)
        assert re.search(r'(?m)^\s*0\s*(个警告|Warning)', text) and re.search(r'(?m)^\s*0\s*(个错误|Error)', text)
        summaries[configuration] = summary
    for name, digest in summaries['debug']['assemblies'].items():
        assert sha(ROOT / '.godot/mono/temp/bin/Debug' / name).upper() == digest
        for part in ('', '-whole-main'):
            assert sha(EVIDENCE / f'{tag}{part}-optimize-debug-backup' / name).upper() == digest
    for name, digest in summaries['optimize']['assemblies'].items():
        assert sha(ROOT / '.godot/mono/temp/bin/ExportRelease' / name).upper() == digest
    debug_runs = {r['name']: r for r in summaries['debug']['runs']}
    for run in summaries['optimize']['runs']:
        if 'report' in run:
            assert read(Path(run['report'])) == read(Path(debug_runs[run['name']]['report']))
            # No callback mutation in these regression scenes: retain their
            # already accepted pose/movement/gameplay summaries exactly.
            assert read(Path(run['report'])) == read(EVIDENCE / f"notify-termination-v1-debug-{run['name']}.json")
    result = dict(auditPassed=True, nativeCases=13, nativeCallbacks=47, corePassed=607,
        godotRuns=dict(debug=23, optimize=23), physicalRoleFramesPerConfiguration=1260,
        physicalRoleRetriesPerConfiguration=1260, frozenSources=len(frozen), protectedJson=869,
        protectedPackages=710, protectedProject=9, liveNotifyInventory=True,
        originalBlueprintStateBodies=False, worldTeardownAccepted=False, fullPrivateFieldParity=False,
        goalComplete=False, evidenceSha256=evidence)
    destination.write_text(json.dumps(result, indent=2)+'\n', encoding='utf-8')
    print('LYRA_NOTIFY_LIVE_AUDIT_OK', json.dumps({k:v for k,v in result.items() if k!='evidenceSha256'}))


if __name__ == '__main__':
    main()
