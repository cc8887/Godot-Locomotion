"""Original pure IK on captured Godot inputs; diagnostic only, no assets saved."""
import hashlib
import json
from pathlib import Path
import unreal

repo = Path(__file__).resolve().parents[2]
assets = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
protected = {p: sha(p) for p in assets.rglob('*.json')}
calls = [json.loads(line.split('CMC_IK_INPUT ', 1)[1]) for line in
         (logs / 'cmc120-ik-diagnostic.log').read_text(encoding='utf-8-sig').splitlines()
         if line.startswith('CMC_IK_INPUT ')]
assert len(calls) == 2
result = json.loads(unreal.AlsLyraRigIkMathLibrary.read_trace(json.dumps({'calls': calls}, separators=(',', ':'))))
for p, digest in protected.items():
    assert sha(p) == digest, p
with (logs / 'cmc120-ik-native-diagnostic.json').open('x', encoding='utf-8', newline='\n') as stream:
    json.dump(dict(inputs=calls, native=result, protectedJson=len(protected)), stream, separators=(',', ':'))
    stream.write('\n')
unreal.log('LYRA_CMC_IK_MATH_DIAGNOSTIC_OK calls=2 assets_saved=0')
