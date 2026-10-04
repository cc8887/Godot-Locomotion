"""Verify native merged queues, actual role transactions, and immutable evidence."""
import hashlib
import json
import re
import struct
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

from locomotion_paths import project_path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
load = lambda p: json.loads(p.read_bytes())

def read(p):
    data = p.read_bytes()
    if data.startswith((b'\xff\xfe', b'\xfe\xff')):
        return data.decode('utf-16')
    try:
        return data.decode('utf-8-sig')
    except UnicodeDecodeError:
        return data.decode('gb18030')

def compare(a, b, label='traces'):
    if isinstance(a, dict):
        assert a.keys() == b.keys(), label
        for k in a:
            compare(a[k], b[k], label + '/' + k)
    elif isinstance(a, list):
        assert len(a) == len(b), (label, len(a), len(b))
        for i, (x, y) in enumerate(zip(a, b)):
            compare(x, y, label + '/' + str(i))
    elif label.endswith('/current'):
        assert struct.pack('f', a) == struct.pack('f', b), (label, a, b)
    else:
        assert a == b, (label, a, b)

requests = load(root / 'montage_notify_queue_v1_requests.json')
native = load(root / 'montage_notify_queue_v1_native.json')
assert requests['schemaVersion'] == native['schemaVersion'] == 1
assert native['requestSha256'] == sha(root / 'montage_notify_queue_v1_requests.json')
for name, digest in requests['dependencies'].items():
    assert sha(root / name) == digest, name
for name, digest in native['pluginSourceSha256'].items():
    assert sha(repo / 'tools/unreal/LyraMontageNotifyOracle' / name) == digest, name
    if name.startswith('Source/'):
        assert sha(repo / 'artifacts/unreal/lyra-montage-notify-oracle/package-fourth' / name) == digest, name
assert native['scope'] == dict(assetsSaved=0, nativeHandleEventsAndPostUpdateAccepted=True,
    originalMontageAdvanceAccepted=False, originalClassGraphDispatchAccepted=False,
    fullMainContinuousAccepted=False, typedGameplayConsumersAccepted=False)
compare(load(logs / 'notify-montage-core-output-v2.json')['traces'], native['trace']['traces'])
assert len(requests['traces']) == 15
assert sum(len(t['frames']) for t in requests['traces'][:9]) == 7560
assert sum(len(t['frames']) for t in requests['traces']) == 33235
seen = {m['asset'] for t in requests['traces'] for f in t['frames'] for m in f['montages']}
assert len(seen) == 45 and seen == set(requests['assets'])
assert sum(len(a['tracks']) for a in native['trace']['assets']) == 60
assert sha(root / 'source_notify_queue_v1_requests.json') == '88c5a6a0a4b4da0c1b3641f2cb435a91f13695c496d84264be4c573df5785864'
assert sha(root / 'source_notify_queue_v1_native.json') == 'bece5650d4eed4b2b50e0225043000d7aa96fecd127c20c3d6851dbc460e11e2'
old_native = load(root / 'source_notify_queue_v1_native.json')
for name, digest in old_native['pluginSourceSha256'].items():
    assert sha(repo / 'tools/unreal/LyraNotifyOracle' / name) == digest, name
if '--native-only' in sys.argv:
    print('LYRA_MONTAGE_NOTIFY_NATIVE_COMPARE_OK frames=33235 assets=45 tracks=60')
    sys.exit(0)

for name in ('notify-montage-ue-second.log', 'notify-montage-ue-repeat.log'):
    text = read(logs / name)
    assert text.count('LYRA_MONTAGE_NOTIFY_NATIVE_OK traces=15 frames=33235 assets=45 tracks=60 packages=676 previous=818 assets_saved=0') == 1
    assert 'LYRA_EXPORT_PROCESS_EXIT_OK mode=montage-notify-queue code=0' in text
    assert 'Python script executed successfully' in text
    assert not re.search(r'LogPython: Error:|Fatal error:|Assertion failed:|Ensure condition failed:', text)
