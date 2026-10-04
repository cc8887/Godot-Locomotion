"""Original Main machine Update + original linked sources and actual Sync.

No state/edge forcing and no substitute pose leaves. GroundDistance is an
explicit terrain observation; this capture is not a real terrain simulation.
"""
import copy
import hashlib
import json
import math
import os
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
repo = Path(__file__).resolve().parents[2]
sha = lambda data: hashlib.sha256(data).hexdigest()
prefix = 'main_machine_runtime_v2'
names = ('runtime_graph.json', 'main_pivot_requests.json', 'main_ground_scope_v2_native.json',
         'logical_controls/calibration.json', 'main_update_policy.json', 'source_nodes.json')
files = {n: (root/n).read_bytes() for n in names}
graph = json.loads(files['runtime_graph.json'])
base = json.loads(files['main_pivot_requests.json'])
packages = json.loads(files['main_ground_scope_v2_native.json'])['assetSha256']
content = Path(unreal.Paths.project_content_dir())
previous = {str(p.relative_to(root)).replace('\\', '/'): sha(p.read_bytes()) for p in root.rglob('*.json')
            if p.name not in (prefix+'_requests.json', prefix+'_native.json')}
if (root/(prefix+'_native.json')).exists():
    previous = json.loads((root/(prefix+'_native.json')).read_bytes())['previousFixtureSha256']

def protect():
    for p, digest in packages.items():
        if sha((content/(p.split('.')[0].removeprefix('/Game/')+'.uasset')).read_bytes()) != digest:
            raise ValueError('Changed original machine package: '+p)
    for p, digest in previous.items():
        if sha((root/p).read_bytes()) != digest: raise ValueError('Changed previous machine fixture: '+p)

def save(name, data):
    p = root/name
    if p.exists():
        if json.loads(p.read_bytes()) != data: raise ValueError('Immutable machine capture differs: '+name)
    else: p.write_text(json.dumps(data, separators=(',', ':')), encoding='utf-8')
    return sha(p.read_bytes())

protect()
requests = dict(schemaVersion=1, traces=[])
for seed in base['traces']:
    hz = seed['hz']; location = [111.25, -23.5, 150.0]; frames = []
    for i in range(hz*12):
        t = i/hz; speed = 0.0; acceleration = 0.0; z = 0.0; ground = True; distance = 0.0
        if .3 <= t < 1.8 or 3.5 <= t < 4.8 or 7.1 <= t < 8.5 or 10 <= t < 11.2:
            speed = 240.0; acceleration = 1000.0
        elif 1.8 <= t < 2.05 or 8.5 <= t < 8.8:
            speed = 240.0; acceleration = -1000.0
        elif 2.05 <= t < 2.45 or 8.8 <= t < 9.1:
            speed = -180.0; acceleration = -1000.0
        elif 2.45 <= t < 3.1 or 9.1 <= t < 9.6 or 11.2 <= t < 11.6:
            start = 2.45 if t < 3.1 else 9.1 if t < 9.6 else 11.2
            speed = -180.0 * max(0.0, 1-(t-start)/.4)
        if 4.8 <= t < 6.9:
            ground = False; distance = 500.0 if t < 6.5 else 150.0
            speed = 160.0; acceleration = 0.0; z = 600.0 if t < 5.8 else -320.0
        obs = copy.deepcopy(seed['frames'][0]['observation']); obs.pop('snapshot')
        location[0] += speed/hz; location[2] += z/hz
        obs.update(delta=1/hz, location=location.copy(), rotation=[0, 0, 0], velocity=[speed, 0, z],
                   acceleration=[acceleration, 0, 0], movementMode=1 if ground else 3,
                   crouching=3.9 <= t < 4.3, ads=2.8 <= t < 3 or 7.4 <= t < 7.8, firing=False, montage=False,
                   aimPitch=0, dashing=False, enabled=True)
        # Real parent context inactivity is distinct from no traversal.
        hidden = 1.64 <= t < 1.72 or 3.12 <= t < 3.22 or 9.65 <= t < 9.73
        alternate = base['traces'][3 if seed['profile'] != 'pistol' else 0]['class']
        provider = alternate if 7.9 <= t < 8.1 or 9.25 <= t < 9.4 else seed['class']
        frames.append(dict(delta=1/hz, observation=obs, groundDistance=distance, providerClass=provider,
                           active=not hidden, contextActive=not (5.2 <= t < 5.3),
                           weight=.63 if i % 7 < 3 else 1.0,
                           reinitialize=i in (0, round(4.4*hz), round(8.2*hz))))
    requests['traces'].append(dict(profile=seed['profile'], hz=hz, class_=seed['class'], frames=frames))
    requests['traces'][-1]['class'] = requests['traces'][-1].pop('class_')

