"""Thin immutable ground-v2 evidence for immediate-unit solver comparison.

Only collision results are injected by the test provider. All other after
values are assertions. Keep the original probes and native JSON untouched.
"""
import hashlib
import json
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
program = json.loads((root / 'rig_traversal_v1_program.json').read_bytes())
ground = json.loads((root / 'footplant_rig_ground_v2_native.json').read_bytes())
requests = json.loads((root / 'footplant_rig_ground_v2_requests.json').read_bytes())
traversal = json.loads((root / 'rig_traversal_v1_native.json').read_bytes())
inputs = json.loads((root / 'footplant_rig_inputs_v1_input.json').read_bytes())
names = [name for name, type_ in program['workTypes'].items()
         if type_ not in ('TArray', 'FRigVMInstructionSetExecuteState')]
sweep_ops = [op for op in program['instructions']
             if 'FRigUnit_SphereTraceByTraceChannel::Execute(' in op['text']]
traces = []
for ti, trace in enumerate(ground['traces']):
    frames = []
    for fi, frame in enumerate(trace['frames']):
        order = traversal['traces'][ti]['frames'][fi]['visits']
        work = frame['after']['work']
        sweeps = {}
        for op in sweep_ops:
            if op['index'] not in order:
                continue
            def value(index):
                operand = op['operands'][index]
                assert not operand['path']
                return (program['literal'] if operand['memory'] == 1 else work)[operand['name']]
            sweeps[str(op['index'])] = [value(i) for i in range(7)]
        frames.append({'request': requests['traces'][ti]['frames'][fi], 'visits': order,
                       'alpha': frame['alpha'], 'input': frame.get('input'),
                       'variables': frame['after']['variables'],
                       'work': [work[name] for name in names],
                       'hierarchy': frame['after']['hierarchy'], 'sweeps': sweeps})
    traces.append({'mode': trace['mode'], 'hz': trace['hz'], 'frames': frames})
result = {'schemaVersion': 1, 'dependencies': {name: sha(root / name) for name in (
    'rig_traversal_v1_program.json', 'footplant_rig_ground_v2_native.json',
    'footplant_rig_ground_v2_requests.json', 'rig_traversal_v1_native.json',
    'footplant_rig_inputs_v1_input.json')}, 'names': names,
    'descriptor': inputs['traces'][0]['descriptor'], 'traces': traces,
    'scope': 'Actual math, hierarchy, dynamics and IK; recorded collision provider, no final pose adapter or production.'}
destination = root / 'rig_solver_v1_native.json'
if destination.exists():
    raise RuntimeError('Preserve existing solver evidence; use a new fixture version.')
destination.write_text(json.dumps(result, separators=(',', ':'), allow_nan=False), encoding='utf-8')
print('LYRA_RIG_SOLVER_FIXTURE_OK frames=%d work=%d bytes=%d' % (
    sum(len(t['frames']) for t in traces), len(names), destination.stat().st_size))
