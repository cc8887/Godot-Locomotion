"""Audit formal default-root export and production call-site routing gates."""
import hashlib
import json
from pathlib import Path
from locomotion_paths import project_path

import re
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[1]
out = repo/'artifacts/lyra-analysis'
tag = 'default-main-v5'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read = lambda p: json.loads(p.read_bytes())
def log(path):
    b = path.read_bytes()
    return b.decode('utf-16') if b.startswith((b'\xff\xfe',b'\xfe\xff')) else b.decode('utf-8-sig')

closure = read(out/'default-layer-runtime-v2-closure.json')
assert (out/'default-layer-runtime-v2-closure.json').read_bytes() == (out/'default-layer-runtime-v2-repeat-closure.json').read_bytes()
assets = repo/'assets/generated/lyra_als'
resource = assets/'default_layer_graphs_v1.json'
assert sha(resource) == closure['resourceSha256']
assert sha(repo/'tools/unreal/export_lyra_default_layer_graphs.py') == closure['exporterSha256']
current = {p.relative_to(assets).as_posix(): sha(p) for p in assets.rglob('*.json') if p != resource}
assert current == closure['previousFixtureSha256'] and len(current) == 869
data = read(resource)
assert data['dependencies'] == {p: sha(assets/p) for p in ('linked_layer_contracts.json','main_layer_graph_v1.json')}
assert data['nativeReferenceSha256'] == sha(out/'layer-fallback-v4-native.json')
assert len(data['functions']) == 14 and data['nodePropertyCount'] == 103
contracts = read(assets/'linked_layer_contracts.json')['classes']['main']
functions = {f['name']:f for f in contracts['functions'] if f['name'] != 'AnimGraph'}
native = {f['name']:f for f in read(out/'layer-fallback-v4-native.json')['functions']}
for hook, f in data['functions'].items():
    assert f['signature'] == functions[hook] and not f['signature']['implemented']
    graph = f['graph']; node, = graph['nodes']
    assert graph['rootPropertyIndex'] == native[hook]['rootPropertyIndex'] == native[hook]['rootIndex']
    assert graph['root']+graph['rootPropertyIndex']+1 == 103
    assert node['type'] == '/Script/Engine.AnimNode_Root' and not node['links']
    assert node['index'] == graph['root'] and node['settings']['name'] == hook
    assert all(v == 'None' for v in node['functions'].values())
project = project_path()
for p, d in closure['protectedProject'].items(): assert sha(project/p) == d, p
for p, d in closure['assetSha256'].items():
    path = p.split('.')[0]
    if path.startswith('/Game/'): file = project/'Content'/(path.removeprefix('/Game/')+'.uasset')
    elif path.startswith('/ShooterCore/'): file = project/'Plugins/GameFeatures/ShooterCore/Content'/(path.removeprefix('/ShooterCore/')+'.uasset')
    else: raise ValueError(path)
    assert sha(file) == d, p
package = repo/'artifacts/unreal/gasp58-lyra-masks/package/AlsV4AssetExporter'
for p, d in closure['probePackageSha256'].items(): assert sha(package/p) == d, p
for repeat in ('','-repeat'):
    text = log(out/f'default-layer-runtime-v2{repeat}-export.log')
    assert 'LYRA_DEFAULT_GRAPH_EXPORT_EXIT=0' in text and 'LYRA_DEFAULT_LAYER_GRAPHS_OK roots=14' in text
    assert not any(s in text for s in ('Error:', 'Fatal error:', 'Ensure condition failed'))
changed = {
    'src/Als.Godot/Animation/Lyra/LyraLinkedLayerGraphSet.cs',
    'src/Als.Godot/Animation/Lyra/LyraMainLayerGraphCatalog.cs',
    'src/Als.Godot/Animation/Lyra/LyraMainGraphStateOwner.cs',
    'src/Als.Godot/Animation/Lyra/LyraMainLocomotionHost.cs',
    'src/Als.Godot/Animation/Lyra/LyraMainLeanCompositionHost.cs',
}
previous = read(out/'notify-live-v2-frozen-sources-final.json')
for p,d in previous.items():
    if p not in changed: assert sha(repo/p) == d, p
