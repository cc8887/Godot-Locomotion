"""Extract original SafeMove and MoveAlongFloor/StepUp geometry, preserving bytes."""
import hashlib,json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
E=ROOT/'artifacts/lyra-analysis'
TAG='cmc60-ground-sweep-v1'
OUT=E/'character-ground-v1-reference.json'
assert not OUT.exists(),'Preserve ground evidence'
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def load(path): return json.loads(path.read_bytes())
native_path=E/f'whole-main-{TAG}-native.json'; closure_path=E/f'whole-main-{TAG}-closure.json'
native=load(native_path); old=load(E/'whole-main-cmc60-floor-v2-native.json')
rows=native['traces'][0]['movementKernel']
assert len(rows)==32 and len(native['traces'])==3
for trace,prior in zip(native['traces'],old['traces'],strict=True):
    assert trace['actualCharacterMovement'] and trace['movementKernel']==rows
    assert trace['motorProfile']==prior['motorProfile']
    for key in ('velocityKernel','fallingKernel','floorKernel'):
        assert trace[key]==prior[key],key
    assert [f['physicalInput'] for f in trace['frames']]==[f['physicalInput'] for f in prior['frames']]
data=dict(schemaVersion=1,actualOriginalCMC=True,actualPrimitiveSweep=True,actualMoveAlongFloor=True,
          fullPhysWalking=False,originalPhysicalPrefixUnchanged=True,evidenceSha256=sha(native_path),closureSha256=sha(closure_path),
          floor=dict(center=[0,0,-5],extent=[10000,10000,5]),
          obstacles=[dict(center=[650,0,100],extent=[25,5000,100]),
                     dict(center=[1200,0,15],extent=[100,100,15]),dict(center=[1600,0,27.5],extent=[100,100,27.5])],rows=rows)
with OUT.open('x',encoding='utf-8',newline='\n') as stream: json.dump(data,stream,separators=(',',':'),allow_nan=False)
print('ORIGINAL_GROUND_EXPORT_OK queries=32 providers=3 priorFloor144AndKernels425Unchanged=true physicalPrefixUnchanged=true')
print(sha(OUT))
