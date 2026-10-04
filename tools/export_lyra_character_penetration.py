"""Export actual CMC recovery policy without rewriting prior resources."""
import hashlib
import json
import argparse
import re
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
E=ROOT/'artifacts/lyra-analysis'
A=ROOT/'assets/generated/lyra_als'
out=A/'character_penetration_v1.json'
assert not out.exists(),'Preserve penetration resource'
parser=argparse.ArgumentParser()
parser.add_argument('--native-tag',default='cmc60-penetration-v1')
tag=parser.parse_args().native_tag
assert re.fullmatch(r'[a-zA-Z0-9_-]+',tag),'Invalid native tag'
def load(p):return json.loads(p.read_bytes())
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
native=E/f'whole-main-{tag}-native.json'
data=load(native);old=load(E/'whole-main-cmc60-air-v2-native.json')
profile=data['traces'][0]['penetrationProfile']
for trace,prior in zip(data['traces'],old['traces'],strict=True):
    assert trace['penetrationProfile']==profile
    for key in ('motorProfile','velocityKernel','fallingKernel','floorKernel','movementKernel','airPhysicalKernel'):
        rows=trace[key]
        if key=='airPhysicalKernel':rows=[r for r in rows if r['name'] not in ('wall-penetration','corner-penetration')]
        assert rows==prior[key],key
    assert [f['physicalInput'] for f in trace['frames']]==[f['physicalInput'] for f in prior['frames']]
assert profile['p.MoveIgnoreFirstBlockingOverlap']==0 and profile['role']==3
resource=dict(schemaVersion=1,baseMotorSha256=sha(A/'character_motor_v2.json'),
    evidenceSha256=sha(native),closureSha256=sha(E/f'whole-main-{tag}-closure.json'),profile=profile)
with out.open('x',encoding='utf-8',newline='\n') as s:json.dump(resource,s,separators=(',',':'),allow_nan=False)
print('ORIGINAL_PENETRATION_EXPORT_OK providers=3 oldKernelsAndPhysicalPrefixUnchanged=true',sha(out))
