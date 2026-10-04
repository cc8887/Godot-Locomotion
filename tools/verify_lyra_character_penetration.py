"""Independently audit actual PhysFalling results and retained strict failures."""
import hashlib
import json
import math
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
E=ROOT/'artifacts/lyra-analysis'
ASSETS=ROOT/'assets/generated/lyra_als'
PROJECT=Path('../GASP58')
TAG='cmc-penetration-v2-final'
OUT=E/'character-penetration-v2-integrity.json'
assert not OUT.exists(),'Preserve air audit'
def load(path):return json.loads(path.read_bytes())
def sha(path):
    h=hashlib.sha256()
    with path.open('rb') as s:
        for block in iter(lambda:s.read(1024*1024),b''):h.update(block)
    return h.hexdigest()
def text(path):
    b=path.read_bytes()
    return b.decode('utf-16' if b.startswith((b'\xff\xfe',b'\xfe\xff')) else 'utf-8-sig')
def distance(a,b):return math.sqrt(sum((x-y)**2 for x,y in zip(a,b,strict=True)))

closure_path=E/'whole-main-cmc60-penetration-v2-closure.json'
closure=load(closure_path)
for rel,digest in closure['previousFixtureSha256'].items():assert sha(ASSETS/rel)==digest,rel
for group in ('protectedProject','movementSourceSha256'):
    for rel,digest in closure[group].items():assert sha(PROJECT/rel)==digest,rel
for name,digest in closure['assetSha256'].items():
    path=name.split('.')[0]
    original=PROJECT/('Content/'+path.removeprefix('/Game/')+'.uasset' if path.startswith('/Game/') else
        'Plugins/GameFeatures/ShooterCore/Content/'+path.removeprefix('/ShooterCore/')+'.uasset')
    assert sha(original)==digest,name
probe=ROOT/'tools/unreal/LyraWholeMainOracle'
package=ROOT/'artifacts/unreal/lyra-whole-main-oracle/package-cmc-penetration-v2'
for rel,digest in closure['probeSourceSha256'].items():assert sha(probe/rel)==sha(package/rel)==digest,rel
assert not (PROJECT/'Plugins/LyraWholeMainOracle').exists()
native_path=E/'whole-main-cmc60-penetration-v2-native.json'
native,old=load(native_path),load(E/'whole-main-cmc60-ground-sweep-v1-native.json')
reference_path=E/'character-air-v3-reference.json'
reference=load(reference_path)
assert reference['actualOriginalPhysFalling'] and not reference['rootMotionCases']
assert reference['evidenceSha256']==sha(native_path) and reference['closureSha256']==sha(closure_path)
assert len(reference['rows'])==112
policy=load(ASSETS/'character_penetration_v1.json')
assert policy['profile']==native['traces'][0]['penetrationProfile']
assert policy['evidenceSha256']==sha(E/'whole-main-cmc60-penetration-v1-native.json')
assert policy['closureSha256']==sha(E/'whole-main-cmc60-penetration-v1-closure.json')
assert policy['baseMotorSha256']==sha(ASSETS/'character_motor_v2.json')
for trace,prior in zip(native['traces'],old['traces'],strict=True):
    assert trace['penetrationProfile']==policy['profile']
    assert trace['airPhysicalKernel']==reference['rows'] and len(trace['frames'])==60
    assert [r for r in trace['airPhysicalKernel'] if r['name'] not in ('wall-penetration','corner-penetration')]==load(E/'whole-main-cmc60-air-v2-native.json')['traces'][0]['airPhysicalKernel']
    for key in ('motorProfile','velocityKernel','fallingKernel','floorKernel','movementKernel'):assert trace[key]==prior[key],key
    assert [f['physicalInput'] for f in trace['frames']]==[f['physicalInput'] for f in prior['frames']]
previous=load(E/'character-floor-v1-integrity.json')
for name,digest in previous['unchangedSources'].items():
    if name not in ('src/Als.Core/Locomotion/AlsCharacterFalling.cs','src/Als.Godot/Locomotion/LyraCharacterMovementSettings.cs'):
        assert sha(ROOT/name)==digest,name
assert sha(ASSETS/'character_floor_v1.json')==previous['floorResourceSha256']
assert sha(ASSETS/'character_motor_v2.json')==previous['motorResourceSha256']

