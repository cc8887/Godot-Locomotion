"""Audit shared Core changes, protected assets and focused runtime evidence."""
from pathlib import Path
import argparse
import hashlib
import json
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts/lyra-analysis'
read = lambda p: json.loads(p.read_bytes())
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
DOCS = {'ROADMAP.md', 'docs/verification/2026-10-03-lyra-als-interface-review.md'}
CHANGED = {'src/Als.Core/Locomotion/AlsPoseCacheEvaluation.cs'} | {
    f'src/Als.Godot/Animation/Lyra/{n}.cs' for n in (
        'LyraCacheOwnerSmoke', 'LyraItemLayerGraphInstance', 'LyraMainLocomotionHost',
        'LyraMainPoseCacheScope', 'LyraMainPoseHost', 'LyraMainSelfGraphPhases',
        'LyraMainSlotComposition', 'LyraProviderGraphPhases')}
DELETED = {f'src/Als.Godot/Animation/Lyra/{n}.cs' for n in (
    'LyraPoseCacheLifecycle', 'LyraProxyTraversalHistory')}
NEW = [f'src/Als.Core/Animation/{n}.cs' for n in (
    'AlsAnimationProxyTraversal', 'AlsAnimationBoneCacheGate', 'AlsAnimationGraphStartup')]
NEW += [f'src/Als.Core/Locomotion/{n}.cs' for n in (
    'AlsPoseCacheLifecycle', 'AlsPoseCacheHistory')]
NEW += ['tests/Als.Core.Tests/AlsAnimationGraphLifecycleTests.cs',
        'src/Als.Godot/Animation/Lyra/LyraRootBoneSmoke.cs',
        'scenes/tests/lyra_root_bone_smoke.tscn',
        'scripts/verify-lyra-core-reuse.ps1', 'tools/verify_lyra_core_reuse.py',
        'scripts/build-lyra-root-bone-oracle.ps1', 'scripts/capture-lyra-root-bones.ps1',
        'tools/unreal/capture_lyra_root_bones.py']
NEW += [p.relative_to(ROOT).as_posix() for p in
        (ROOT/'tools/unreal/LyraRootBoneOracle').rglob('*') if p.is_file()]


def save(path, value):
    with path.open('x', encoding='utf-8', newline='\n') as stream:
        stream.write(json.dumps(value, indent=2, allow_nan=False) + '\n')


def check_hashes(base, hashes):
    for name, expected in hashes.items():
        assert sha(base/name) == expected.lower(), name


def native():
    for kind in ('requests', 'native', 'closure'):
        assert (OUT/f'root-bones-native-v1-{kind}.json').read_bytes() == (OUT/f'root-bones-native-v2-{kind}.json').read_bytes(), kind
    closure = read(OUT/'root-bones-native-v2-closure.json')
    assert sha(OUT/'root-bones-native-v2-native.json') == closure['nativeSha256']
    assert sha(OUT/'root-bones-native-v2-requests.json') == closure['requestSha256']
    scope = closure['scope']
    assert scope['directCounterWrites'] == scope['globalFrameWrites'] == scope['assetsSaved'] == 0
    assert scope['originalBlueprint'] and scope['explicitOriginalLinkedNodeCalls'] and scope['providerRecalcRequiredCurves']
    assert not scope['naturalSceneScheduler'] and not scope['als81BoneMapping']
    check_hashes(ROOT/'assets/generated/lyra_als', closure['previousFixtureSha256'])
    check_hashes(Path('../GASP58'), closure['protectedProject'])
    check_hashes(ROOT/'tools/unreal/LyraRootBoneOracle', closure['probeSourceSha256'])
    check_hashes(Path('../UE_5.8/Engine'), closure['engineSourceSha256'])
    for name, expected in closure['assetSha256'].items():
        package = name.split('.')[0]
        if package.startswith('/Game/'):
            file = Path('../GASP58/Content')/(package[6:]+'.uasset')
        else:
            assert package.startswith('/ShooterCore/')
            file = Path('../GASP58/Plugins/GameFeatures/ShooterCore/Content')/(package[13:]+'.uasset')
        assert sha(file) == expected, name
    cases = read(OUT/'root-bones-native-v2-native.json')['cases']
    counts = dict(first=0, late=0, invalidated=0, direct=0, invalidatedDirect=0)
    divergence = rows = 0
    for case in cases:
        assert case['bones'] == 164 and case['counterWrites'] == case['globalFrameWrites'] == 0
        for row in case['rows']:
            rows += 1
            stage = row['stage']
            events = row['events']
            if stage == 'first-update':
                start = next(i for i, e in enumerate(events) if e['hook'] == 'AnimGraph' and e['phase'] == 'update' and e['boundary'] == 'enter')
                events = events[start:]
            roots = [e for e in events if e['boundary'] == 'enter' and e['phase'] == 'bones' and e['hook'] != 'AnimGraph']
            key = ('first' if stage == 'first-update' else 'late' if stage == 'linked-update' else
                   'invalidated' if stage == 'providers-invalidated-update' else
                   'direct' if stage.startswith('direct-') else
                   'invalidatedDirect' if stage.startswith('invalidated-direct-') else None)
            if key:
                counts[key] += len(roots)
                divergence += sum(e['phases']['bones']['counter'] != row['main']['bones']['counter'] for e in roots)
            if stage.startswith('repeat-') or stage in ('repeated-linked-update', 'repeated-providers-update'):
                assert not roots, (case['profile'], stage)
    assert len(cases) == 36 and rows == 1668
    assert counts == dict(first=39, late=39, invalidated=78, direct=54, invalidatedDirect=132), counts
    assert divergence == 108, divergence
    for tag in ('root-bones-native-v1', 'root-bones-native-v2'):
        log = (OUT/f'{tag}-native.log').read_text(encoding='utf-8-sig')
        assert 'LYRA_ROOT_BONES_NATIVE_OK cases=36' in log
        assert not re.search(r'Error:|Fatal error:|Ensure condition failed', log)
    return dict(cases=len(cases), rows=rows, rootEntries=counts, ownMainDivergence=divergence,
                assets=len(closure['assetSha256']), json=len(closure['previousFixtureSha256']),
                configs=len(closure['protectedProject']), counterWrites=0)


