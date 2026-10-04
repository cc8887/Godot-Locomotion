"""Actual full self Main initialization and repeated bone-cache traversal."""
import hashlib
import json
import os
from pathlib import Path
import unreal

repo = Path(__file__).resolve().parents[2]
assets = repo / 'assets/generated/lyra_als'
out = repo / 'artifacts/lyra-analysis'
tag = os.environ['LYRA_MAIN_PHASE_TAG']
assert tag.replace('-', '').isalnum()
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read = lambda p: json.loads(p.read_bytes())
project = Path(unreal.Paths.get_project_file_path()).parent
previous = {p.relative_to(assets).as_posix(): sha(p) for p in assets.rglob('*.json')}
protected = {p.relative_to(project).as_posix(): sha(p) for p in [project/'GASP58.uproject', *(project/'Config').rglob('*.ini')]}
packages = read(out/'whole-main-linked-montage-events-v1-30-full-closure.json')['assetSha256']
source = repo/'tools/unreal/LyraMainPhasesOracle'
sources = {p.relative_to(source).as_posix(): sha(p) for p in source.rglob('*') if p.is_file()}
engine = Path(unreal.Paths.engine_dir()).resolve()
engine_files = [
    'Source/Runtime/Engine/Private/Animation/AnimNode_LinkedAnimGraph.cpp',
    'Source/Runtime/Engine/Private/Animation/AnimNode_LinkedAnimLayer.cpp',
    'Source/Runtime/Engine/Classes/Animation/AnimNodeBase.h',
    'Source/Runtime/Engine/Private/Animation/AnimNodeBase.cpp',
    'Source/Runtime/Engine/Private/Animation/AnimInstanceProxy.cpp',
    'Source/Runtime/Engine/Private/Animation/AnimNode_LinkedInputPose.cpp',
    'Source/Runtime/Engine/Private/Animation/AnimNode_SaveCachedPose.cpp',
    'Source/Runtime/Engine/Private/Animation/AnimNode_StateMachine.cpp',
    'Source/Runtime/AnimGraphRuntime/Private/AnimNodes/AnimNode_LayeredBoneBlend.cpp',
]
engine_sources = {p: sha(engine/p) for p in engine_files}

def package_file(p):
    p = p.split('.')[0]
    if p.startswith('/Game/'):
        return project/'Content'/(p.removeprefix('/Game/')+'.uasset')
    if p.startswith('/ShooterCore/'):
        return project/'Plugins/GameFeatures/ShooterCore/Content'/(p.removeprefix('/ShooterCore/')+'.uasset')
    raise ValueError(p)

def protect():
    for p, d in previous.items(): assert sha(assets/p) == d, p
    for p, d in protected.items(): assert sha(project/p) == d, p
    for p, d in packages.items(): assert sha(package_file(p)) == d, p
    for p, d in sources.items(): assert sha(source/p) == d, p
    for p, d in engine_sources.items(): assert sha(engine/p) == d, p

def save(kind, value):
    path = out/f'{tag}-{kind}.json'
    with path.open('x', encoding='utf-8', newline='\n') as f:
        f.write(json.dumps(value, separators=(',', ':'), allow_nan=False)+'\n')
    return sha(path)

contracts=read(assets/'linked_layer_contracts.json')
graph=read(assets/'main_layer_graph_v1.json')['classes']['main']['graphs']['AnimGraph']
request=dict(schemaVersion=1,main=contracts['classes']['main']['class'],
    mesh='/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny.SKM_Manny',nodes=graph['nodes'])
protect();request_sha=save('requests',request)
try:
    native=json.loads(unreal.LyraWholeMainOracleLibrary.read_main_phases(json.dumps(request,separators=(',',':'))))
    native_sha=save('native',native)
    assert native['linkedInstances']==0 and native['state']==0 and native['elapsed']==0
    assert native['weights']==[1]+[0]*11
    for field in ('initialize','cacheBones','repeatedCacheBones'):
        assert native[field][0]=='node:85'
        assert 'self:FullBody_SkeletalControls' in native[field]
    assert 'node:7' in native['initialize'] and 'node:7' in native['cacheBones']
    assert 'node:7' not in native['repeatedCacheBones']
    assert not any('node:'+str(i) in native['initialize'] for i in (12,16,22))
    protect()
    save('closure',dict(schemaVersion=1,requestSha256=request_sha,nativeSha256=native_sha,
        engineVersion=unreal.SystemLibrary.get_engine_version(),engineSourceSha256=engine_sources,
        probeSourceSha256=sources,previousFixtureSha256=previous,protectedProject=protected,assetSha256=packages,
        scope=dict(originalFullMainRoot=True,initialSelf=True,fullLayout=True,
            repeatedCacheCounter=True,defaultFinalPoseEvaluated=False,rigConstructionCompared=False,
            providerFullPhases=False,assetsSaved=0,goalComplete=False)))
    unreal.log('LYRA_MAIN_PHASE_NATIVE_OK phases=3 linked=0 full_main=true assets_saved=0')
finally:protect()
