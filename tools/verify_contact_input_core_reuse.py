"""Audit consumed input and contact velocity Core ownership with actual Godot operators and physical root moves."""
from pathlib import Path
import argparse
import hashlib
import json
import re
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'artifacts/lyra-analysis'
TAG='contact-input-core-v1'
CHANGED={'src/Als.Godot/Locomotion/LyraSceneMovementService.cs','src/Als.Godot/Locomotion/LyraRootMovementMotor.cs',
         'src/Als.Core/Locomotion/AlsCharacterSweepMath.cs','src/Als.Core/Locomotion/AlsCharacterGroundMovement.cs',
         'src/Als.Godot/Animation/Lyra/LyraRootMovementPhysicsSmoke.cs'}
NEW={'src/Als.Core/Locomotion/AlsCharacterInput.cs','src/Als.Core/Locomotion/AlsCharacterContactVelocity.cs',
     'tests/Als.Core.Tests/Locomotion/AlsCharacterInputContactTests.cs',
     'src/Als.Godot/Locomotion/LyraCharacterInputContactSmoke.cs','scenes/tests/lyra_character_input_contact_smoke.tscn',
     'tools/verify_contact_input_core_reuse.py'}
DOCS={'ROADMAP.md','docs/verification/2026-10-03-lyra-als-interface-review.md'}
PREVIOUS=['motion-step-core-v2-final-audit.json','motion-step-core-v2-frozen.json',
          'startup-runtime-v5-debug-ordinary-emote.json','startup-runtime-v5-optimize-ordinary-emote.json',
          'floor-probe-core-v2-ordinary-debug-ordinary-ten.json','floor-probe-core-v2-ordinary-debug-rig-physics.json',
          'character-air-debug-floor-probe-core-v2-queries.json','character-floor-debug-floor-probe-core-v2-queries.json',
          'character-ground-debug-cmc-penetration-v2-final-queries.json',
          *(f'character-motor-debug-cmc-penetration-v2-final-physics-{hz}.json' for hz in (30,60,120)),
          *(f'character-ground-debug-cmc-penetration-v2-final-steps-{hz}.json' for hz in (30,60,120)),
          *(f'character-floor-debug-floor-probe-core-v2-physics-{hz}.json' for hz in (30,60,120)),
          *(f'floor-probe-core-v2-terrain-debug-{hz}.json' for hz in (30,60,120))]
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
read=lambda p:json.loads(p.read_bytes())


def save(p,value):
    with p.open('x',encoding='utf-8',newline='\n') as f:f.write(json.dumps(value,indent=2,allow_nan=False)+'\n')


