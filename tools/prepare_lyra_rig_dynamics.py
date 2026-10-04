"""Derive FootPlant unit arguments from the pinned real Rig program/ground trace."""
import hashlib
import json
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
load = lambda name: json.loads((root / name).read_bytes())
def sha(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        while data := f.read(1024 * 1024): h.update(data)
    return h.hexdigest()
program = load('footplant_rig_inputs_v1_program.json')
literal = program['literal']
indices = [242, 299, 311, 348, 371, 317, 326]
configs = []
for index in indices:
    instruction = program['instructions'][index]
    operands = instruction['operands']
    value = lambda n: literal[operands[n]['name']]
    if index in (317, 326):
        cfg = dict(instruction=index, kind='alpha', scale=value(1), bias=value(2), map=value(3),
                   inRange=value(4), outRange=value(5), clamp=value(6), minimum=value(7), maximum=value(8),
                   interp=value(9), increasing=value(10), decreasing=value(11))
    else:
        cfg = dict(instruction=index, kind='vector' if index < 300 else 'scalar', strength=value(1),
                   damping=value(2), force=value(3), useCurrent=value(4), targetVelocity=value(6), initializeFromTarget=value(7))
    configs.append(cfg)

ground = load('footplant_rig_ground_v2_native.json')
source_requests = load('footplant_rig_ground_v2_requests.json')
traces = []
for t, q in zip(ground['traces'], source_requests['traces'], strict=True):
    frames = []
    for row, frame in zip(t['frames'], q['frames'], strict=True):
        calls = []
        if 'output' in row and row['alpha'] > .00001:
            work = row['after']['work']
            variables = row['after']['variables']
            def observed(operand):
                assert operand['path'] == ''
                return work[operand['name']] if operand['memory'] == 0 else variables[operand['name']]
            for owner, index in enumerate(indices):
                operands = program['instructions'][index]['operands']
                current = observed(operands[5]) if owner < 5 else 0
                calls.append(dict(owner=owner, target=observed(operands[0]), current=current))
        frames.append(dict(delta=row['updated']['delta'], reset=frame['initialize'], calls=calls))
    traces.append(dict(name=t['mode']+'-'+str(t['hz']), hz=t['hz'], source='Observed original ground-v2 unit argument registers', frames=frames))
# Controlled traversal stress uses exactly the original seven configurations:
# independent/reversed visitation, omitted units, repeated invocation, zero and
# tiny deltas, large deltas, resets and a copy/retry history gate in Godot.
for hz in (30, 60, 120):
    frames = []
    for i in range(hz * 4):
        delta = 0 if i % 37 == 0 else 1e-9 if i % 73 == 0 else .35 if i % 91 == 0 else 1/hz
        calls = []
        for owner in (range(7) if i % 2 == 0 else reversed(range(7))):
            if (i + owner) % 11 == 0: continue
            target = ((i % 29)-14) * (.03125 if owner < 2 else .75)
            current = ((i % 17)-8) * (.125 if owner < 2 else 1.125)
            if owner < 2:
                target = [target, target * -.7 + owner * .03, .3 + (i % 3)*.2]
                current = [current, .07 * owner - current, .95]
            calls.append(dict(owner=owner, target=target, current=current))
            if (i + owner) % 47 == 0: calls.append(dict(calls[-1]))
        if i % 23 == 0: calls = []
        frames.append(dict(delta=delta, reset=i == 0 or i == hz * 2, calls=calls))
    traces.append(dict(name='TraversalStress-'+str(hz), hz=hz, source='Controlled component arguments with unchanged original configurations', frames=frames))
value = dict(schemaVersion=1, configs=configs, traces=traces,
             dependencies={p:sha(root/p) for p in ('footplant_rig_inputs_v1_program.json', 'footplant_rig_ground_v2_native.json',
                 'footplant_rig_ground_v2_requests.json', 'footplant_rig_graph_v1.json', 'logical_controls/curve_bank.json')},
             scope='Original unit configurations; observed argument replay and controlled unit visitation, not real complete VM traversal')
p = root/'rig_dynamics_v1_requests.json'
if p.exists(): assert json.loads(p.read_bytes()) == value, 'Immutable unit requests changed'
else:p.write_text(json.dumps(value,separators=(',',':'),allow_nan=False),encoding='utf-8')
print('LYRA_RIG_DYNAMICS_REQUESTS_OK traces=%d frames=%d calls=%d' % (len(traces),sum(len(t['frames']) for t in traces),sum(len(f['calls']) for t in traces for f in t['frames'])))
