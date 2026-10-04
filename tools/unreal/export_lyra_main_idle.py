"""Capture the real Main Idle StateResult callback, with an empty child."""
import hashlib
import json
import math
import os
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
repo = root.parents[2]
sha = lambda b: hashlib.sha256(b).hexdigest()
native_idle = json.loads((root/'idle_runtime_v2_native.json').read_bytes())
packages = native_idle['assetSha256']; del native_idle
fixtures = {str(p.relative_to(root)).replace('\\','/'):sha(p.read_bytes()) for p in root.rglob('*.json')}
content = Path(unreal.Paths.project_content_dir())
def protect():
    assert all(sha((content/(p.split('.')[0].removeprefix('/Game/')+'.uasset')).read_bytes()) == digest for p,digest in packages.items())
    assert all(sha((root/p).read_bytes()) == digest for p,digest in fixtures.items())
protect()
frames = []
weights = [0.0, 1.0, .75, .0001, .00010001, -.0001, -.00010001, .25]
for hz in (30,60,120):
    for i in range(hz*12):
        main = dict(RootYawOffset=math.sin(i*.17)*179.123456789, AimYaw=17.125,
                    IsCrouching=i%17<8, bEnableRootYawOffset=i%19>=3, RootYawOffsetMode=i%3)
        if i==0: main['TurnYawCurveValue'] = 0
        frames.append(dict(hz=hz,delta=1/hz,current=(0,2,1,3)[i%4], previousIdleWeight=(0,.4)[i//4%2],
                           visited=i%31!=0,main=main,remaining=90.0-i*.1135,
                           curveWeight=weights[i//8%len(weights)]))
graph = json.loads((root/'runtime_graph.json').read_bytes())
calibration = json.loads((root/'logical_controls/calibration.json').read_bytes())
result = json.loads(unreal.AlsLyraMainIdleLibrary.read_main_idle_trace(
    unreal.load_class(None,graph['classes']['main']['class']),unreal.load_asset(calibration['calibration']['sourceMesh']),
    json.dumps(dict(frames=frames))))
assert result['root'] == 8 and len(result['frames']) == len(frames) == 2520
protect()
source = repo/'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
payload = dict(schemaVersion=1,requests=dict(frames=frames),native=result,packages=packages,fixtures=fixtures,
               dependencies={p:sha((root/p).read_bytes()) for p in ('runtime_graph.json','main_update_policy.json')},
               probeSourceSha256={p:sha((source/p).read_bytes()) for p in ('Private/AlsLyraMainIdleLibrary.cpp','Public/AlsLyraMainIdleLibrary.h')})
path = repo/'artifacts/lyra-analysis/main-idle-root-native.json'
if path.exists(): assert json.loads(path.read_bytes()) == payload, 'Immutable Main Idle capture differs'
else: path.write_text(json.dumps(payload,separators=(',',':')),encoding='utf-8')
unreal.log(f'LYRA_MAIN_IDLE_ROOT_NATIVE_OK frames=2520 root=8 packages={len(packages)} fixtures={len(fixtures)} assets_saved=0')
