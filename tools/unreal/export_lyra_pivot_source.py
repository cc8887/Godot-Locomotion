"""Original PivotA/B callbacks on one linked instance and one native Sync.

The caller supplies visits/weights and Main observations. This capture does
not substitute those observations for a complete PivotSM or Main graph.
"""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
run_machine = os.environ.get('LYRA_PIVOT_MACHINE') == '1'
reentry = os.environ.get('LYRA_PIVOT_REENTRY') == '1'
if reentry and not run_machine:
    raise ValueError('Reentry capture requires the actual machine')
prefix = 'pivot_machine_reentry' if reentry else 'pivot_machine' if run_machine else 'pivot_source'
sha = lambda data: hashlib.sha256(data).hexdigest()
names = ('source_nodes.json', 'linked_layer_inventory.json', 'runtime_graph.json',
         'logical_controls/catalog.json', 'logical_controls/calibration.json',
         'locomotion_layer_closures.json', 'locomotion_extras/catalog.json')
files = {name: (root/name).read_bytes() for name in names}
nodes, inventory, machines, catalog, calibration, closures, extras = map(json.loads, files.values())
packages = dict(calibration['assetSha256'])
for data in (nodes, closures, extras):
    packages.update(data['assetSha256'])
content = Path(unreal.Paths.project_content_dir())
old_files = {str(p.relative_to(root)).replace('\\', '/'): sha(p.read_bytes()) for p in root.rglob('*.json')
             if not p.name.startswith(prefix+'_')}
# A repeated capture protects the same preceding fixture set even after later
# stages add resources. Do not rewrite an immutable manifest to include them.
if (root/(prefix+'_native.json')).exists():
    old_files = json.loads((root/(prefix+'_native.json')).read_bytes())['previousFixtureSha256']

def protect():
    for path, expected in packages.items():
        if sha((content/(path.split('.')[0].removeprefix('/Game/')+'.uasset')).read_bytes()) != expected:
            raise ValueError('Changed Pivot package: '+path)
    for path, expected in old_files.items():
        if sha((root/path).read_bytes()) != expected:
            raise ValueError('Changed previous fixture: '+path)

def save(name, data):
    path = root/name
    if path.exists():
        if json.loads(path.read_bytes()) != data:
            raise ValueError('Immutable Pivot fixture differs: '+name)
    else:
        path.write_text(json.dumps(data, separators=(',', ':')), encoding='utf-8')
    return sha(path.read_bytes())

protect()
targets = {}
for entry in catalog['entries']:
    targets.setdefault(entry['source'], entry['target'])
