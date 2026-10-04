"""Audit the shared ALS/Lyra cache runtime and generic transform attribute move."""
from pathlib import Path
import argparse
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from verify_lyra_core_reuse import native

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT/'artifacts/lyra-analysis'
read = lambda p: json.loads(p.read_bytes())
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
CHANGED = {'src/Als.Core/Locomotion/AlsPoseCacheEvaluation.cs',
           'src/Als.Godot/Animation/Lyra/LyraMainPoseCacheScope.cs',
           'src/Als.Godot/Animation/Lyra/LyraRootMotionAttribute.cs',
           'src/Als.Godot/Animation/MainGroundedPoseSmoke.cs',
           'scripts/verify-lyra-core-reuse.ps1'}
NEW = {'src/Als.Core/Locomotion/AlsScopedPoseCache.cs',
       'src/Als.Core/Locomotion/AlsTransformAnimationAttribute.cs',
       'tests/Als.Core.Tests/Locomotion/AlsScopedPoseCacheTests.cs',
       'tools/verify_scoped_cache_core.py'}
DOCS = {'ROADMAP.md', 'docs/verification/2026-10-03-lyra-als-interface-review.md'}
CASES = {'cache-owner', 'root-motion', 'main-cache-pose', 'slot-composition',
         'main-pose-feedback', 'main-rig', 'als-ordinary', 'als-aim', 'als-grounded', 'ordinary-ten'}
PREVIOUS_DEBUG_CASES = {'cache-owner', 'root-motion', 'main-cache-pose',
                        'slot-composition', 'main-pose-feedback', 'main-rig'}
INPUTS = ['scoped-cache-core-v3-debug-verification.json', 'scoped-cache-core-v3-frozen.json',
          'scoped-cache-core-v3-baseline-restore.json', 'scoped-cache-core-v3-baseline-als-demo.log',
          'scoped-cache-core-v2-tests/scoped-cache-core-v2.trx',
          'scoped-cache-core-v5-build.log', 'scoped-cache-core-v5-optimize-build.log',
          'scoped-cache-core-v4-debug-verification.json',
          'scoped-cache-core-v4-grounded-baseline-restore.json',
          'scoped-cache-core-v4-baseline-als-grounded.log']


def save(path, value):
    with path.open('x', encoding='utf-8', newline='\n') as stream:
        stream.write(json.dumps(value, indent=2, allow_nan=False)+'\n')


def check(hashes):
    for name, expected in hashes.items():
        assert sha(ROOT/name) == expected.lower(), name


def freeze(tag):
    baseline = read(OUT/'scoped-cache-core-v1-before.json')
    actual = {name for name, expected in baseline.items() if sha(ROOT/name) != expected}
    assert actual == CHANGED, actual
    save(OUT/f'{tag}-frozen.json', dict(sources={n: sha(ROOT/n) for n in sorted(CHANGED|NEW)},
        protected={n: h for n, h in baseline.items() if n not in CHANGED|DOCS},
        evidence={n: sha(OUT/n) for n in INPUTS}))
    print(f'SCOPED_CACHE_CORE_FROZEN sources={len(CHANGED|NEW)} protected={len(baseline)-len(CHANGED|DOCS)}')