source = read(out/f'{tag}-frozen-sources-accepted.json')
for p,d in source.items(): assert sha(repo/p) == d, p
processes = 0
reports = {}
for config in ('debug','optimize'):
    build = log(out/f'{tag}-build-{config}-final.log')
    assert re.search(r'^\s*0\s*(个警告|Warning)',build,re.M) and re.search(r'^\s*0\s*(个错误|Error)',build,re.M)
    summary = read(out/f'{tag}-{config}-verification.json')
    assert summary['passed'] and len(summary['runs']) == 7
    for run in summary['runs']:
        path = Path(run['log']); text = log(path)
        assert run['passed'] and run['exitCode'] == 0 and sha(path).upper() == run['logSha256']
        assert not re.search(r'^\s*(ERROR|WARNING):',text,re.M)
        if run['name'] == 'main-default':
            assert 'frames=1260 retry=1260 poses=1082 fields=88200 linked=0 sources=0 preRig=true ordinaryUnlink=false' in text
        if run['name'] == 'routes':
            assert 'self=336 unbound=336 external=168 rejected=60 fullMainUnlink=false' in text
        if 'report' in run:
            file = Path(run['report'])
            assert sha(file).upper() == run['reportSha256']
            reports[(config,run['name'])] = read(file)
        processes += 1
    graph_sets = [read(out/f'{tag}-whole-main-{config}-verification.json'),
        read(out/f'{tag}-other-groups-{config}-verification.json')]
    assert all(g['passed'] for g in graph_sets)
    graph_runs = [r for g in graph_sets for r in g['runs']]
    assert len(graph_runs) == 4 and {r['layout'] for r in graph_runs} == {'single','three-groups','mixed','per-call'}
    for graph in graph_sets:
        assert not graph['fullPrivateFieldParity']
        assert all(graph[k] for k in ('workerFields','preUpdateFields','movementFields','graphFields'))
        if graph['runTag'] == 'linked-montage-events-v1-30-full': assert graph['leftSettings'] and graph['montageEventFields']
        else: assert graph['runTag'] == 'linked-private-v2-30-full'
    for run in graph_runs:
        path = Path(run['log']);text = log(path)
        assert run['passed'] and run['exitCode'] == 0 and run['boundary'] == 'final' and run['frames'] == 1080
        assert sha(path).upper() == run['logSha256'] and not re.search(r'^\s*(ERROR|WARNING):',text,re.M)
        assert 'retry=1080 controlledPhysicalInputs=true' in text
        processes += 1
    if config == 'optimize':
        assert summary['debugRestored'] and all(g['debugRestored'] for g in graph_sets)
        for prefix in (f'{tag}-optimize', f'{tag}-whole-main-optimize', f'{tag}-other-groups-optimize'):
            backup = out/f'{prefix}-debug-backup'
            for p in backup.iterdir(): assert sha(p) == sha(repo/'.godot/mono/temp/bin/Debug'/p.name), p.name
        for name,d in summary['assemblies'].items(): assert sha(repo/'.godot/mono/temp/bin/ExportRelease'/name).upper() == d
for scene in ('ordinary-ten','ordinary-emote'):
    assert reports[('debug',scene)] == reports[('optimize',scene)]
    assert reports[('debug',scene)] == read(out/f'notify-live-v2-debug-{scene}.json'), scene
trx = ET.parse(out/f'{tag}-core-final.trx')
counters = trx.find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters').attrib
assert int(counters['total']) == int(counters['passed']) == 59
assert int(counters['failed']) == int(counters['notExecuted']) == 0
native_closure=read(out/f'{tag}-closure.json')
for kind in ('requests','native','closure'):
    assert (out/f'{tag}-{kind}.json').read_bytes()==(out/f'{tag}-repeat-{kind}.json').read_bytes(),kind
