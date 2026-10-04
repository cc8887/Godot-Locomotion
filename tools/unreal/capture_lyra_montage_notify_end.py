"""Read-only native four-container dispatch and original Montage producers."""
import hashlib
import json
import os
from pathlib import Path
import unreal

repo = Path(__file__).resolve().parents[2]
assets = repo / 'assets/generated/lyra_als'
out = repo / 'artifacts/lyra-analysis'
tag = os.environ['LYRA_NOTIFY_TERMINATION_TAG']
assert tag.replace('-', '').isalnum()
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read = lambda p: json.loads(p.read_bytes())
project = Path(unreal.Paths.get_project_file_path()).parent
previous = {p.relative_to(assets).as_posix(): sha(p) for p in assets.rglob('*.json')}
protected = {p.relative_to(project).as_posix(): sha(p) for p in [project/'GASP58.uproject', *(project/'Config').rglob('*.ini')]}
closure = read(out/'whole-main-linked-montage-events-v1-30-full-closure.json')
packages = closure['assetSha256']
source = repo/'tools/unreal/LyraMontageNotifyEndOracle'
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

catalog = read(assets/'montage_catalog_v2.json')
indices = (0, 1)
def state(i, instance=1, direct=True, context=True, trigger=True, original=False, asset=0, notify=0):
    return dict(id=i, instance=instance, direct=direct, context=context, trigger=trigger, original=original, asset=asset, notify=notify)
reverse=[state(0),state(1,2),state(2),state(3,direct=False),state(4)]
cases=[dict(mode='reverse',states=reverse),dict(mode='filtered',states=[state(0,trigger=False),state(1,direct=False),state(2,context=False)]),
 dict(mode='no-context',states=[state(0,context=False)]),dict(mode='clear',states=[state(0),state(1)]),
 dict(mode='nested',states=[state(0,2),state(1),state(2)]),dict(mode='append',states=[state(0),state(1,2)]),
 dict(mode='rebind',states=[state(0)]),dict(mode='queued',states=reverse),
 dict(mode='original',states=[state(0,original=True)]),
 dict(mode='original-concurrent',states=[state(0,original=True),state(1,2,original=True)]),
 dict(mode='original-mw',states=[state(0,original=True,asset=1,notify=0),state(1,original=True,asset=1,notify=1),state(2,direct=False)])]
request=dict(schemaVersion=1,mesh='/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny.SKM_Manny',catalogIndices=indices,
 assets=[catalog['assets'][i]['path'] for i in indices],sequence=catalog['assets'][0]['slots'][0]['segments'][0]['animation'],cases=cases)
protect()
request_sha = save('requests', request)
native = json.loads(unreal.LyraWholeMainOracleLibrary.read_montage_notify_end(json.dumps(request, separators=(',', ':'))))
assert len(native['cases'])==len(cases)
protect()
save('native', dict(schemaVersion=1, requestSha256=request_sha, engineVersion=unreal.SystemLibrary.get_engine_version(),
    catalogSha256=sha(assets/'montage_catalog_v2.json'), probeSourceSha256=sources, previousFixtureSha256=previous,
    protectedProject=protected, assetSha256=packages, trace=native,
    scope=dict(originalMontageEndedFunction=True,controlledActiveStates=True,originalStateObjects=True,
        originalStateAudioPlayback=False,fullNotifyDispatch=False,assetsSaved=0,goalComplete=False)))
unreal.log(f'LYRA_NOTIFY_TERMINATION_NATIVE_OK cases={len(cases)} assets_saved=0')
