"""Audit actual skeletal phases, immutable resources and the character matrix."""
from pathlib import Path
from locomotion_paths import engine_path, project_path

import hashlib
import json
import re
import sys
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[1]
out = repo / 'artifacts/lyra-analysis'
tag = 'skeletal-initialize-v6'
native_tag_base = 'skeletal-initialize-v2'
read = lambda p: json.loads(p.read_bytes())
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()


def log(p):
    b = p.read_bytes()
    return b.decode('utf-16') if b.startswith((b'\xff\xfe', b'\xfe\xff')) else b.decode('utf-8-sig')


sources = read(out / f'{tag}-frozen-sources.json')
assert len(sources) == 24
for p, digest in sources.items():
    assert sha(repo / p) == digest, p
previous = read(out / 'skeletal-initialize-v1-before.json')
protected_sources = 0
for p, digest in previous.items():
    if p not in sources:
        assert sha(repo / p) == digest, p
        protected_sources += 1
assert protected_sources == 1474

for kind in ('requests', 'native', 'closure'):
    assert (out / f'{native_tag_base}-{kind}.json').read_bytes() == (out / f'{native_tag_base}-repeat-{kind}.json').read_bytes(), kind
closure = read(out / f'{native_tag_base}-closure.json')
warnings = {}
for native_tag in (native_tag_base, f'{native_tag_base}-repeat'):
    assert sha(out / f'{native_tag}-requests.json') == closure['requestSha256']
    assert sha(out / f'{native_tag}-native.json') == closure['nativeSha256']
    text = log(out / f'{native_tag}-native.log')
    assert 'LYRA_SKELETAL_INITIALIZE_NATIVE_OK profiles=3 nodes=24 seeded_rows=48 als=81 assets_saved=0' in text
    assert 'LYRA_SKELETAL_INITIALIZE_PROCESS_EXIT=0' in text
    assert not re.search(r'Error:|Fatal error:|Ensure condition failed', text)
    warnings[native_tag] = len(re.findall('Warning:', text))
assert set(warnings.values()) == {744}
scope = closure['scope']
assert scope['originalSkeletalInitialize'] and scope['seededReinitialization'] and scope['originalCacheBones'] and scope['fixedAls81']
assert not scope['sourceUpdateOrEvaluate'] and not scope['privateFootLegStorageCompared'] and not scope['fullRequiredBonesLOD'] and scope['assetsSaved'] == 0
native = read(out / f'{native_tag_base}-native.json')
assert [c['profile'] for c in native['cases']] == ['unarmed', 'pistol', 'rifle']
control_nodes = [103,102,104,110,109,105,107,106]
for case in native['cases']:
    assert not case['sourceUpdate'] and not case['poseEvaluate'] and len(case['reference']) == 81
    assert case['reference'] == native['cases'][0]['reference']
    assert [r['round'] for r in case['rounds']] == [0,1]
    for row in case['rounds']:
        before,after = row['before'],row['after']
        assert after == row['cached']
        assert [n['node'] for n in after['nodes']] == control_nodes
        for b,a in zip(before['nodes'],after['nodes'],strict=True):
            assert b['clampInitialized'] and not a['clampInitialized'] and b['clampValue'] == a['clampValue'] and not a['clampInterp']
            assert b['alpha'] == a['alpha'] and b['bool']['initialized'] and not a['bool']['initialized']
            assert {k:v for k,v in b['bool'].items() if k != 'initialized'} == {k:v for k,v in a['bool'].items() if k != 'initialized'}
        assert [n['alphaType'] for n in after['nodes']] == [2,0,1,0,0,1,2,2]
        bf,af = before['foot'],after['foot']
        assert not bf['first'] and af['first'] and af['pelvisOffset'] == [0,0,0]
        for key in ('delta','counter','component','componentDelta','groundNormal','groundSpring','onGround','root'):
            assert bf[key] == af[key], key
        assert af['delta'] == (.03125 if row['round'] == 0 else .375) and af['counter'] == (25 if row['round'] == 0 else 32767)
        for spring in [af['pelvisSpring'], *(s for leg in af['legs'] for s in leg.values())]:
            assert not spring['valid'] and spring['velocity'] == (0 if isinstance(spring['velocity'],(float,int)) else [0,0,0])
        assert not before['leg']['proxyBound'] and after['leg']['proxyBound']
        assert before['leg']['legs'] == after['leg']['legs']
        for leg in after['leg']['legs']:
            assert leg['links'] == 3 and leg['real'] == [.25,.5,.75] and leg['base'] == [-.25,-.5,-.75]
        assert len(row['footLengths']) == 4 and all(v > 0 for v in row['footLengths'])

