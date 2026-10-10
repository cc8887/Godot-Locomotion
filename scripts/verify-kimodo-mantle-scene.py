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
    parser.add_argument('--include-legacy', action='store_true', help='Also measure the previous linear movement policy as negative controls')
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
    controls += [ROOT/'src/Als.Core/Actions/AlsMantlingProgressCurve.cs', ROOT/'src/Als.Core/Actions/AlsMantlingRootMotion.cs',
                 ROOT/'src/Als.Import/Compilation/AlsMantlingMotionCompiler.cs', ROOT/'scripts/verify-kimodo-mantle-scene.py']
    controls += list((ROOT/'.godot/mono/temp/bin/Debug').glob('Als*.dll'))
    controls += list((ROOT/'.godot/mono/temp/bin/Debug').glob('GodotALS.dll'))
    controls += list((ROOT/'assets/generated/mantle_contacts').glob('*.json'))
    controls += list((ROOT/'assets/config').glob('refactored_mantle*.json'))
    controls += [ROOT/'src/Als.Core/Actions/AlsMantlingContactCurve.cs',ROOT/'src/Als.Core/Locomotion/AlsMantlingHandSolver.cs',
                 ROOT/'src/Als.Core/Actions/AlsMantlingFrame.cs',ROOT/'src/Als.Import/Compilation/AlsMantlingContactCompiler.cs',
                 ROOT/'src/Als.Godot/Animation/AlsBaseLayerMantling.cs',ROOT/'src/Als.Godot/Animation/AlsProductionMovementRuntime.cs',
                 ROOT/'src/Als.Godot/Animation/AlsMantlingDemoResources.cs',ROOT/'src/Als.Godot/Locomotion/AlsCharacterMantling.cs',
                 ROOT/'src/Als.Godot/Locomotion/AlsMantlingProbe.cs',ROOT/'tools/kimodo_pipeline/mantle_contacts.py']
    frozen = {str(path): sha(path) for path in controls if path.is_file()}
    cases = [dict(name=f'low-{hz}-parallel-off', hz=hz, flags=[], render=False, original=False, root=False)
             for hz in (30, 60, 120)]
    # Injured selects the other actual Low sequence and its right support hand.
    cases += [dict(name=f'low-{hz}-right-support-off', hz=hz, flags=['--overlay=Injured'],
                   render=False, original=False, root=False) for hz in (30,60,120)]
    cases += [dict(name='low-60-parallel-on', hz=60, flags=[], render=False, original=False, root=True),
              dict(name='low-60-single-off', hz=60, flags=['--mantle-single'], render=False, original=False, root=False),
              dict(name='rendered-low-candidate', hz=60, flags=[], render=True, original=False, root=False),
              dict(name='rendered-low-original', hz=60, flags=[], render=True, original=True, root=False)]
    cases += [dict(name='rendered-low-right-support',hz=60,flags=['--overlay=Injured'],
                   render=True,original=False,root=False)]
    if args.include_legacy:
        cases += [dict(name='legacy-low-'+kind, hz=60, flags=['--mantle-legacy-linear'], render=False,
                       original=kind=='original', root=False) for kind in ('candidate','original')]
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
            command += ['--rendering-method', 'gl_compatibility', '--resolution', '1280x720', '--max-fps', '60', '--audio-driver', 'Dummy']
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
            elif any(f['committed'] != f['presented'] for f in frames):
                errors.append('Observed bones do not belong to the committed character frame')
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
                active = [f for f in frames if f['active']]
                montage = active[0].get('montage', '')
                config_root = ROOT/'assets/config' if case['original'] else resource_path(pack['configRoot'])
                root_bindings = read(config_root/'refactored_mantle_root_tracks.json')
                binding = next((m for m in root_bindings['montages'] if m['path']==montage), None)
                sequence = binding['segments'][0]['sequence'] if binding and len(binding['segments'])==1 else ''
                side = 'l' if sequence.endswith('A_Als_Mantle_Low_Right') else 'r' if sequence.endswith('A_Als_Mantle_Low_Left') else None
                support = [f['bones']['hand_'+side]['world'][1] for f in active
                           if 11/30-1e-6 <= f['montageTime'] <= 22/30+1e-6] if side else []
                if len(support) < 2:
                    errors.append('Missing configured Low support-hand timing observations')
                else:
                    quality.update(sourceSequence=sequence, supportHand=side, supportSamples=len(support),
                                   supportMinimumHeightMeters=min(support), supportMaximumHeightMeters=max(support),
                                   supportHeightExcursionMeters=max(support)-min(support))
                if not quality['orientationPassed']:
                    errors.append('Actor yaw deviated from the static platform forward axis')
                if not quality['skinPassed']:
                    errors.append(f'Published skin penetrates platform by {depth*100:.3f} cm at tick {worst["tick"]}')
                if not case['root'] and '--mantle-legacy-linear' not in case['flags']:
                    support_frames=[f for f in frames if f.get('handContacts',{}).get('LeftWeight',0)>=.999 or f.get('handContacts',{}).get('RightWeight',0)>=.999]
                    position_error=orientation_error=residual=0.
                    for f in support_frames:
                        contact=f['handContacts'];residual=max(residual,contact['ResidualCm'])
                        for side,suffix in [('left','l'),('right','r')]:
                            if contact[side.title()+'Weight']<.999:continue
                            actual=f['bones']['hand_'+suffix];desired=contact[side+'Anchor']
                            position_error=max(position_error,math.dist(actual['world'],desired['world']))
                            q=actual['rotation'];r=desired['rotation']
                            dot=abs(sum(a*b for a,b in zip(q,r)))/math.sqrt(sum(a*a for a in q)*sum(b*b for b in r))
                            orientation_error=max(orientation_error,math.degrees(2*math.acos(min(1,dot))))
                    locked=bool(support_frames) and position_error<=.001 and orientation_error<=.1 and residual<=.1
                    quality.update(contactPassed=locked,contactSamples=len(support_frames),maximumAnchorErrorMeters=position_error,
                        maximumPalmErrorDegrees=orientation_error,maximumReachResidualCm=residual)
                    if not locked:errors.append('Published hand does not maintain its baked ledge anchor/orientation')
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
    baseline = next(r for r in report['checks'] if r['name']=='rendered-low-original')['quality']
    reference_excursion = baseline.get('supportHeightExcursionMeters')
    for result in report['checks']:
        observed = result['quality'].get('supportHeightExcursionMeters')
        passed = observed is not None and reference_excursion is not None and observed <= reference_excursion+.01
        result['quality'].update(timingPassed=passed, nativeSupportHeightExcursionMeters=reference_excursion,
                                 allowedAdditionalExcursionMeters=.01)
        if not passed:
            result['passed'] = False
            result['errors'].append('Support-hand height excursion exceeds the original curve-controlled baseline + 1 cm')
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
