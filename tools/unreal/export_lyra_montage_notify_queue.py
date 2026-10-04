"""Actual HandleEvents/Proxy.PostUpdate queue primitives; no asset saves."""
import hashlib
import json
import os
import struct
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
plugin_source = Path(os.environ['LYRA_MONTAGE_NOTIFY_PLUGIN_SOURCE'])
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
load = lambda p: json.loads(p.read_bytes())
f32 = lambda x: struct.unpack('<f', struct.pack('<f', x))[0]
fixture_version = os.environ.get('LYRA_MONTAGE_NOTIFY_FIXTURE_VERSION', 'v2')
assert fixture_version in ('v1', 'v2'), 'Unknown immutable queue fixture version'
requests_path = root / f'montage_notify_queue_{fixture_version}_requests.json'
requests = load(requests_path)
request_digest = sha(requests_path)
assert requests['schemaVersion'] == 1 and len(requests['assets']) == 45
contract = load(root / 'notify_contract_v1.json')
content = Path(unreal.Paths.project_content_dir())

def protected():
    for name, digest in {**contract['dependencies'], **contract['previousFixtureSha256'], **requests['dependencies']}.items():
        assert sha(root / name) == digest, name
    for path, digest in contract['assetSha256'].items():
        assert sha(content / (path.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, path

protected()
source_hashes = {p.relative_to(plugin_source).as_posix(): sha(p)
    for p in sorted(plugin_source.rglob('*')) if p.is_file() and p.suffix in ('.h', '.cpp', '.cs', '.uplugin')
    and not any(part in ('Intermediate', 'Binaries') for part in p.relative_to(plugin_source).parts)}
trace = json.loads(unreal.LyraMontageNotifyOracleLibrary.read_trace(requests_path.read_text(encoding='utf-8')))
assert len(trace['traces']) == len(requests['traces']) == 15
assert all(len(a['frames']) == len(b['frames']) for a, b in zip(trace['traces'], requests['traces']))
catalog = load(root / 'montage_catalog_v2.json')
assert [a['path'] for a in catalog['assets']] == requests['assets'] == [a['asset'] for a in trace['assets']]
tracks = 0
for original, actual in zip(catalog['assets'], trace['assets']):
    assert len(original['slots']) == len(actual['tracks'])
    for slot, native in zip(original['slots'], actual['tracks']):
        segment = slot['segments'][0]
        assert slot['name'] == native['slot'] and segment['animation'] == native['asset']
        # GetValidPlayRate includes the Sequence RateScale, absent from raw segment metadata.
        # A disagreement is a resource contract gap, never a silently assumed rate.
        assert f32(segment['clipRate']) == f32(native['rate']), (actual['asset'], native)
        assert f32(segment['clipStart']) == f32(native['start']) and f32(segment['clipEnd']) == f32(native['end'])
        expected_length = f32(f32(f32(segment['clipEnd']) - f32(segment['clipStart'])) / f32(segment['clipRate']))
        assert expected_length == f32(native['length']), (actual['asset'], native)
        tracks += 1
protected()
assert sha(requests_path) == request_digest
result = dict(schemaVersion=1, requestSha256=request_digest, pluginSourceSha256=source_hashes,
    engineVersion=unreal.SystemLibrary.get_engine_version(), trace=trace,
    scope=dict(assetsSaved=0, nativeHandleEventsAndPostUpdateAccepted=True,
        originalMontageAdvanceAccepted=False, originalClassGraphDispatchAccepted=False,
        fullMainContinuousAccepted=False, typedGameplayConsumersAccepted=False))
output = root / f'montage_notify_queue_{fixture_version}_native.json'
if output.exists():
    assert load(output) == result, 'Independent native Montage queue output differs'
else:
    with output.open('x', encoding='utf-8', newline='\n') as stream:
        stream.write(json.dumps(result, separators=(',', ':'), allow_nan=False) + '\n')
count = sum(len(t['frames']) for t in trace['traces'])
unreal.log(f'LYRA_MONTAGE_NOTIFY_NATIVE_OK traces=15 frames={count} assets=45 tracks={tracks} packages={len(contract["assetSha256"])} previous={len(contract["previousFixtureSha256"])} assets_saved=0')
