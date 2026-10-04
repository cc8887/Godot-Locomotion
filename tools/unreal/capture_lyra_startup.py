"""Original deferred component startup and linked function phase history."""
from pathlib import Path
import hashlib
import json
import os
import unreal

repo = Path(__file__).resolve().parents[2]
out = repo / 'artifacts/lyra-analysis'
assets = repo / 'assets/generated/lyra_als'
tag = os.environ['LYRA_STARTUP_TAG']
assert tag.replace('-', '').isalnum()
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read = lambda p: json.loads(p.read_bytes())
project = Path(unreal.Paths.get_project_file_path()).parent
previous = {p.relative_to(assets).as_posix(): sha(p) for p in assets.rglob('*.json')}
protected = {p.relative_to(project).as_posix(): sha(p) for p in [project/'GASP58.uproject', *(project/'Config').rglob('*.ini')]}
packages = read(out/'whole-main-proxy-update-joint-v2-full-closure.json')['assetSha256']
source = repo/'tools/unreal/LyraStartupOracle'
sources = {p.relative_to(source).as_posix(): sha(p) for p in source.rglob('*') if p.is_file()}
engine = Path(unreal.Paths.engine_dir()).resolve()
engine_names = ['Source/Runtime/Engine/Private/Animation/AnimInstance.cpp',
                'Source/Runtime/Engine/Private/Animation/AnimInstanceProxy.cpp',
                'Source/Runtime/Engine/Private/Animation/AnimNode_LinkedAnimGraph.cpp',
                'Source/Runtime/Engine/Private/Components/SkeletalMeshComponent.cpp']
engine_sources = {p: sha(engine/p) for p in engine_names}

def protect():
    for p, h in previous.items(): assert sha(assets/p) == h, p
    for p, h in protected.items(): assert sha(project/p) == h, p
    for p, h in packages.items():
        path = p.split('.')[0]
        if path.startswith('/Game/'):
            file = project/'Content'/(path.removeprefix('/Game/')+'.uasset')
        elif path.startswith('/ShooterCore/'):
            file = project/'Plugins/GameFeatures/ShooterCore/Content'/(path.removeprefix('/ShooterCore/')+'.uasset')
        else:
            raise ValueError(path)
        assert sha(file) == h, p
    for p, h in sources.items(): assert sha(source/p) == h, p
    for p, h in engine_sources.items(): assert sha(engine/p) == h, p

def save(kind, value):
    path = out/f'{tag}-{kind}.json'
    with path.open('x', encoding='utf-8', newline='\n') as f:
        f.write(json.dumps(value, separators=(',', ':'), allow_nan=False)+'\n')
    return sha(path)

requests = []
original = read(out/'whole-main-proxy-update-joint-v2-full-request.json')
for trace in original['traces']:
    for mode in ('before-root', 'after-root', 'self'):
        requests.append(dict(profile=trace['profile'], layout=trace['layout'], mode=mode,
            main=original['mainClass'], provider=trace['class'], groups=trace['functionGroups'],
            mesh='/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny.SKM_Manny'))
assert len(requests) == 36
protect()
request_sha = save('requests', dict(schemaVersion=1, cases=requests))
try:
    results = []
    for request in requests:
        text = unreal.LyraStartupOracleLibrary.read_startup(json.dumps(request, separators=(',', ':')))
        assert text, request
        result = json.loads(text)
        result.update({k: request[k] for k in ('profile', 'layout', 'mode')})
        assert result['counterWrites'] == result['globalFrameWrites'] == 0
        assert result['rows'][0]['stage'] == 'registered'
        results.append(result)
    native_sha = save('native', dict(schemaVersion=1, cases=results))
    protect()
    save('closure', dict(schemaVersion=1, requestSha256=request_sha, nativeSha256=native_sha,
        engineVersion=unreal.SystemLibrary.get_engine_version(), engineSourceSha256=engine_sources,
        probeSourceSha256=sources, previousFixtureSha256=previous, protectedProject=protected,
        assetSha256=packages, scope=dict(originalBlueprint=True, deferredComponentStartup=True,
        directCounterWrites=0, globalFrameWrites=0, originalManny164=True, assetsSaved=0,
        naturalSceneScheduler=False, als81BoneMapping=False, goalComplete=False)))
    unreal.log('LYRA_STARTUP_NATIVE_OK cases=36 counter_writes=0 global_frame_writes=0 assets_saved=0')
finally:
    protect()