reports=[];air_stats=[];trajectory_stats=[]
for config,directory in [('debug','Debug'),('optimize','ExportRelease')]:
    summaries={kind:load(E/f'character-{kind}-{config}-{TAG}-verification.json') for kind in ('motor','floor','ground','air','trajectory')}
    assemblies=summaries['motor']['assemblies']
    assert all(s['assemblies']==assemblies for s in summaries.values())
    assert summaries['motor']['passed'] and len(summaries['motor']['runs'])==9
    assert summaries['floor']['floorPhysicsPassed'] and summaries['floor']['diagnosticsCompleted']
    assert summaries['ground']['stepPhysicsPassed'] and summaries['ground']['diagnosticsCompleted']
    assert summaries['air']['diagnosticsCompleted'] and summaries['trajectory']['diagnosticsCompleted']
    for name,digest in assemblies.items():assert sha(ROOT/f'.godot/mono/temp/bin/{directory}'/name)==digest.lower()
    for kind,summary in summaries.items():
        if config=='optimize':
            assert summary['debugRestored']
            for name,digest in load(E/f'character-{kind}-debug-{TAG}-verification.json')['assemblies'].items():
                assert sha(E/f'character-{kind}-optimize-{TAG}-debug-backup'/name)==digest.lower()
        for run in summary['runs']:
            assert sha(Path(run['log']))==run['logSha256'].lower()
            assert not any(l.lstrip().startswith(('ERROR:','WARNING:')) for l in text(Path(run['log'])).splitlines())
            if 'report' in run:assert sha(Path(run['report']))==run['reportSha256'].lower()
            if 'passed' in run:assert run['passed'] and run['exitCode']==0
            else:assert run['exitCode']==(0 if run['comparisonPassed'] else 1)
        reports.append(dict(configuration=config,kind=kind,sha256=sha(E/f'character-{kind}-{config}-{TAG}-verification.json')))
    air=load(E/f'character-air-{config}-{TAG}-queries.json')
    mismatches=apex=landings=multi=0
    for row,ref in zip(air['rows'],reference['rows'],strict=True):
        assert row['native']==ref
        a=row['actual'];p=distance(a['position'],ref['after']);v=distance(a['velocity'],ref['output'])
        flags=a['grounded']==ref['grounded'] and a['ApexSplits']==ref['apexAttempts'] and a['randomSeedAfter']==ref['randomSeedAfter']
        match=p<=.01 and v<=.001 and flags
        assert abs(row['p']-p)<1e-12 and abs(row['v']-v)<1e-12 and row['flags']==flags and row['match']==match
        mismatches+=not match;apex+=a['ApexSplits'];landings+=a['grounded'];multi+=a['contacts']>1
    assert air['queries']==112 and mismatches==air['mismatches']
    assert apex==air['apexSplits']==20 and landings==air['landings'] and multi==air['multipleContacts'] and multi>0
    assert max(r['p'] for r in air['rows'])==air['maxPositionCm'] and max(r['v'] for r in air['rows'])==air['maxVelocityCmps']
    assert air['comparisonPassed']==(mismatches==0) and not air['completeAcceptance']
    assert air['comparisonPassed'] and mismatches==0
    assert sum(r['actual']['recovery']['teleport'] for r in air['rows'])==16
    assert sum(r['actual']['recovery']['combined'] for r in air['rows'])==8
    air_stats.append({k:v for k,v in air.items() if k!='rows'}|dict(configuration=config))
    for kind in ('floor','ground'):
        data=load(E/f'character-{kind}-{config}-{TAG}-queries.json')
        assert data['mismatches']==(3 if kind=='floor' else 8) and not data['comparisonPassed']
        prior_query=load(E/f'character-{kind}-{config}-cmc-ground-v1-final3-queries.json')
        assert {k:v for k,v in data.items() if k!='tag'}=={k:v for k,v in prior_query.items() if k!='tag'}
        if config=='optimize':assert data==load(E/f'character-{kind}-debug-{TAG}-queries.json')
    trajectory_reference=load(E/'character-motor-trajectory-v1-reference.json')
    for hz in (30,60,120):
        data=load(E/f'character-trajectory-{config}-{TAG}-{hz}.json')
        trace=next(t for t in trajectory_reference['traces'] if t['hz']==hz)
        assert data['frames']==data['moves']==data['retries']==hz*8 and not data['replayedPhysicalObservations']
        count=ground=stance=0;maxima=dict(p=0,xy=0,z=0,v=0,a=0)
        for row,ref in zip(data['rows'],trace['frames'],strict=True):
            assert row['native']==ref['physical'] and row['control']==ref['control']
            a,n=row['actual'],ref['physical'];diff=[x-y for x,y in zip(a['location'],n['location'],strict=True)]
            values=dict(p=distance(a['location'],n['location']),xy=math.hypot(*diff[:2]),z=abs(diff[2]),v=distance(a['velocity'],n['velocity']),a=distance(a['acceleration'],n['acceleration']))
            flags=a['Ground']==n['ground'] and a['Crouching']==n['crouching']
            match=values['p']<=.01 and values['v']<=.001 and values['a']<=.001 and flags
            for k,v in values.items():assert abs(row[k]-v)<1e-9;maxima[k]=max(maxima[k],v)
            assert row['match']==match;count+=not match;ground+=a['Ground']!=n['ground'];stance+=a['Crouching']!=n['crouching']
        assert count==data['mismatchFrames'] and ground==data['groundMismatchFrames'] and stance==data['stanceMismatchFrames']
        assert data['comparisonPassed']==(count==0) and not data['completeAcceptance']
        assert ground==stance==0 and maxima['a']==0
        trajectory_stats.append(dict(configuration=config,hz=hz,frames=data['frames'],mismatches=count,maxima=maxima))
        if config=='optimize':assert data==load(E/f'character-trajectory-debug-{TAG}-{hz}.json')
    if config=='optimize':
        assert air==load(E/f'character-air-debug-{TAG}-queries.json')
        for hz in (30,60,120):
            for kind,case in (('motor','physics'),('motor','ordinary'),('floor','physics'),('ground','steps')):
                assert load(E/f'character-{kind}-optimize-{TAG}-{case}-{hz}.json')==load(E/f'character-{kind}-debug-{TAG}-{case}-{hz}.json')
        for case in ('warp-60','emote-60'):
            assert load(E/f'character-motor-optimize-{TAG}-{case}.json')==load(E/f'character-motor-debug-{TAG}-{case}.json')
