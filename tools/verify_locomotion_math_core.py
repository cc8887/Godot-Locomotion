"""Audit shared interpolation/rotation/pose math and actual character regressions."""
from pathlib import Path
import argparse
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from verify_lyra_core_reuse import native

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts/lyra-analysis'
read = lambda p: json.loads(p.read_bytes())
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
CHANGED = {'src/Als.Core/Math/AlsMath.cs', 'scripts/verify-lyra-core-reuse.ps1'} | {
    f'src/Als.Core/Locomotion/{n}.cs' for n in ('AlsMovementInputFunctions', 'AlsPrecisePose',
        'AlsFootPlacementMath', 'AlsCharacterRotationMath', 'AlsRefactoredLocomotionHistory')} | {
    f'src/Als.Godot/Animation/Lyra/{n}.cs' for n in ('LyraAimWeightHost', 'LyraLeftHandLayerHost',
        'LyraMainUpdateHost', 'LyraMainObservationHost')}
NEW = {'tests/Als.Core.Tests/Locomotion/AlsSharedMotionMathTests.cs',
       'tools/verify_locomotion_math_core.py'}
DOCS = {'ROADMAP.md', 'docs/verification/2026-10-03-lyra-als-interface-review.md'}
CASES = {'main-data', 'main-observation', 'main-update', 'aim-weights', 'left-hand', 'aiming-data',
         'slot-composition', 'main-rig', 'weapon', 'als-ordinary', 'als-aim', 'als-grounded', 'ordinary-ten'}


def save(path, value):
    with path.open('x', encoding='utf-8', newline='\n') as stream:
        stream.write(json.dumps(value, indent=2, allow_nan=False) + '\n')


def check(base, hashes):
    for name, expected in hashes.items():
        assert sha(base / name) == expected.lower(), name


def freeze(tag):
    baseline = read(OUT / 'locomotion-math-core-v1-before.json')
    assert {n for n, h in baseline.items() if sha(ROOT / n) != h} == CHANGED
    save(OUT / f'{tag}-frozen.json', dict(
        sources={n: sha(ROOT / n) for n in sorted(CHANGED | NEW)},
        protected={n: h for n, h in baseline.items() if n not in CHANGED | DOCS},
        evidence={n: sha(OUT / n) for n in ('locomotion-math-core-v1-before.json',
            'pose-data-inertia-core-v4-audit.json', 'pose-data-inertia-core-v4-debug-ordinary-ten.json')}))
    print(f'MOTION_MATH_CORE_FROZEN sources={len(CHANGED | NEW)} protected={len(baseline)-len(CHANGED | DOCS)}')


def audit(tag):
    frozen = read(OUT / f'{tag}-frozen.json')
    check(ROOT, frozen['sources']); check(ROOT, frozen['protected']); check(OUT, frozen['evidence'])
    original = native()
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    trx = ET.parse(OUT / f'{tag}-tests/{tag}.trx')
    counts = trx.find('.//t:Counters', ns).attrib
    assert int(counts['total']) > 31 and counts['total'] == counts['passed']
    assert counts['failed'] == counts['notExecuted'] == '0'
    methods = [t.find('t:TestMethod', ns).get('className').split(',')[0]
               for t in trx.findall('.//t:UnitTest', ns)]
    new_tests = sum(n.endswith('.AlsSharedMotionMathTests') for n in methods)
    assert new_tests == 31, new_tests
    ordinary = []; summaries = []; runs = 0
    for config in ('debug', 'optimize'):
        summary = read(OUT / f'{tag}-{config}-verification.json'); summaries.append(summary)
        assert summary['passed']
        assert {r['name'] for r in summary['runs']} == CASES | ({'rendered'} if config == 'debug' else set())
        if config == 'optimize': assert summary['debugRestored']
        for row in summary['runs']:
            assert row['passed'] and row['exitCode'] == 0
            assert sha(Path(row['log'])) == row['logSha256'].lower()
            assert not re.search(r'^\s*(ERROR|WARNING):', Path(row['log']).read_text(encoding='utf-8-sig'), re.M)
            if 'report' in row: assert sha(Path(row['report'])) == row['reportSha256'].lower()
            if row['name'] == 'ordinary-ten': ordinary.append(read(Path(row['report'])))
            runs += 1
    assert ordinary[0] == ordinary[1] == read(OUT / 'pose-data-inertia-core-v4-debug-ordinary-ten.json')
    assert summaries[0]['assemblies'] != summaries[1]['assemblies']
    for file in (OUT / f'{tag}-optimize-debug-backup').iterdir():
        assert sha(file) == sha(ROOT / '.godot/mono/temp/bin/Debug' / file.name)
    for suffix in ('build', 'optimize-build'):
        log = (OUT / f'{tag}-{suffix}.log').read_text(encoding='utf-8-sig')
        assert '0 个警告' in log and '0 个错误' in log
    frames = read(OUT / f'{tag}-debug-rendered-frames/frames.json')
    assert frames['phase'] == 'FramePostDraw' and len(frames['frames']) == 7
    value = dict(passed=True, sources=len(frozen['sources']), protected=len(frozen['protected']),
        corePassed=int(counts['passed']), newCoreTests=new_tests, godotRuns=runs, renderedFrames=7,
        debugRestored=True, interpolationSharedWithAls=True, safeNormalSharedWithAls=True,
        rotationMatrixSharedWithAls=True, localBlendWithInCore=True, ordinaryReportMatchesPrevious=True,
        original=original, completeEngineMigration=False, hardwareKeyboardAccepted=False,
        terrainVisualAccepted=False, goalComplete=False)
    save(OUT / f'{tag}-audit.json', value)
    print('MOTION_MATH_CORE_AUDIT_OK ' + json.dumps(value))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('mode', choices=('freeze', 'audit'))
    parser.add_argument('--tag', default='locomotion-math-core-v1'); args = parser.parse_args()
    assert re.fullmatch(r'[a-zA-Z0-9_-]+', args.tag)
    (freeze if args.mode == 'freeze' else audit)(args.tag)
