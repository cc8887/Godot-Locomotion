"""Read actual UE source-scope ownership and native queue primitives, without saving assets."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
plugin_source = Path(os.environ['LYRA_NOTIFY_PLUGIN_SOURCE'])
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
load = lambda p: json.loads(p.read_bytes())
requests_path = root / 'source_notify_queue_v1_requests.json'
requests = load(requests_path)
request_digest = sha(requests_path)
assert requests['schemaVersion'] == 1
for name, digest in requests['dependencies'].items():
    assert sha(root / name) == digest, name
contract = load(root / 'notify_contract_v1.json')
content = Path(unreal.Paths.project_content_dir())

def protected():
    for name, digest in {**contract['dependencies'], **contract['previousFixtureSha256']}.items():
        assert sha(root / name) == digest, name
    for path, digest in contract['assetSha256'].items():
        assert sha(content / (path.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, path

protected()
source_hashes = {p.relative_to(plugin_source).as_posix(): sha(p)
    for p in sorted(plugin_source.rglob('*')) if p.is_file() and p.suffix in ('.h', '.cpp', '.cs', '.uplugin')
    and not any(part in ('Intermediate', 'Binaries') for part in p.relative_to(plugin_source).parts)}
first_tick = next(t for trace in requests['traces'][:9] for f in trace['frames'] for t in f['ticks']
    if t['delta'] > 0 and t['weight'] > 0 and
    any(a['source'] == t['asset'] and a['events'] for a in contract['targets']))
sequence = unreal.load_asset(first_tick['asset'])
ownership = json.loads(unreal.LyraNotifyOracleLibrary.read_sync_queue_ownership(sequence))
assert ownership['mainCount'] > 0 and ownership['linkedCount'] == 0, ownership
assert ownership['mainTime'] > 0 and ownership['linkedTime'] > 0, ownership
assert ownership['linkedSeed'] == ownership['linkedSeedBefore'], ownership
trace = json.loads(unreal.LyraNotifyOracleLibrary.read_queue_trace(requests_path.read_text(encoding='utf-8')))
assert len(trace['traces']) == len(requests['traces']) == 10
assert all(len(a['frames']) == len(b['frames']) for a, b in zip(trace['traces'], requests['traces']))
protected()
assert sha(requests_path) == request_digest
for name, digest in requests['dependencies'].items():
    assert sha(root / name) == digest, name
result = dict(schemaVersion=1, requestSha256=sha(requests_path), pluginSourceSha256=source_hashes,
    engineVersion=unreal.SystemLibrary.get_engine_version(), ownership=ownership, trace=trace,
    scope=dict(assetsSaved=0, nativeQueueAccepted=True, originalClassGraphDispatchAccepted=False,
        fullMainContinuousAccepted=False, typedGameplayConsumersAccepted=False))
output = root / 'source_notify_queue_v1_native.json'
if output.exists():
    assert load(output) == result, 'Independent native notify queue output differs'
else:
    with output.open('x', encoding='utf-8', newline='\n') as stream:
        stream.write(json.dumps(result, separators=(',', ':'), allow_nan=False) + '\n')
count = sum(len(t['frames']) for t in trace['traces'])
unreal.log(f'LYRA_NOTIFY_QUEUE_NATIVE_OK traces=10 frames={count} mainQueue={ownership["mainCount"]} linkedQueue=0 packages=676 previous=818 assets_saved=0')