mesh = json.loads(files['logical_controls/calibration.json'])['calibration']['sourceMesh']
text = unreal.AlsLyraLocomotionLibrary.read_machine_trace(unreal.load_class(None, graph['classes']['main']['class']),
    unreal.load_asset(mesh), json.dumps(requests, separators=(',', ':')))
if not text: raise ValueError('Empty original Main machine trace')
native = json.loads(text)
counts = dict(frames=0, updates=0, transitions=0, hidden=0, inactiveParent=0, states=[], maxDepth=0)
states = set()
for trace, authored in zip(native['traces'], requests['traces'], strict=True):
    for row, frame in zip(trace['frames'], authored['frames'], strict=True):
        frame['observation']['snapshot'] = row['observation']['input']
        frame['observation']['mode'] = row['observation']['tailBefore']['mode']
        counts['frames'] += 1; counts['updates'] += len(row['updates'])
        counts['hidden'] += not frame['active']; counts['inactiveParent'] += frame['active'] and not frame['contextActive']
        counts['transitions'] += frame['active'] and row['beforeState'] != row['state']
        counts['maxDepth'] = max(counts['maxDepth'], len(row['active']))
        states.add(row['state'])
counts['states'] = sorted(states)
(repo/'artifacts/lyra-analysis/main-machine-v2-native-diagnostic.json').write_text(json.dumps(native, separators=(',', ':')), encoding='utf-8')
sync_frames = sum(row['syncValid'] for trace in native['traces'] for row in trace['frames'])
if counts['frames'] != 7560 or len(states) != 10 or counts['maxDepth'] < 2 or not sync_frames:
    raise ValueError('Insufficient original machine coverage: '+str(counts))
protect()
source_root = repo/'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
probe_names = ('Public/AlsLyraLocomotionLibrary.h', 'Private/AlsLyraLocomotionLibrary.cpp',
               'Private/AlsLyraMainObservationLibrary.cpp', 'Private/AlsLyraMainObservationProbe.h')
source_sha = {p: sha((source_root/p).read_bytes()) for p in probe_names}
for tree in ('source', 'package'):
    built = repo/'artifacts/unreal/gasp58-lyra-masks'/tree/'AlsV4AssetExporter/Source/AlsV4AssetExporter'
    for p, digest in source_sha.items():
        if sha((built/p).read_bytes()) != digest: raise ValueError('Different built original machine probe: '+tree+'/'+p)
request_sha = save(prefix+'_requests.json', requests)
native.update(schemaVersion=1, requestSha256=request_sha, dependencies={n: sha(b) for n, b in files.items()},
              assetSha256=packages, previousFixtureSha256=previous, probeSourceSha256=source_sha, counts=counts,
              sourceSkeleton=True, statePosesCaptured=False, fullGodotSourceHost=False, syncValidFrames=sync_frames)
save(prefix+'_native.json', native)
unreal.log('LYRA_MAIN_MACHINE_NATIVE_OK frames=7560 states='+str(len(states))+' updates='+str(counts['updates'])+
           ' depth='+str(counts['maxDepth'])+' packages=508 previous='+str(len(previous))+' assets_saved=0')
