"""Actual BlueprintThreadSafeUpdateAnimation with live Character PropertyAccess; no saves."""
import hashlib
import json
import math
import os
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
sha = lambda data: hashlib.sha256(data).hexdigest()
names = ('main_observation_policy.json', 'main_lean/catalog.json', 'main_lean/inventory.json', 'source_nodes.json')
files = {name: (root/name).read_bytes() for name in names}
assets = {}
for data in files.values():
    for path, value in json.loads(data).get('assetSha256', {}).items():
        if path in assets and assets[path] != value:
            raise ValueError('Conflicting package provenance')
        assets[path] = value
content = Path(unreal.Paths.project_content_dir())
def protect():
    for path, value in assets.items():
        if sha((content/(path.split('.')[0].removeprefix('/Game/')+'.uasset')).read_bytes()) != value:
            raise ValueError('Protected package changed: '+path)
def save(name, data):
    path = root/name
    if path.exists():
        if json.loads(path.read_bytes()) != data:
            raise ValueError('Existing immutable Main update fixture differs: '+name)
    else:
        path.write_text(json.dumps(data, separators=(',', ':')), encoding='utf-8')
    return sha(path.read_bytes())
protect()
requests = json.loads((root/'main_observation_requests.json').read_bytes())
requests['completeMain'] = True
for trace in requests['traces']:
    hz = trace['hz']
    for i, frame in enumerate(trace['frames']):
        t = i/hz
        for key in ('first', 'rootYaw', 'snapshot'):
            frame.pop(key, None)
        frame.update(completeMain=True, mode=(2, 1, 0, 2, 0, 1)[int(t*2)%6],
            dashing=2.5 <= t < 3.25 or 9.5 <= t < 10,
            enabled=not (.6 <= t < .9 or 5 <= t < 6 or 8 <= t < 9),
            montage=1 <= t < 2 or 3 <= t < 4 or 7 <= t < 9,
            gravityScale=(1, .33, 2)[int(t)%3],
            aimPitch=239.125*math.sin(t*1.13))
main_path = json.loads(files['source_nodes.json'])['classes']['main']['class']
result = unreal.AlsLyraGraphLibrary.read_main_observation_trace(unreal.load_class(None, main_path),
    unreal.load_asset('/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny'),
    json.dumps(requests, separators=(',', ':')))
if not result:
    raise ValueError('Empty complete native Main update trace')
native = json.loads(result)
order = ['UpdateLocationData', 'UpdateRotationData', 'UpdateVelocityData', 'UpdateAccelerationData',
    'UpdateWallDetectionHeuristic', 'UpdateCharacterStateData', 'UpdateBlendWeightData',
    'UpdateRootYawOffset', 'UpdateAimingData', 'UpdateJumpFallData', 'ClearFirstUpdate']
if native['order'] != order or len(native['traces']) != 3:
    raise ValueError('Incomplete whole Main update capture')
for trace, capture in zip(requests['traces'], native['traces']):
    if trace['hz'] != capture['hz'] or len(trace['frames']) != len(capture['frames']):
        raise ValueError('Changed Main trace identity')
    for frame, row in zip(trace['frames'], capture['frames']):
        frame['snapshot'] = row['input']
        if row['after']['IsFirstUpdate'] or row['tailAfter']['mode'] != 0:
            raise ValueError('Whole Main macro did not finish its update')
protect()
request_sha = save('main_update_requests.json', requests)
payload = dict(schemaVersion=1, requestSha256=request_sha,
    dependencies={name: sha(data) for name,data in files.items()}, assetSha256=assets,
    order=order, traces=native['traces'],
    scope='Actual complete BlueprintThreadSafeUpdateAnimation on transient Character. Prior-graph RootYaw mode, tags and enable flag are external; real Controller aim, Movement gravity and actual montage activity with native zero-time stopped-instance teardown. No animation graph traversal, playing montage advancement/Notify, production physics or final pose.')
save('main_update_native.json', payload)
initial = native['traces'][0]
if any(t['initial'] != initial['initial'] or t['tailInitial'] != initial['tailInitial'] for t in native['traces']):
    raise ValueError('Initial Main state depends on frequency')
save('main_update_policy.json', dict(schemaVersion=1, dependencies=payload['dependencies'], order=order,
    initial=initial['initial'], tailInitial=initial['tailInitial'], scope=payload['scope']))
unreal.log(f"LYRA_MAIN_UPDATE_NATIVE_OK traces=3 frames=2520 functions=10 clear_first=true packages={len(assets)} assets_saved=0")
