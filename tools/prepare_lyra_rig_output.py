"""Preserve old probes; capture complete original ground-v2 output packets."""
import hashlib
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1] / 'assets/generated/lyra_als'
ground = json.loads((root / 'footplant_rig_ground_v2_native.json').read_bytes())
result = {'schemaVersion': 1, 'dependencies': {name: hashlib.sha256((root / name).read_bytes()).hexdigest()
          for name in ('rig_solver_v1_native.json', 'footplant_rig_ground_v2_native.json')},
          'traces': [{'mode': t['mode'], 'hz': t['hz'], 'frames': [f.get('output') for f in t['frames']]}
                     for t in ground['traces']]}
dest = root / 'rig_output_v1_native.json'
if dest.exists():
    raise RuntimeError('Preserve existing output evidence; use a new version.')
dest.write_text(json.dumps(result, separators=(',', ':'), allow_nan=False), encoding='utf-8')
print('LYRA_RIG_OUTPUT_FIXTURE_OK frames=2520 poses=%d bytes=%d' % (
    sum(f is not None for t in result['traces'] for f in t['frames']), dest.stat().st_size))
solver = json.loads((root / 'rig_solver_v1_native.json').read_bytes())
policy = {'schemaVersion': 1, 'descriptor': solver['descriptor'], 'dependencies': {
    name: hashlib.sha256((root / name).read_bytes()).hexdigest() for name in (
        'rig_traversal_v1_program.json', 'footplant_rig_graph_v1.json', 'rig_control_settings_v1.json',
        'logical_controls/calibration.json')},
    'descriptorSourceSha256': hashlib.sha256((root / 'footplant_rig_inputs_v1_input.json').read_bytes()).hexdigest()}
policy_path = root / 'rig_pose_v1_policy.json'
if policy_path.exists(): raise RuntimeError('Preserve existing Rig policy.')
policy_path.write_text(json.dumps(policy, separators=(',', ':')), encoding='utf-8')
