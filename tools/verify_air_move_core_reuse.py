"""Audit Core air extraction against frozen sources and prior actual physics."""
from pathlib import Path
import argparse
import hashlib
import json
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts/lyra-analysis'
TAG = 'air-move-core-v3'
RUNTIME_TAG = 'air-move-core-v2'
CHANGED = {'src/Als.Godot/Locomotion/LyraCharacterAirMovement.cs',
           'src/Als.Godot/Locomotion/LyraCharacterFloorProbe.cs',
           'src/Als.Core/Events/AlsAssetNotifyQueue.cs', 'scripts/verify-lyra-core-reuse.ps1'}
NEW = {'src/Als.Core/Locomotion/AlsCharacterAirMovement.cs', 'src/Als.Core/Math/AlsRandomStream.cs',
       'tests/Als.Core.Tests/Locomotion/AlsCharacterAirMovementTests.cs', 'tools/verify_air_move_core_reuse.py'}
DOCS = {'ROADMAP.md', 'docs/verification/2026-10-03-lyra-als-interface-review.md'}
PREVIOUS = ['air-move-core-v2-frozen.json', 'ground-move-core-v2-audit.json', 'rig-runtime-core-v3-audit.json',
            'character-air-debug-cmc-penetration-v2-final-queries.json',
            'rig-runtime-core-v2-ordinary-debug-rig-physics.json',
            'terrain-course-v3-ordinary-debug-ordinary-ten.json',
            *(f'character-motor-debug-cmc-penetration-v2-final-physics-{hz}.json' for hz in (30,60,120)),
            *(f'terrain-course-v3-debug-{hz}.json' for hz in (30,60,120))]
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read = lambda p: json.loads(p.read_bytes())


def save(path, value):
    with path.open('x', encoding='utf-8', newline='\n') as stream:
        stream.write(json.dumps(value, indent=2, allow_nan=False)+'\n')


def freeze():
    before=read(OUT/'air-move-core-v1-before.json')
    assert {n for n,h in before.items() if sha(ROOT/n)!=h}==CHANGED
    save(OUT/f'{TAG}-frozen.json',dict(sources={n:sha(ROOT/n) for n in sorted(CHANGED|NEW)},
        protected={n:h for n,h in before.items() if n not in CHANGED|DOCS},
        previousEvidence={n:sha(OUT/n) for n in PREVIOUS}))
    print('AIR_MOVE_CORE_SOURCES_FROZEN')


