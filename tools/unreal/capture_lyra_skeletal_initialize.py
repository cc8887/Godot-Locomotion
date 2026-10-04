"""Original full SkeletalControls Initialize and CacheBones on transient ALS81."""
import hashlib
import json
import os
from pathlib import Path
import unreal

repo = Path(__file__).resolve().parents[2]
assets = repo / 'assets/generated/lyra_als'
out = repo / 'artifacts/lyra-analysis'
tag = os.environ['LYRA_SKELETAL_INITIALIZE_TAG']
assert tag.replace('-', '').isalnum()
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read = lambda p: json.loads(p.read_bytes())
project = Path(unreal.Paths.get_project_file_path()).parent
previous = {p.relative_to(assets).as_posix(): sha(p) for p in assets.rglob('*.json')}
protected = {p.relative_to(project).as_posix(): sha(p) for p in [project/'GASP58.uproject', *(project/'Config').rglob('*.ini')]}
packages = read(out/'main-phases-v3-closure.json')['assetSha256']
source = repo/'tools/unreal/LyraSkeletalInitializeOracle'
sources = {p.relative_to(source).as_posix(): sha(p) for p in source.rglob('*') if p.is_file()}
engine = Path(unreal.Paths.engine_dir()).resolve()
engine_files = list(read(out/'skeletal-initialize-v2-engine-sources.json'))
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
cal = read(assets/'logical_controls/calibration.json')['calibration']
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
        provider=provider['classPath'], sourceMesh=cal['sourceMesh'], targetMesh=cal['targetMesh'], handBasis=cal['handBasis']['rotation'], mesh='/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny.SKM_Manny',
        mainNodes=graphs['main']['graphs']['AnimGraph']['nodes'],
        providerNodes=[nodes[k] for k in sorted(nodes)],
        roots={name: graph['root'] for name, graph in provider['graphs'].items()}, cacheSteps=steps))
protect()
request_sha = save('requests', dict(schemaVersion=1, cases=requests))
try:
    for request in requests:
        result = json.loads(unreal.LyraWholeMainOracleLibrary.read_skeletal_initialization(json.dumps(request, separators=(',', ':'))))
        result['profile'] = request['profile']
        results.append(result)
    native_sha = save('native', dict(schemaVersion=1, cases=results))
    assert len(results) == 3 and all(len(r['reference']) == 81 for r in results)
    assert results[0]['reference'] == results[1]['reference'] == results[2]['reference']
    for result in results:
        assert result['sourceUpdate'] is False and result['poseEvaluate'] is False
        assert len(result['rounds']) == 2
        for row in result['rounds']:
            before, after, cached = row['before'], row['after'], row['cached']
            assert after == cached
            assert [n['node'] for n in after['nodes']] == [103,102,104,110,109,105,107,106]
            for b,a in zip(before['nodes'], after['nodes'], strict=True):
                assert b['alpha'] == a['alpha'] and b['clampValue'] == a['clampValue']
                assert b['clampInitialized'] is True and a['clampInitialized'] is False
                assert b['bool']['initialized'] is True and a['bool']['initialized'] is False
                assert {k:v for k,v in b['bool'].items() if k != 'initialized'} == {k:v for k,v in a['bool'].items() if k != 'initialized'}
                assert not a['clampInterp']
            bf, af = before['foot'], after['foot']
            assert bf['first'] is False and af['first'] is True
            for key in ('delta','counter','component','componentDelta','groundNormal','groundSpring','onGround','root'):
                assert bf[key] == af[key], key
            assert af['pelvisOffset'] == [0,0,0] and af['pelvisSpring']['valid'] is False
            for leg in af['legs']:
                for value in leg.values():
                    assert value['valid'] is False
                    assert value['velocity'] == (0 if isinstance(value['velocity'],(float,int)) else [0,0,0])
            assert before['leg']['proxyBound'] is False and after['leg']['proxyBound'] is True
            assert before['leg']['legs'] == after['leg']['legs']
            assert all(l['links'] == 3 and l['real'] == [.25,.5,.75] and l['base'] == [-.25,-.5,-.75] for l in after['leg']['legs'])
    protect()
    save('closure', dict(schemaVersion=1, requestSha256=request_sha, nativeSha256=native_sha,
        engineVersion=unreal.SystemLibrary.get_engine_version(), engineSourceSha256=engine_sources,
        probeSourceSha256=sources, previousFixtureSha256=previous, protectedProject=protected, assetSha256=packages,
        scope=dict(originalSkeletalInitialize=True, controlsPerProvider=8, seededReinitialization=True,
                   originalCacheBones=True, fixedAls81=True, privateFootLegStorageCompared=False,
                   sourceUpdateOrEvaluate=False, fullRequiredBonesLOD=False, assetsSaved=0, goalComplete=False)))
    unreal.log('LYRA_SKELETAL_INITIALIZE_NATIVE_OK profiles=3 nodes=24 seeded_rows=48 als=81 assets_saved=0')
finally:
    protect()
