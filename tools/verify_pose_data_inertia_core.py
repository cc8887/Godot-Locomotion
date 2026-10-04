"""Protect the pose/payload inertia migration and verify actual production callers."""
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
CHANGED = {'src/Als.Core/Locomotion/AlsPrecisePoseBlender.cs', 'scripts/verify-lyra-core-reuse.ps1'} | {
    f'src/Als.Godot/Animation/Lyra/{n}.cs' for n in ('LyraMainCompositionOperators',
        'LyraMontageSlotPose', 'LyraMainInertialization', 'LyraMainInertiaSmoke',
        'LyraMainInertiaDiagnosticSmoke', 'LyraRootRotationMath')}
NEW = {f'src/Als.Core/Locomotion/{n}.cs' for n in ('AlsPoseDataInertialization', 'AlsRootRotationMath')} | {
    f'tests/Als.Core.Tests/Locomotion/{n}Tests.cs' for n in ('AlsPoseDataInertialization', 'AlsRootRotationMath')} | {
    'scripts/verify-lyra-managed-yaw.ps1', 'tools/verify_pose_data_inertia_core.py'}
DOCS = {'ROADMAP.md', 'docs/verification/2026-10-03-lyra-als-interface-review.md'}
CASES = {'main-data', 'aiming-data', 'slot-composition', 'main-inertia', 'main-rig',
         'als-ordinary', 'als-aim', 'als-grounded', 'ordinary-ten'}


def save(path, value):
    with path.open('x', encoding='utf-8', newline='\n') as stream:
        stream.write(json.dumps(value, indent=2, allow_nan=False) + '\n')


def check(base, hashes):
    for name, expected in hashes.items():
        assert sha(base / name) == expected.lower(), name


def freeze(tag):
    baseline = read(OUT / 'pose-data-inertia-core-v1-before.json')
    assert {n for n, h in baseline.items() if sha(ROOT / n) != h} == CHANGED
    save(OUT / f'{tag}-frozen.json', dict(
        sources={n: sha(ROOT / n) for n in sorted(CHANGED | NEW)},
        protected={n: h for n, h in baseline.items() if n not in CHANGED | DOCS},
        evidence={n: sha(OUT / n) for n in ('pose-data-inertia-core-v1-before.json',
            'pose-data-inertia-core-v1-main-inertia-before.cs', 'pose-data-inertia-core-v2-build.log',
            'pose-data-inertia-core-v2-tests.log', 'animation-data-core-v3-audit.json',
            'animation-data-core-v3-debug-ordinary-ten.json')}))
    print(f'POSE_DATA_INERTIA_CORE_FROZEN sources={len(CHANGED | NEW)} protected={len(baseline)-len(CHANGED | DOCS)}')


def audit(tag):
    frozen = read(OUT / f'{tag}-frozen.json')
    check(ROOT, frozen['sources']); check(ROOT, frozen['protected']); check(OUT, frozen['evidence'])
    original = native()
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    trx = ET.parse(OUT / f'{tag}-tests/{tag}.trx')
    counts = trx.find('.//t:Counters', ns).attrib
    assert counts['total'] == counts['passed'] == '102' and counts['failed'] == counts['notExecuted'] == '0'
    methods = [t.find('t:TestMethod', ns).get('className') for t in trx.findall('.//t:UnitTest', ns)]
    assert sum(n.split(',')[0].endswith('.AlsPoseDataInertializationTests') for n in methods) == 3
    assert sum(n.split(',')[0].endswith('.AlsRootRotationMathTests') for n in methods) == 9
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
    assert ordinary[0] == ordinary[1] == read(OUT / 'animation-data-core-v3-debug-ordinary-ten.json')
    assert summaries[0]['assemblies'] != summaries[1]['assemblies']
    for file in (OUT / f'{tag}-optimize-debug-backup').iterdir():
        assert sha(file) == sha(ROOT / '.godot/mono/temp/bin/Debug' / file.name)
    for suffix in ('build', 'optimize-build'):
        log = (OUT / f'{tag}-{suffix}.log').read_text(encoding='utf-8-sig')
        assert '0 个警告' in log and '0 个错误' in log
    managed = read(OUT / f'{tag}-managed-closure.json')
    assert managed['passed'] and managed['nativeAbsentDuringRun'] and managed['nativeRestored']
    for file in managed['nativeFiles']: assert sha(Path(file['source'])) == file['sha256'].lower()
    extra = read(OUT / f'{tag}-managed-debug-verification.json')
    assert extra['passed'] and extra['assemblies'] == summaries[0]['assemblies']
    assert len(extra['runs']) == 1 and extra['runs'][0]['name'] == 'ordinary-ten'
    row = extra['runs'][0]; assert row['passed'] and row['exitCode'] == 0
    assert sha(Path(row['log'])) == row['logSha256'].lower()
    assert sha(Path(row['report'])) == row['reportSha256'].lower()
    managed_report = read(Path(row['report']))
    # The portable path preserves functionality; native floating-point rounding
    # is intentionally not its acceptance criterion.
    assert managed_report['model']['player']['model']['frames'] == 480
    assert managed_report['model']['player']['model']['published'] == 480
    frames = read(OUT / f'{tag}-debug-rendered-frames/frames.json')
    assert frames['phase'] == 'FramePostDraw' and len(frames['frames']) == 7
    value = dict(passed=True, sources=len(frozen['sources']), protected=len(frozen['protected']),
        corePassed=102, newCoreTests=12, godotRuns=runs+1, renderedFrames=7, debugRestored=True,
        additivePoseSharedWithAls=True, fullPayloadInertiaInCore=True, yawMathInCore=True,
        managedYawOrdinaryPassed=True, managedReportMatchesNative=managed_report==ordinary[0],
        nativeMathRestored=True, ordinaryReportMatchesPrevious=True, original=original,
        completeEngineMigration=False, hardwareKeyboardAccepted=False, terrainVisualAccepted=False, goalComplete=False)
    save(OUT / f'{tag}-audit.json', value)
    print('POSE_DATA_INERTIA_CORE_AUDIT_OK ' + json.dumps(value))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('mode', choices=('freeze', 'audit'))
    parser.add_argument('--tag', default='pose-data-inertia-core-v4'); args = parser.parse_args()
    assert re.fullmatch(r'[a-zA-Z0-9_-]+', args.tag)
    (freeze if args.mode == 'freeze' else audit)(args.tag)
