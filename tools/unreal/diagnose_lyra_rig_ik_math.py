"""Call installed native IK with exact Godot diagnostic inputs; save no assets."""
import hashlib
import json
from pathlib import Path
import unreal

repo = Path(__file__).resolve().parents[2]
root = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'
paths = list(root.rglob('*.json'))
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
protected = {p: sha(p) for p in paths}
calls = json.loads((logs / 'rig-solver-ik-input-diagnostic.json').read_bytes())
result = json.loads(unreal.AlsLyraRigIkMathLibrary.read_trace(json.dumps({'calls': calls}, separators=(',', ':'))))
for p, digest in protected.items():
    assert sha(p) == digest, p
output = logs / 'rig-solver-ik-native-diagnostic.json'
output.write_text(json.dumps(result, separators=(',', ':')), encoding='utf-8')
unreal.log('LYRA_RIG_IK_MATH_DIAGNOSTIC_OK calls=%d protected_json=%d assets_saved=0' % (len(calls), len(protected)))
