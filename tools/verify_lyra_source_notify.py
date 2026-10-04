"""Check source-notify execution evidence and preserve the authored assets."""
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read(path):
    data = path.read_bytes()
    if data.startswith((b'\xff\xfe', b'\xfe\xff')):
        return data.decode('utf-16')
    try:
        return data.decode('utf-8-sig')
    except UnicodeDecodeError:
        return data.decode('gb18030')


modes = json.loads((root / 'notify_source_modes_v1.json').read_bytes())
assert modes['schemaVersion'] == 1 and modes['assetsSaved'] == 0
assert len(modes['spaces']) == 4 and all(s['notifyMode'] == 1 for s in modes['spaces'])
for name, digest in modes['dependencies'].items():
    assert sha(root / name) == digest, name
for name in ('notify-source-modes-ue-first.log', 'notify-source-modes-ue-repeat.log'):
    text = read(logs / name)
    assert text.count('LYRA_NOTIFY_SOURCE_MODES_OK spaces=4 assets_saved=0') == 1
    assert 'LYRA_EXPORT_PROCESS_EXIT_OK mode=notify-source-modes code=0' in text
    assert 'Python script executed successfully' in text
    assert not re.search(r'LogPython: Error:|Fatal error:|Assertion failed:', text)

gates = {}
for config in ('debug', 'optimize'):
    text = read(logs / f'notify-source-{config}-matrix.log')
    assert not re.search(r'^\s*(ERROR|WARNING):', text, re.M)
    markers = re.findall(r'^LYRA_SOURCE_NOTIFY_OK (.*)$', text, re.M)
    assert len(markers) == 1
    row = dict(pair.split('=', 1) for pair in markers[0].split())
    assert row['frames'] == '7560' and row['classChanges'] == '27'
    assert row['definitions'] == '2975' and row['assets'] == '644'
    assert row['mainNodes'] == '12,16,22' and row['typedKinds'] == '11'
    assert row['nativeJoint'] == row['production'] == row['queue'] == 'false'
    assert set(re.findall(r'LYRA_NOTIFY_PROCESS_EXIT label=(\S+) code=0', text)) == {
        'source-notify', 'main-native-regression', 'ordinary-rebind-regression'}
    if config == 'optimize':
        assert 'LYRA_NOTIFY_DEBUG_RESTORED hashVerified=true' in text
    gates[config] = row
assert gates['debug'] == gates['optimize'], 'Debug/Optimize source boundaries differ'

tests = {}
for suite in ('core', 'import'):
    tree = ET.parse(logs / f'notify-source-{suite}-release.trx')
    counts = tree.getroot().find('.//{*}Counters').attrib
    assert counts['failed'] == '0' and counts['passed'] == counts['total']
    tests[suite] = int(counts['passed'])

contract = json.loads((root / 'notify_contract_v1.json').read_bytes())
for name, digest in contract['previousFixtureSha256'].items():
    assert sha(root / name) == digest, name
for path, digest in contract['assetSha256'].items():
    package = Path('../GASP58/Content') / (path.split('.')[0].removeprefix('/Game/') + '.uasset')
    assert sha(package) == digest, path

result = dict(schemaVersion=1, source=gates['debug'], releaseTests=tests,
              modesSha256=sha(root / 'notify_source_modes_v1.json'),
              notifySha256=sha(root / 'notify_contract_v1.json'),
              protectedJson=len(contract['previousFixtureSha256']),
              protectedPackages=len(contract['assetSha256']),
              independentPolicyExports=2, nativeNotifyDispatchAccepted=False,
              productionIntegrated=False, assetsSaved=0)
report = logs / 'lyra-source-notify-verification.json'
if report.exists():
    assert json.loads(report.read_bytes()) == result, 'Preserve previous verification evidence'
else:
    with report.open('x', encoding='utf-8', newline='\n') as stream:
        stream.write(json.dumps(result, indent=2, allow_nan=False) + '\n')
print('LYRA_SOURCE_NOTIFY_VERIFICATION_OK ' + json.dumps(result, separators=(',', ':')))
