"""Verify the live notify export, original timing, identities and resource hashes."""
import hashlib
import json
import math
import re
import struct
from collections import Counter
from pathlib import Path

from locomotion_paths import project_path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'
content = project_path('Content')


def sha(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        while block := stream.read(1024 * 1024):
            digest.update(block)
    return digest.hexdigest()


def load(name):
    return json.loads((root / name).read_bytes())


def read(path):
    data = path.read_bytes()
    if data.startswith((b'\xff\xfe', b'\xfe\xff')):
        return data.decode('utf-16')
    try:
        return data.decode('utf-8-sig')
    except UnicodeDecodeError:
        return data.decode('gb18030')


f32 = lambda value: struct.unpack('f', struct.pack('f', value))[0]
contract = load('notify_contract_v1.json')
assert contract['schemaVersion'] == 1
assert contract['scope'] == dict(assetsSaved=0, runtimeIntegrated=False,
    nativeNotifyDispatchAccepted=False, blueprintDslIsEvidence=True, audioPlaybackDeferred=True)
for group in ('dependencies', 'previousFixtureSha256'):
    for name, digest in contract[group].items():
        assert sha(root / name) == digest, (group, name)
for path, digest in contract['assetSha256'].items():
    assert path.startswith('/Game/')
    assert sha(content / (path.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, path

assets = {a['source']: a for a in contract['assets']}
targets = {a['source']: a for a in contract['targets']}
assert len(assets) == len(contract['assets']) == 344
assert len(targets) == len(contract['targets']) == 300
montages = load('montage_catalog_v2.json')
assert {a['path'] for a in montages['assets']} <= assets.keys()
assert montages['sequences'].keys() <= assets.keys()
original_keys = ('index', 'name', 'notifyClass', 'notifyStateClass', 'time', 'duration',
    'triggerTime', 'endTriggerTime', 'triggerTimeOffset', 'endTriggerTimeOffset', 'track',
    'triggerWeightThreshold', 'triggerChance', 'filterType', 'filterLod',
    'canBeFilteredViaRequest', 'triggerOnDedicatedServer', 'triggerOnFollower', 'branchingPoint')


def original(events):
    return [{k: e[k] for k in original_keys} for e in events]


legacy_clips = legacy_events = 0
for name in ('unarmed_notifies.json', 'pistol_notifies.json', 'rifle_notifies.json',
             'unarmed_aux_notifies.json', 'unarmed_remaining_notifies.json'):
    for row in load(name)['clips']:
        assert original(assets[row['source']]['events']) == row['sourceEvents'], row['source']
        assert original(targets[row['target']]['events']) == row['targetEvents'], row['target']
        legacy_clips += 1
        legacy_events += len(row['sourceEvents'])
for row in montages['assets']:
    assert original(assets[row['path']]['events']) == row['notifies']['events'], row['path']
for path, row in montages['sequences'].items():
    assert original(assets[path]['events']) == row['notifies']['events'], path
assert (legacy_clips, legacy_events) == (189, 1450)

objects = {}
states = named = source_events = target_events = 0
counts = Counter()
duration_differences = []
for is_target, inventory in ((False, assets), (True, targets)):
    for path, asset in inventory.items():
        for index, event in enumerate(asset['events']):
            assert event['index'] == index
            assert math.isfinite(event['triggerTime']) and math.isfinite(event['endTriggerTime'])
            assert event['triggerTime'] == f32(event['time'] + event['triggerTimeOffset'])
            state = event['stateObject']
            notify = event['notifyObject']
            cls = event['notifyStateClass'] or event['notifyClass']
            assert not (state and notify)
            expected_end = f32(f32(event['triggerTime'] + event['duration']) + event['endTriggerTimeOffset']) if state else event['triggerTime']
            assert expected_end == event['endTriggerTime'], (path, index)
            if state or notify:
                identity = state or notify
                assert identity.startswith(path + ':'), (path, identity)
                assert objects.setdefault(identity, cls) == cls
                assert cls in contract['classes'] and event['objectNativeText'].strip()
                assert contract['classes'][cls]['nativeText'].strip()
                if cls.startswith('/Game/'):
                    assert cls in contract['blueprints']
            else:
                assert event['name'] in ('SaveAttack', 'ResetCombo') and not cls and not event['payload']
            if not is_target:
                source_events += 1
                states += bool(state)
                named += not cls
                counts[cls or '<named>'] += 1
                if event['duration'] != event['storedDuration']:
                    duration_differences.append(dict(asset=path, index=index,
                        linkedDuration=event['duration'], storedDuration=event['storedDuration']))
            else:
                target_events += 1
            if cls == '/Script/LyraGame.AnimNotify_LyraContextEffects':
                p = event['payload']
                assert p['effect'] in ('AnimEffect.Footstep.Walk', 'AnimEffect.Footstep.Land')
                assert p['socketName'] in ('foot_l', 'foot_r')
                assert len(p['traceEndOffset']) == len(p['locationOffset']) == len(p['vfxScale']) == 3
            if cls == '/Script/MotionWarping.AnimNotifyState_MotionWarping':
                assert event['payload']['rootMotionModifier'] and event['payload']['modifierNativeText'].strip()
assert (source_events, states, named) == (1515, 42, 6)
assert len(contract['classes']) == 10 and len(contract['blueprints']) == 7
assert len(duration_differences) == 1

for binding in contract['bindings']:
    a, b = assets[binding['source']], targets[binding['target']]
    assert a['length'] == b['length'] and original(a['events']) == original(b['events'])
    assert len(a['events']) == len(b['events'])
    for x, y in zip(a['events'], b['events'], strict=True):
        assert x['payload'] == y['payload'] and x['stateBehaviorFlags'] == y['stateBehaviorFlags']
        assert x['tickMode'] == y['tickMode']
assert len(contract['bindings']) == 345
aim_sources = {r['source'] for s in load('aiming_layer_v1_policy.json')['spaces'] for r in s['samples']}
assert len(aim_sources) == 45 and all(not assets[path]['events'] for path in aim_sources)
# Only fixed source-node assets are present here. AimOffset's dynamically
# selected assets have no authored events; their runtime context remains open.
assert len(contract['blendSpaces']) == 1
assert contract['blendSpaces'][0]['sync']['notifyMode'] == 1

marker = 'LYRA_NOTIFY_CONTRACT_OK assets=344 targets=300 events=1515 classes=10 blueprints=7 packages=676 previous=818 assets_saved=0'
for name in ('notify-contract-ue-duration.log', 'notify-contract-ue-repeat.log'):
    text = read(logs / name)
    assert text.count(marker) == 1
    assert 'LYRA_EXPORT_PROCESS_EXIT_OK mode=notify-contract code=0' in text
    assert 'Python script executed successfully' in text
    assert not re.search(r'LogPython: Error:|Fatal error:|Assertion failed:', text)

result = dict(schemaVersion=1, sha256=sha(root / 'notify_contract_v1.json'),
    sourceAssets=len(assets), targetAssets=len(targets), sourceEvents=source_events,
    targetEvents=target_events, stateEvents=states, namedEvents=named,
    uniqueObjects=len(objects), classes=10, blueprints=7, counts=dict(counts),
    legacyClips=legacy_clips, legacyEvents=legacy_events,
    bindings=345, protectedJson=818, protectedPackages=676,
    independentUeExports=2, assetsSaved=0, durationDifferences=duration_differences,
    aimSamplesWithoutNotifies=45, runtimeIntegrated=False, nativeNotifyDispatchAccepted=False)
report = logs / 'lyra-notify-contract-verification.json'
if report.exists():
    assert json.loads(report.read_bytes()) == result, 'Preserve verification evidence'
else:
    with report.open('x', encoding='utf-8', newline='\n') as stream:
        stream.write(json.dumps(result, indent=2, allow_nan=False) + '\n')
print('LYRA_NOTIFY_CONTRACT_VERIFICATION_OK ' + json.dumps(result, separators=(',', ':')))
