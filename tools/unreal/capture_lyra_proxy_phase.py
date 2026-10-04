"""Original Proxy entrypoints and LinkedAnimGraph counter inheritance.

Only external GFrameCounter and root/target bindings are controlled. No Proxy
counter, bone-invalidated flag, worker gate, or node phase is implemented here.
"""
import hashlib
import json
import os
from pathlib import Path
import unreal

repo = Path(__file__).resolve().parents[2]
out = repo / 'artifacts/lyra-analysis'
tag = os.environ['LYRA_PROXY_PHASE_TAG']
assert tag.replace('-', '').isalnum()
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read = lambda p: json.loads(p.read_bytes())
assets = repo / 'assets/generated/lyra_als'
project = Path(unreal.Paths.get_project_file_path()).parent
source = repo / 'tools/unreal/LyraProxyPhaseOracle'
engine = Path(unreal.Paths.engine_dir()).resolve()
previous = {p.relative_to(assets).as_posix(): sha(p) for p in assets.rglob('*.json')}
protected = {p.relative_to(project).as_posix(): sha(p) for p in [project/'GASP58.uproject', *(project/'Config').rglob('*.ini')]}
packages = read(out/'main-phases-v3-closure.json')['assetSha256']
sources = {p.relative_to(source).as_posix(): sha(p) for p in source.rglob('*') if p.is_file()}
engine_paths = [
 'Source/Runtime/Engine/Private/Animation/AnimInstanceProxy.cpp',
 'Source/Runtime/Engine/Private/Animation/AnimInstance.cpp',
 'Source/Runtime/Engine/Private/Animation/AnimNode_LinkedAnimGraph.cpp',
 'Source/Runtime/Engine/Public/Animation/AnimInstanceProxy.h',
 'Source/Runtime/Engine/Public/Animation/AnimTypes.h',
 'Source/Runtime/Engine/Classes/Animation/AnimNode_LinkedAnimGraph.h',
 'Source/Runtime/Engine/Classes/Animation/AnimInstance.h',
]
engine_sources = {p: sha(engine/p) for p in engine_paths}


def protect():
    for p, d in previous.items(): assert sha(assets/p) == d, p
    for p, d in protected.items(): assert sha(project/p) == d, p
    for p, d in sources.items(): assert sha(source/p) == d, p
    for p, d in engine_sources.items(): assert sha(engine/p) == d, p
    for p, d in packages.items():
        asset = p.split('.')[0]
        if asset.startswith('/Game/'):
            file = project/'Content'/(asset.removeprefix('/Game/')+'.uasset')
        elif asset.startswith('/ShooterCore/'):
            file = project/'Plugins/GameFeatures/ShooterCore/Content'/(asset.removeprefix('/ShooterCore/')+'.uasset')
        else:
            raise ValueError(asset)
        assert sha(file) == d, p


def save(kind, value):
    path = out/f'{tag}-{kind}.json'
    with path.open('x', encoding='utf-8', newline='\n') as f:
        f.write(json.dumps(value, separators=(',', ':'), ensure_ascii=False)+'\n')
    return sha(path)


steps = []


def step(op, frame, mask=7, **values):
    steps.append(dict(op=op, frame=frame, mask=mask, **values))


step('initialize', 0)
step('initialize-function', 0)
step('bones', 0)  # Not invalidated: no visit, including after InitializeRoot.
step('invalidate', 0)
step('bones', 0)
step('bones', 0)
step('bones-function', 0)  # Linked initialization bypasses proxy cache gate.
step('update', 0)  # Native FrameCounterForUpdate starts at zero.
step('update', 7)
step('update', 7)
step('update-function', 7)
step('evaluate', 7)
step('evaluate', 7)
step('evaluate-function', 7)
step('update', 8, mask=3)  # Actual second instance remains hidden.
step('evaluate', 8, mask=3)
step('update', 9)
step('evaluate', 9)
step('null-initialize', 9)
step('null-update', 10)
step('null-evaluate', 10)
step('invalidate', 11)
step('bones', 11, mask=3)
step('initialize', 12)
step('bones', 12)  # Initialization alone does not invalidate the cache.
step('proxy-initialize', 12)  # Proxy::Initialize resets Update only.
step('update', 12)
step('evaluate', 12)
step('wrap', 13, mask=0, iterations=32767)
step('wrap', 13, mask=0, iterations=32768)
step('wrap', 13, mask=0, iterations=1)
request = dict(mesh='/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny.SKM_Manny', steps=steps)
protect()
request_sha = save('requests', dict(schemaVersion=1, request=request))
try:
    result = json.loads(unreal.LyraProxyPhaseOracleLibrary.read_proxy_phases(json.dumps(request, separators=(',', ':'))))
    assert len(result['rows']) == len(steps) and len(result['initial']['proxies']) == 3
    native_sha = save('native', dict(schemaVersion=1, result=result))
    for proxy in result['initial']['proxies']:
        assert proxy['workerFrame'] == proxy['workerCalls'] == 0
        assert all(proxy[key]['counter'] == proxy[key]['frame'] == -1 for key in ('initialization', 'bones', 'update', 'evaluation'))
    protect()
    save('closure', dict(schemaVersion=1, requestSha256=request_sha, nativeSha256=native_sha,
         engineVersion=unreal.SystemLibrary.get_engine_version(), engineSourceSha256=engine_sources,
         probeSourceSha256=sources, previousFixtureSha256=previous, protectedProject=protected,
         assetSha256=packages, scope=dict(originalProxyEntryPoints=True, originalLinkedNodes=True,
         proxyCounterWrites=0, controlledExternalFrames=True, naturalComponentCounters=False,
         fullLyraGraphs=False, actualLODChanges=False, assetsSaved=0, goalComplete=False)))
    unreal.log('LYRA_PROXY_PHASE_NATIVE_OK steps='+str(len(steps))+' proxies=3 counter_writes=0 assets_saved=0')
finally:
    protect()
