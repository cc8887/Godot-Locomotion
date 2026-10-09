"""Repeat the real Low Mantle Demo scene without desktop automation.

Uses existing production-scene assertions; adds pack provenance, actor yaw,
published skin/platform penetration and fresh GPU captures. This is a scoped
diagnostic, not full-pack certification. Build/import before running.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import subprocess
import time

ROOT = Path(__file__).resolve().parents[1]
SCENE = 'res://scenes/tests/mantling_demo_smoke.tscn'
MAX_SKIN_DEPTH_METERS = .02


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False)+'\n', encoding='utf-8')


def resource_path(resource):
    if not resource.startswith('res://'):
        raise ValueError('Expected res:// resource: '+resource)
    path = (ROOT/resource[6:]).resolve()
    path.relative_to(ROOT)
    return path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--godot', type=Path, default=ROOT/'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe')
    parser.add_argument('--pack', required=True, help='Explicit diagnostic candidate resource-pack.json, res:// path')
    parser.add_argument('--output', type=Path, required=True, help='New evidence directory; never overwrites earlier results')
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    pack_path = resource_path(args.pack)
    pack_path.relative_to(ROOT/'assets/generated')
    pack = read(pack_path)
    if pack['scope'] != 'non-prop' or pack['replacedAnimations'] < 1:
        raise ValueError('Candidate must contain actual non-prop replacements')
    payloads = {}
    for item in pack['files']:
        path = resource_path(item['path'])
        path.relative_to(pack_path.parent)
        if sha(path) != item['sha256']:
            raise ValueError('Changed candidate payload: '+item['path'])
        payloads[path] = item['sha256']
    controls = [pack_path, ROOT/'assets/config/als_animation_settings.json', ROOT/'assets/config/als_resource_pack.json']
    controls += [ROOT/'src/Als.Godot/Locomotion/MantlingDemoSmoke.cs', ROOT/'scenes/tests/mantling_demo_smoke.tscn']
    controls += list((ROOT/'.godot/mono/temp/bin/Debug').glob('Als*.dll'))
    controls += list((ROOT/'.godot/mono/temp/bin/Debug').glob('GodotALS.dll'))
    frozen = {str(path): sha(path) for path in controls if path.is_file()}
    cases = [dict(name=f'low-{hz}-parallel-off', hz=hz, flags=[], render=False, original=False, root=False)
             for hz in (30, 60, 120)]
    cases += [dict(name='low-60-parallel-on', hz=60, flags=[], render=False, original=False, root=True),
              dict(name='low-60-single-off', hz=60, flags=['--mantle-single'], render=False, original=False, root=False),
              dict(name='rendered-low-candidate', hz=60, flags=[], render=True, original=False, root=False),
              dict(name='rendered-low-original', hz=60, flags=[], render=True, original=True, root=False)]
    report = dict(schemaVersion=1, scene=SCENE, pack=args.pack, packSha256=sha(pack_path),
                  replacedAnimations=pack['replacedAnimations'], verifiedPayloads=len(payloads),
                  frozenInputs=frozen, computerUse=False, productionReady=False, checks=[])
    write(output/'manifest.json', dict(**report, cases=cases))
    env = os.environ.copy()
    for key in ('ALS_ASSET_ROOT', 'ALS_RAW_SOURCE_GENERATED_DIR', 'ALS_REQUIRE_KIMODO_SOURCE',
                'ALS_RAW_SOURCE_REQUEST_OUTPUT', 'KIMODO_IDLE_DIAGNOSTIC_OUTPUT', 'KIMODO_IDLE_CAPTURE_DIR'):
        env.pop(key, None)
    trajectory_pattern = re.compile(r'MANTLE_FRAME tick=(\d+) active=(True|False) time=([^ ]+) p=\(([^)]+)\)')
    for case in cases:
        log = output/(case['name']+'.log')
        command = [str(args.godot.resolve()), '--path', str(ROOT)]
        flags = ['--mantle-case=low', '--mantle-hz='+str(case['hz']),
                 '--mantle-output='+str(output/(case['name']+'.json')),
                 '--enable-root-motion' if case['root'] else '--disable-root-motion', *case['flags']]
        if case['render']:
            command += ['--rendering-method', 'gl_compatibility', '--resolution', '1280x720', '--max-fps', '60']
            flags += ['--capture-dir='+str(output/case['name'])]
        else:
            command += ['--headless', '--fixed-fps', '60']
        command += [SCENE, '--', *flags]
        child_env = dict(env, ALS_RESOURCE_PACK_PATH='original' if case['original'] else args.pack)
        started = time.monotonic()
        with log.open('w', encoding='utf-8') as stream:
            try:
                code = subprocess.run(command, cwd=ROOT, env=child_env, stdout=stream,
                                      stderr=subprocess.STDOUT, timeout=240,
                                      creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0).returncode
            except subprocess.TimeoutExpired:
                code = -1
                stream.write('\nMANTLE_SCENE_TIMEOUT\n')
        text = log.read_text(encoding='utf-8', errors='replace')
        errors = re.findall(r'(?m)^ERROR:.*$', text)
        expected_selection = f"ALS_RESOURCE_PACK_SELECTED scope=non-prop replacements={pack['replacedAnimations']} pack={args.pack}"
        if not case['original'] and expected_selection not in text:
            errors.append('Candidate pack selection was not confirmed')
        if case['original'] and 'ALS_RESOURCE_PACK_SELECTED' in text:
            errors.append('Original baseline unexpectedly selected a replacement pack')
        marker = f"MANTLE_DEMO_OK case=low hz={case['hz']} "
        if marker not in text or 'starts=1 ' not in text or 'type=Low ' not in text:
            errors.append('Existing Low scene assertions did not complete')
        trajectory = [dict(tick=int(m[1]), active=m[2]=='True', time=float(m[3]),
                           position=[float(x.strip()) for x in m[4].split(',')])
                      for m in trajectory_pattern.finditer(text)]
        if not trajectory or any(abs(r['position'][0]+4)>.01 or r['position'][2]>.01 for r in trajectory):
            errors.append('Static Low fixture capsule moved away from its -Z forward plane')
        screenshots = []
        if case['render']:
            screenshots = sorted((output/case['name']).glob('mantle-*.png'),
                                 key=lambda p: int(p.stem.split('-')[-1]))
            observed = {int(p.stem.split('-')[-1]) for p in screenshots}
            expected = set(range(6, 361, 6))
            if observed != expected:
                errors.append('Missing/unexpected rendered capture ticks: '+str(sorted(expected-observed)))
        runtime_passed = code == 0 and not errors
        quality = dict(orientationPassed=False, skinPassed=False,
                       maximumAllowedSkinDepthMeters=MAX_SKIN_DEPTH_METERS)
        frame_path = output/(case['name']+'.json')
        if frame_path.is_file():
            frames = read(frame_path)['frames']
            if len(frames) != case['hz']*6 or not any(f['active'] for f in frames):
                errors.append('Incomplete published-frame diagnostics')
            elif any(f['penetration']['Vertices'] == 0 for f in frames):
                errors.append('No published skin vertices observed')
            else:
                max_yaw = max(abs(math.atan2(math.sin(f['actorYaw']), math.cos(f['actorYaw']))) for f in frames)
                worst = max(frames, key=lambda f: f['penetration']['MaximumDepthMeters'])
                depth = worst['penetration']['MaximumDepthMeters']
                quality.update(orientationPassed=max_yaw <= math.radians(1),
                               maximumActorYawDegrees=math.degrees(max_yaw),
                               skinPassed=depth <= MAX_SKIN_DEPTH_METERS,
                               maximumSkinDepthMeters=depth, worstTick=worst['tick'],
                               worstBone=worst['penetration']['DominantBone'],
                               frameReport=str(frame_path), frameReportSha256=sha(frame_path))
                if not quality['orientationPassed']:
                    errors.append('Actor yaw deviated from the static platform forward axis')
                if not quality['skinPassed']:
                    errors.append(f'Published skin penetrates platform by {depth*100:.3f} cm at tick {worst["tick"]}')
        else:
            errors.append('Missing published-frame diagnostics')
        result = dict(**case, command=command, exitCode=code, runtimePassed=runtime_passed,
                      quality=quality, passed=code==0 and not errors,
                      errors=errors, seconds=round(time.monotonic()-started, 3), log=str(log),
                      trajectory=trajectory, screenshots=[dict(path=str(p), sha256=sha(p)) for p in screenshots],
                      evidence=[line for line in text.splitlines() if 'MANTLE_START' in line or 'MANTLE_DEMO_OK' in line])
        report['checks'].append(result)
        write(output/'results.json', report)
        print(('PASS ' if result['passed'] else 'FAIL ')+case['name'], flush=True)
    changed = [str(path) for path, expected in payloads.items() if sha(path)!=expected]
    changed += [path for path, expected in frozen.items() if sha(Path(path))!=expected]
    report.update(changedFiles=changed, inputsUnchanged=not changed,
                  passed=all(r['passed'] for r in report['checks']) and not changed,
                  validationScope='Low static fixture runtime, actor yaw and published skin penetration, GPU captures and original baseline; not full-pack certification')
    write(output/'results.json', report)
    print('KIMODO_MANTLE_SCENE_'+('OK' if report['passed'] else 'FAILED'), flush=True)
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
