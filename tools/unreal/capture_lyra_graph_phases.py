"""Original Main plus real Provider graph phases with persistent cache histories."""
import hashlib
import json
import os
from pathlib import Path
import unreal

repo = Path(__file__).resolve().parents[2]
assets = repo / 'assets/generated/lyra_als'
out = repo / 'artifacts/lyra-analysis'
tag = os.environ['LYRA_GRAPH_PHASE_TAG']
assert tag.replace('-', '').isalnum()
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read = lambda p: json.loads(p.read_bytes())
project = Path(unreal.Paths.get_project_file_path()).parent
previous = {p.relative_to(assets).as_posix(): sha(p) for p in assets.rglob('*.json')}
protected = {p.relative_to(project).as_posix(): sha(p) for p in [project/'GASP58.uproject', *(project/'Config').rglob('*.ini')]}
packages = read(out/'main-phases-v3-closure.json')['assetSha256']
source = repo/'tools/unreal/LyraGraphPhasesOracle'
sources = {p.relative_to(source).as_posix(): sha(p) for p in source.rglob('*') if p.is_file()}
engine = Path(unreal.Paths.engine_dir()).resolve()
engine_files = [*read(out/'main-phases-v3-engine-sources.json'),
    'Source/Runtime/Engine/Private/Animation/AnimInstance.cpp',
    'Source/Runtime/Engine/Public/Animation/AnimInstanceProxy.h',
    'Source/Runtime/Engine/Public/Animation/AnimTypes.h']
engine_sources = {p: sha(engine/p) for p in engine_files}


def protect():
    for p, d in previous.items(): assert sha(assets/p) == d, p
    for p, d in protected.items(): assert sha(project/p) == d, p
    for p, d in packages.items():
        path = p.split('.')[0]
        if path.startswith('/Game/'):
            file = project/'Content'/(path.removeprefix('/Game/')+'.uasset')
        elif path.startswith('/ShooterCore/'):
            file = project/'Plugins/GameFeatures/ShooterCore/Content'/(path.removeprefix('/ShooterCore/')+'.uasset')
        else:
            raise ValueError(path)
        assert sha(file) == d, p
    for p, d in sources.items(): assert sha(source/p) == d, p
    for p, d in engine_sources.items(): assert sha(engine/p) == d, p


def save(kind, value):
    path = out/f'{tag}-{kind}.json'
    with path.open('x', encoding='utf-8', newline='\n') as f:
        f.write(json.dumps(value, separators=(',', ':'), allow_nan=False)+'\n')
    return sha(path)


graphs = read(assets/'main_layer_graph_v1.json')['classes']
contracts = read(assets/'linked_layer_contracts.json')
steps = [dict(counter=c, frame=f) for c, f in [(1,10),(1,10),(2,11),(2,11),(1,10),(1,10),(1,12),(32767,13),(-32768,14),(-32768,14)]]
requests, results = [], []
for profile in ('unarmed', 'pistol', 'rifle'):
    provider = graphs[profile]
    nodes = {}
    for graph in provider['graphs'].values():
        for node in graph['nodes']:
            old = nodes.setdefault(node['index'], node)
            assert old == node
    requests.append(dict(profile=profile, main=contracts['classes']['main']['class'],
        provider=provider['classPath'], mesh='/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny.SKM_Manny',
        mainNodes=graphs['main']['graphs']['AnimGraph']['nodes'],
        providerNodes=[nodes[k] for k in sorted(nodes)],
        roots={name: graph['root'] for name, graph in provider['graphs'].items()}, cacheSteps=steps))
protect()
request_sha = save('requests', dict(schemaVersion=1, cases=requests))
try:
    for request in requests:
        result = json.loads(unreal.LyraWholeMainOracleLibrary.read_graph_phases(json.dumps(request, separators=(',', ':'))))
        result['profile'] = request['profile']
        results.append(result)
    native_sha = save('native', dict(schemaVersion=1, cases=results))
    for result in results:
        assert result['linkedInstances'] == 1 and result['initialize'][0] == 'main:85'
        assert 'provider:113' in result['initialize'] and 'provider:36' in result['initialize']
        assert len(result['cacheSteps']) == len(steps)
        rows = result['cacheSteps']
        assert rows[0]['order'] == rows[2]['order'] == rows[4]['order'] == rows[6]['order'] == rows[7]['order'] == rows[8]['order']
        assert rows[1]['order'] == rows[3]['order'] == rows[5]['order'] == rows[9]['order']
        assert len(rows[0]['order']) > len(rows[1]['order'])
        assert all(row['time'] == 0 for row in result['sourceTimes'])
    protect()
    save('closure', dict(schemaVersion=1, requestSha256=request_sha, nativeSha256=native_sha,
        engineVersion=unreal.SystemLibrary.get_engine_version(), engineSourceSha256=engine_sources,
        probeSourceSha256=sources, previousFixtureSha256=previous, protectedProject=protected, assetSha256=packages,
        scope=dict(originalMainAndProviderPhases=True, originalNamedGroup=True, counterRegressions=True,
                   counterWrap=True, equalCounterDifferentFrame=True, sourceUpdateOrEvaluate=False,
                   runtimePhaseScheduler=False, assetsSaved=0, goalComplete=False)))
    unreal.log('LYRA_GRAPH_PHASE_NATIVE_OK profiles=3 cache_steps=30 linked=1 assets_saved=0')
finally:
    protect()