def freeze(tag):
    baseline = read(OUT/'root-bones-v1-before.json')
    changes = set()
    for name, expected in baseline.items():
        path = ROOT/name
        if not path.exists() or sha(path) != expected:
            changes.add(name)
    assert changes == CHANGED | DELETED | {'ROADMAP.md'}, changes
    names = sorted(CHANGED | set(NEW))
    save(OUT/f'{tag}-frozen.json', dict(sources={n: sha(ROOT/n) for n in names},
        protected={n: h for n, h in baseline.items() if n not in CHANGED | DELETED | DOCS},
        deleted=sorted(DELETED), baselineCount=len(baseline), scope='Shared Core and playable Lyra locomotion'))
    print(f'LYRA_CORE_REUSE_FROZEN sources={len(names)} protected={len(baseline)-len(CHANGED | DELETED | DOCS & set(baseline))}')


def audit(tag, frozen_tag=None):
    frozen = read(OUT/f'{frozen_tag or tag}-frozen.json')
    check_hashes(ROOT, frozen['sources'])
    check_hashes(ROOT, frozen['protected'])
    assert all(not (ROOT/n).exists() for n in frozen['deleted'])
    original = native()
    test = ET.parse(OUT/'lyra-core-reuse-v1-tests/lyra-core-reuse-v1.trx')
    counters = test.find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters').attrib
    assert counters['total'] == counters['passed'] == '197' and counters['failed'] == '0'
    summaries = []
    for config in ('debug', 'optimize'):
        summary = read(OUT/f'{tag}-{config}-verification.json')
        assert summary['passed'] and (config != 'optimize' or summary['debugRestored'])
        for row in summary['runs']:
            assert row['passed'] and row['exitCode'] == 0 and sha(Path(row['log'])) == row['logSha256'].lower()
            if 'report' in row:
                assert sha(Path(row['report'])) == row['reportSha256'].lower()
        summaries.append(summary)
    assert read(Path(summaries[0]['runs'][-2]['report'])) == read(Path(summaries[1]['runs'][-1]['report']))
    # Check the assemblies restored by the Optimize runner against its saved originals.
    backup = OUT/f'{tag}-optimize-debug-backup'
    for file in backup.iterdir():
        assert sha(file) == sha(ROOT/'.godot/mono/temp/bin/Debug'/file.name), file.name
    for suffix in ('build', 'optimize-build'):
        log = (OUT/f'{tag}-{suffix}.log').read_bytes().decode('utf-8', errors='replace')
        assert '0 个警告' in log and '0 个错误' in log
    captures = OUT/f'{tag}-debug-rendered-frames'
    frames = read(captures/'frames.json')
    assert frames['phase'] == 'FramePostDraw' and len(frames['frames']) == 7
    assert len(list(captures.glob('*.png'))) == 7
    value = dict(passed=True, sources=len(frozen['sources']), protected=len(frozen['protected']),
                 deleted=len(frozen['deleted']), native=original, corePassed=197,
                 godotRuns=sum(len(s['runs']) for s in summaries), renderedFrames=7,
                 debugRestored=True, completeEngineMigration=False, goalComplete=False)
    save(OUT/f'{tag}-audit.json', value)
    print('LYRA_CORE_REUSE_AUDIT_OK ' + json.dumps(value))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('mode', choices=['freeze', 'audit'])
    parser.add_argument('--tag', default='lyra-core-reuse-v2')
    parser.add_argument('--frozen-tag', help='Explicit final source snapshot when only the auditor was repaired.')
    args = parser.parse_args()
    assert re.fullmatch(r'[a-zA-Z0-9_-]+', args.tag)
    if args.mode == 'freeze':
        freeze(args.tag)
    else:
        assert args.frozen_tag is None or re.fullmatch(r'[a-zA-Z0-9_-]+', args.frozen_tag)
        audit(args.tag, args.frozen_tag)
