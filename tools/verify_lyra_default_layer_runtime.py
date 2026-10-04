"""Audit formal default-root export and production call-site routing gates."""
import hashlib
import json
from pathlib import Path
from locomotion_paths import project_path

import re
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[1]
out = repo/'artifacts/lyra-analysis'
tag = 'default-layer-runtime-v3'
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
    assert summary['passed'] and len(summary['runs']) == 6
    for run in summary['runs']:
        path = Path(run['log']); text = log(path)
        assert run['passed'] and run['exitCode'] == 0 and sha(path).upper() == run['logSha256']
        assert not re.search(r'^\s*(ERROR|WARNING):',text,re.M)
        if run['name'] == 'routes':
            assert 'self=336 unbound=336 external=168 rejected=60 fullMainUnlink=false' in text
        if 'report' in run:
            file = Path(run['report'])
            assert sha(file).upper() == run['reportSha256']
            reports[(config,run['name'])] = read(file)
        processes += 1
    graph_sets = [read(out/f'{tag}-whole-main-{config}-verification.json'),
        read(out/f'{tag}-other-groups-{config}-verification.json')]
    if config == 'debug':
        # The first driver used a fixture lacking three-groups/mixed. Its
        # single comparison completed successfully; preserve the failed driver
        # and finish the remaining layouts against their actual references.
        assert not graph_sets[0]['passed'] and len(graph_sets[0]['runs']) == 1
        graph_sets.append(read(out/f'{tag}-whole-main-continuation-debug-verification.json'))
    else: assert all(g['passed'] for g in graph_sets)
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
result = dict(auditPassed=True, godotProcesses=processes, managedTests=59,
    sourceSha256=source, resourceSha256=closure['resourceSha256'], originalJson=869,newJson=1,
    originalPackages=len(closure['assetSha256']),projectFiles=len(closure['protectedProject']),
    routeCasesPerBuild=dict(self=336,unbound=336,external=168,rejected=60,alsLogicalBones=81),
    fullMainReferenceFramesPerBuild=4320,
    scope=dict(productionCallSiteLookup=True,defaultPoseRootExecutor=True,
        fullMainUnlink=False,defaultScalarParameterPropagation=False,fullPrivateFieldParity=False,
        fullPhysicsRetested=False,gpuRetested=False,goalComplete=False))
with (out/f'{tag}-integrity.json').open('x',encoding='utf-8') as f: f.write(json.dumps(result,indent=2)+'\n')
print(f'LYRA_DEFAULT_LAYER_RUNTIME_AUDIT_OK processes={processes} core=59 oldJson=869 newJson=1 fullMainUnlink=false')
