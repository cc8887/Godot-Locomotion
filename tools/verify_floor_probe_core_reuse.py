"""Audit Core floor ownership, actual physics and unchanged native differences."""
from pathlib import Path
import argparse
import hashlib
import json
import re
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'artifacts/lyra-analysis'
TAG='floor-probe-core-v2'
CHANGED={'src/Als.Godot/Locomotion/LyraCharacterFloorProbe.cs',
         'src/Als.Godot/Locomotion/LyraCharacterFloorSettings.cs','src/Als.Godot/Locomotion/LyraCharacterSweep.cs'}
NEW={'src/Als.Core/Locomotion/AlsCharacterFloorProbe.cs','src/Als.Core/Locomotion/AlsCharacterSweepMath.cs',
     'tests/Als.Core.Tests/Locomotion/AlsCharacterFloorProbeTests.cs','tools/verify_floor_probe_core_reuse.py'}
DOCS={'ROADMAP.md','docs/verification/2026-10-03-lyra-als-interface-review.md'}
PREVIOUS=['air-move-core-v3-audit.json','air-move-core-v3-frozen.json',
          'air-move-core-v2-ordinary-debug-ordinary-ten.json','air-move-core-v2-ordinary-debug-rig-physics.json',
          'character-air-debug-air-move-core-v2-queries.json','character-floor-debug-cmc-penetration-v2-final-queries.json',
          *(f'character-floor-debug-cmc-penetration-v2-final-physics-{hz}.json' for hz in (30,60,120)),
          *(f'air-move-core-v2-terrain-debug-{hz}.json' for hz in (30,60,120))]
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
read=lambda p:json.loads(p.read_bytes())


def save(p,value):
    with p.open('x',encoding='utf-8',newline='\n') as f:f.write(json.dumps(value,indent=2,allow_nan=False)+'\n')


def freeze():
    before=read(OUT/'floor-probe-core-v1-before.json')
    assert {n for n,h in before.items() if sha(ROOT/n)!=h}==CHANGED
    save(OUT/f'{TAG}-frozen.json',dict(sources={n:sha(ROOT/n) for n in sorted(CHANGED|NEW)},
        protected={n:h for n,h in before.items() if n not in CHANGED|DOCS},
        previousEvidence={n:sha(OUT/n) for n in PREVIOUS}))
    print('FLOOR_CORE_SOURCES_FROZEN')


def check_row(row,expected_exit=0):
    assert row['exitCode']==expected_exit
    for kind in ('log','report'):
        if kind in row:assert sha(Path(row[kind]))==row[kind+'Sha256'].lower()
    log=Path(row['log']).read_text(encoding='utf-8-sig')
    assert not re.search(r'^\s*(ERROR|WARNING):',log,re.M)
    return read(Path(row['report'])) if 'report' in row else None


def restore(prefix,summary):
    assert summary['debugRestored']
    for p in (OUT/f'{prefix}-debug-backup').iterdir():assert sha(p)==sha(ROOT/'.godot/mono/temp/bin/Debug'/p.name)