def audit(tag):
    frozen = read(OUT/f'{tag}-frozen.json'); check(frozen['sources']); check(frozen['protected'])
    for name, expected in frozen['evidence'].items(): assert sha(OUT/name) == expected, name
    previous = read(OUT/'scoped-cache-core-v3-debug-verification.json')
    previous_sources = read(OUT/'scoped-cache-core-v3-frozen.json')['sources']
    for name in (CHANGED | NEW) - {'scripts/verify-lyra-core-reuse.ps1', 'tools/verify_scoped_cache_core.py',
                                  'src/Als.Godot/Animation/MainGroundedPoseSmoke.cs'}:
        assert previous_sources[name] == frozen['sources'][name], name
    assert {r['name'] for r in previous['runs']} == PREVIOUS_DEBUG_CASES | {'als-demo'}
    legacy = next(r for r in previous['runs'] if r['name'] == 'als-demo')
    assert not previous['passed'] and not legacy['passed'] and legacy['exitCode'] == 1
    assert sha(Path(legacy['log'])) == legacy['logSha256'].lower()
    assert Path(legacy['log']).read_bytes() == (OUT/'scoped-cache-core-v3-baseline-als-demo.log').read_bytes()
    assert 'AimOffset did not publish positive/negative yaw/pitch evidence' in Path(legacy['log']).read_text()
    restored = read(OUT/'scoped-cache-core-v3-baseline-restore.json')
    assert restored['restored'] and restored['exitCode'] == 1
    assert restored['baseline'] == 'lyra-core-reuse-v2-optimize-debug-backup'
    assert restored['currentAssemblies'] == previous['assemblies']
    interrupted = read(OUT/'scoped-cache-core-v4-debug-verification.json')
    assert not interrupted['passed'] and interrupted['assemblies'] == previous['assemblies']
    assert {r['name'] for r in interrupted['runs']} == {'als-ordinary', 'als-aim', 'als-grounded'}
    grounded_failure = next(r for r in interrupted['runs'] if r['name'] == 'als-grounded')
    assert not grounded_failure['passed'] and grounded_failure['exitCode'] == 1
    assert sha(Path(grounded_failure['log'])) == grounded_failure['logSha256'].lower()
    assert Path(grounded_failure['log']).read_bytes() == (OUT/'scoped-cache-core-v4-baseline-als-grounded.log').read_bytes()
    grounded_restore = read(OUT/'scoped-cache-core-v4-grounded-baseline-restore.json')
    assert grounded_restore['restored'] and grounded_restore['exitCode'] == 1
    assert grounded_restore['currentAssemblies'] == previous['assemblies']
    original = native()
    trx = ET.parse(OUT/'scoped-cache-core-v2-tests/scoped-cache-core-v2.trx')
    counters = trx.find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters').attrib
    assert counters['total'] == counters['passed'] == '206' and counters['failed'] == '0'
    ordinary = []; runs = 0
    for config in ('debug', 'optimize'):
        summary = read(OUT/f'{tag}-{config}-verification.json')
        assert summary['passed']
        expected = {'als-grounded', 'ordinary-ten', 'rendered'} if config == 'debug' else CASES
        assert {r['name'] for r in summary['runs']} == expected
        rows = summary['runs']
        if config == 'debug':
            # Only the Grounded fixture changed in GodotALS. Both engine-independent
            # assemblies and all production sources still match the earlier runs.
            for name in ('Als.Core.dll', 'Als.Core.pdb', 'Als.Import.dll', 'Als.Import.pdb'):
                assert summary['assemblies'][name] == previous['assemblies'][name], name
            rows = ([r for r in previous['runs'] if r['name'] != 'als-demo'] +
                    [r for r in interrupted['runs'] if r['name'] != 'als-grounded'] + rows)
        if config == 'optimize': assert summary['debugRestored']
        for row in rows:
            assert row['passed'] and row['exitCode'] == 0
            assert sha(Path(row['log'])) == row['logSha256'].lower()
            if 'report' in row: assert sha(Path(row['report'])) == row['reportSha256'].lower()
            if row['name'] == 'ordinary-ten': ordinary.append(read(Path(row['report'])))
            runs += 1
    assert ordinary[0] == ordinary[1]
    prior = read(OUT/'lyra-core-reuse-v2-debug-ordinary-ten.json')
    assert ordinary[0] == prior, 'Shared cache changed the ordinary character report.'
    backup = OUT/f'{tag}-optimize-debug-backup'
    for file in backup.iterdir(): assert sha(file) == sha(ROOT/'.godot/mono/temp/bin/Debug'/file.name)
    for suffix in ('build', 'optimize-build'):
        text = (OUT/f'{tag}-{suffix}.log').read_text(encoding='utf-8-sig')
        assert '0 个警告' in text and '0 个错误' in text
    frames = read(OUT/f'{tag}-debug-rendered-frames/frames.json')
    assert frames['phase'] == 'FramePostDraw' and len(frames['frames']) == 7
    value = dict(passed=True, sources=len(frozen['sources']), protected=len(frozen['protected']),
                 corePassed=206, godotRuns=runs, renderedFrames=7, debugRestored=True,
                 alsAndLyraShareCacheControl=True, transformAttributeInCore=True,
                 ordinaryReportMatchesPrevious=True, original=original,
                 legacyFailedRuns=2, legacyFailureMatchesPreviousBuild=True,
                 groundedFailedRunsBeforeFixtureRepair=2, groundedFixtureUsesLogicalSources=True,
                 productionAlsDemo='refactored_stance_demo_smoke',
                 hardwareKeyboardAccepted=False, completeEngineMigration=False, goalComplete=False)
    save(OUT/f'{tag}-audit.json', value)
    print('SCOPED_CACHE_CORE_AUDIT_OK '+json.dumps(value))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('mode', choices=('freeze', 'audit'))
    parser.add_argument('--tag', default='scoped-cache-core-v5'); args = parser.parse_args()
    assert re.fullmatch(r'[a-zA-Z0-9_-]+', args.tag)
    (freeze if args.mode == 'freeze' else audit)(args.tag)