for configuration in ('debug','optimize'):
    log=text(E/('character-penetration-v2-first-build.log' if configuration=='debug' else 'character-penetration-v2-final-optimize-build.log'))
    assert '已成功生成' in log and '0 个错误' in log and '0 个警告' in log
sources=[f'src/Als.Godot/Locomotion/{n}.cs' for n in ('LyraCharacterAirMovement','LyraCharacterAirQuerySmoke','LyraCharacterSweep','LyraCharacterMovementSettings','LyraCharacterFloorSettings','LyraRootMovementMotor','LyraSceneMovementService','LyraCharacterTrajectorySmoke','LyraCharacterMovementPhysicsSmoke')]
sources+=['src/Als.Godot/Locomotion/LyraCharacterPenetrationSettings.cs','src/Als.Godot/Locomotion/LyraCharacterGroundMovement.cs','tools/export_lyra_character_penetration.py','tools/export_lyra_character_air_v3.py','tools/verify_lyra_character_penetration.py','src/Als.Core/Locomotion/AlsCharacterFalling.cs','src/Als.Godot/Animation/Lyra/LyraCharacterAnimation.cs','scripts/verify-lyra-character-air.ps1','tools/export_lyra_character_air.py','tools/verify_lyra_character_air.py','scenes/tests/lyra_character_air_query_smoke.tscn']
result=dict(auditPassed=True,nativeQueries=112,nativeProviders=3,originalPhysicalPrefixUnchanged=True,
    airQueryComparisonPassed=True,sceneRegressionProcesses=18,floorPhysicsProcesses=6,stepPhysicsProcesses=6,air=air_stats,trajectory=trajectory_stats,
    protectedJson=len(closure['previousFixtureSha256']),protectedPackages=len(closure['assetSha256']),protectedConfig=len(closure['protectedProject']),protectedMovementSources=len(closure['movementSourceSha256']),
    penetrationResourceSha256=sha(ASSETS/'character_penetration_v1.json'),nativeCaptureSha256=sha(native_path),closureSha256=sha(closure_path),referenceSha256=sha(reference_path),reports=reports,sources={s:sha(ROOT/s) for s in sources},
    nativeWorldTrajectoryParity=False,rootMotionAirAcceptance=False,completeAcceptance=False,goalComplete=False)
with OUT.open('x',encoding='utf-8',newline='\n') as s:json.dump(result,s,separators=(',',':'))
print('CHARACTER_PENETRATION_AUDIT_OK nativeCases=112 sceneProcesses=30 debugOptimizeEqual=true retainedParityFailures=true')
