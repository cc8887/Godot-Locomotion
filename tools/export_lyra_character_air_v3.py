"""Extract real PhysFalling cases; preserve original captured bytes and prefixes."""
import hashlib
import json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
E=ROOT/'artifacts/lyra-analysis'
OUT=E/'character-air-v3-reference.json'
assert not OUT.exists(),'Preserve air reference'
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def load(path):return json.loads(path.read_bytes())
native_path=E/'whole-main-cmc60-penetration-v2-native.json'
closure_path=E/'whole-main-cmc60-penetration-v2-closure.json'
native,prior=load(native_path),load(E/'whole-main-cmc60-ground-sweep-v1-native.json')
rows=native['traces'][0]['airPhysicalKernel']
assert len(rows)==112 and all(r['forceJumpPeakSubstep']==1 for r in rows)
for trace,old in zip(native['traces'],prior['traces'],strict=True):
    assert trace['actualCharacterMovement'] and trace['airPhysicalKernel']==rows
    for key in ('motorProfile','velocityKernel','fallingKernel','floorKernel','movementKernel'):
        assert trace[key]==old[key],key
    assert [r for r in trace['airPhysicalKernel'] if r['name'] not in ('wall-penetration','corner-penetration')]==load(E/'whole-main-cmc60-air-v2-native.json')['traces'][0]['airPhysicalKernel']
    assert [f['physicalInput'] for f in trace['frames']]==[f['physicalInput'] for f in old['frames']]
data=dict(schemaVersion=1,actualOriginalPhysFalling=True,actualProcessLandedAndWalkingRemainder=True,
          rootMotionCases=False,originalPhysicalPrefixUnchanged=True,evidenceSha256=sha(native_path),closureSha256=sha(closure_path),
          floor=dict(center=[0,0,-5],extent=[10000,10000,5],rotation=[0,0,0]),
          obstacles=[dict(center=[650,0,100],extent=[25,5000,100],rotation=[0,0,0]),
                     dict(center=[600,1080,1000],extent=[100,25,1000],rotation=[0,0,0]),
                     dict(center=[1500,0,100],extent=[200,200,5],rotation=[-30,0,0])],rows=rows)
with OUT.open('x',encoding='utf-8',newline='\n') as stream:json.dump(data,stream,separators=(',',':'),allow_nan=False)
print('ORIGINAL_AIR_EXPORT_OK cases=112 providers=3 oldKernelsAndPhysicalPrefixUnchanged=true')
print(sha(OUT))
