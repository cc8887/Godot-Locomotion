"""Derive target solver assertions and full output oracle; preserve original captures."""
import hashlib
import json
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
load = lambda name: json.loads((root / name).read_bytes())
program = load('rig_traversal_v1_program.json')
captured = load('rig_target_v1_native.json')
requests = load('footplant_rig_ground_v2_requests.json')
names = [name for name, type_ in program['workTypes'].items()
         if type_ not in ('TArray', 'FRigVMInstructionSetExecuteState')]
sweep_ops = [op for op in program['instructions']
             if 'FRigUnit_SphereTraceByTraceChannel::Execute(' in op['text']]
traces, outputs = [], []
for ti, trace in enumerate(captured['traces']):
    frames = []
    for fi, frame in enumerate(trace['frames']):
        work = frame['after']['work']
        sweeps = {}
        for op in sweep_ops:
            if op['index'] not in frame['visits']:
                continue
            assert frame['visits'].count(op['index']) == 1

            def value(index):
                operand = op['operands'][index]
                assert not operand['path']
                return (program['literal'] if operand['memory'] == 1 else work)[operand['name']]

            sweeps[str(op['index'])] = [value(i) for i in range(7)]
        frames.append(dict(request=requests['traces'][ti]['frames'][fi], visits=frame['visits'],
                           alpha=frame['alpha'], input=frame.get('input'),
                           variables=frame['after']['variables'], work=[work[name] for name in names],
                           hierarchy=frame['after']['hierarchy'], sweeps=sweeps))
    traces.append(dict(mode=trace['mode'], hz=trace['hz'], frames=frames))
    outputs.append(dict(mode=trace['mode'], hz=trace['hz'], frames=[frame.get('output') for frame in trace['frames']]))
dependencies = {name: sha(root / name) for name in (
    'rig_traversal_v1_program.json', 'rig_target_v1_native.json',
    'footplant_rig_ground_v2_requests.json', 'rig_reference_v1_policy.json',
    'footplant_rig_inputs_v1_input.json')}
solver = dict(schemaVersion=1, dependencies=dependencies, names=names,
              descriptor=load('footplant_rig_inputs_v1_input.json')['traces'][0]['descriptor'],
              traces=traces, counts=captured['counts'], reference='AlsCompactReference')
output = dict(schemaVersion=1, dependencies=dependencies, traces=outputs, reference='AlsCompactReference')
for suffix, data in (('solver', solver), ('output', output)):
    destination = root / ('rig_target_v1_' + suffix + '.json')
    assert not destination.exists(), 'Preserve existing target fixture: ' + suffix
    destination.write_text(json.dumps(data, separators=(',', ':'), allow_nan=False), encoding='utf-8')
    print('LYRA_RIG_TARGET_FIXTURE_OK kind=%s bytes=%d sha256=%s' % (suffix, destination.stat().st_size, sha(destination)))
