"""Audit shared ALS/Rig scale-bias-clamp and its production regressions."""
from pathlib import Path
import argparse
import hashlib
import json
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts/lyra-analysis'
TAG = 'alpha-core-v1'
CHANGED = {'src/Als.Core/Locomotion/AlsOverlayPoseWeights.cs',
           'src/Als.Godot/Animation/Lyra/LyraFootPlantRigDynamics.cs',
           'scripts/verify-lyra-core-reuse.ps1'}
NEW = {'src/Als.Core/Locomotion/AlsInputScaleBiasClamp.cs',
       'tests/Als.Core.Tests/Locomotion/AlsInputScaleBiasClampTests.cs',
       'tools/verify_alpha_core_reuse.py'}
DOCS = {'ROADMAP.md', 'docs/verification/2026-10-03-lyra-als-interface-review.md'}
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read = lambda p: json.loads(p.read_bytes())


def save(path, value):
    with path.open('x', encoding='utf-8', newline='\n') as stream:
        stream.write(json.dumps(value, indent=2, allow_nan=False) + '\n')


def freeze():
    before = read(OUT / f'{TAG}-before.json')
    assert {n for n, h in before.items() if sha(ROOT / n) != h} == CHANGED
    save(OUT / f'{TAG}-frozen.json', dict(
        sources={n: sha(ROOT / n) for n in sorted(CHANGED | NEW)},
        protected={n: h for n, h in before.items() if n not in CHANGED | DOCS},
        previousEvidence={n: sha(OUT / n) for n in (
            'terrain-course-v3-audit.json', 'rig-math-core-v4-audit.json',
            'terrain-course-v3-ordinary-debug-ordinary-ten.json',
            *(f'terrain-course-v3-debug-{hz}.json' for hz in (30, 60, 120)))}))
    print('ALPHA_CORE_SOURCES_FROZEN')


def audit():
    frozen = read(OUT / f'{TAG}-frozen.json')
    for group in ('sources', 'protected'):
        for n, h in frozen[group].items(): assert sha(ROOT / n) == h, n
    for n, h in frozen['previousEvidence'].items(): assert sha(OUT / n) == h, n
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    counts = {}
    for suite in ('tests', 'import-tests'):
        trx = ET.parse(OUT / f'{TAG}-{suite}/{TAG}.trx')
        counters = trx.find('.//t:Counters', ns).attrib
        assert counters['total'] == counters['passed']
        assert counters['failed'] == counters['notExecuted'] == '0'
        counts[suite] = int(counters['passed'])
        if suite == 'tests':
            methods = [t.find('t:TestMethod', ns).get('className').split(',')[0]
                       for t in trx.findall('.//t:UnitTest', ns)]
            assert sum(n.endswith('.AlsInputScaleBiasClampTests') for n in methods) == 11
    ordinary = []; terrain = {}; runs = 0; assemblies = {}
    for suffix, cases in (('ordinary', {'rig-dynamics', 'main-rig', 'als-ordinary', 'ordinary-ten'}),
                          ('terrain', {'30', '60', '120'})):
        for config in ('debug', 'optimize'):
            summary = read(OUT / f'{TAG}-{suffix}-{config}-verification.json')
            assert summary['passed'] and {r['name'] for r in summary['runs']} == cases
            if config == 'optimize': assert summary['debugRestored']
            assemblies[suffix, config] = summary['assemblies']
            for row in summary['runs']:
                assert row['passed'] and row['exitCode'] == 0
                assert sha(Path(row['log'])) == row['logSha256'].lower()
                log = Path(row['log']).read_text(encoding='utf-8-sig')
                assert not re.search(r'^\s*(ERROR|WARNING):', log, re.M)
                if row['name'] == 'rig-dynamics':
                    assert 'frames=3360 calls=19217 retries=3360' in log
                    assert 'maxFloat=0' in log
                if 'report' in row:
                    assert sha(Path(row['report'])) == row['reportSha256'].lower()
                    data = read(Path(row['report']))
                    if row['name'] == 'ordinary-ten': ordinary.append(data)
                    else: terrain[config, row['name']] = data
                runs += 1
    assert ordinary[0] == ordinary[1] == read(OUT / 'terrain-course-v3-ordinary-debug-ordinary-ten.json')
    for hz in ('30', '60', '120'):
        assert terrain['debug', hz] == terrain['optimize', hz] == read(OUT / f'terrain-course-v3-debug-{hz}.json')
    for suffix in ('ordinary', 'terrain'):
        assert assemblies[suffix, 'debug'] != assemblies[suffix, 'optimize']
        for file in (OUT / f'{TAG}-{suffix}-optimize-debug-backup').iterdir():
            assert sha(file) == sha(ROOT / '.godot/mono/temp/bin/Debug' / file.name)
    assert assemblies['ordinary', 'debug'] == assemblies['terrain', 'debug']
    assert assemblies['ordinary', 'optimize'] == assemblies['terrain', 'optimize']
    for suffix in ('build', 'optimize-build'):
        log = (OUT / f'{TAG}-{suffix}.log').read_text(encoding='utf-8-sig')
        assert '0 个警告' in log and '0 个错误' in log
    evidence = {}
    for p in OUT.glob(f'{TAG}*'):
        if p.is_file() and p.suffix in ('.json', '.log', '.cs'):
            evidence[p.name] = sha(p)
    save(OUT / f'{TAG}-audit.json', dict(passed=True, sources=len(frozen['sources']),
        protected=len(frozen['protected']), testCounts=counts, newCoreTests=11,
        godotRuns=runs, ordinaryReportMatchesPrevious=True,
        terrainReportsMatchPrevious=True, rigNativeFloatExact=True,
        debugAssembliesRestored=True, evidence=evidence,
        newGpuCapture=False, hardwareKeyboardAccepted=False, goalComplete=False))
    print('ALPHA_CORE_REUSE_AUDIT_OK', counts, 'runs=', runs)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('mode', choices=('freeze', 'audit'))
    args = parser.parse_args(); (freeze if args.mode == 'freeze' else audit)()