gates = {}
roles = {}
for config in ('debug', 'optimize'):
    build = read(logs / f'notify-montage-{config}-build-coverage-final.log')
    assert re.search(r'0\s*(个警告|Warning)', build) and re.search(r'0\s*(个错误|Error)', build)
    text = read(logs / f'notify-montage-{config}-matrix.log')
    assert not re.search(r'^\s*(ERROR|WARNING):', text, re.M)
    markers = re.findall(r'^LYRA_MONTAGE_NOTIFY_OK (.*)$', text, re.M)
    assert len(markers) == 1
    row = dict(p.split('=', 1) for p in markers[0].split())
    assert row['frames'] == '33235' and row['main'] == '7560' and row['assets'] == '45' and row['native'] == 'True'
    assert row['rebinds'] == '27' and int(row['reverse']) > 0 and int(row['hidden']) > 0
    assert int(row['retry']) > 32000 and int(row['rejected']) == int(row['retry']) * 2
    assert int(row['montage']) > 0 and int(row['source']) > 0 and int(row['callbacks']) > 0 and int(row['randomFrames']) > 0
    assert re.search(r'LYRA_NOTIFY_QUEUE_OK .* native=True production=false', text)
    assert re.search(r'LYRA_MONTAGE_SLOTS_GODOT_OK .* frames=13440 ', text)
    assert set(re.findall(r'LYRA_MONTAGE_PROCESS_EXIT label=(\S+) code=0', text)) == {
        'native-montage-queue', 'source-queue-regression', 'pairs-30', 'pairs-60', 'pairs-120',
        'ordinary-ten-rebind', 'slot-native-regression', 'main-native-regression'}
    if config == 'optimize':
        assert 'LYRA_MONTAGE_DEBUG_RESTORED hashVerified=true' in text
    gates[config] = row
    roles[config] = {}
    for hz in (30, 60, 120):
        r = load(logs / f'notify-montage-pairs-{config}-{hz}.json')
        assert r['roleFrames'] == hz * 8 * 6 and r['retries'] == hz * 8 * 3
        assert r['samePoseAndHistory'] and r['switches'] == 24 and r['replacements'] == 2
        assert r['activeRoles'] == 6 and r['lateFailures'] > 0 and r['montageFrames'] > 0
        assert r['montageNotifyQueue'] and r['montageNotifyCallbacks'] > 0 and r['sourceNotifyCallbacks'] > 0
        assert not r['nativeWholeMainParity'] and not r['productionAccepted']
        roles[config][hz] = r
    ten = load(logs / f'notify-montage-ten-{config}.json')
    model = ten['player']['model']
    assert ten['characters'] == 10 and len(ten['companions']) == 9
    assert model['sourceNotifyFrames'] == 4800 and model['sourceNotifyCallbacks'] > 0
    assert model['sourceNotifyQueue'] and model['montageNotifyQueue'] and not model['typedNotifyConsumers']
    assert model['frames'] == model['published'] == model['retries'] == 480 and model['lateFailures'] > 0
    assert model['maxSkinPositionM'] == 0 and not model['nativeWholeMainParity']
    assert all(c['published'] == 480 for c in ten['companions'])
    roles[config]['ten'] = ten
assert gates['debug'] == gates['optimize']
assert roles['debug'] == roles['optimize']
counts = ET.parse(logs / 'notify-montage-core-release.trx').getroot().find('.//{*}Counters').attrib
assert counts['failed'] == '0' and counts['passed'] == counts['total'] == '63'
bank_counts = ET.parse(logs / 'notify-montage-bank-release.trx').getroot().find('.//{*}Counters').attrib
assert bank_counts['failed'] == '0' and bank_counts['passed'] == bank_counts['total'] == '61'
backup = logs / 'notify-montage-debug-assemblies'
for name in ('GodotALS.dll', 'GodotALS.pdb', 'Als.Core.dll', 'Als.Core.pdb', 'Als.Import.dll', 'Als.Import.pdb'):
    assert sha(backup / name) == sha(repo / '.godot/mono/temp/bin/Debug' / name), name
contract = load(root / 'notify_contract_v1.json')
for group in ('dependencies', 'previousFixtureSha256'):
    for name, digest in contract[group].items():
        assert sha(root / name) == digest, name
for path, digest in contract['assetSha256'].items():
    assert sha(project_path('Content') / (path.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, path
result = dict(schemaVersion=1, queue=gates['debug'], requestSha256=native['requestSha256'],
    nativeSha256=sha(root / 'montage_notify_queue_v1_native.json'), releaseTests=124,
    roleFramesPerConfiguration=sum(roles['debug'][h]['roleFrames'] for h in (30, 60, 120)),
    roleMontageCallbacksPerConfiguration=sum(roles['debug'][h]['montageNotifyCallbacks'] for h in (30, 60, 120)),
    ordinaryFramesPerConfiguration=4800, protectedJson=len(contract['previousFixtureSha256']),
    protectedPackages=len(contract['assetSha256']), assetsSaved=0,
    montageQueueProductionIntegrated=True, mergedQueueNativeAccepted=True,
    originalClassLifecycleNativeAccepted=False, typedConsumersIntegrated=False,
    globalNativeInstanceIdsAccepted=False, fullMainAccepted=False, newRenderAccepted=False, newPerformanceAccepted=False)
report = logs / 'lyra-montage-notify-verification.json'
if report.exists():
    assert load(report) == result, 'Preserve previous validation evidence'
else:
    with report.open('x', encoding='utf-8', newline='\n') as stream:
        stream.write(json.dumps(result, indent=2, allow_nan=False) + '\n')
print('LYRA_MONTAGE_NOTIFY_VERIFICATION_OK ' + json.dumps(result, separators=(',', ':')))
