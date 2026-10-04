"""Validate queue oracle, source lifecycle and actual character transaction evidence."""
import hashlib
import json
import re
import struct
import xml.etree.ElementTree as ET
from pathlib import Path

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
        assert len(a) == len(b), label
        for i, (x, y) in enumerate(zip(a, b)):
            compare(x, y, label + '/' + str(i))
    elif label.endswith('/current'):
        assert struct.pack('f', a) == struct.pack('f', b), (label, a, b)
    else:
        assert a == b, (label, a, b)

requests = load(root / 'source_notify_queue_v1_requests.json')
native = load(root / 'source_notify_queue_v1_native.json')
assert requests['schemaVersion'] == native['schemaVersion'] == 1
assert native['requestSha256'] == sha(root / 'source_notify_queue_v1_requests.json')
for name, digest in requests['dependencies'].items():
    assert sha(root / name) == digest, name
for name, digest in native['pluginSourceSha256'].items():
    assert sha(repo / 'tools/unreal/LyraNotifyOracle' / name) == digest, name
    if name.startswith('Source/'):
        assert sha(repo / 'artifacts/unreal/lyra-notify-oracle/package-sixth' / name) == digest, name
ownership = native['ownership']
assert ownership['mainCount'] == 20 and ownership['linkedCount'] == 0
assert ownership['mainTime'] == ownership['linkedTime'] == 1.5
assert ownership['linkedSeed'] == ownership['linkedSeedBefore'] == 0x05629063
assert native['scope'] == dict(assetsSaved=0, nativeQueueAccepted=True,
    originalClassGraphDispatchAccepted=False, fullMainContinuousAccepted=False, typedGameplayConsumersAccepted=False)
compare(load(logs / 'notify-queue-core-output.json')['traces'], native['trace']['traces'])
assert sum(len(t['frames']) for t in requests['traces'][:9]) == 7560
assert len(requests['traces'][9]['frames']) == 482
for name in ('notify-queue-ue-shared-context.log', 'notify-queue-ue-repeat.log'):
    text = read(logs / name)
    assert text.count('LYRA_NOTIFY_QUEUE_NATIVE_OK traces=10 frames=8042 mainQueue=20 linkedQueue=0 packages=676 previous=818 assets_saved=0') == 1
    assert 'LYRA_EXPORT_PROCESS_EXIT_OK mode=notify-queue code=0' in text
    assert 'Python script executed successfully' in text
    assert not re.search(r'LogPython: Error:|Fatal error:|Assertion failed:|Ensure condition failed:', text)

gates = {}
roles = {}
for config in ('debug', 'optimize'):
    build = read(logs / f'notify-queue-{config}-build-final.log')
    assert re.search(r'0\s*(个警告|Warning)', build) and re.search(r'0\s*(个错误|Error)', build)
    text = read(logs / f'notify-queue-{config}-matrix.log')
    assert not re.search(r'^\s*(ERROR|WARNING):', text, re.M)
    markers = re.findall(r'^LYRA_NOTIFY_QUEUE_OK (.*)$', text, re.M)
    assert len(markers) == 1
    row = dict(p.split('=', 1) for p in markers[0].split())
    assert row['frames'] == '8042' and row['states'] == '78' and row['named'] == '6'
    assert row['native'] == 'True' and row['production'] == 'false'
    assert row['requestSha256'] == native['requestSha256']
    assert row['begin'] == row['end'] == '160' and row['tick'] == '316'
    assert set(re.findall(r'LYRA_QUEUE_PROCESS_EXIT label=(\S+) code=0', text)) == {
        'native-source-queue', 'pairs-30', 'pairs-60', 'pairs-120', 'ordinary-ten-rebind', 'main-native-regression'}
    if config == 'optimize':
        assert 'LYRA_QUEUE_DEBUG_RESTORED hashVerified=true' in text
    gates[config] = row
    roles[config] = {}
    for hz in (30, 60, 120):
        r = load(logs / f'notify-queue-pairs-{config}-{hz}.json')
        assert r['roleFrames'] == hz * 8 * 6 and r['retries'] == hz * 8 * 3
        assert r['samePoseAndHistory'] and r['switches'] == 24 and r['replacements'] == 2
        assert r['activeRoles'] == 6 and r['lateFailures'] > 0 and r['montageFrames'] > 0
        assert not r['nativeWholeMainParity'] and not r['productionAccepted']
        roles[config][hz] = r
    ten = load(logs / f'notify-queue-ten-{config}.json')
    player = ten['player']
    model = player['model']
    assert ten['characters'] == 10 and len(ten['companions']) == 9
    assert player['switches'] == player['sameClassReuse'] == 6
    assert model['sourceNotifyFrames'] == 4800 and model['sourceNotifyCallbacks'] > 0
    assert model['sourceNotifyQueue'] and not model['montageNotifyQueue'] and not model['typedNotifyConsumers']
    assert model['frames'] == model['published'] == model['retries'] == 480 and model['lateFailures'] > 0
    assert model['maxSkinPositionM'] == 0 and not model['nativeWholeMainParity']
    assert all(c['published'] == 480 for c in ten['companions'])
    roles[config]['ten'] = model
assert gates['debug'] == gates['optimize']
for hz in (30, 60, 120):
    assert roles['debug'][hz] == roles['optimize'][hz], hz
assert roles['debug']['ten'] == roles['optimize']['ten']
counts = ET.parse(logs / 'notify-queue-core-release.trx').getroot().find('.//{*}Counters').attrib
assert counts['failed'] == '0' and counts['passed'] == counts['total'] == '62'
contract = load(root / 'notify_contract_v1.json')
for group in ('dependencies', 'previousFixtureSha256'):
    for name, digest in contract[group].items():
        assert sha(root / name) == digest, name
for path, digest in contract['assetSha256'].items():
    assert sha(Path('../GASP58/Content') / (path.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, path
result = dict(schemaVersion=1, source=gates['debug'], ownership=ownership, nativeQueueSha256=sha(root / 'source_notify_queue_v1_native.json'),
    releaseTests=62, roleFramesPerConfiguration=sum(roles['debug'][h]['roleFrames'] for h in (30, 60, 120)),
    ordinaryFramesPerConfiguration=4800, ordinaryCallbacks=roles['debug']['ten']['sourceNotifyCallbacks'],
    protectedJson=len(contract['previousFixtureSha256']), protectedPackages=len(contract['assetSha256']), assetsSaved=0,
    sourceQueueProductionIntegrated=True, sourceQueueNativeAccepted=True,
    originalClassLifecycleNativeAccepted=False, montageMerged=False, typedConsumersIntegrated=False,
    globalNativeInstanceIdsAccepted=False, fullMainAccepted=False, newRenderAccepted=False, newPerformanceAccepted=False)
report = logs / 'lyra-notify-queue-verification.json'
if report.exists():
    assert load(report) == result, 'Preserve previous validation evidence'
else:
    with report.open('x', encoding='utf-8', newline='\n') as stream:
        stream.write(json.dumps(result, indent=2, allow_nan=False) + '\n')
print('LYRA_NOTIFY_QUEUE_VERIFICATION_OK ' + json.dumps(result, separators=(',', ':')))
