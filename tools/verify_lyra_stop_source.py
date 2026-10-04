"""Verify immutable inputs, original package provenance and native Stop coverage."""
import hashlib
import json
from pathlib import Path

repo=Path(__file__).resolve().parents[1]
root=repo/'assets/generated/lyra_als'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
requests=json.loads((root/'stop_source_requests.json').read_bytes())
native=json.loads((root/'stop_source_native.json').read_bytes())
definitions=json.loads((root/'stop_source_definitions.json').read_bytes())
expected=sha(root/'stop_source_requests.json')
assert native['schemaVersion']==definitions['schemaVersion']==requests['schemaVersion']==1
assert native['requestSha256']==definitions['requestSha256']==expected
for n,h in native['dependencies'].items(): assert sha(root/n)==h, n
for p,h in native['assetSha256'].items():
    package=Path('../GASP58/Content')/(p.split('.')[0].removeprefix('/Game/')+'.uasset')
    assert sha(package)==h, p
assert len(native['assetSha256'])==489 and len(definitions['assets'])==36
assert len(native['traces'])==len(requests['traces'])==9
counts={'frames':0,'active':0,'hidden':0,'unmatchedAdvance':0,'matched':0,'zeroAdvance':0,'outsideExplicit':0,'preparedOutside':0}
for trace, authored in zip(native['traces'],requests['traces'],strict=True):
    assert (trace['profile'],trace['hz'])==(authored['profile'],authored['hz'])
    assert trace['hz'] in (30,60,120)
    for row, frame in zip(trace['frames'],authored['frames'],strict=True):
        counts['frames']+=1
        active=frame['active']; counts['active' if active else 'hidden']+=1
        assert row['active']==active and row['asset'] in definitions['assets']
        should=frame['main']['HasVelocity'] and not frame['main']['HasAcceleration']
        assert row['HasVelocity']==frame['main']['HasVelocity']
        assert row['HasAcceleration']==frame['main']['HasAcceleration'] and row['shouldMatch']==should
        assert row['movement']['lastUpdateVelocity']==frame['movement']['lastUpdateVelocity']
        assert row['movement']['separate']==frame['movement']['bUseSeparateBrakingFriction']
        if active:
            counts['unmatchedAdvance']+=not should
            counts['matched']+=should and row['predictedDistance']>0
            counts['zeroAdvance']+=should and row['predictedDistance']==0
            length=definitions['assets'][row['asset']]['length']
            counts['outsideExplicit']+=row['explicit']<0 or row['explicit']>length
            counts['preparedOutside']+=row['prepared']>length
assert counts['frames']==3780 and counts['active']==3672 and counts['hidden']==108
assert all(counts[n]>0 for n in ('unmatchedAdvance','matched','zeroAdvance','outsideExplicit','preparedOutside'))
print('LYRA_STOP_SOURCE_FIXTURES_OK '+json.dumps(counts,separators=(',',':'))+' packages=489 assets=36')
