"""Freeze implementation and audit original startup plus final runtime evidence."""
from pathlib import Path
from locomotion_paths import engine_path, project_path

import argparse
import hashlib
import json
import re

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts/lyra-analysis'
read = lambda p: json.loads(p.read_bytes())
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()

FILES = [f'src/Als.Godot/Animation/Lyra/{name}.cs' for name in (
    'LyraMainSelfGraphPhases', 'LyraProviderGraphPhases', 'LyraPoseCacheLifecycle',
    'LyraMainObservationHost', 'LyraMainUpdateHost', 'LyraLocomotionMachineHost',
    'LyraMainPoseHost', 'LyraGraphPhasesSmoke', 'LyraMainPoseHostSmoke',
    'LyraCharacterUnlinkSmoke', 'LyraMainRigPoseHostSmoke',
    'LyraMainInertiaPoseHostSmoke', 'LyraMainSlotPoseHostSmoke', 'LyraStartupSmoke')]
FILES += ['scenes/tests/lyra_startup_smoke.tscn', 'scripts/verify-lyra-startup.ps1',
          'tools/verify_lyra_startup.py']
DOCS = ['ROADMAP.md', 'docs/verification/2026-10-03-lyra-als-interface-review.md']

def save(path, value):
    with path.open('x', encoding='utf-8', newline='\n') as f:
        f.write(json.dumps(value, indent=2, allow_nan=False) + '\n')

def check_hashes(base, hashes):
    for name, expected in hashes.items():
        assert sha(base / name) == expected.lower(), name

def native_audit():
    for kind in ('requests', 'native', 'closure'):
        assert (OUT/f'startup-phase-v1-{kind}.json').read_bytes() == (OUT/f'startup-phase-v2-{kind}.json').read_bytes(), kind
    closure = read(OUT/'startup-phase-v2-closure.json')
    assert sha(OUT/'startup-phase-v2-native.json') == closure['nativeSha256']
    assert sha(OUT/'startup-phase-v2-requests.json') == closure['requestSha256']
    scope = closure['scope']
    assert scope['directCounterWrites'] == scope['globalFrameWrites'] == scope['assetsSaved'] == 0
    assert scope['originalBlueprint'] and scope['deferredComponentStartup'] and scope['originalManny164']
    check_hashes(ROOT/'assets/generated/lyra_als', closure['previousFixtureSha256'])
    check_hashes(project_path(), closure['protectedProject'])
    check_hashes(ROOT/'tools/unreal/LyraStartupOracle', closure['probeSourceSha256'])
    check_hashes(engine_path('Engine'), closure['engineSourceSha256'])
    for name, h in closure['assetSha256'].items():
        package = name.split('.')[0]
        if package.startswith('/Game/'):
            file = project_path('Content')/(package[6:]+'.uasset')
        else:
            assert package.startswith('/ShooterCore/')
            file = project_path('Plugins/GameFeatures/ShooterCore/Content')/(package[13:]+'.uasset')
        assert sha(file) == h, name
    native = read(OUT/'startup-phase-v2-native.json')
    assert len(native['cases']) == 36
    entries = hidden = 0
    for case in native['cases']:
        assert case['counterWrites'] == case['globalFrameWrites'] == 0 and case['bones'] == 164
        rows = {r['stage']: r for r in case['rows']}
        assert all(p['counter'] == -1 for p in rows['registered']['main'].values())
        first = rows['first-update']
        assert first['main']['initialization'] == first['main']['bones'] == first['main']['update'] == dict(counter=0, frame=0)
        assert first['main']['evaluation'] == dict(counter=-1, frame=-1)
        assert not rows['repeated-bones']['events'] and not rows['repeated-invalidated-bones']['events']
        prefix = []
        for event in first['events']:
            if event['phase'] == 'update':
                break
            if event['boundary'] == 'enter':
                prefix.append(event)
        entries += len(prefix)
        if case['mode'] == 'before-root':
            assert all(p['phases']['initialization']['counter'] == 0 for p in first['providers'])
            hidden += sum(p['phases']['bones']['counter'] == -1 for p in first['providers'])
            # Every function's deferred bone-cache entry inherits Main's -1.
            roots = [e for e in prefix if e['phase'] == 'bones' and e['phases']['bones']['counter'] == -1]
            assert [e['hook'] for e in roots] == [c['function'] for c in first['calls']]
        elif case['mode'] == 'after-root':
            assert all(p['phases']['initialization']['counter'] == p['phases']['bones']['counter'] == 0 for p in rows['linked']['providers'])
    assert entries == 528 and hidden == 27
    for tag in ('startup-phase-v1', 'startup-phase-v2'):
        log = (OUT/f'{tag}-native.log').read_text(encoding='utf-8-sig')
        assert 'LYRA_STARTUP_NATIVE_OK cases=36' in log and 'LYRA_STARTUP_PROCESS_EXIT=0' in log
        assert not re.search(r'Error:|Fatal error:|Ensure condition failed', log)
    return dict(cases=36, firstUpdatePhaseEntries=entries, hiddenUncachedProviders=hidden,
                assets=len(closure['assetSha256']), json=len(closure['previousFixtureSha256']),
                configs=len(closure['protectedProject']), nativeCounterWrites=0)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--tag', default='startup-runtime-v3')
    parser.add_argument('--freeze', action='store_true')
    args = parser.parse_args()
    assert re.fullmatch(r'[A-Za-z0-9_-]+', args.tag)
    freeze = OUT/f'{args.tag}-frozen.json'
    if args.freeze:
        old = read(OUT/'startup-phase-v1-before.json')
        baseline = {p:h for p,h in old.items() if p not in FILES and p not in DOCS}
        check_hashes(ROOT, baseline)
        save(freeze, dict(source={p:sha(ROOT/p) for p in FILES}, baseline=baseline))
        for name in DOCS:
            path = OUT/f'{args.tag}-{Path(name).name}.before'
            with path.open('xb') as f:
                f.write((ROOT/name).read_bytes())
        print(f'LYRA_STARTUP_FROZEN sources={len(FILES)} baseline={len(baseline)}')
        return
    frozen = read(freeze)
    check_hashes(ROOT, frozen['source'])
    check_hashes(ROOT, frozen['baseline'])
    result = native_audit()
    processes = 0
    for configuration in ('debug', 'optimize'):
        for suffix, expected in (('',31),('-whole-main',2),('-other-groups',2),('-joint',4)):
            report = read(OUT/f'{args.tag}{suffix}-{configuration}-verification.json')
            assert report['passed'] and len(report['runs']) == expected
            assert configuration != 'optimize' or report['debugRestored']
            for run in report['runs']:
                assert run['passed'] and run['exitCode'] == 0
                assert sha(Path(run['log'])) == run['logSha256'].lower()
                log = Path(run['log']).read_text(encoding='utf-8-sig')
                assert not re.search(r'^\s*(ERROR|WARNING):',log,re.M)
                processes += 1
            if not suffix:
                for name,h in report['assemblies'].items():
                    build = 'Debug' if configuration == 'debug' else 'ExportRelease'
                    assert sha(ROOT/f'.godot/mono/temp/bin/{build}'/name) == h.lower(), name
    assert processes == 78
    result.update(passed=True, processes=processes, frozenSources=len(frozen['source']),
                  protectedBaseline=len(frozen['baseline']), goalComplete=False,
                  updateRootBoneCallbacksCompared=False, requiredBonesLodComplete=False)
    save(OUT/f'{args.tag}-integrity.json',result)
    print(json.dumps(result))

if __name__ == '__main__':
    main()
