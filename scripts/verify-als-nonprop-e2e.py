"""Run the integrated ALS non-prop runtime matrix against one immutable pack.

Build/import and final source-request compilation precede this runner. Each
process keeps its own clock; existing scene assertions and tolerances apply.
"""
from __future__ import annotations

import argparse
from concurrent.futures import ThreadPoolExecutor, as_completed
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from tools.kimodo_pipeline.locomotion_quality import compare
OFF = '--disable-root-motion'
ON = '--enable-root-motion'
COMPLETE = ['--layered-frame', '--foot-ik-frame', '--based-foot-lock',
            '--refactored-pose-curves', '--refactored-movement-curves',
            '--refactored-foot-frame', '--foot-lock-gravity-twist',
            '--foot-lock-final-contact', '--foot-ground-clearance', '--foot-contact-toes',
            '--refactored-stance-hosts', '--refactored-layering-frame']


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False)+'\n', encoding='utf-8')


def validate_release_captures(path):
    data = read(path)
    frames = {row['tick']: row for row in data['frames']}
    expected = {29, 31, 35, 40, 45, 60, 90, 150, 209, 211, 220, 230, 240, 250, 270, 330, 450, 599}
    seen = set()
    errors = []
    for capture in data['captures']:
        tick = capture['requestedTick']
        row = frames.get(tick)
        screenshot = Path(capture['screenshot'])
        if (tick in seen or row is None or capture['observedTick'] != tick or
            capture['committed'] != tick or capture['presented'] != tick or
            capture['bones'] != row['bones'] or not screenshot.is_file() or
            screenshot.stem != f"{data['scenario']}-{tick}"):
            errors.append(f'Capture did not present the requested committed pose: tick={tick}')
        seen.add(tick)
    if seen != expected:
        errors.append(f'Capture ticks differ: missing={sorted(expected-seen)}, extra={sorted(seen-expected)}')
    return dict(passed=not errors, count=len(data['captures']), errors=errors)


def validate_directional_captures(path):
    data=read(path)
    frames={row['tick']:row for row in data['frames']}
    expected={row['tick'] for row in data['frames'] if row['phase'] in (120,135,150,165)}
    seen=set(); errors=[]
    for capture in data['captures']:
        tick=capture['requestedTick'];row=frames.get(tick);screenshot=Path(capture['screenshot'])
        if (tick in seen or row is None or capture['observedTick']!=tick or
            capture['committed']!=tick or capture['presented']!=tick or
            capture['bones']!=row['bones'] or not screenshot.is_file() or
            screenshot.stem!=row['scenario']+'-'+str(row['phase'])):
            errors.append('Directional capture differs from committed pose: '+str(tick))
        seen.add(tick)
    if len(expected)!=112 or seen!=expected:
        errors.append('Missing or unexpected directional capture frames')
    standing={c['bodyPixelsSha256'] for c in data['captures'] if Path(c['screenshot']).stem.startswith('walk-forward-')}
    crouching={c['bodyPixelsSha256'] for c in data['captures'] if Path(c['screenshot']).stem.startswith('crouch-forward-')}
    if not standing or not crouching or standing & crouching:
        errors.append('Standing and crouching captures reused the same 3D image')
    groups={}
    for c in data['captures']:
        groups.setdefault(frames[c['requestedTick']]['scenario'],[]).append(c)
    for scenario,captures in groups.items():
        base=captures[0]['bones']
        pose_delta=max((sum((a-b)**2 for a,b in zip(base[bone]['component'],c['bones'][bone]['component']))**.5
            for c in captures for bone in base),default=0)
        if pose_delta>.005 and len({c['bodyPixelsSha256'] for c in captures})<2:
            errors.append('Moving pose reused frozen 3D image: '+scenario)
    return dict(passed=not errors,count=len(data['captures']),errors=errors)