for repeat in ('','-repeat'):
    text=log(out/f'{tag}{repeat}-native.log')
    assert 'LYRA_DEFAULT_MAIN_PROCESS_EXIT=0' in text and 'LYRA_DEFAULT_MAIN_NATIVE_OK traces=6 frames=2520' in text
    assert not any(s in text for s in ('Error:', 'Fatal error:', 'Ensure condition failed'))
assert native_closure['previousFixtureSha256']=={p.relative_to(assets).as_posix():sha(p) for p in assets.rglob('*.json')}
probe=repo/'tools/unreal/LyraDefaultMainOracle'
package=repo/'artifacts/unreal/lyra-whole-main-oracle/package-default-main-v5'
for p,d in native_closure['probeSourceSha256'].items():
    assert sha(probe/p)==sha(package/p)==d,p
assert native_closure['captureSourceSha256']==sha(repo/'tools/unreal/capture_lyra_default_main.py')
q=read(out/f'{tag}-requests.json');native=read(out/f'{tag}-native.json')
assert len(native['traces'])==6
native_frames=self_frames=external_frames=self_poses=0
reference=native['traces'][0]['frames'][0]['preRig']['pose']
for index,(t,request) in enumerate(zip(native['traces'],q['traces'])):
    assert len(t['frames'])==len(request['frames'])==6*t['hz']
    previous_frame=None
    for row,f in zip(t['frames'],request['frames']):
        assert row['mainUpdated']['WorldLocation']==f['location'] and row['mainUpdated']['WorldVelocity']==f['velocity']
        assert not row['mainUpdated']['IsFirstUpdate'] and len(row['calls'])==14
        is_self=all(c['self'] for c in row['calls'])
        assert is_self==(row['linkedInstances']==0)
        assert row['preRigUpdates']==1 and row['preRigEvaluations']==int(f['evaluate'])
        if is_self:
            self_frames+=1
            assert row['upstreamUpdates']==row['upstreamEvaluations']==0
            if previous_frame is not None:
                assert row['machineState']==previous_frame['machineState'] and row['machineElapsed']==previous_frame['machineElapsed']
                assert row['mainLean']==previous_frame['mainLean']
            if f['evaluate']:
                self_poses+=1
                assert row['preRig']['pose']==reference and not row['preRig']['curves']
        else:
            external_frames+=1
            assert row['upstreamUpdates']==1 and row['upstreamEvaluations']==int(f['evaluate'])
        if f['evaluate']:assert len(row['output']['pose'])==164
        if index>=3:assert is_self and row['machineState']==row['machineElapsed']==0
        native_frames+=1;previous_frame=row
assert native_frames==2520 and self_frames==2100 and external_frames==420
origin=read(out/f'{tag}-engine-source.json')
for p,d in origin['engineSourceSha256'].items():
    assert sha(Path(origin['engineRoot'])/p)==sha(out/f'{tag}-engine-source'/p)==d,p
result=dict(auditPassed=True,godotProcesses=processes,managedTests=59,nativeFrames=native_frames,
    nativeSelfFrames=self_frames,nativeExternalFrames=external_frames,nativeSelfEvaluations=self_poses,
    defaultMainFramesPerBuild=1260,defaultMainRetriesPerBuild=1260,defaultMainFieldComparisonsPerBuild=88200,
    fullExternalMainReferenceFramesPerBuild=4320,sourceSha256=source,originalJson=869,newJson=1,
    originalPackages=710,projectFiles=9,
    scope=dict(defaultMainWorkerTransaction=True,defaultMainZeroLinkedInstanceBranch=True,
        productionCharacterUnlink=False,defaultGodotFinalRig=False,montageDuringDefault=False,
        fullPrivateFieldParity=False,fullPhysicsRetested=False,gpuRetested=False,goalComplete=False))
with (out/f'{tag}-integrity.json').open('x',encoding='utf-8') as f:json.dump(result,f,indent=2)
print(f'LYRA_DEFAULT_MAIN_AUDIT_OK processes={processes} core=59 native={native_frames} self={self_frames} external={external_frames} ordinaryUnlink=false')
