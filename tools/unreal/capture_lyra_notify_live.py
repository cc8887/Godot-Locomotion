"""Read-only native four-container dispatch and original Montage producers."""
import hashlib
import json
import os
from pathlib import Path
import unreal

repo = Path(__file__).resolve().parents[2]
assets = repo / 'assets/generated/lyra_als'
out = repo / 'artifacts/lyra-analysis'
tag = os.environ['LYRA_NOTIFY_LIVE_TAG']
assert tag.replace('-', '').isalnum()
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read = lambda p: json.loads(p.read_bytes())
project = Path(unreal.Paths.get_project_file_path()).parent
previous = {p.relative_to(assets).as_posix(): sha(p) for p in assets.rglob('*.json')}
protected = {p.relative_to(project).as_posix(): sha(p) for p in [project/'GASP58.uproject', *(project/'Config').rglob('*.ini')]}
closure = read(out/'whole-main-linked-montage-events-v1-30-full-closure.json')
packages = closure['assetSha256']
source = repo/'tools/unreal/LyraNotifyLiveOracle'
sources = {p.relative_to(source).as_posix(): sha(p) for p in source.rglob('*') if p.is_file()}

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

def save(kind, value):
    path = out/f'{tag}-{kind}.json'
    with path.open('x', encoding='utf-8', newline='\n') as f:
        f.write(json.dumps(value, separators=(',', ':'), allow_nan=False)+'\n')
    return sha(path)

def state(policy,handle=None,instance=None,sourceKind=1,source=1):
    return dict(policy=policy,handle=policy if handle is None else handle,instance=4000+policy if instance is None else instance,sourceKind=sourceKind,source=source)
def case(action,before=(),queued=(),mode=3,skipGraph=False,skippedPolicy=-1,**kw):
    return dict(action=action,before=list(before),queued=list(queued),mode=mode,skipGraph=skipGraph,skippedPolicy=skippedPolicy,**kw)
cases=[
 case('ordinary',[state(0),state(1),state(2)],[state(0,9),state(4),state(3)]),
 case('begin-clear',[state(0)],[state(1)]),
 case('tick-clear',[],[state(0),state(1)]),
 case('tick-append',[],[state(0)],append=state(2)),
 case('end-append',[state(0)],[state(1)],append=state(2)),
 case('instant-clear',[state(0)],[state(5,instance=-1),state(4),state(0,9)]),
 case('nested-tick',[],[state(0)],child=[state(1)]),
 case('nested-begin',[state(0)],[state(1)],child=[state(2)]),
 case('filtered',[],[state(0),state(1)],skippedPolicy=0),
 case('skip-graph',[state(0),state(1,sourceKind=2)],[],mode=0,skipGraph=True),
 case('forced-montage',[state(0),state(1,sourceKind=2)],[state(0,9),state(1,8,sourceKind=2)],mode=2),
 case('concurrent',[state(2)],[state(2,9,source=2),state(2,8,sourceKind=2)]),
 case('end-all',[state(0),state(1)],[],mode=4,skippedPolicy=0),
]
request=dict(schemaVersion=1,mesh='/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny.SKM_Manny',cases=cases)
protect()
request_sha = save('requests', request)
native = json.loads(unreal.LyraWholeMainOracleLibrary.read_notify_live(json.dumps(request, separators=(',', ':'))))
assert len(native['cases'])==len(cases)
protect()
save('native', dict(schemaVersion=1, requestSha256=request_sha, engineVersion=unreal.SystemLibrary.get_engine_version(),
    probeSourceSha256=sources, previousFixtureSha256=previous,
    protectedProject=protected, assetSha256=packages, trace=native,
    scope=dict(originalTriggerAnimNotifies=True,callbackTimeInventory=True,controlledStates=True,
        originalBlueprintStateBodies=False,worldTeardownAccepted=False,assetsSaved=0,goalComplete=False)))
unreal.log(f'LYRA_NOTIFY_LIVE_NATIVE_OK cases={len(cases)} assets_saved=0')