traces, definitions = [], {}
for profile in ('unarmed', 'pistol', 'rifle'):
    owner = nodes['classes'][profile]
    source = sorted((s for s in owner['sources'] if s['functions']['update'] == 'UpdatePivotAnim'),
                    key=lambda s: s['nodeIndex'])
    machine = next(m for m in machines['classes'][profile]['machines'] if m['machineName'] == 'PivotSM')
    if len(source) != 2 or [s['playerNodeIndices'] for s in machine['states']] != [[s['nodeIndex']] for s in source]:
        raise ValueError('Changed original Pivot pair topology')
    rules = {s['transitions'][0]['canTakeDelegateIndex'] for s in machine['states']}
    if len(rules) != 1 or any(s['role'] != 2 for s in source):
        raise ValueError('Changed Pivot rule or AlwaysLeader role')
    bindings = {group: {direction: targets[path] for direction, path in inventory['classes'][profile]['cardinals'][group].items()}
                for group in ('Jog_Pivot_Cardinals', 'ADS_Pivot_Cardinals', 'Crouch_Pivot_Cardinals')}
    for group in bindings.values():
        for path in group.values():
            if path in definitions:
                continue
            seq = unreal.load_asset(path)
            text = unreal.AlsLyraGraphLibrary.read_distance_sequence_data(seq, 'Distance')
            if not text:
                raise ValueError('Missing actual Pivot distance codec: '+path)
            data = json.loads(text)
            data['markers'] = json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(seq))['markers']
            definitions[path] = data
    for hz in (30, 60, 120):
        frames = []
        for i in range(hz*6):
            cell, local = divmod(i, hz//2)
            selector = cell if local < hz//4 else (cell+5)%12
            acceleration = ((101.123456789, -57.456789123, 0), (-42.123456789, 27.789123456, 0),
                            (0, 0, 0), (1e-9, -2e-9, 0))[local%4]
            matching = local%8 not in (2, 5)
            velocity = [-a*(1.713 if matching else -2.125) for a in acceleration]
            movement_acc = ((0, 0, -800), (-512.123456789, 123.789123456, 64),
                            (2345.123456789, -500.987654321, -99), (1e-9, 0, 150))[local%4]
            movement_velocity = ((511.123456789, -98.123456789, 789), (311.123456789, -150.456789123, -901),
                                 (-700.123456789, 198.987654321, 222), (-1e-9, 20, 300))[local%4]
            frames.append({'delta': 0 if local==3 else 1e-9 if local==4 else 1/hz,
                'order': [0, 1] if cell%2==0 else [1, 0],
                'sources': [{'active': local!=hz//2-1 and (s==cell%2 or local>0),
                             'weight': 1e-6 if local==7 else ((.73, .27) if cell%2==0 else (.27, .73))[s],
                             'reinitialize': local==0 or (cell in (2, 6) and local==5+s) or
                                             (cell==8 and local==hz//2-1)} for s in range(2)],
                'main': {'IsCrouching': selector>=8, 'GameplayTag_IsADS': 4<=selector<8,
                         'CardinalDirectionFromAcceleration': selector%4,
                         'LocalAcceleration2D': list(acceleration), 'LocalVelocity2D': velocity,
                         'PivotInitialDirection': 0 if cell%2==0 else 2,
                         'LocalVelocityDirection': (2 if cell%2==0 else 0) if local%5==0 else (0 if cell%2==0 else 2),
                         'LastPivotTime': (.2, 0, -.01, 1e-9)[(local//3)%4],
                         'DisplacementSinceLastUpdate': (-1, 0, .0001, .5, 4, 20, 1000)[local%7]},
                'movement': {'acceleration': list(movement_acc), 'lastUpdateVelocity': list(movement_velocity),
                             'groundFriction': (0, 7.125, .6789123, -1)[local%4]}})
            if run_machine:
                frame = frames[-1]
                del frame['order'], frame['sources']
                frame.update(active=local < hz//2-2 and not (cell==6 and 8<=local<12),
                             weight=1e-6 if local in (7, 8) else (.73 if cell%2==0 else .27),
                             reinitialize=(cell in (0, 4, 8) and local==0) or
                                          (cell==5 and local==hz//2-1))
                if reentry and local==1:
                    # Keep the previous provider acceleration, initialize the
                    # machine, then evaluate the original opposite-direction rule.
                    frame['reinitialize'] = True
        traces.append({'profile': profile, 'class': owner['class'], 'nodeIndices': [s['nodeIndex'] for s in source],
                       'ruleIndex': next(iter(rules)), 'hz': hz, 'bindings': bindings, 'frames': frames})
        if run_machine:
            closure = closures['providers'][profile]['layers']['FullBody_PivotState']
            # The baked state machine index and actual compiled node are separate
            # identities. Bind the original compiled node from the exported graph.
            traces[-1]['machineNode'] = next(n['index'] for n in closure['nodes']
                if n['type']=='/Script/Engine.AnimNode_StateMachine' and
                   n['settings']['stateMachineIndexInClass']==machine['machineIndex'])
requests = {'schemaVersion': 1, 'traces': traces}
if run_machine:
    text = unreal.AlsLyraGraphLibrary.read_pivot_machine_trace(unreal.load_class(None, nodes['classes']['main']['class']),
        unreal.load_asset('/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny'), json.dumps(requests, separators=(',', ':')))
else:
    text = unreal.AlsLyraGraphLibrary.read_pivot_source_trace(unreal.load_class(None, nodes['classes']['main']['class']),
        unreal.load_asset('/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny'), json.dumps(requests, separators=(',', ':')))
if not text:
    raise ValueError('Empty original Pivot trace')
native = json.loads(text)
if len(native['traces']) != 9:
    raise ValueError('Incomplete original Pivot traces')
policies = {}
for trace in native['traces']:
    profile = trace['profile']
    if profile in policies and policies[profile] != trace['policy']:
        raise ValueError('Changed Pivot policy')
    policies[profile] = trace['policy']
for path, definition in definitions.items():
    if json.loads(unreal.AlsLyraGraphLibrary.read_distance_sequence_data(unreal.load_asset(path), 'Distance')) != {
            key: value for key, value in definition.items() if key != 'markers'}:
        raise ValueError('Changed Pivot distance codec during traversal: '+path)
protect()
request_sha = save(prefix+'_requests.json', requests)
save(prefix+'_definitions.json', {'schemaVersion': 1, 'requestSha256': request_sha,
    'assets': definitions, 'policies': policies,
    'scope': 'Static distance codec/markers and original class policy. No source or expected execution history.'})
save(prefix+'_native.json', {'schemaVersion': 1, 'requestSha256': request_sha,
    'dependencies': {name: sha(data) for name, data in files.items()}, 'assetSha256': packages,
    'previousFixtureSha256': old_files, 'traces': native['traces'],
    'scope': ('Actual original PivotSM, state roots, callbacks and transitions; visits, entry initialization, '
              'inertia and common Sync follow the native machine. Explicit Main observations; no pose evaluation, '
              'complete Main/Notify/Demo acceptance.' if run_machine else
              'Original compiled Pivot callbacks and transition delegate on one registered Main/Linked owner, real CharacterMovement properties and common native Sync. Explicit source visits/weights and Main observations; no complete PivotSM/root pose/Main/Notify/Demo acceptance.')})
marker = 'LYRA_PIVOT_MACHINE_REENTRY_NATIVE_OK' if reentry else 'LYRA_PIVOT_MACHINE_NATIVE_OK' if run_machine else 'LYRA_PIVOT_SOURCE_NATIVE_OK'
unreal.log(f'{marker} traces=9 frames=3780 packages={len(packages)} assets={len(definitions)} assets_saved=0')