def cases(output):
    matrix = []

    def add(name, scene, marker, flags, render=False):
        if render:
            flags = [*flags, '--capture-dir='+str(output/name)]
        matrix.append(dict(name=name, scene='res://scenes/tests/'+scene+'.tscn', marker=marker,
                           flags=flags, render=render))

    add('idle-default-off', 'kimodo_idle_diagnostic', 'KIMODO_IDLE_DIAGNOSTIC_OK', [OFF])
    matrix.append(dict(name='ordinary-default-entry', scene='res://scenes/demo/als_demo.tscn',
                       marker='ALS_ROOT_MOTION_POLICY enabled='+str(read(ROOT/'assets/config/als_animation_settings.json')['enableRootMotion']), flags=[], render=False,
                       engineArgs=['--quit-after', '360', '--max-fps', '60']))
    for hz in (30, 60, 120):
        for mode in ('single', 'parallel'):
            extra = ['--stance-single'] if mode == 'single' else []
            add(f'stance-{hz}-{mode}', 'refactored_stance_demo_smoke', 'ALS_REFACTORED_STANCE_DEMO_OK',
                [OFF, '--stance-hz='+str(hz), '--foot-quality', *extra])
            extra = ['--parallel'] if mode == 'parallel' else []
            add(f'platform-{hz}-{mode}', 'refactored_foot_dispatch_smoke', 'NON_PROP_OVERLAYS_DISPATCH_OK',
                [OFF, *COMPLETE, '--hz='+str(hz), '--characters=10', '--overlay-cycle',
                 '--non-prop-overlays', '--contact-platform', '--platform-motion', '--action-requests', *extra])
        for case in ('low', 'high'):
            add(f'mantle-{hz}-{case}-off', 'mantling_demo_smoke', 'MANTLE_DEMO_OK',
                [OFF, '--mantle-hz='+str(hz), '--mantle-case='+case])
        for mode in ('single', 'parallel'):
            extra = ['--single'] if mode == 'single' else []
            add(f'roll-{hz}-{mode}-on', 'rolling_gameplay_smoke', 'ROLLING_GAMEPLAY_OK',
                [ON, '--hz='+str(hz), *extra])
    for case in ('moving', 'air', 'blocked', 'fast', 'destroy', 'cancel'):
        add('mantle-60-'+case+'-off', 'mantling_demo_smoke', 'MANTLE_DEMO_OK', [OFF, '--mantle-case='+case])
    for case in ('low', 'high', 'moving', 'destroy'):
        add('mantle-60-'+case+'-on', 'mantling_demo_smoke', 'MANTLE_DEMO_OK', [ON, '--mantle-case='+case])
    for overlay in ('Default', 'Masculine', 'Feminine', 'Injured', 'HandsTied'):
        for facing in ('front', 'back'):
            add(f'recovery-{overlay}-{facing}', 'character_ragdoll_recovery_smoke', 'CHARACTER_RAGDOLL_RECOVERY_OK',
                [OFF, '--overlay='+overlay, '--'+facing])
    for case in ('pending', 'active', 'suspend', 'generation'):
        add('recovery-lifecycle-'+case, 'character_ragdoll_recovery_smoke', 'CHARACTER_RAGDOLL_RECOVERY_OK',
            [OFF, '--lifecycle='+case])
    for case in ('landing', 'roll'):
        add('recovery-auto-'+case, 'character_ragdoll_recovery_smoke', 'CHARACTER_RAGDOLL_RECOVERY_OK',
            [ON if case == 'roll' else OFF, '--auto='+case])
    add('recovery-interrupt', 'character_ragdoll_recovery_smoke', 'CHARACTER_RAGDOLL_RECOVERY_OK', [OFF, '--interrupt'])
    for mode in ('single', 'parallel'):
        add('flail-'+mode, 'character_ragdoll_flail_smoke', 'CHARACTER_RAGDOLL_FLAIL_OK',
            [OFF, '--key-entry', '--pause', *(['--single'] if mode == 'single' else [])])
        add('camera-'+mode, 'native_camera_demo_smoke', 'ALS_NATIVE_CAMERA_DEMO_OK',
            [OFF, *(['--camera-single'] if mode == 'single' else [])])
    for enabled in (False, True):
        add('root-policy-'+str(enabled).lower(), 'root_motion_policy_smoke', 'ALS_ROOT_MOTION_POLICY_OK', [ON if enabled else OFF])
    add('keyboard-mouse', 'p4_keyboard_mouse_smoke', 'P4_KEYBOARD_MOUSE_OK', [OFF])
    add('camera-query-recovery', 'native_camera_recovery_smoke', 'ALS_CAMERA_RECOVERY_OK', [OFF])
    for mode in ('single', 'parallel'):
        add('directional-'+mode, 'als_directional_movement_smoke', 'ALS_DIRECTIONAL_MOVEMENT_OK',
            [OFF, '--direction-output='+str(output/('directional-'+mode+'.json')),
             *(['--parallel'] if mode == 'parallel' else [])])
    add('rendered-directional', 'als_directional_movement_smoke', 'ALS_DIRECTIONAL_MOVEMENT_OK',
        [OFF, '--direction-output='+str(output/'rendered-directional.json')], render=True)
    add('rendered-idle', 'kimodo_idle_diagnostic', 'KIMODO_IDLE_DIAGNOSTIC_OK', [OFF])
    # Idle's diagnostic capture switch differs from the generic frame capture.
    matrix[-1].update(render=True, flags=[OFF])
    add('rendered-stance', 'refactored_stance_demo_smoke', 'ALS_REFACTORED_STANCE_DEMO_OK',
        [OFF, '--stance-single', '--foot-quality'], render=True)
    add('rendered-mantle', 'mantling_demo_smoke', 'MANTLE_DEMO_OK', [OFF, '--mantle-case=high'], render=True)
    for direction in ('forward', 'left-forward'):
        add('release-'+direction, 'als_movement_release_diagnostic', 'ALS_MOVEMENT_RELEASE_OK',
            [OFF, '--flat-floor', '--verify-release', '--release-case='+direction,
             '--release-output='+str(output/('release-'+direction+'.json'))])
        add('rendered-release-'+direction, 'als_movement_release_diagnostic', 'ALS_MOVEMENT_RELEASE_OK',
            [OFF, '--verify-release', '--release-case='+direction,
             '--release-output='+str(output/('rendered-release-'+direction+'.json'))], render=True)
    return matrix


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--godot', type=Path, default=ROOT/'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe')
    parser.add_argument('--pack', required=True, help='res:// path to a staged/finalized pack')
    parser.add_argument('--output', type=Path, required=True, help='Fresh evidence directory')
    parser.add_argument('--jobs', type=int, choices=(1, 2), default=2)
    parser.add_argument('--headless-fixed-fps', type=int, choices=(0, 60), default=0,
                        help='60 accelerates controlled headless simulation; rendered scenarios retain real-time synchronization')
    parser.add_argument('--only', action='append', help='Exact scenario name, for an independently logged repair run')
    parser.add_argument('--diagnostic-partial-pack',action='store_true',
                        help='Exercise all requested cases on an explicitly incomplete diagnostic pack; never certify full replacement')
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    if not args.pack.startswith('res://'):
        raise ValueError('Expected a resource pack path')
    pack_path = (ROOT/args.pack[6:]).resolve()
    pack_path.relative_to(ROOT/'assets/generated')
    pack = read(pack_path)
    expected_replacements=183
    if args.diagnostic_partial_pack:
        from tools.kimodo_pipeline.scope import is_prop
        contracts=read(ROOT/'docs/architecture/kimodo-als-asset-contracts.json')['assets']
        expected_sources={c['source'] for c in contracts if not is_prop(c)}
        missing=pack.get('missingNonPropSources',[])
        native=read(Path(pack['nativeRetargetBundle']))
        replaced={j['source'] for j in native['jobs']}
        if (pack.get('status')!='diagnostic' or not missing or len(set(missing))!=len(missing) or
            pack['replacedAnimations']!=len(replaced) or replaced & set(missing) or replaced | set(missing)!=expected_sources):
            raise ValueError('Diagnostic missing-source coverage is inconsistent')
        expected_replacements=pack['replacedAnimations']
    if pack['scope'] != 'non-prop' or pack['replacedAnimations'] != expected_replacements or len(pack['preservedProps']) != 70:
        raise ValueError('Wrong replacement scope')
    snapshot = {r['path']: r['sha256'] for r in pack['files']}
    for resource, expected in snapshot.items():
        if sha(ROOT/resource[6:]) != expected:
            raise ValueError('Changed pack payload: '+resource)
    matrix = cases(output)
    settings_path = ROOT/'assets/config/als_animation_settings.json'
    settings_sha = sha(settings_path)
    if args.only:
        selected = set(args.only)
        if selected-set(r['name'] for r in matrix):
            raise ValueError('Unknown scenario')
        matrix = [r for r in matrix if r['name'] in selected]
    write(output/'matrix.json', dict(schemaVersion=1, pack=args.pack, packSha256=sha(pack_path),
                                    animationSettings=read(settings_path),headlessFixedFps=args.headless_fixed_fps,cases=matrix,
                                    diagnosticPartialPack=args.diagnostic_partial_pack))
    env = os.environ.copy()
    for key in ('ALS_ASSET_ROOT', 'ALS_RAW_SOURCE_GENERATED_DIR', 'ALS_REQUIRE_KIMODO_SOURCE',
                'ALS_RAW_SOURCE_REQUEST_OUTPUT', 'KIMODO_IDLE_DIAGNOSTIC_OUTPUT', 'KIMODO_IDLE_CAPTURE_DIR'):
        env.pop(key, None)
    env['ALS_RESOURCE_PACK_PATH'] = args.pack
    results = []

    def run(case):
        command = [str(args.godot.resolve())]
        if case['render']:
            command += ['--rendering-method', 'gl_compatibility']
        else:
            command += ['--headless']
            if args.headless_fixed_fps:
                command += ['--fixed-fps', str(args.headless_fixed_fps)]
        command += ['--path', str(ROOT), *case.get('engineArgs', []), case['scene'], '--', *case['flags']]
        log = output/(case['name']+'.log')
        child_env = env.copy()
        if case.get('original', False):
            child_env['ALS_RESOURCE_PACK_PATH'] = 'original'
        if 'idle' in case['name']:
            child_env['KIMODO_IDLE_DIAGNOSTIC_OUTPUT'] = str(output/(case['name']+'.json'))
            if case['render']:
                child_env['KIMODO_IDLE_CAPTURE_DIR'] = str(output/case['name'])
        started = time.monotonic()
        with log.open('w', encoding='utf-8') as stream:
            try:
                result = subprocess.run(command, cwd=ROOT, env=child_env, stdout=stream, stderr=subprocess.STDOUT,
                                        timeout=240, creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)
                code = result.returncode
            except subprocess.TimeoutExpired:
                code = -1
                stream.write('\nE2E_TIMEOUT\n')
        text = log.read_text(encoding='utf-8', errors='replace')
        errors = re.findall(r'(?m)^ERROR:.*$', text)
        screenshots = [str(p) for p in (output/case['name']).glob('*.png')]
        capture_validation = None
        if case['name'].startswith('rendered-release-'):
            try:
                capture_validation = validate_release_captures(output/(case['name']+'.json'))
            except (OSError, ValueError, KeyError) as error:
                capture_validation = dict(passed=False, errors=[str(error)])
        if case['name']=='rendered-directional':
            try:
                capture_validation=validate_directional_captures(output/(case['name']+'.json'))
            except (OSError, ValueError, KeyError) as error:
                capture_validation=dict(passed=False,errors=[str(error)])
        passed = (code == 0 and case['marker'] in text and
                  (case.get('original', False) or f'ALS_RESOURCE_PACK_SELECTED scope=non-prop replacements={expected_replacements}' in text) and
                  not errors and
                  (not case['render'] or len(screenshots) >= (112 if case['name']=='rendered-directional' else
                                                             18 if case['name'].startswith('rendered-release-') else 3)) and
                  (capture_validation is None or capture_validation['passed']))
        lines = [line for line in text.splitlines() if '_OK ' in line or '_OK' == line[-3:]]
        record = dict(**case, exitCode=code, passed=passed, seconds=round(time.monotonic()-started, 3),
                      log=str(log), evidence=lines, errors=errors,
                      screenshots=screenshots, captureValidation=capture_validation)
        print(('PASS ' if passed else 'FAIL ')+case['name'], flush=True)
        return record

    with ThreadPoolExecutor(max_workers=args.jobs) as executor:
        pending = [executor.submit(run, case) for case in matrix if not case['render']]
        for future in as_completed(pending):
            results.append(future.result())
            write(output/'results.json', results)
    for case in matrix:
        if case['render']:
            results.append(run(case))
            write(output/'results.json', results)
    directional_quality = dict(passed=False, reason='Directional coverage not selected')
    directional_names = {'directional-single', 'directional-parallel', 'rendered-directional'}
    if directional_names <= {r['name'] for r in results if r['passed']}:
        baseline = run(dict(name='directional-original-baseline',
                            scene='res://scenes/tests/als_directional_movement_smoke.tscn',
                            marker='ALS_DIRECTIONAL_MOVEMENT_OK', flags=[OFF,
                            '--direction-output='+str(output/'directional-original-baseline.json')],
                            render=False, original=True))
        write(output/'original-baseline-result.json', baseline)
        if baseline['passed']:
            reference = read(output/'directional-original-baseline.json')
            quality = {}
            for name in sorted(directional_names):
                quality[name] = compare(reference, read(output/(name+'.json')))
            fields = ('tick', 'scenario', 'phase', 'speed', 'velocity', 'inputDirection',
                      'yaw', 'gait', 'stance', 'rotationMode', 'bones',
                      'horizontalPelvisOffsetMeters', 'actorPosition')
            sequences = {name: [{k: row[k] for k in fields} for row in read(output/(name+'.json'))['frames']]
                         for name in directional_names}
            exact = all(rows == sequences['directional-single'] for rows in sequences.values())
            report = dict(pack=args.pack, packSha256=sha(pack_path), modes=quality,
                          exactBodyPhysicsParity=exact,
                          passed=exact and all(r['passed'] for r in quality.values()),
                          traceHashes={name: sha(output/(name+'.json')) for name in
                                       sorted(directional_names | {'directional-original-baseline'})})
            write(output/'directional-quality.json', report)
            directional_quality = dict(passed=report['passed'], report=str(output/'directional-quality.json'),
                                       sha256=sha(output/'directional-quality.json'))
            print(('PASS ' if report['passed'] else 'FAIL ')+'directional-skating-quality', flush=True)
        else:
            directional_quality = dict(passed=False, reason='Original baseline failed')
    changed = [resource for resource, expected in snapshot.items() if sha(ROOT/resource[6:]) != expected]
    parity = []
    for hz in (30, 60, 120):
        pair = [r for r in results if r['name'] in (f'platform-{hz}-single', f'platform-{hz}-parallel')]
        if len(pair) != 2:
            continue
        def digests(record):
            text = Path(record['log']).read_text(encoding='utf-8')
            m = re.search(r'pose=([0-9A-F]+) root=([0-9A-F]+) result=([0-9A-F]+)', text)
            return m.groups() if m else None
        parity.append(dict(hz=hz, passed=pair[0]['passed'] and pair[1]['passed'] and
                           digests(pair[0]) is not None and digests(pair[0]) == digests(pair[1]),
                           digests=[digests(r) for r in pair]))
    summary = dict(schemaVersion=1, pack=args.pack, total=len(results), passed=sum(r['passed'] for r in results),
                   failed=[r['name'] for r in results if not r['passed']], payloadChanges=changed,
                   animationSettingsChanged=sha(settings_path) != settings_sha,
                   singleParallelParity=parity, propsUsed=False, manualAcceptance=False,
                   headlessFixedFps=args.headless_fixed_fps,
                   completeMatrix=not bool(args.only) and not args.diagnostic_partial_pack,
                   executedAllScenarios=not bool(args.only),diagnosticPartialPack=args.diagnostic_partial_pack,
                   missingNonPropSources=pack.get('missingNonPropSources',[]) if args.diagnostic_partial_pack else [])
    summary['packSha256'] = sha(pack_path)
    summary['directionalQuality'] = directional_quality
    write(output/'summary.json', summary)
    directional_required = not args.only or bool(directional_names & {r['name'] for r in results})
    success = (not args.diagnostic_partial_pack and not summary['failed'] and not changed and not summary['animationSettingsChanged'] and
               all(r['passed'] for r in parity) and (not directional_required or directional_quality['passed']))
    prefix = 'ALS_NON_PROP_DIAGNOSTIC_' if args.diagnostic_partial_pack else ('ALS_NON_PROP_PARTIAL_' if args.only else 'ALS_NON_PROP_E2E_')
    print(prefix+('OK' if success else 'FAILED')+' '+str(output/'summary.json'), flush=True)
    return 0 if success else 1


if __name__ == '__main__':
    sys.exit(main())
