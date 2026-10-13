"""Inspect published twist rotations and determinism in fresh Mantle traces."""
import argparse
import json
from pathlib import Path
import numpy as np
from scipy.spatial.transform import Rotation

SEGMENTS = [('upperarm', 'lowerarm'), ('lowerarm', 'hand'), ('thigh', 'calf'), ('calf', 'foot')]


def read(path): return json.loads(Path(path).read_text(encoding='utf-8'))


def observe(frames, driver=False):
    rows = []
    for side in ('l', 'r'):
        for main, end in SEGMENTS:
            name = main + '_twist_01_' + side
            samples = [frame['driverBones'] if driver and frame.get('driverBones') else frame['bones'] for frame in frames]
            local = Rotation.from_quat([bones[name]['localRotation'] for bones in samples])
            rest = Rotation.from_quat(samples[0][name]['restRotation'])
            axis = np.asarray(samples[0][end + '_' + side]['restPosition'], dtype=float)
            axis /= np.linalg.norm(axis)
            delta = (local * rest.inv()).as_quat()
            projection = delta[:, :3] @ axis
            twist = np.column_stack((projection[:, None] * axis, delta[:, 3]))
            twist /= np.linalg.norm(twist, axis=1)[:, None]
            swing = Rotation.from_quat(delta) * Rotation.from_quat(twist).inv()
            steps = (local[:-1].inv() * local[1:]).magnitude() * 180 / np.pi
            support = [i for i, f in enumerate(frames) if f['handContacts'][('Left' if side == 'l' else 'Right') + 'Weight'] >= .999]
            angles = (local * rest.inv()).magnitude() * 180 / np.pi
            rows.append(dict(bone=name, maximumLocalDeltaDegrees=float(np.max(angles)),
                maximumSupportDeltaDegrees=float(np.max(angles[support], initial=0)),
                maximumNonAxialSwingDegrees=float(np.max(swing.magnitude()) * 180 / np.pi),
                maximumStepDegrees=float(np.max(steps, initial=0)), supportSamples=len(support)))
    return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--runtime-dir', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    if args.output.exists(): raise ValueError('Preserve existing twist evidence; choose a fresh output')
    runtime = read(args.runtime_dir/'results.json'); checks = []; pairs = {}
    for case in runtime['checks']:
        trace = read(args.runtime_dir/(case['name']+'.json')); frames = trace['frames']
        rows = observe(frames); driver_rows = observe(frames, True)
        full_support = [f for f in frames if f['handContacts']['LeftWeight'] >= .999 or f['handContacts']['RightWeight'] >= .999]
        # Geometry policy, not a reference-animation amplitude threshold:
        # twist must remain axial and must not flip by 180 between frames.
        axial = all(r['maximumNonAxialSwingDegrees'] <= .1 for r in rows + driver_rows)
        continuous = all(r['maximumStepDegrees'] <= 90.00001 for r in rows + driver_rows)
        response = True
        if case['case'] == 'medium-high':
            response = any(r['bone'].startswith('lowerarm') and r['supportSamples'] and
                           r['maximumSupportDeltaDegrees'] > 1 for r in rows)
        checks.append(dict(name=case['name'], frames=len(frames), fullSupportFrames=len(full_support),
            axialPassed=axial, continuityPassed=continuous, highSupportTwistResponded=response,
            presentation=rows, driver=driver_rows, passed=axial and continuous and response))
        key = (case['case'], case['model'], case['root'], case['hz'])
        pairs.setdefault(key, {})[case['mode']] = frames
    parity = [dict(case=list(key), exactFramesMatch=modes['single'] == modes['parallel'])
              for key, modes in pairs.items() if 'single' in modes and 'parallel' in modes]
    report = dict(schemaVersion=1, pack=runtime['pack'], packSha256=runtime['packSha256'],
        runtimePassed=runtime['passed'], checks=checks, singleParallelParity=parity,
        passed=runtime['passed'] and all(c['passed'] for c in checks) and all(p['exactFramesMatch'] for p in parity),
        scope='published physical twist axial rotation, continuity, High support response and frame parity; joint posture and artistic acceptance remain open',
        productionReady=False)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False)+'\n', encoding='utf-8')
    print('MANTLE_TWIST_OK' if report['passed'] else 'MANTLE_TWIST_FAILED', len(checks), len(parity), flush=True)
    return 0 if report['passed'] else 1


if __name__ == '__main__': raise SystemExit(main())
