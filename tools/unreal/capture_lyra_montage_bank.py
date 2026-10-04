"""Read-only native four-container dispatch and original Montage producers."""
import hashlib
import json
import os
from pathlib import Path
import unreal

repo = Path(__file__).resolve().parents[2]
assets = repo / 'assets/generated/lyra_als'
out = repo / 'artifacts/lyra-analysis'
tag = os.environ['LYRA_MONTAGE_BANK_TAG']
assert tag.replace('-', '').isalnum()
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read = lambda p: json.loads(p.read_bytes())
project = Path(unreal.Paths.get_project_file_path()).parent
previous = {p.relative_to(assets).as_posix(): sha(p) for p in assets.rglob('*.json')}
protected = {p.relative_to(project).as_posix(): sha(p) for p in [project/'GASP58.uproject', *(project/'Config').rglob('*.ini')]}
closure = read(out/'whole-main-linked-montage-events-v1-30-full-closure.json')
packages = closure['assetSha256']
source = repo/'tools/unreal/LyraMontageBankOracle'
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
indices = (0, 34)
traces = []
for hz in (30, 60, 120):
    for mode in ('ended-play', 'out-stop', 'out-play', 'captured-rebind'):
        frames = [dict(delta=1/hz, plays=[0] if mode == 'ended-play' else [0, 1], stop=False)]
        frames += [dict(delta=1/hz, plays=[], stop=mode != 'ended-play' and i == hz//4)
                   for i in range(1, hz*7)]
        traces.append(dict(name=f'{mode}-{hz}', mode=mode, hz=hz, frames=frames))
request = dict(schemaVersion=1, mesh='/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny.SKM_Manny',
    catalogIndices=indices, assets=[catalog['assets'][i]['path'] for i in indices], traces=traces)
protect()
request_sha = save('requests', request)
native = json.loads(unreal.LyraWholeMainOracleLibrary.read_montage_bank_callbacks(json.dumps(request, separators=(',', ':'))))
assert len(native['traces']) == len(traces)
assert all(len(t['frames']) == len(q['frames']) for q, t in zip(traces, native['traces'], strict=True))
protect()
save('native', dict(schemaVersion=1, requestSha256=request_sha, engineVersion=unreal.SystemLibrary.get_engine_version(),
    catalogSha256=sha(assets/'montage_catalog_v2.json'), probeSourceSha256=sources, previousFixtureSha256=previous,
    protectedProject=protected, assetSha256=packages, trace=native,
    scope=dict(originalFourContainers=True, originalMontageProducers=True, controlledQueuePayloads=False,
        originalResourceNotifySideEffects=False, committedCallbackBankMutation=True, immediateCallbackBankMutation=False, assetsSaved=0, goalComplete=False)))
unreal.log(f'LYRA_MONTAGE_BANK_NATIVE_OK traces={len(traces)} frames={sum(len(t["frames"]) for t in native["traces"])} assets_saved=0')