engine = engine_path('Engine')
ledger = read(out / 'skeletal-initialize-v2-engine-sources.json')
assert ledger == closure['engineSourceSha256'] and len(ledger) == 23
for p,digest in ledger.items():
    assert sha(engine / p) == sha(out / 'skeletal-initialize-v2-engine-source' / p) == digest, p
package = repo / 'artifacts/unreal/lyra-whole-main-oracle/package-skeletal-initialize-v4'
assert len(closure['probeSourceSha256']) == 10
for p,digest in closure['probeSourceSha256'].items():
    assert sha(repo / 'tools/unreal/LyraSkeletalInitializeOracle' / p) == sha(package / p) == digest, p
assert 'Result: Succeeded' in log(out / 'whole-main-build-package-skeletal-initialize-v4.log')
assert read(package / 'Binaries/Win64/UnrealEditor.modules')['BuildId'] == read(project_path('Binaries/Win64/UnrealEditor.modules'))['BuildId']
assets = repo / 'assets/generated/lyra_als'
assert {p.relative_to(assets).as_posix():sha(p) for p in assets.rglob('*.json')} == closure['previousFixtureSha256']
assert len(closure['previousFixtureSha256']) == 870
for p,digest in read(assets / 'logical_controls/calibration.json')['assetSha256'].items():
    assert closure['assetSha256'][p] == digest
project = project_path()
assert not (project / 'Plugins/LyraWholeMainOracle').exists()
for p,digest in closure['protectedProject'].items():
    assert sha(project / p) == digest, p
assert len(closure['protectedProject']) == 9
for p,digest in closure['assetSha256'].items():
    path = p.split('.')[0]
    if path.startswith('/Game/'):
        file = project / 'Content' / (path.removeprefix('/Game/') + '.uasset')
    elif path.startswith('/ShooterCore/'):
        file = project / 'Plugins/GameFeatures/ShooterCore/Content' / (path.removeprefix('/ShooterCore/') + '.uasset')
    else:
        raise ValueError(path)
    assert sha(file) == digest, p
assert len(closure['assetSha256']) == 710
for config in ('debug','optimize'):
    text = log(out / f'{tag}-build-{config}.log')
    assert re.search(r'^\s*0\s*(个警告|Warning)', text, re.M)
    assert re.search(r'^\s*0\s*(个错误|Error)', text, re.M)
