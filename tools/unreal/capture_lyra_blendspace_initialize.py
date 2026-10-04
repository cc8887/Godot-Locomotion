"""Original Main plus real Provider graph phases with persistent cache histories."""
import hashlib
import json
import os
from pathlib import Path
import unreal

repo = Path(__file__).resolve().parents[2]
assets = repo / 'assets/generated/lyra_als'
out = repo / 'artifacts/lyra-analysis'
tag = os.environ['LYRA_BLENDSPACE_INITIALIZE_TAG']
assert tag.replace('-', '').isalnum()
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read = lambda p: json.loads(p.read_bytes())
project = Path(unreal.Paths.get_project_file_path()).parent
previous = {p.relative_to(assets).as_posix(): sha(p) for p in assets.rglob('*.json')}
protected = {p.relative_to(project).as_posix(): sha(p) for p in [project/'GASP58.uproject', *(project/'Config').rglob('*.ini')]}
packages = read(out/'main-phases-v3-closure.json')['assetSha256']
source = repo/'tools/unreal/LyraBlendSpaceInitializeOracle'
sources = {p.relative_to(source).as_posix(): sha(p) for p in source.rglob('*') if p.is_file()}
engine = Path(unreal.Paths.engine_dir()).resolve()
engine_files = list(read(out/'blendspace-initialize-v1-engine-sources.json'))
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
        result = json.loads(unreal.LyraWholeMainOracleLibrary.read_blend_space_initialization(json.dumps(request, separators=(',', ':'))))
        result['profile'] = request['profile']
        results.append(result)
    native_sha = save('native', dict(schemaVersion=1, cases=results))
    for result in results:
        assert result['linkedInstances'] == 1 and result['sourceUpdate'] is False
        assert len(result['sources']) == 10
        for row in result['sources']:
            b, a = row['before'], row['after']
            assert a == row['cached']
            assert a['previousIndex'] == a['nextIndex'] == -2
            assert a['fullWeight'] is False
            for key in ('weight','previousDistance','nextDistance','deltaPrevious','delta','deltaValid'):
                assert b[key] == a[key], (row['node'], key)
            assert a['internal'] == a['public'] == 0
            assert a['sampleCount'] == 0 and a['triangle'] == b['triangle']
            assert a['asset'] == a['previousAsset'] and a['asset']
            assert len(a['filters']) == 2 and all(f['output'] == 0 for f in a['filters'])
            if row['aim']: assert a['alpha'] == b['alpha'] and a['lodEnabled'] == b['lodEnabled']
    protect()
    save('closure', dict(schemaVersion=1, requestSha256=request_sha, nativeSha256=native_sha,
        engineVersion=unreal.SystemLibrary.get_engine_version(), engineSourceSha256=engine_sources,
        probeSourceSha256=sources, previousFixtureSha256=previous, protectedProject=protected, assetSha256=packages,
        scope=dict(originalBlendSpaceInitialize=True, sourcesPerProfile=5, seededReinitialization=True,
                   originalCacheBones=True, privateFilterHistoryRead=False, sourceUpdateOrEvaluate=False,
                   runtimePhaseScheduler=False, assetsSaved=0, goalComplete=False)))
    unreal.log('LYRA_BLENDSPACE_INITIALIZE_NATIVE_OK profiles=3 nodes=15 seeded_rows=30 linked=1 assets_saved=0')
finally:
    protect()