def audit():
    frozen=read(OUT/f'{TAG}-frozen.json')
    for group in ('sources','protected'):
        for n,h in frozen[group].items():assert sha(ROOT/n)==h,n
    for n,h in frozen['previousEvidence'].items():assert sha(OUT/n)==h,n
    ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    trx=ET.parse(OUT/f'{TAG}-tests/{TAG}.trx');counts=trx.find('.//t:Counters',ns).attrib
    assert counts['total']==counts['passed']=='76' and counts['failed']==counts['notExecuted']=='0'
    definitions={x.get('id'):x.find('t:TestMethod',ns).get('className').split(',')[0] for x in trx.findall('.//t:UnitTest',ns)}
    assert sum(definitions[x.get('testId')].endswith('.AlsCharacterFloorProbeTests') for x in trx.findall('.//t:UnitTestResult',ns))==32
    runs=0;success=0;assemblies={};floor_queries=[]
    for config in ('debug','optimize'):
        for suffix,cases in (('ordinary',{'rig-physics','main-rig','als-ordinary','ordinary-ten'}),('terrain',{'30','60','120'})):
            prefix=f'{TAG}-{suffix}-{config}';summary=read(OUT/f'{prefix}-verification.json')
            assert summary['passed'] and {r['name'] for r in summary['runs']}==cases
            assemblies[suffix,config]=summary['assemblies']
            if config=='optimize':restore(prefix,summary)
            for row in summary['runs']:
                assert row['passed'];data=check_row(row);runs+=1;success+=1
                if suffix=='terrain':assert data==read(OUT/f'air-move-core-v2-terrain-debug-{row["name"]}.json')
                elif row['name']=='ordinary-ten':assert data==read(OUT/'air-move-core-v2-ordinary-debug-ordinary-ten.json')
                elif row['name']=='rig-physics':assert data==read(OUT/'air-move-core-v2-ordinary-debug-rig-physics.json')
        prefix=f'character-floor-{config}-{TAG}';summary=read(OUT/f'{prefix}-verification.json')
        assert summary['floorPhysicsPassed'] and summary['diagnosticsCompleted'] and not summary['floorQueryComparisonPassed']
        assert len(summary['runs'])==4 and summary['assemblies']==assemblies['ordinary',config]
        if config=='optimize':restore(prefix,summary)
        for row in summary['runs']:
            if row['kind']=='physics':
                assert row['passed'];data=check_row(row)
                assert data['actualJolt'] and data['nativeBand'] and data['blockedHeight'] and data['perchRejected']
                assert data['moves']==data['retries']
                assert data==read(OUT/f'character-floor-debug-cmc-penetration-v2-final-physics-{row["hz"]}.json')
                success+=1
            else:
                assert row['kind']=='query' and not row['comparisonPassed'] and row['mismatches']==3
                data=check_row(row,1);assert data.pop('tag')==TAG
                previous=read(OUT/'character-floor-debug-cmc-penetration-v2-final-queries.json');previous.pop('tag')
                assert data==previous and data['queries']==144 and not data['queryMutatesActor']
                floor_queries.append(data)
            runs+=1
        prefix=f'character-air-{config}-{TAG}';summary=read(OUT/f'{prefix}-verification.json')
        assert summary['diagnosticsCompleted'] and summary['comparisonPassed'] and len(summary['runs'])==1
        assert summary['assemblies']==assemblies['ordinary',config]
        if config=='optimize':restore(prefix,summary)
        for row in summary['runs']:
            assert row['comparisonPassed'] and row['mismatches']==0
            assert check_row(row)==read(OUT/'character-air-debug-air-move-core-v2-queries.json')
            runs+=1;success+=1
    assert floor_queries[0]==floor_queries[1]
    for config in ('debug','optimize'):assert assemblies['ordinary',config]==assemblies['terrain',config]
    assert assemblies['ordinary','debug']!=assemblies['ordinary','optimize']
    for suffix in ('build','optimize-build'):
        log=(OUT/f'{TAG}-{suffix}.log').read_text(encoding='utf-8-sig')
        assert '0 个警告' in log and '0 个错误' in log
    assert runs==24 and success==22
    evidence={p.name:sha(p) for p in OUT.glob(f'*{TAG}*') if p.is_file() and p.suffix in ('.json','.log')}
    save(OUT/f'{TAG}-audit.json',dict(passed=True,sources=len(frozen['sources']),protected=len(frozen['protected']),
        coreTests=76,newCoreTests=32,godotProcesses=runs,successfulGodotProcesses=success,expectedDifferenceDiagnostics=2,
        nativeFloorQueriesPerBuild=144,nativeFloorMismatchesPerBuild=3,nativeFloorDifferencesUnchanged=True,
        floorReportsMatchPrevious=True,airReportsMatchPrevious=True,ordinaryReportMatchesPrevious=True,
        terrainReportsMatchPrevious=True,rigPhysicsReportMatchesPrevious=True,debugAssembliesRestored=True,
        nativeWholeWorldParity=False,newGpuCapture=False,hardwareKeyboardAccepted=False,goalComplete=False,evidence=evidence))
    print('FLOOR_CORE_AUDIT_OK tests=76 new=32 success=',success,'diagnostics=',runs-success)


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('mode',choices=('freeze','audit'));args=parser.parse_args()
    (freeze if args.mode=='freeze' else audit)()