ns = {'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
tests = ET.parse(out / f'{tag}-core.trx').find('.//t:Counters',ns).attrib
assert tests['total'] == tests['passed'] == '163' and tests['failed'] == tests['notExecuted'] == '0'
if '--preflight' in sys.argv:
    print('LYRA_SKELETAL_INITIALIZE_PREFLIGHT_OK native=2 nodes=24 seededRows=48 core=163 sources=24 resources=870 runtimeMatrixPending=true')
    sys.exit(0)
# Failed runs remain reviewable against their original source and binary manifests.
failed_runs = {}
for failed in ('skeletal-initialize-v4','skeletal-initialize-v5'):
    archived = out / f'{failed}-superseded-sources'
    frozen = read(out / f'{failed}-frozen-sources.json')
    assert len(frozen) == 24
    for p,digest in frozen.items():
        assert sha(archived / p) == digest,p
    report = read(out / f'{failed}-debug-verification.json')
    assert not report['passed'] and any(not row['passed'] for row in report['runs'])
    for name,digest in report['assemblies'].items():
        assert sha(out / f'{failed}-superseded-debug' / name).upper() == digest
    assert len(list((out / f'{failed}-superseded-exportrelease').iterdir())) == 6
    for row in report['runs']:
        assert sha(Path(row['log'])).upper() == row['logSha256']
    failed_runs[failed] = dict(sourceCount=24,debugAssemblies=6,exportReleaseAssemblies=6,reportSha256=sha(out / f'{failed}-debug-verification.json'))

frequencies = {'30':30, '60':60, '120':120, 'three':60, 'mixed':60, 'per-call':60}
expected_names = ['main-pose', 'main-pose-feedback', 'skeletal-initialize', 'skeletal-update', 'foot-placement', 'leg-ik', 'skeletal-controls', 'main-skeletal', 'main-rig', 'main-composition'] + ['blendspace-initialize','aiming','main-lean','main-lean-composition','main-start-lean','main-cycle-lean','source-initialize','start-source','stop-source','pivot-source','cycle-source','idle-source','air-source','additives-source','left-source','phases'] + [f'{kind}-{suffix}' for kind in ('initial','unlink') for suffix in frequencies]
expected_names += ['main-default','routes','named','weapon','live','ordinary-ten','ordinary-emote']
counts, reports, ordinary = {}, {}, {}
processes = 0
for config in ('debug','optimize'):
    report = read(out / f'{tag}-{config}-verification.json')
    assert report['passed'] and report['fullMainUnlink'] and not report['goalComplete']
    assert [r['name'] for r in report['runs']] == expected_names
    assert report['debugRestored'] == (config == 'optimize')
    values = {}
    for row in report['runs']:
        path = Path(row['log']); text = log(path)
        assert sha(path) == row['logSha256'].lower() and row['exitCode'] == 0 and row['passed'], row['name']
        assert 'LYRA_DEFAULT_ROUTES_EXIT=0' in text and not re.search(r'^\s*(ERROR|WARNING):', text, re.M)
        if row['name'] == 'skeletal-initialize':
            assert 'LYRA_SKELETAL_INITIALIZE_NATIVE_GODOT_OK profiles=3 nodes=24 seededRows=48 cacheBoneRefs=162 repeatedCache=6 pendingRejected=12 hiddenRetry=6 deferredRejected=24 als=81 sourceUpdate=false evaluate=false privateFootLegStorageCompared=false' in text
        skeletal_markers = {
            'skeletal-update':'LYRA_SKELETAL_UPDATE_GODOT_OK frames=3780 poses=3051 footEvaluations=1626',
            'foot-placement':'LYRA_FOOT_PLACEMENT_GODOT_OK frames=3780 poses=3231',
            'leg-ik':'LYRA_LEG_IK_GODOT_OK frames=3780 retries=3780',
            'skeletal-controls':'LYRA_SKELETAL_CONTROLS_GODOT_OK frames=3780 poses=3051',
            'main-skeletal':'LYRA_MAIN_SKELETAL_SCOPE_GODOT_OK frames=11340 poses=9762',
            'main-rig':'LYRA_MAIN_RIG_HOST_GODOT_OK frames=7560',
            'main-composition':'LYRA_MAIN_COMPOSITION_SCOPE_GODOT_OK frames=11340 poses=9762',
            'main-pose':'LYRA_MAIN_POSE_HOST_GODOT_OK frames=11340 poses=9762',
            'main-pose-feedback':'LYRA_MAIN_POSE_HOST_FEEDBACK_GODOT_OK frames=11340 poses=9762'}
        if row['name'] in skeletal_markers:
            assert skeletal_markers[row['name']] in text,row['name']
        if row['name'] == 'source-initialize':
            assert 'LYRA_SEQUENCE_INITIALIZE_NATIVE_GODOT_OK profiles=3 nodes=78 seededRows=156 retry=3 deferredHip=3 pendingRejected=3 sourceUpdateDuringInitialize=false evaluate=false fullWeightFieldCompared=false' in text
        if row['name'] == 'blendspace-initialize':
            assert 'LYRA_BLENDSPACE_INITIALIZE_NATIVE_GODOT_OK profiles=3 nodes=15 seededRows=30 pendingRejected=6 hiddenRetry=6 sourceUpdateDuringInitialize=false evaluate=false privateFilterHistoryCompared=false' in text
        component_markers={
            'aiming':'LYRA_AIMING_LAYER_GODOT_OK frames=7560 poses=6075 samples=26917',
            'main-lean':'LYRA_MAIN_LEAN_RUNTIME_GODOT_OK frames=2100 ticks=2121 sampleTicks=6155',
            'main-lean-composition':'LYRA_MAIN_LEAN_COMPOSITION_GODOT_OK frames=2100 ticks=2121 bones=171801',
            'main-start-lean':'LYRA_MAIN_START_LEAN_GODOT_OK frames=3780 poses=3672 bones=297432',
            'main-cycle-lean':'LYRA_MAIN_CYCLE_LEAN_GODOT_OK frames=3780 poseFrames=3528'}
        if row['name'] in component_markers:
            assert component_markers[row['name']] in text,row['name']
        if row['name'] == 'phases':
            assert 'LYRA_GRAPH_PHASES_NATIVE_GODOT_OK profiles=3 cacheSteps=30 machines=15 sourceUpdate=false evaluate=false layout=81 lifecycle=persistent' in text
        kind, _, suffix = row['name'].partition('-')
        if kind in ('initial','unlink'):
            match = re.search(r'LYRA_CHARACTER_UNLINK_OK hz=(\d+) frames=(\d+) self=(\d+) retry=(\d+) rejected=(\d+) switches=(\d+) montageSelf=(\d+) rigChanged=(\d+) rigCompleted=(\d+) airSelf=(\d+)', text)
            assert match is not None, row['name']
            hz, frames, self_frames, retry, rejected, switches, montage, rig, completed, air = map(int, match.groups())
            assert hz == frequencies[suffix]
            assert (frames,self_frames,retry,switches) == (36*hz,(18 if kind=='initial' else 12)*hz,18*hz,30 if kind=='initial' else 24)
            assert rejected > 0 and montage > 0 and rig > 0 and completed == self_frames and air > 0
            values[row['name']] = dict(hz=hz,frames=frames,self=self_frames,retry=retry,rejected=rejected,switches=switches,montage=montage,rig=rig,rigCompleted=completed,air=air)
        if 'report' in row:
            p = Path(row['report']); assert sha(p) == row['reportSha256'].lower()
            ordinary[config,row['name']] = read(p)
        processes += 1
    counts[config],reports[config] = values,report
    for group,layouts in (('whole-main',['single','per-call']),('other-groups',['three-groups','mixed'])):
        whole = read(out / f'{tag}-{group}-{config}-verification.json')
        assert whole['passed'] and len(whole['runs']) == 2 and not whole['goalComplete']
        assert [r['layout'] for r in whole['runs']] == layouts
        for flag in ('workerFields','preUpdateFields','movementFields','graphFields'):
            assert whole[flag]
        if group == 'whole-main':
            assert whole['leftSettings'] and whole['montageEventFields']
        for row in whole['runs']:
            path = Path(row['log']); text = log(path)
            assert sha(path) == row['logSha256'].lower() and row['exitCode'] == 0 and row['passed']
            assert not re.search(r'^\s*(ERROR|WARNING):', text, re.M)
            assert 'LYRA_MULTI_OWNER_MAIN_GRAPH_OK' in text and 'LYRA_WHOLE_MAIN_DIAGNOSTIC_OK frames=1080' in text and 'retry=1080' in text
            assert row['frames'] == 1080 and row['boundary'] == 'final'
            assert whole['assemblies'] == report['assemblies']
            processes += 1
assert counts['debug'] == counts['optimize'] == read(out / 'source-initialize-v2-integrity.json')['counts']['debug']
for name in ('ordinary-ten','ordinary-emote'):
    assert ordinary['debug',name] == ordinary['optimize',name] == read(out / f'source-initialize-v2-debug-{name}.json')
restored = {}
for group in ('','-whole-main','-other-groups'):
    backup = out / f'{tag}{group}-optimize-debug-backup'
    assert len(list(backup.iterdir())) == 6
    restored[group or 'primary'] = {p.name:sha(p).upper() for p in backup.iterdir()}
    assert restored[group or 'primary'] == reports['debug']['assemblies']
    for p in backup.iterdir():
        assert sha(p) == sha(repo / '.godot/mono/temp/bin/Debug' / p.name), p.name
assert reports['debug']['assemblies']['GodotALS.dll'] != reports['optimize']['assemblies']['GodotALS.dll']
assert reports['optimize']['assemblies'] == {name:sha(repo / '.godot/mono/temp/bin/ExportRelease' / name).upper() for name in reports['optimize']['assemblies']}
assert len(reports['optimize']['assemblies']) == 6
assert processes == 98
result = dict(auditPassed=True,nativeEvidenceTag=native_tag_base,runtimeEvidenceTag=tag,nativeProcesses=2,nativeProfiles=3,nativeControlNodes=24,nativeSeededRows=48,coreTests=163,
              godotProcesses=processes,counts=counts,resources=870,protectedPackages=710,protectedProjectFiles=9,protectedOtherSources=1474,
              engineCopies=23,sourceSha256=sources,auditorSha256=sha(Path(__file__)),restoredAssemblies=restored,
              nativeWarnings=744,archivedFailedRuns=failed_runs,
              scope=dict(realProviderSkeletalInitialization=True,controlsPerProvider=8,actualCacheBonesBindings=True,
                         definedFootInterpolationCompared=True,deltaAndShortCounterRetained=True,alphaStorageCompared=True,
                         legBendHistoryRetained=True,hiddenRetryTransactional=True,deferredCacheBeforeEvaluation=True,fixedAls81Layout=True,
                         sourceTicksDuringInitialize=False,poseEvaluationDuringInitialize=False,privateFootLegStorageCompared=False,
                         fullTraversalCounterFrameHistoryCompared=False,fullProviderPhaseSchedule=False,unifiedUpdateEvaluateCaches=False,
                         subsequentGraphReinitialization=False,liveRequiredBonesLOD=False,rigConstructionPhaseCompared=False,
                         newAlsDefaultFinalNative=False,partialBindings=False,fullPrivateFields=False,fullPhysicsParity=False,goalComplete=False))
with (out / f'{tag}-integrity.json').open('x',encoding='utf-8') as f:
    json.dump(result,f,indent=2)
print('LYRA_SKELETAL_INITIALIZE_AUDIT_OK native=2 nodes=24 seededRows=48 core=163 godot=98 resources=870 goalComplete=false')
