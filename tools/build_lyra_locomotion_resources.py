"""Build a compact immutable address space from validated original captures."""
import hashlib
import json
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
root = repo/'assets/generated/lyra_als'
sha = lambda data: hashlib.sha256(data).hexdigest()
previous = {p:sha(p.read_bytes()) for p in root.rglob('*.json')}
inputs = {}
def load(name):
    data = (root/name).read_bytes(); inputs[name] = sha(data)
    return json.loads(data)
assets = {}
dependencies = {}
for name in ('main_ground_scope_v2_native.json','air_runtime_native.json','idle_runtime_v2_native.json'):
    native = load(name)
    for path,digest in native['dependencies'].items():
        assert dependencies.get(path,digest) == digest
        assert sha((root/path).read_bytes()) == digest
        dependencies[path] = digest
    for row in native['assets']:
        assert assets.get(row['path'],row) == row, row['path']
        assets[row['path']] = row
    del native
roots = {}
for name in ('main_state_history_roots.json','pivot_runtime_roots.json','air_runtime_roots.json','idle_runtime_v2_roots.json'):
    for path,row in load(name)['assets'].items():
        assert roots.get(path,row) == row, path
        roots[path] = row
assert set(roots) == set(assets) and len(assets) == 194
providers = {p:{} for p in ('unarmed','pistol','rifle')}
for name,key in (('main_start_lean_requests.json','start'),('main_stop_runtime_requests.json','stop'),
                 ('main_pivot_requests.json','pivot'),('air_runtime_requests.json','air'),('idle_runtime_v2_requests.json','idle')):
    request = load(name)
    for profile,provider in providers.items():
        traces = [t for t in request['traces'] if t['profile'] == profile]
        assert traces and all(t['bindings'] == traces[0]['bindings'] for t in traces)
        provider[key] = traces[0]['bindings']
        if key in ('air','idle'):
            assert provider.get('class',traces[0]['class']) == traces[0]['class']
            provider['class'] = traces[0]['class']
        if key == 'idle':
            assert all(t['breaks'] == traces[0]['breaks'] for t in traces)
            provider['idleBreaks'] = traces[0]['breaks']
        def paths(value):
            if isinstance(value,dict):
                for child in value.values():yield from paths(child)
            elif isinstance(value,list):
                for child in value:yield from paths(child)
            else:yield value
        assert all(path in assets for path in paths(provider[key]))
dependencies.update({name:digest for name,digest in inputs.items() if not name.endswith('_native.json')})
payload = dict(schemaVersion=1,dependencies=dependencies,provenance=inputs,
    assets=list(assets.values()),providers=providers,compressedRoots=dict(schemaVersion=1,assets=roots),
    groups=['Locomotion','Stop','Test'],scope='Original ten Locomotion Provider roots; source state belongs to each character instance.')
target = root/'locomotion_resources.json'
encoded = json.dumps(payload,separators=(',',':'),ensure_ascii=False).encode('utf-8')
if target.exists():
    assert target.read_bytes() == encoded, 'Existing locomotion resource contract differs'
else:
    target.write_bytes(encoded)
assert all(sha(p.read_bytes()) == digest for p,digest in previous.items())
report = dict(schemaVersion=1,resourceSha256=sha(encoded),resourceBytes=len(encoded),sequenceCount=len(assets),
    providers=list(providers),groups=payload['groups'],inputs=inputs,previousFixtureSha256={str(p.relative_to(root)).replace('\\','/'):v for p,v in previous.items()},
    wholeMain=False,production=False)
path = repo/'artifacts/lyra-analysis/locomotion-resources-generation.json'
path.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print(f'LYRA_LOCOMOTION_RESOURCES_GENERATED sequences=194 groups=3 providers=3 bytes={len(encoded)} previous={len(previous)}')
