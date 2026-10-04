"""Audit shared Rig kernels, hierarchy state, previews and ordinary character regressions."""
from pathlib import Path
import argparse
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from verify_lyra_core_reuse import native

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts/lyra-analysis'
BUILD_TAG = 'rig-math-core-v3'
read = lambda p: json.loads(p.read_bytes())
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
CHANGED = {'scripts/verify-lyra-core-reuse.ps1', 'src/Als.Core/Math/AlsMath.cs'} | {
    f'src/Als.Core/Locomotion/{n}.cs' for n in ('AlsQuaternion', 'AlsPrecisePose', 'AlsTwoBoneIk', 'AlsCharacterRotationMath')} | {
    f'src/Als.Godot/Animation/Lyra/{n}.cs' for n in ('LyraFootPlantRigMath', 'LyraFootPlantRigExecutor',
        'LyraFootPlantRigHierarchy', 'LyraHipFirePoseLayer', 'LyraRootYawOffset')}
NEW = {f'src/Als.Core/Locomotion/{n}.cs' for n in ('AlsRigAim', 'AlsRigHierarchy')} | {
    f'tests/Als.Core.Tests/Locomotion/{n}Tests.cs' for n in ('AlsRigAimMath', 'AlsRigHierarchy')} | {'tools/verify_rig_core_reuse.py'}
DOCS = {'ROADMAP.md', 'docs/verification/2026-10-03-lyra-als-interface-review.md'}
CASES = {'rig-hierarchy', 'rig-solver', 'rig-output', 'rig-physics', 'root-yaw-preview', 'pose-preview',
    'main-rig', 'main-data', 'als-ordinary', 'als-aim', 'als-grounded', 'ordinary-ten', 'weapon'}


def save(path, value):
    with path.open('x', encoding='utf-8', newline='\n') as stream:
        stream.write(json.dumps(value, indent=2, allow_nan=False) + '\n')


def check(base, hashes):
    for name, expected in hashes.items():
        assert sha(base / name) == expected.lower(), name


def freeze(tag):
    baseline = read(OUT / 'rig-math-core-v1-before.json')
    assert {n for n, h in baseline.items() if sha(ROOT / n) != h} == CHANGED
    save(OUT / f'{tag}-frozen.json', dict(
        sources={n: sha(ROOT / n) for n in sorted(CHANGED | NEW)},
        protected={n: h for n, h in baseline.items() if n not in CHANGED | DOCS},
        evidence={n: sha(OUT / n) for n in ('rig-math-core-v1-before.json',
            'locomotion-math-core-v1-audit.json', 'locomotion-math-core-v1-debug-ordinary-ten.json',
            'rig-math-core-v1-lyra-math-before.cs', 'rig-math-core-v1-hierarchy-before.cs',
            f'{BUILD_TAG}-build.log', f'{BUILD_TAG}-optimize-build.log',
            f'{BUILD_TAG}-tests/{BUILD_TAG}.trx', f'{BUILD_TAG}-tests.log',
            f'{BUILD_TAG}-debug-verification.json', f'{BUILD_TAG}-debug-rig-physics.log')}))
    print(f'RIG_CORE_REUSE_FROZEN sources={len(CHANGED | NEW)} protected={len(baseline)-len(CHANGED | DOCS)}')


def audit(tag):
    frozen = read(OUT / f'{tag}-frozen.json')
    check(ROOT, frozen['sources']); check(ROOT, frozen['protected']); check(OUT, frozen['evidence'])
    original = native()
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    previous = read(OUT / f'{BUILD_TAG}-frozen.json')
    # Only the PowerShell report argument and this audit changed after the build.
    check(ROOT, {n: h for n, h in previous['sources'].items()
                 if n not in {'scripts/verify-lyra-core-reuse.ps1', 'tools/verify_rig_core_reuse.py'}})
    trx = ET.parse(OUT / f'{BUILD_TAG}-tests/{BUILD_TAG}.trx')
    counts = trx.find('.//t:Counters', ns).attrib
    assert counts['total'] == counts['passed'] == '148'
    assert counts['failed'] == counts['notExecuted'] == '0'
    methods = [t.find('t:TestMethod', ns).get('className').split(',')[0]
               for t in trx.findall('.//t:UnitTest', ns)]
    new_tests = sum(n.endswith(('.AlsRigAimMathTests', '.AlsRigHierarchyTests')) for n in methods)
    assert new_tests == 26, new_tests
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
    assert ordinary[0] == ordinary[1] == read(OUT / 'locomotion-math-core-v1-debug-ordinary-ten.json')
    assert summaries[0]['assemblies'] != summaries[1]['assemblies']
    for file in (OUT / f'{tag}-optimize-debug-backup').iterdir():
        assert sha(file) == sha(ROOT / '.godot/mono/temp/bin/Debug' / file.name)
    for suffix in ('build', 'optimize-build'):
        log = (OUT / f'{BUILD_TAG}-{suffix}.log').read_text(encoding='utf-8-sig')
        assert '0 个警告' in log and '0 个错误' in log
    frames = read(OUT / f'{tag}-debug-rendered-frames/frames.json')
    assert frames['phase'] == 'FramePostDraw' and len(frames['frames']) == 7
    value = dict(passed=True, sources=len(frozen['sources']), protected=len(frozen['protected']),
        corePassed=int(counts['passed']), newCoreTests=new_tests, buildAndCoreEvidenceTag=BUILD_TAG,
        godotRuns=runs, renderedFrames=7,
        debugRestored=True, rigIkSharedWithAls=True, singleParentHierarchyInCore=True, rigAimInCore=True,
        quaternionAndInverseInCore=True, oldPreviewMathShared=True, ordinaryReportMatchesPrevious=True,
        original=original, completeEngineMigration=False, hardwareKeyboardAccepted=False,
        terrainVisualAccepted=False, goalComplete=False)
    save(OUT / f'{tag}-audit.json', value)
    print('RIG_CORE_REUSE_AUDIT_OK ' + json.dumps(value))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('mode', choices=('freeze', 'audit'))
    parser.add_argument('--tag', default='rig-math-core-v4'); args = parser.parse_args()
    assert re.fullmatch(r'[a-zA-Z0-9_-]+', args.tag)
    (freeze if args.mode == 'freeze' else audit)(args.tag)
