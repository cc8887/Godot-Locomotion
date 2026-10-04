"""Initialize/CacheBones of actual Lyra calls across four binding stages."""
import hashlib
import json
import os
from pathlib import Path
import unreal

repo = Path(__file__).resolve().parents[2]
assets = repo / 'assets/generated/lyra_als'
out = repo / 'artifacts/lyra-analysis'
tag = os.environ['LYRA_LAYER_FALLBACK_TAG']
assert tag.replace('-', '').isalnum()
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read = lambda p: json.loads(p.read_bytes())
project = Path(unreal.Paths.get_project_file_path()).parent
previous = {p.relative_to(assets).as_posix(): sha(p) for p in assets.rglob('*.json')}
protected = {p.relative_to(project).as_posix(): sha(p) for p in [project/'GASP58.uproject', *(project/'Config').rglob('*.ini')]}
packages = read(out/'whole-main-linked-montage-events-v1-30-full-closure.json')['assetSha256']
source = repo/'tools/unreal/LyraLayerPhasesOracle'
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

contracts = read(assets/'linked_layer_contracts.json')
request = dict(schemaVersion=1, main=contracts['classes']['main']['class'],
    provider=contracts['classes']['unarmed']['class'],
    mesh='/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny.SKM_Manny')
protect()
request_sha = save('requests', request)
native = json.loads(unreal.LyraWholeMainOracleLibrary.read_layer_fallback(json.dumps(request, separators=(',', ':'))))
save('native', native)
assert len(native['cases']) == 68
assert len(native['functions']) == 15
for row in native['cases']:
    valid=row.get('self') is not None
    assert row['rootInitializations']==row['rootCaches']==int(valid), row
    assert row['firstInitializations']==row['firstCaches']
    assert row['secondInitializations']==row['secondCaches']
    assert row['firstInitializations']>=int(row['inputs']>0)
    assert row['secondInitializations']>=int(row['inputs']>1)
    if valid: assert row['initializeOrder'][0]=='root'
    if row.get('self'):
        assert row['firstUpdates']==row['secondUpdates']==row['firstEvaluations']==row['secondEvaluations']==0
protect()
save('closure', dict(schemaVersion=1, requestSha256=request_sha, nativeSha256=sha(out/f'{tag}-native.json'),
    engineVersion=unreal.SystemLibrary.get_engine_version(), engineSourceSha256=engine_sources,
    probeSourceSha256=sources, previousFixtureSha256=previous, protectedProject=protected, assetSha256=packages,
    scope=dict(originalLinkedLayerNodes=True, originalSelfAndUnlink=True, controlledInputPoses=True,
        originalExternalInitializeCache=True,externalPoseEvaluated=False,
        fullMainEvaluated=False, fullMainInitializationIntegrated=False, assetsSaved=0, goalComplete=False)))
unreal.log('LYRA_LAYER_FALLBACK_NATIVE_OK cases=68 stages=4 initialize_cache=true assets_saved=0')
