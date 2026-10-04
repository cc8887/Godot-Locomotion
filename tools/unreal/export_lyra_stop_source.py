"""Original Stop callbacks and CharacterMovement property access, common native Sync.

Controlled Main selectors are an explicit boundary; no hand-written callback
or movement prediction is substituted for the original compiled Blueprint.
"""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
sha = lambda b: hashlib.sha256(b).hexdigest()
files = {n: (root/n).read_bytes() for n in ('source_nodes.json', 'linked_layer_inventory.json',
    'logical_controls/catalog.json', 'logical_controls/calibration.json', 'stop_layer_graph.json')}
nodes, inventory, catalog, calibration, graphs = map(json.loads, files.values())
packages = dict(calibration['assetSha256']); packages.update(nodes['assetSha256'])
content = Path(unreal.Paths.project_content_dir())

def protect():
    for p, h in packages.items():
        if sha((content/(p.split('.')[0].removeprefix('/Game/')+'.uasset')).read_bytes()) != h:
            raise ValueError('Changed Stop package: '+p)

def save(n, data):
    p = root/n
    if p.exists():
        if json.loads(p.read_bytes()) != data: raise ValueError('Immutable Stop fixture differs: '+n)
    else: p.write_text(json.dumps(data, separators=(',', ':')), encoding='utf-8')
    return sha(p.read_bytes())

protect()
targets = {e['source']: e['target'] for e in catalog['entries']}
traces, definitions = [], {}
for profile in ('unarmed', 'pistol', 'rifle'):
    owner = nodes['classes'][profile]
    source = [s for s in owner['sources'] if s['functions']['update'] == 'UpdateStopAnim']
    if len(source) != 1: raise ValueError('Ambiguous Stop source')
    bindings = {g: {d: targets[p] for d, p in inventory['classes'][profile]['cardinals'][g].items()}
        for g in ('Jog_Stop_Cardinals', 'ADS_Stop_Cardinals', 'Crouch_Stop_Cardinals')}
    for group in bindings.values():
        for p in group.values():
            if p not in definitions:
                seq = unreal.load_asset(p)
                text = unreal.AlsLyraGraphLibrary.read_distance_sequence_data(seq, 'Distance')
                if not text: raise ValueError('Missing actual Stop distance data: '+p)
                data = json.loads(text)
                data['markers'] = json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(seq))['markers']
                definitions[p] = data
    for hz in (30, 60, 120):
        frames = []
        for i in range(hz*6):
            cell, local = divmod(i, hz//2)
            selector = cell if local < hz//4 else (cell+5)%12
            match = local%8 not in (0, 4)
            velocity = ((0, 0, 900), (413.123456789, -127.456789123, 200),
                (1e-9, -2e-9, 300), (1e5, 2e5, -400), (36.789123456, 42.123456789, 0))[local%5]
            frames.append({'delta': 0 if local==3 else 1e-9 if local==4 else 1/hz,
                'weight': 1e-6 if local==7 else (1, .73, 1.1)[cell%3],
                'active': local!=hz//2-1,
                'reinitialize': local==0 or (cell in (2, 6) and local==5) or (cell==8 and local==hz//2-1),
                'main': {'IsCrouching': selector>=8, 'GameplayTag_IsADS': 4<=selector<8,
                    'LocalVelocityDirection': selector%4, 'HasVelocity': match or local%8==4,
                    'HasAcceleration': local%8==4},
                'movement': {'lastUpdateVelocity': list(velocity), 'bUseSeparateBrakingFriction': local%2==0,
                    'BrakingFriction': (-2, 0, .3789, 6.25)[local%4],
                    'GroundFriction': (0, -1, 7.125, .6789123)[local%4],
                    'BrakingFrictionFactor': (-2, .9876123, 0, 2)[(local//4)%4],
                    'BrakingDecelerationWalking': (0, 2048.123, -4, 123.4567)[(local//3)%4]}})
        traces.append({'profile': profile, 'class': owner['class'], 'nodeIndex': source[0]['nodeIndex'],
            'hz': hz, 'bindings': bindings, 'frames': frames})
requests = {'schemaVersion': 1, 'traces': traces}
text = unreal.AlsLyraGraphLibrary.read_stop_source_trace(unreal.load_class(None, nodes['classes']['main']['class']),
    unreal.load_asset('/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny'), json.dumps(requests, separators=(',', ':')))
if not text: raise ValueError('Empty original Stop trace')
native = json.loads(text)
if len(native['traces']) != 9: raise ValueError('Incomplete original Stop trace')
for p, data in definitions.items():
    if json.loads(unreal.AlsLyraGraphLibrary.read_distance_sequence_data(unreal.load_asset(p), 'Distance')) != {k:v for k,v in data.items() if k!='markers'}:
        raise ValueError('Distance codec changed during original Stop traversal: '+p)
protect()
request_sha = save('stop_source_requests.json', requests)
save('stop_source_definitions.json', {'schemaVersion':1, 'requestSha256':request_sha, 'assets':definitions,
    'scope':'Static sequence distance codec and markers; no callback outputs or source history.'})
save('stop_source_native.json', {'schemaVersion':1, 'requestSha256':request_sha,
    'dependencies':{n:sha(b) for n,b in files.items()}, 'assetSha256':packages, 'traces':native['traces'],
    'scope':'Original Stop evaluator callbacks on registered Character/Main/Linked owner, actual CharacterMovement snapshot and property access batches, common native Stop Sync. Controlled Main selectors; no complete Main update/provider pose/Main Stop root/LocomotionSM/Demo acceptance.'})
unreal.log(f'LYRA_STOP_SOURCE_NATIVE_OK traces=9 frames=3780 packages={len(packages)} assets={len(definitions)} assets_saved=0')