def freeze():
    before=read(OUT/f'{TAG}-before.json')
    assert {n for n,h in before.items() if sha(ROOT/n)!=h}==CHANGED
    save(OUT/f'{TAG}-frozen.json',dict(sources={n:sha(ROOT/n) for n in sorted(CHANGED|NEW)},
        protected={n:h for n,h in before.items() if n not in CHANGED|DOCS},
        previousEvidence={n:sha(OUT/n) for n in PREVIOUS}))
    print('CONTACT_INPUT_CORE_SOURCES_FROZEN')


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
    assert counts['total']==counts['passed']=='166' and counts['failed']==counts['notExecuted']=='0'
    definitions={x.get('id'):x.find('t:TestMethod',ns).get('className').split(',')[0] for x in trx.findall('.//t:UnitTest',ns)}
    classes=[definitions[x.get('testId')].split('.')[-1] for x in trx.findall('.//t:UnitTestResult',ns)]
    assert classes.count('AlsCharacterInputContactTests')==24 and classes.count('AlsCharacterMotionTests')==26 and classes.count('AlsCharacterSweepTests')==21 and classes.count('AlsCharacterCrouchTests')==19
    runs=0;success=0;assemblies={};differences={}
    for config in ('debug','optimize'):
        for suffix,cases in (('ordinary',{'rig-physics','main-rig','als-ordinary','ordinary-ten','air-30','air-60','air-120'}),
                             ('terrain',{'30','60','120'})):
            prefix=f'{TAG}-{suffix}-{config}';summary=read(OUT/f'{prefix}-verification.json')
            assert summary['passed'] and {r['name'] for r in summary['runs']}==cases
            assemblies[suffix,config]=summary['assemblies']
            if config=='optimize':restore(prefix,summary)
            for row in summary['runs']:
                assert row['passed'];data=check_row(row);runs+=1;success+=1
                if suffix=='terrain':assert data==read(OUT/f'floor-probe-core-v2-terrain-debug-{row["name"]}.json')
                elif row['name']=='ordinary-ten':assert data==read(OUT/'floor-probe-core-v2-ordinary-debug-ordinary-ten.json')
                elif row['name']=='rig-physics':assert data==read(OUT/'floor-probe-core-v2-ordinary-debug-rig-physics.json')
                elif row['name'].startswith('air-'):
                    assert data['actualJolt'] and data['blockedStandFrames']>0 and data['releasedStandFrames']>0 and data['moves']==data['retries']
                    assert data==read(OUT/f'character-motor-debug-cmc-penetration-v2-final-physics-{row["name"][4:]}.json')
        for scope,kind,mismatch,count in [('ground','steps',8,32),('floor','physics',3,144)]:
            prefix=f'character-{scope}-{config}-{TAG}';summary=read(OUT/f'{prefix}-verification.json')
            assert summary['stepPhysicsPassed' if scope=='ground' else 'floorPhysicsPassed'] and summary['diagnosticsCompleted']
            assert not summary['groundQueryComparisonPassed' if scope=='ground' else 'floorQueryComparisonPassed']
            assert len(summary['runs'])==4 and summary['assemblies']==assemblies['ordinary',config]
            if config=='optimize':restore(prefix,summary)
            for row in summary['runs']:
                if row['kind']==kind:
                    assert row['passed'];data=check_row(row)
                    assert data['actualJolt'] and data['moves']==data['retries']
                    previous=f'character-ground-debug-cmc-penetration-v2-final-steps-{row["hz"]}.json' if scope=='ground' else f'character-floor-debug-floor-probe-core-v2-physics-{row["hz"]}.json'
                    assert data==read(OUT/previous);success+=1
                else:
                    assert row['kind']=='query' and not row['comparisonPassed'] and row['mismatches']==mismatch
                    data=check_row(row,1)
                    previous=read(OUT/('character-ground-debug-cmc-penetration-v2-final-queries.json' if scope=='ground' else 'character-floor-debug-floor-probe-core-v2-queries.json'))
                    if scope=='floor':assert data.pop('tag')==TAG;previous.pop('tag')
                    assert data==previous and data['queries']==count
                    differences[scope,config]=data
                runs+=1
        prefix=f'character-air-{config}-{TAG}';summary=read(OUT/f'{prefix}-verification.json')
        assert summary['diagnosticsCompleted'] and summary['comparisonPassed'] and len(summary['runs'])==1
        assert summary['assemblies']==assemblies['ordinary',config]
        if config=='optimize':restore(prefix,summary)
        for row in summary['runs']:
            assert row['comparisonPassed'] and row['mismatches']==0
            assert check_row(row)==read(OUT/'character-air-debug-floor-probe-core-v2-queries.json');runs+=1;success+=1
    for scope in ('ground','floor'):assert differences[scope,'debug']==differences[scope,'optimize']
    for config in ('debug','optimize'):assert assemblies['ordinary',config]==assemblies['terrain',config]
    assert assemblies['ordinary','debug']!=assemblies['ordinary','optimize']
    for config in ('debug','optimize'):
        prefix=f'{TAG}-extra-{config}';summary=read(OUT/f'{prefix}-verification.json')
        assert summary['passed'] and {r['name'] for r in summary['runs']}=={'input-operators','root-30','root-60','root-120','ordinary-emote'}
        assert summary['assemblies']==assemblies['ordinary',config]
        if config=='optimize':restore(prefix,summary)
        for row in summary['runs']:
            assert row['passed'];data=check_row(row);runs+=1;success+=1
            log=Path(row['log']).read_text(encoding='utf-8-sig')
            if row['name']=='input-operators':
                assert 'LYRA_INPUT_CONTACT_CORE_GODOT_OK inputs=4096 rotations=512 contacts=1024 bitMismatches=0 actualGodotOperators=true' in log
            elif row['name'].startswith('root-'):
                hz=int(row['name'][5:]);assert f'LYRA_ROOT_MOVEMENT_PHYSICS_GODOT_OK hz={hz} roles=6 frames={hz*8} moves={hz*48}' in log
                assert 'LYRA_ROOT_MOVEMENT_CONTACT_VELOCITY_OK cases=5 finalVelocity=true actualJoltContacts=true' in log
                assert 'LYRA_ROOT_MOVEMENT_LATE_FRAME_OK rejected=1 moves=0' in log
            else:
                assert data==read(OUT/f'startup-runtime-v5-{config}-ordinary-emote.json')
                model=data['model']['model']['model'];assert model['rootCapsuleMoves']==1 and model['capsuleMoves']==480 and model['actualGodotPhysics']
    for n,h in read(OUT/f'{TAG}-reference-sources.json').items():assert sha(Path(n))==h,n
    for suffix in ('build','optimize-build'):
        log=(OUT/f'{TAG}-{suffix}.log').read_text(encoding='utf-8-sig');assert '0 个警告' in log and '0 个错误' in log
    assert runs==48 and success==44
    evidence={p.name:sha(p) for p in OUT.glob(f'*{TAG}*') if p.is_file() and p.suffix in ('.json','.log')}
    save(OUT/f'{TAG}-audit.json',dict(passed=True,sources=len(frozen['sources']),protected=len(frozen['protected']),
        coreTests=166,newInputContactTests=24,godotProcesses=runs,successfulGodotProcesses=success,
        expectedDifferenceDiagnostics=4,nativeGroundMismatchesPerBuild=8,nativeFloorMismatchesPerBuild=3,
        nativeDifferencesUnchanged=True,airReportsMatchPrevious=True,groundAndFloorReportsMatchPrevious=True,
        ordinaryReportMatchesPrevious=True,terrainReportsMatchPrevious=True,rigPhysicsReportMatchesPrevious=True,
        debugAssembliesRestored=True,optimizeRestoreGroups=6,actualGodotOperatorBitMismatches=0,physicalRootRates=[30,60,120],nativeWholeWorldParity=False,newGpuCapture=False,hardwareKeyboardAccepted=False,
        goalComplete=False,evidence=evidence))
    print('CONTACT_INPUT_CORE_AUDIT_OK tests=166 new=24 success=',success,'diagnostics=',runs-success)


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('mode',choices=('freeze','audit'));args=parser.parse_args()
    (freeze if args.mode=='freeze' else audit)()
