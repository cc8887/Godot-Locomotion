"""Observe ordinary ALS gait/release cases against the unchanged original pack.

This adds focused closure evidence; it never replaces the full non-prop matrix.
All-frame root/body/penetration assertions remain enabled. Failed frames are
retained while collection continues, and every failure remains a failing result.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import statistics
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from tools.kimodo_pipeline.locomotion_quality import POLICY


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + '\n', encoding='utf-8')


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def capture_check(data):
    frames = {row['tick']: row for row in data['frames']}
    expected = set(data['captureTicks'])
    seen, errors = set(), []
    for capture in data['captures']:
        tick = capture['requestedTick']
        row = frames.get(tick)
        path = Path(capture['screenshot'])
        if (tick in seen or row is None or capture['observedTick'] != tick or
                capture['committed'] != tick or capture['presented'] != tick or
                capture['bones'] != row['bones'] or not path.is_file() or
                capture['inputAxes'] != row['inputAxes'] or capture['inputHeld'] != row['inputHeld'] or
                ('| HELD |' if row['inputHeld'] else '| RELEASED / IDLE |') not in capture['label'] or
                any(capture[key] != row[key] for key in
                    ('presentationPending', 'visualVisible', 'visualReady', 'skeletonVisible')) or
                path.stem != f"{data['scenario']}-{tick}"):
            errors.append(f'Requested/observed/committed/presented pose mismatch at {tick}')
        seen.add(tick)
    if seen != expected:
        errors.append(f'Incomplete captures: missing={sorted(expected-seen)}, extra={sorted(seen-expected)}')
    return dict(passed=not errors, count=len(seen), errors=errors)


def presentation_check(data):
    errors = []
    for row in data['frames']:
        cold = row['tick'] == 1
        if (row['presentationPending'] != cold or
                any(row[key] != (not cold) for key in ('visualVisible', 'visualReady', 'skeletonVisible')) or
                row['preFoot']['LocationEvaluated'] != (not cold) or
                row['committed'] != row['tick'] or row['publishedInput'] != row['tick']):
            errors.append(dict(tick=row['tick'], reason='Cold/warm presentation contract violated'))
    return dict(passed=not errors, errors=errors)


def measure(data):
    """Measure actual skin geometry independently of generated contact curves."""
    frames = data['frames']
    if [r['tick'] for r in frames] != list(range(1, data['lastTick']+1)):
        raise ValueError('Incomplete committed frame sequence')
    release = data['actualReleaseTick']
    if not release or release+120 >= data['lastTick']:
        raise ValueError('Missing release or incomplete stopped observation')
    windows = dict(start=(data['pressTick']+1, data['pressTick']+60),
                   steady=(data['pressTick']+61, release),
                   release=(release+1, release+60), idle=(release+61, data['lastTick']))
    result = {}
    for name, (begin, end) in windows.items():
        rows = [r for r in frames if begin <= r['tick'] <= end]
        speeds, per_side = [], {'Item1': [], 'Item2': []}
        for a, b in zip(rows, rows[1:]):
            if b['tick'] != a['tick']+1 or b['dt'] <= 0:
                raise ValueError('Nonconsecutive skin samples or invalid delta time')
            for side in per_side:
                p, q = a['soles'][side], b['soles'][side]
                # These are the unchanged directional gate's geometric windows.
                # Contact/FootPlanted/lock curves do not select measurement rows.
                if not all(s['MinimumSupportHeight'] < POLICY['maximumSupportHeightCm'] and
                           s['SoleCentroid']['Y']-POLICY['floorLocalTopCm'] < POLICY['maximumCentroidHeightCm']
                           for s in (p, q)):
                    continue
                p, q = p['SoleCentroid'], q['SoleCentroid']
                speed = math.hypot(q['X']-p['X'], q['Z']-p['Z'])/b['dt']
                if not math.isfinite(speed):
                    raise ValueError('Nonfinite final skin speed')
                speeds.append(speed)
                per_side[side].append(speed)
        result[name] = dict(firstTick=begin, lastTick=end, samples=len(speeds),
                            perSideSamples={s: len(v) for s, v in per_side.items()},
                            medianCmPerSecond=statistics.median(speeds) if speeds else None,
                            maximumCmPerSecond=max(speeds) if speeds else None)
    return dict(windows=result,
                presentationValidation=presentation_check(data),
                firstFrame=dict(tick=1, minimumSupportHeightCm=min(
                    frames[0]['soles'][s]['MinimumSupportHeight'] for s in ('Item1', 'Item2')),
                    locationEvaluated=frames[0]['preFoot']['LocationEvaluated'],
                    rendered=frames[0]['skeletonVisible']),
                firstRenderedFrame=next((dict(tick=r['tick'], minimumSupportHeightCm=min(
                    r['soles'][s]['MinimumSupportHeight'] for s in ('Item1', 'Item2')))
                    for r in frames if r['skeletonVisible']), None),
                supportAtRelease=frames[release-1]['footPlanted'],
                stopSelectionAtRelease=frames[release-1]['stopSelectionFromCurrentCurve'],
                inputReleasedOnNextFrame=frames[release]['inputAxes'] == [0, 0],
                actualReleaseTick=release,
                allFrameViolations=data['violations'],
                supportSelectionCoverage=sorted({r['stopSelectionFromCurrentCurve'] for r in frames if r['phase']=='held'}))


def compare(reference, candidate):
    comparisons = []
    for name, a in reference['windows'].items():
        b = candidate['windows'][name]
        enough = all(r['samples'] >= POLICY['minimumSamples'] and
                     all(n > 0 for n in r['perSideSamples'].values()) for r in (a, b))
        limit = max(POLICY['medianFloorCmPerSecond'], POLICY['originalMedianMultiplier']*a['medianCmPerSecond']) \
            if a['medianCmPerSecond'] is not None else None
        passed = enough and b['medianCmPerSecond'] <= limit
        comparisons.append(dict(window=name, original=a, candidate=b, limitCmPerSecond=limit,
                                passed=passed, reason='pass' if passed else
                                'insufficient independent near-ground samples' if not enough else 'near-ground skating regression'))
    return dict(passed=all(r['passed'] for r in comparisons), windows=comparisons)


def run(args, label, pack, mode, support, release_tick, output, direction='forward'):
    name = f'{label}-{mode}-{support}' + (f'-r{release_tick}' if release_tick != 210 else '')
    if direction!='forward':name+='-'+direction
    result_path = output/(name+'.json')
    if result_path.exists() or (output/(name+'.log')).exists():
        raise ValueError(f'Refusing to overwrite existing evidence: {name}')
    capture_ticks = sorted({1, 2, 3, 4, 5, 29, 31, 35, 45, 60, *range(90, 166, 5),
                            *(release_tick+offset for offset in (-1, 0, 1, 5, 10, 20, 30, 40, 60, 120)),
                            450, 599})
    command = [str(args.godot.resolve()), '--path', str(ROOT), '--fixed-fps', '60']
    command += ['--rendering-method', 'gl_compatibility', '--max-fps', '60'] if args.render else ['--headless']
    command += ['res://scenes/tests/als_movement_release_diagnostic.tscn', '--',
                '--release-output='+str(result_path), '--release-case='+direction, '--release-mode='+mode,
                '--release-tick='+str(release_tick),
                '--release-support='+support, '--flat-floor', '--disable-root-motion',
                '--verify-release', '--collect-release-violations',
                '--capture-ticks='+','.join(map(str, capture_ticks))]
    if args.render:
        command.append('--capture-dir='+str(output/name))
    env = os.environ.copy()
    for key in ('ALS_ASSET_ROOT', 'ALS_RAW_SOURCE_GENERATED_DIR', 'ALS_REQUIRE_KIMODO_SOURCE',
                'ALS_RAW_SOURCE_REQUEST_OUTPUT', 'KIMODO_IDLE_DIAGNOSTIC_OUTPUT', 'KIMODO_IDLE_CAPTURE_DIR'):
        env.pop(key, None)
    env['ALS_RESOURCE_PACK_PATH'] = pack
    started = time.monotonic()
    with (output/(name+'.log')).open('w', encoding='utf-8') as log:
        try:
            process = subprocess.run(command, cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT,
                                     timeout=180, creationflags=subprocess.CREATE_NO_WINDOW if os.name=='nt' else 0)
            code = process.returncode
        except subprocess.TimeoutExpired:
            code = -1
    result = dict(name=name, label=label, mode=mode, support=support, direction=direction, command=command,
                  earliestReleaseTick=release_tick,
                  returncode=code, elapsedSeconds=time.monotonic()-started)
    try:
        data = read(result_path)
        result['observation'] = measure(data)
        result['captureValidation'] = capture_check(data) if args.render else None
        result['passed'] = code == 0 and data['passed'] and result['observation']['inputReleasedOnNextFrame'] and \
            result['observation']['presentationValidation']['passed'] and (
            not args.render or result['captureValidation']['passed'])
    except Exception as error:
        result.update(passed=False, error=str(error))
    print(json.dumps({k: result[k] for k in ('name','passed','returncode','elapsedSeconds')}, ensure_ascii=False), flush=True)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--godot', type=Path, default=ROOT/'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe')
    parser.add_argument('--pack', help='Candidate resource-pack path; omission runs the original baseline only')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--modes', default='walk,run,crouch')
    parser.add_argument('--directions', default='forward', help='Ordinary input directions, comma separated')
    parser.add_argument('--supports', default='auto', help='auto: four standing FootPlanted windows; crouch uses four temporal offsets because its original cycle has no FootPlanted curve')
    parser.add_argument('--release-ticks', help='Comma-separated earliest release ticks; defaults to 210 or auto crouch 210,227,244,261')
    parser.add_argument('--render', action='store_true')
    args = parser.parse_args()
    modes, supports = args.modes.split(','), args.supports.split(',')
    directions=args.directions.split(',')
    if not set(directions)<={'forward','back','left','right','left-forward','right-forward','left-back','right-back'}:
        parser.error('Unknown input direction')
    release_ticks = [int(value) for value in (args.release_ticks or '210').split(',')]
    if not set(modes) <= {'walk','run','crouch'} or (supports != ['auto'] and
            not set(supports) <= {'scheduled','left-lock','right-lock','left-plant','right-plant'}):
        parser.error('Unknown gait or support window')
    if any(tick <= 90 or tick > 299 for tick in release_ticks):
        parser.error('Release tick must leave a full steady window and 300 frames for support search/stopping')
    output = args.output.resolve()
    if (output/'manifest.json').exists():
        raise ValueError('Refusing to overwrite existing evidence manifest: '+str(output))
    output.mkdir(parents=True, exist_ok=True)
    pack_hash = None
    if args.pack:
        pack_path = ROOT/args.pack.removeprefix('res://')
        pack_hash = sha(pack_path)
    manifest = dict(schemaVersion=1, pack=args.pack, packSha256=pack_hash, policy=POLICY,
                    scope='Ordinary directional input, original/candidate final skin. Additive to existing 71-case matrix; no artistic acceptance.',
                    modes=modes, supports=supports, directions=directions, releaseTicks=release_ticks, render=args.render,
                    implementationSha256={str(p.relative_to(ROOT)): sha(p) for p in (
                        Path(__file__).resolve(), ROOT/'src/Als.Godot/Locomotion/AlsMovementReleaseDiagnostic.cs')})
    write(output/'manifest.json', manifest)
    results, comparisons = [], []
    for mode in modes:
        mode_supports = (['scheduled'] if mode == 'crouch' else ['left-lock','right-lock','left-plant','right-plant']) \
            if supports == ['auto'] else supports
        mode_ticks = [210,227,244,261] if mode == 'crouch' and supports == ['auto'] and args.release_ticks is None else release_ticks
        for support in mode_supports:
            for release_tick in mode_ticks:
                for direction in directions:
                    baseline = run(args, 'original', 'original', mode, support, release_tick, output, direction)
                    results.append(baseline)
                    if args.pack:
                        candidate = run(args, 'candidate', args.pack, mode, support, release_tick, output, direction)
                        results.append(candidate)
                        if 'observation' in baseline and 'observation' in candidate:
                            comparisons.append(dict(mode=mode, support=support, direction=direction, earliestReleaseTick=release_tick,
                                                    **compare(baseline['observation'], candidate['observation'])))
                    write(output/'results.json', results)
                    write(output/'comparisons.json', comparisons)
    summary = dict(**manifest, passed=all(r['passed'] for r in results) and all(r['passed'] for r in comparisons),
                   resultCount=len(results), passedResults=sum(r['passed'] for r in results), comparisons=comparisons)
    write(output/'summary.json', summary)
    print(json.dumps({k: summary[k] for k in ('passed','resultCount','passedResults')}, ensure_ascii=False))
    return 0 if summary['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