def audit():
    frozen=read(OUT/f'{TAG}-frozen.json')
    for group in ('sources','protected'):
        for n,h in frozen[group].items(): assert sha(ROOT/n)==h,n
    for n,h in frozen['previousEvidence'].items(): assert sha(OUT/n)==h,n
    ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    trx=ET.parse(OUT/f'{RUNTIME_TAG}-tests/{RUNTIME_TAG}.trx')
    counters=trx.find('.//t:Counters',ns).attrib
    assert counters['total']==counters['passed']=='40'
    assert counters['failed']==counters['notExecuted']=='0'
    definitions={x.get('id'):x.find('t:TestMethod',ns).get('className').split(',')[0]
                 for x in trx.findall('.//t:UnitTest',ns)}
    assert sum(definitions[x.get('testId')].endswith('.AlsCharacterAirMovementTests')
               for x in trx.findall('.//t:UnitTestResult',ns))==23
    assemblies={};reports={};runs=0
    for suffix,cases in (('ordinary',{'air-30','air-60','air-120','rig-physics','main-rig','als-ordinary','ordinary-ten'}),
                         ('terrain',{'30','60','120'})):
        for config in ('debug','optimize'):
            summary=read(OUT/f'{RUNTIME_TAG}-{suffix}-{config}-verification.json')
            assert summary['passed'] and {r['name'] for r in summary['runs']}==cases
            assemblies[suffix,config]=summary['assemblies']
            if config=='optimize':
                assert summary['debugRestored']
                for p in (OUT/f'{RUNTIME_TAG}-{suffix}-{config}-debug-backup').iterdir():
                    assert sha(p)==sha(ROOT/'.godot/mono/temp/bin/Debug'/p.name)
            for row in summary['runs']:
                assert row['passed'] and row['exitCode']==0
                assert sha(Path(row['log']))==row['logSha256'].lower()
                log=Path(row['log']).read_text(encoding='utf-8-sig')
                assert not re.search(r'^\s*(ERROR|WARNING):',log,re.M)
                if 'report' in row:
                    assert sha(Path(row['report']))==row['reportSha256'].lower()
                    data=read(Path(row['report']));reports[suffix,config,row['name']]=data
                    if row['name'].startswith('air-'):
                        assert data['actualJolt'] and data['moves']==data['retries'] and data['jumpHeightM']>1.24
                        assert data['wallContacts']>0 and data['blockedStandFrames']>0
                    elif row['name']=='rig-physics': assert data['actualGodotPhysics']
                runs+=1
    for config in ('debug','optimize'):
        assert reports['ordinary',config,'ordinary-ten']==read(OUT/'terrain-course-v3-ordinary-debug-ordinary-ten.json')
        assert reports['ordinary',config,'rig-physics']==read(OUT/'rig-runtime-core-v2-ordinary-debug-rig-physics.json')
        for hz in ('30','60','120'):
            assert reports['ordinary',config,'air-'+hz]==read(OUT/f'character-motor-debug-cmc-penetration-v2-final-physics-{hz}.json')
            assert reports['terrain',config,hz]==read(OUT/f'terrain-course-v3-debug-{hz}.json')
        summary=read(OUT/f'character-air-{config}-{RUNTIME_TAG}-verification.json')
        assert summary['diagnosticsCompleted'] and summary['comparisonPassed']
        assert summary['assemblies']==assemblies['ordinary',config]
        if config=='optimize':
            assert summary['debugRestored']
            for p in (OUT/f'character-air-{config}-{RUNTIME_TAG}-debug-backup').iterdir():
                assert sha(p)==sha(ROOT/'.godot/mono/temp/bin/Debug'/p.name)
        for row in summary['runs']:
            assert row['exitCode']==0 and row['comparisonPassed'] and row['mismatches']==0
            for kind in ('log','report'): assert sha(Path(row[kind]))==row[kind+'Sha256'].lower()
            log=Path(row['log']).read_text(encoding='utf-8-sig')
            assert not re.search(r'^\s*(ERROR|WARNING):',log,re.M)
            data=read(Path(row['report']))
            assert data==read(OUT/'character-air-debug-cmc-penetration-v2-final-queries.json')
            assert data['queries']==112 and data['apexSplits']==20 and data['landings']==28 and data['multipleContacts']==6
            runs+=1
    for suffix in ('ordinary','terrain'):
        assert assemblies[suffix,'debug']!=assemblies[suffix,'optimize']
    for config in ('debug','optimize'):
        assert assemblies['ordinary',config]==assemblies['terrain',config]
    for suffix in ('build','optimize-build'):
        log=(OUT/f'{RUNTIME_TAG}-{suffix}.log').read_text(encoding='utf-8-sig')
        assert '0 个警告' in log and '0 个错误' in log
    evidence={p.name:sha(p) for p in OUT.glob(f'*air-move-core-v*') if p.is_file() and p.suffix in ('.log','.json')}
    save(OUT/f'{TAG}-audit.json',dict(passed=True,sources=len(frozen['sources']),protected=len(frozen['protected']),
        coreTests=40,newCoreTests=23,runtimeAndBuildTag=RUNTIME_TAG,godotRuns=runs,airQueries=224,airQueryReportsMatchPrevious=True,
        airPhysicsReportsMatchPrevious=True,ordinaryReportMatchesPrevious=True,terrainReportsMatchPrevious=True,
        rigPhysicsReportMatchesPrevious=True,debugAssembliesRestored=True,nativeWholeWorldParity=False,
        newGpuCapture=False,hardwareKeyboardAccepted=False,goalComplete=False,evidence=evidence))
    print('AIR_MOVE_CORE_AUDIT_OK tests=40 new=23 runs=',runs)


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('mode',choices=('freeze','audit'))
    args=parser.parse_args();(freeze if args.mode=='freeze' else audit)()
