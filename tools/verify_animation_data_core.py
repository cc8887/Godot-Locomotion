"""Verify the shared integer attribute and flagged curve operators in production."""
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
CHANGED = {'src/Als.Core/Locomotion/AlsStandingCycleCurves.cs',
           'scripts/verify-lyra-core-reuse.ps1'} | {
    f'src/Als.Godot/Animation/Lyra/{n}.cs' for n in (
        'LyraAdditivesLayerHost', 'LyraAimingLayerHost', 'LyraIdleLayerHost',
        'LyraLayeredDataBlend', 'LyraLocomotionPoseState', 'LyraMainCompositionOperators',
        'LyraMainLocomotionHost', 'LyraMontageSlotPose', 'LyraSourceAttributeBank',
        'LyraSourceCurveBank')}
NEW = {'src/Als.Core/Animation/AlsAnimationAttributeIdentity.cs',
       'src/Als.Core/Animation/AlsIntegerAnimationAttribute.cs',
       'src/Als.Core/Curves/AlsAnimationCurveSample.cs',
       'src/Als.Godot/Animation/Lyra/LyraAnimationDataAliases.cs',
       'tools/verify_animation_data_core.py'}
DOCS = {'ROADMAP.md', 'docs/verification/2026-10-03-lyra-als-interface-review.md'}
CASES = {'root-motion', 'cycle-data', 'source-data', 'idle-data', 'main-data',
         'aiming-data', 'additives-data', 'slot-composition', 'main-pose-feedback',
         'als-ordinary', 'ordinary-ten'}


def save(path, value):
    with path.open('x', encoding='utf-8', newline='\n') as stream:
        stream.write(json.dumps(value, indent=2, allow_nan=False) + '\n')


def check(base, hashes):
    for name, expected in hashes.items():
        assert sha(base / name) == expected.lower(), name


def freeze(tag):
    baseline = read(OUT / 'animation-data-core-v1-before.json')
    changed = {n for n, h in baseline.items() if sha(ROOT / n) != h}
    assert changed == CHANGED, changed
    save(OUT / f'{tag}-frozen.json', dict(
        sources={n: sha(ROOT / n) for n in sorted(CHANGED | NEW)},
        protected={n: h for n, h in baseline.items() if n not in CHANGED | DOCS},
        evidence={n: sha(OUT / n) for n in (
            'animation-data-core-v1-before.json', 'scoped-cache-core-v5-audit.json',
            'scoped-cache-core-v5-debug-ordinary-ten.json')}))
    print(f'ANIMATION_DATA_CORE_FROZEN sources={len(CHANGED | NEW)} protected={len(baseline)-len(CHANGED | DOCS)}')


def audit(tag):
    frozen = read(OUT / f'{tag}-frozen.json')
    check(ROOT, frozen['sources']); check(ROOT, frozen['protected']); check(OUT, frozen['evidence'])
    original = native()
    counters = ET.parse(OUT / f'{tag}-tests/{tag}.trx').find(
        './/{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters').attrib
    assert counters['total'] == counters['passed'] == '30'
    assert counters['failed'] == counters['notExecuted'] == '0'
    reports = []; runs = 0; summaries = []
    for config in ('debug', 'optimize'):
        summary = read(OUT / f'{tag}-{config}-verification.json'); summaries.append(summary)
        assert summary['passed']
        assert {r['name'] for r in summary['runs']} == CASES | ({'rendered'} if config == 'debug' else set())
        if config == 'optimize': assert summary['debugRestored']
        for row in summary['runs']:
            assert row['passed'] and row['exitCode'] == 0, row['name']
            assert sha(Path(row['log'])) == row['logSha256'].lower()
            log = Path(row['log']).read_text(encoding='utf-8-sig')
            assert not re.search(r'^\s*(ERROR|WARNING):', log, re.M)
            if 'report' in row: assert sha(Path(row['report'])) == row['reportSha256'].lower()
            if row['name'] == 'ordinary-ten': reports.append(read(Path(row['report'])))
            runs += 1
    assert reports[0] == reports[1] == read(OUT / 'scoped-cache-core-v5-debug-ordinary-ten.json')
    assert summaries[0]['assemblies'] != summaries[1]['assemblies'], 'Optimize was not executed.'
    for file in (OUT / f'{tag}-optimize-debug-backup').iterdir():
        assert sha(file) == sha(ROOT / '.godot/mono/temp/bin/Debug' / file.name)
    for suffix in ('build', 'optimize-build'):
        log = (OUT / f'{tag}-{suffix}.log').read_text(encoding='utf-8-sig')
        assert '0 个警告' in log and '0 个错误' in log
    captures = OUT / f'{tag}-debug-rendered-frames'
    frames = read(captures / 'frames.json')
    assert frames['phase'] == 'FramePostDraw' and len(frames['frames']) == 7
    assert len(list(captures.glob('*.png'))) == 7
    value = dict(passed=True, sources=len(frozen['sources']), protected=len(frozen['protected']),
                 corePassed=30, godotRuns=runs, renderedFrames=7, debugRestored=True,
                 integerAttributesInCore=True, flaggedCurvesInCore=True,
                 reusesAlsScalarCurveOperations=True, ordinaryReportMatchesPrevious=True,
                 original=original, completeEngineMigration=False,
                 hardwareKeyboardAccepted=False, terrainVisualAccepted=False, goalComplete=False)
    save(OUT / f'{tag}-audit.json', value)
    print('ANIMATION_DATA_CORE_AUDIT_OK ' + json.dumps(value))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('mode', choices=('freeze', 'audit'))
    parser.add_argument('--tag', default='animation-data-core-v3'); args = parser.parse_args()
    assert re.fullmatch(r'[a-zA-Z0-9_-]+', args.tag)
    (freeze if args.mode == 'freeze' else audit)(args.tag)
