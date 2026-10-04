"""Audit shared Rig output, parent constraints and ALS additive reuse."""
from pathlib import Path
import argparse
import hashlib
import json
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts/lyra-analysis'
TAG = 'rig-transfer-core-v1'
RUNTIME_TAG = TAG
BASELINE = TAG
TEST_TAG = TAG
CHANGED = {'src/Als.Core/Locomotion/AlsPrecisePoseBlender.cs', 'src/Als.Core/Locomotion/AlsRigHierarchy.cs',
           'src/Als.Godot/Animation/Lyra/LyraFootPlantRigHierarchy.cs',
           'src/Als.Godot/Animation/Lyra/LyraFootPlantRigOutputTransfer.cs',
           'src/Als.Godot/Animation/Lyra/LyraFootPlantRigExecutor.cs'}
NEW = {'src/Als.Core/Locomotion/AlsRigPoseAdapter.cs',
       'tests/Als.Core.Tests/Locomotion/AlsRigPoseAdapterTests.cs', 'tools/verify_rig_transfer_core_reuse.py'}
DOCS = {'ROADMAP.md', 'docs/verification/2026-10-03-lyra-als-interface-review.md'}
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read = lambda p: json.loads(p.read_bytes())


def save(path, value):
    with path.open('x', encoding='utf-8', newline='\n') as stream:
        stream.write(json.dumps(value, indent=2, allow_nan=False) + '\n')


def freeze():
    before = read(OUT / f'{BASELINE}-before.json')
    assert {n for n, h in before.items() if sha(ROOT / n) != h} == CHANGED
    save(OUT / f'{TAG}-frozen.json', dict(
        sources={n: sha(ROOT / n) for n in sorted(CHANGED | NEW)},
        protected={n: h for n, h in before.items() if n not in CHANGED | DOCS},
        previousEvidence={n: sha(OUT / n) for n in (
            'compressed-core-v2-audit.json', 'rig-runtime-core-v3-audit.json',
            'rig-runtime-core-v2-ordinary-debug-rig-physics.json',
            'terrain-course-v3-ordinary-debug-ordinary-ten.json',
            *(f'terrain-course-v3-debug-{hz}.json' for hz in (30, 60, 120)))}))
    print('RIG_TRANSFER_CORE_SOURCES_FROZEN')


def audit():
    frozen = read(OUT / f'{TAG}-frozen.json')
    for group in ('sources', 'protected'):
        for n, h in frozen[group].items(): assert sha(ROOT / n) == h, n
    for n, h in frozen['previousEvidence'].items(): assert sha(OUT / n) == h, n
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    counts = {}
    for suite in ('tests',):
        trx = ET.parse(OUT / f'{TEST_TAG}-{suite}/{TEST_TAG}.trx')
        counters = trx.find('.//t:Counters', ns).attrib
        assert counters['total'] == counters['passed']
        assert counters['failed'] == counters['notExecuted'] == '0'
        counts[suite] = int(counters['passed'])
        if suite == 'tests':
            methods = [t.find('t:TestMethod', ns).get('className').split(',')[0]
                       for t in trx.findall('.//t:UnitTest', ns)]
            assert sum(n.endswith(('.AlsRigPoseAdapterTests', '.AlsRigParentConstraintTests')) for n in methods) == 19
    ordinary = []; terrain = {}; physics = []; runs = 0; assemblies = {}
    for suffix, cases in (('ordinary', {'rig-input', 'rig-solver', 'rig-hierarchy', 'rig-output', 'rig-physics', 'main-rig', 'als-ordinary', 'ordinary-ten'}),
                          ('terrain', {'30', '60', '120'})):
        for config in ('debug', 'optimize'):
            summary = read(OUT / f'{RUNTIME_TAG}-{suffix}-{config}-verification.json')
            assert summary['passed'] and {r['name'] for r in summary['runs']} == cases
            if config == 'optimize': assert summary['debugRestored']
            assemblies[suffix, config] = summary['assemblies']
            for row in summary['runs']:
                assert row['passed'] and row['exitCode'] == 0
                assert sha(Path(row['log'])) == row['logSha256'].lower()
                log = Path(row['log']).read_text(encoding='utf-8-sig')
                assert not re.search(r'^\s*(ERROR|WARNING):', log, re.M)
                if 'report' in row:
                    assert sha(Path(row['report'])) == row['reportSha256'].lower()
                    data = read(Path(row['report']))
                    if row['name'] == 'ordinary-ten': ordinary.append(data)
                    elif row['name'] == 'rig-physics':
                        assert data['actualGodotPhysics']
                        physics.append(data)
                    elif suffix == 'terrain': terrain[config, row['name']] = data
                    else: raise AssertionError('Unexpected report case')
                runs += 1
    assert ordinary[0] == ordinary[1] == read(OUT / 'terrain-course-v3-ordinary-debug-ordinary-ten.json')
    assert physics[0] == physics[1] == read(OUT / 'rig-runtime-core-v2-ordinary-debug-rig-physics.json')
    for hz in ('30', '60', '120'):
        assert terrain['debug', hz] == terrain['optimize', hz] == read(OUT / f'terrain-course-v3-debug-{hz}.json')
    for suffix in ('ordinary', 'terrain'):
        assert assemblies[suffix, 'debug'] != assemblies[suffix, 'optimize']
        for file in (OUT / f'{RUNTIME_TAG}-{suffix}-optimize-debug-backup').iterdir():
            assert sha(file) == sha(ROOT / '.godot/mono/temp/bin/Debug' / file.name)
    assert assemblies['ordinary', 'debug'] == assemblies['terrain', 'debug']
    assert assemblies['ordinary', 'optimize'] == assemblies['terrain', 'optimize']
    for suffix in ('build', 'optimize-build'):
        log = (OUT / f'{RUNTIME_TAG}-{suffix}.log').read_text(encoding='utf-8-sig')
        assert '0 个警告' in log and '0 个错误' in log
    evidence = {}
    for p in list(OUT.glob(f'{RUNTIME_TAG}*')) + list(OUT.glob(f'{TAG}*')):
        if p.is_file() and p.suffix in ('.json', '.log', '.cs'):
            evidence[p.name] = sha(p)
    save(OUT / f'{TAG}-audit.json', dict(passed=True, sources=len(frozen['sources']),
        protected=len(frozen['protected']), testCounts=counts, newCoreTests=19,
        godotRuns=runs, runtimeAndBuildTag=RUNTIME_TAG, coreTestTag=TEST_TAG, ordinaryReportMatchesPrevious=True,
        terrainReportsMatchPrevious=True, rigOutputNativeGatesPassed=True, physicsReportMatchesPrevious=True,
        debugAssembliesRestored=True, evidence=evidence,
        newGpuCapture=False, hardwareKeyboardAccepted=False, goalComplete=False))
    print('RIG_TRANSFER_CORE_REUSE_AUDIT_OK', counts, 'runs=', runs)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('mode', choices=('freeze', 'audit'))
    args = parser.parse_args(); (freeze if args.mode == 'freeze' else audit)()
