"""Read-only posture audit of saved MantlingDemoSmoke publication traces.

This adds diagnostics, not production certification or a replacement for existing
Mantle gates. A bent driver becoming numerically straight under full support is
a known solver regression; an intentionally straight source is reported apart.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path

FLOAT32_EPSILON = 2.0 ** -23
# A conservative roundoff allowance for normalized dot/Gram determinant from
# serialized Godot float32 positions. This is a numerical singularity detector,
# not an anatomical or artistic minimum elbow angle (~0.1583 degrees).
GRAM_ROUNDOFF = 64 * FLOAT32_EPSILON
FULL_SUPPORT = .999  # Same support classification as the existing verifier.
SKIN_LIMIT_METERS = .02
ANCHOR_LIMIT_METERS = .001
PALM_LIMIT_DEGREES = .1


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def norm(vector):
    return math.sqrt(sum(x*x for x in vector))


def subtract(a, b):
    return [x-y for x, y in zip(a, b)]


def finite_vector(vector, size):
    if len(vector) != size or not all(math.isfinite(x) for x in vector):
        raise ValueError('Invalid or nonfinite pose vector')
    return vector


def arm(bones, side):
    shoulder, elbow, hand = [finite_vector(bones[name+'_'+side]['world'], 3)
                             for name in ('upperarm', 'lowerarm', 'hand')]
    upper, lower = subtract(elbow, shoulder), subtract(hand, elbow)
    a, b = norm(upper), norm(lower)
    if min(a, b) <= 0:
        raise ValueError('Zero-length arm segment')
    cosine = max(-1., min(1., sum(x*y for x, y in zip(upper, lower))/(a*b)))
    gram = max(0., 1-cosine*cosine)
    return dict(elbowBendDegrees=math.degrees(math.acos(cosine)),
                normalizedReach=math.dist(shoulder, hand)/(a+b),
                reachReserveMeters=a+b-math.dist(shoulder, hand),
                normalizedGramDeterminant=gram,
                numericallyStraight=cosine > 0 and gram <= GRAM_ROUNDOFF,
                armLengthMeters=a+b)


def quaternion(q):
    q = finite_vector(q, 4)
    length = norm(q)
    if length <= 0:
        raise ValueError('Zero quaternion')
    return [x/length for x in q]


def inverse(q):
    return [-q[0], -q[1], -q[2], q[3]]


def multiply(a, b):
    x, y, z, w = a
    u, v, t, s = b
    return [w*u+x*s+y*t-z*v, w*v-x*t+y*s+z*u,
            w*t+x*v-y*u+z*s, w*s-x*u-y*v-z*t]


def rotation_degrees(a, b):
    a, b = quaternion(a), quaternion(b)
    return math.degrees(2*math.acos(min(1., abs(sum(x*y for x, y in zip(a, b))))))


def wrist(bones, side):
    hand = bones['hand_'+side]
    if any(field not in hand for field in ('localRotation', 'restRotation', 'restPosition')):
        return dict(status='unsupported', reason='Trace lacks local/rest hand pose')
    axis = finite_vector(hand['restPosition'], 3)
    length = norm(axis)
    if length <= 0:
        return dict(status='unsupported', reason='No reference forearm segment axis')
    axis = [x/length for x in axis]
    delta = quaternion(multiply(quaternion(hand['localRotation']), inverse(quaternion(hand['restRotation']))))
    projection = sum(x*y for x, y in zip(delta[:3], axis))
    magnitude = math.hypot(projection, delta[3])
    if magnitude*magnitude <= GRAM_ROUNDOFF:
        return dict(status='unsupported', reason='Swing/twist decomposition near pure 180-degree swing is ambiguous')
    twist = [x*projection/magnitude for x in axis] + [delta[3]/magnitude]
    swing = quaternion(multiply(delta, inverse(twist)))
    angle = (math.degrees(2*math.atan2(projection, delta[3]))+180) % 360-180
    return dict(status='measured', swingDegrees=math.degrees(2*math.acos(min(1., abs(swing[3])))),
                wrappedTwistDegrees=angle,
                referenceDeviationDegrees=math.degrees(2*math.acos(min(1., abs(delta[3])))),
                axis='hand restPosition in forearm parent space; not a calibrated anatomical joint axis')


def correction_kinematics(frames, hz):
    if not math.isfinite(hz) or hz <= 0:
        return dict(status='unsupported', reason='Trace has no positive physics Hz')
    if any('modelContactRootCorrectionCm' not in f for f in frames):
        return dict(status='unsupported', reason='Trace lacks body correction vectors')
    rows, previous_velocity, previous_dt = [], None, None
    for a, b in zip(frames, frames[1:]):
        dt = (b['tick']-a['tick'])/hz
        if dt <= 0:
            raise ValueError('Publication ticks must increase')
        # Trace time is montage time and stops after completion; use physical
        # tick spacing for derivatives, including contact release and recovery.
        delta = [b['modelContactRootCorrectionCm'][k]-a['modelContactRootCorrectionCm'][k] for k in 'XYZ']
        finite_vector(delta, 3)
        velocity = [x/dt for x in delta]
        acceleration = None if previous_velocity is None else norm(subtract(velocity, previous_velocity))/((dt+previous_dt)/2)
        rows.append(dict(tick=b['tick'], deltaSeconds=dt, correctionStepCm=norm(delta),
                         correctionVelocityCmPerSecond=norm(velocity),
                         correctionAccelerationCmPerSecondSquared=acceleration))
        previous_velocity, previous_dt = velocity, dt
    return dict(status='measured', calibration='No physiological speed/acceleration limit asserted',
                timeBasis='(tick difference)/physics Hz; correction component only, not total pelvis velocity',
                maximumStepCm=max((r['correctionStepCm'] for r in rows), default=0),
                maximumSpeedCmPerSecond=max((r['correctionVelocityCmPerSecond'] for r in rows), default=0),
                maximumAccelerationCmPerSecondSquared=max((r['correctionAccelerationCmPerSecondSquared'] or 0 for r in rows), default=0),
                rows=rows)


def aligned_comparison(trace, comparison):
    if comparison is None:
        return None
    for key in ('hz', 'fixtureHeightMeters', 'scenario'):
        if trace.get(key) != comparison.get(key):
            raise ValueError('Comparison fixture/time base mismatch: '+key)
    if len(trace['frames']) != len(comparison['frames']):
        raise ValueError('Comparison frame counts differ')
    for a, b in zip(trace['frames'], comparison['frames']):
        if a['tick'] != b['tick'] or abs(a['montageTime']-b['montageTime']) > 1e-6:
            raise ValueError('Comparison does not share ticks and montage phase')
    return comparison['frames']


def audit(trace, comparison=None):
    frames = trace['frames']
    if not frames:
        raise ValueError('No published frames')
    baseline = aligned_comparison(trace, comparison)
    rows, failures = [], []
    for index, frame in enumerate(frames):
        expected = baseline[index] if baseline is not None else frame
        for side, suffix in (('Left', 'l'), ('Right', 'r')):
            target = arm(frame['bones'], suffix)
            source_bones = expected['bones'] if baseline is not None else frame.get('driverBones')
            source = arm(source_bones, suffix) if source_bones else None
            full = expected['handContacts'][side+'Weight'] >= FULL_SUPPORT
            introduced = full and source is not None and not source['numericallyStraight'] and target['numericallyStraight']
            row = dict(tick=frame['tick'], montageTime=frame['montageTime'], side=side.lower(),
                       fullSupport=full, source=source, displayed=target,
                       sourceWrist=wrist(source_bones, suffix) if source_bones else dict(status='unsupported', reason='No driver/comparison bones'),
                       displayedWrist=wrist(frame['bones'], suffix), introducedCollinearSingularity=introduced)
            if introduced:
                failures.append(dict(code='support_bent_source_became_numerically_straight', tick=frame['tick'], side=side.lower()))
            if full:
                anchor = expected['handContacts'][side.lower()+'Anchor']
                hand = frame['bones']['hand_'+suffix]
                row['expectedAnchorErrorMeters'] = math.dist(hand['world'], anchor['world'])
                row['expectedPalmErrorDegrees'] = rotation_degrees(hand['rotation'], anchor['rotation'])
            rows.append(row)
    depths = [f.get('penetration', {}).get('MaximumDepthMeters') for f in frames]
    if any(d is None for d in depths):
        skin = dict(status='unsupported', reason='Trace lacks complete skin depth observations')
    else:
        if any(not math.isfinite(d) or d < 0 for d in depths):
            raise ValueError('Invalid skin depth')
        skin = dict(status='measured', maximumDepthMeters=max(depths), limitMeters=SKIN_LIMIT_METERS,
                    passed=max(depths) <= SKIN_LIMIT_METERS, scope='Existing vertex-versus-fixture-box depth only')
        if not skin['passed']:
            failures.append(dict(code='existing_skin_depth_gate_failed'))
    support = [r for r in rows if r['fullSupport']]
    anchor_max = max((r['expectedAnchorErrorMeters'] for r in support), default=None)
    palm_max = max((r['expectedPalmErrorDegrees'] for r in support), default=None)
    if anchor_max is not None and anchor_max > ANCHOR_LIMIT_METERS:
        failures.append(dict(code='existing_anchor_position_gate_failed'))
    if palm_max is not None and palm_max > PALM_LIMIT_DEGREES:
        failures.append(dict(code='existing_palm_rotation_gate_failed'))
    return dict(schemaVersion=1, policy='mantle-style-quality-observation-v1', productionReady=False,
                status='diagnostic_rejected' if failures else 'diagnostic_no_known_failure',
                source='aligned comparison displayed bones' if baseline is not None else 'same-publication driverBones when available; not authored diffusion',
                knownAlgorithmDegradationPassed=not any(r['introducedCollinearSingularity'] for r in rows),
                thresholds=dict(fullSupportWeight=FULL_SUPPORT, numericalGramRoundoff=GRAM_ROUNDOFF,
                                numericalAngularEquivalentDegrees=math.degrees(math.asin(math.sqrt(GRAM_ROUNDOFF))),
                                numericalProvenance='64 * float32 epsilon for normalized Gram determinant, not a minimum artistic elbow angle',
                                skinMeters=SKIN_LIMIT_METERS, anchorMeters=ANCHOR_LIMIT_METERS, palmDegrees=PALM_LIMIT_DEGREES),
                summary=dict(frames=len(frames), supportSideSamples=len(support),
                             introducedCollinearSupportSamples=sum(r['introducedCollinearSingularity'] for r in support),
                             straightSourceSupportSamples=sum(r['source'] is not None and r['source']['numericallyStraight'] for r in support),
                             sourceComparisonSupportSamples=sum(r['source'] is not None for r in support),
                             displayedNumericallyStraightSupportSamples=sum(r['displayed']['numericallyStraight'] for r in support),
                             minimumDisplayedSupportBendDegrees=min((r['displayed']['elbowBendDegrees'] for r in support), default=None),
                             maximumExpectedAnchorErrorMeters=anchor_max, maximumExpectedPalmErrorDegrees=palm_max),
                existingSkinGate=skin, bodyCorrection=correction_kinematics(frames, trace.get('hz', 0)),
                failures=failures, observations=rows,
                unverified=['Anatomical or artist-approved wrist/shoulder joint limits',
                            'Continuous skin volume loss, triangle intersection and self-intersection',
                            'Acceptable body correction speed/acceleration envelope',
                            'Style preservation and complete production/runtime matrix'],
                scope='Saved-trace diagnostic; no new generation, build, engine execution or modification of old reports')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('trace', type=Path)
    parser.add_argument('--comparison', type=Path, help='Optional same-fixture/tick/montage trace; supplies independent expected support/anchors and comparison bones')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    if args.output.exists():
        raise ValueError('Preserve prior audit evidence; choose a fresh output')
    inputs = [args.trace] + ([args.comparison] if args.comparison else [])
    frozen = {str(p.resolve()): digest(p) for p in inputs}
    report = audit(read(args.trace), read(args.comparison) if args.comparison else None)
    report['inputs'] = frozen
    report['scriptSha256'] = digest(Path(__file__))
    report['inputsUnchanged'] = all(digest(Path(p)) == sha for p, sha in frozen.items())
    if not report['inputsUnchanged']:
        report['failures'].append(dict(code='audit_input_changed'))
        report['status'] = 'diagnostic_rejected'
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False)+'\n', encoding='utf-8')
    print(json.dumps(dict(status=report['status'], summary=report['summary'], output=str(args.output)), ensure_ascii=False))
    return 1 if report['failures'] else 0


if __name__ == '__main__':
    raise SystemExit(main())
