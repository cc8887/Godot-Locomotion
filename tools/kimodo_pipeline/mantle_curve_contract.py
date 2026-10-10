"""Separate authored distance, time-driven clearance and fixed-contact controls.

This describes the implemented consumer, not future distance-driven playback or
root-motion warping. Flat forward segments require time/phase information.
"""
from __future__ import annotations
import numpy as np

POLICY = 'mantle-curve-semantics-v1'
CONTROL_FILE = 'config/refactored_mantle_contacts.json'
DISTANCE_EPSILON_CM = 1e-5


def contract():
    return dict(policy=POLICY, playbackDriver='montage-time',
        curveRoles=dict(rootAdvanceCm='authored-root-planar-distance',
            advanceCm='time-driven-target-planar-progress', liftCm='time-driven-target-lift',
            remainingLiftCm='signed-distance-for-height-entry-match',
            leftHandSupport='fixed-anchor-contact-weight', rightHandSupport='fixed-anchor-contact-weight'),
        distanceMatching=dict(entryHeightMatch=True, continuousPlayback=False, advanceCurveTimeInversion=False),
        motionWarping=dict(rootMotionWarping=False, warpWindowsExported=False),
        requiredConsumerFeatures=['final-input-binding', 'original-key-timeline',
            'independent-planar-and-lift-progress', 'fixed-platform-local-hand-anchors', 'shared-montage-clock'])


def root_progress(trajectory, count):
    roots = [t for t in trajectory['tracks'] if t['bone'].lower() == 'root']
    if len(roots) != 1:
        raise ValueError('Mantle curve contract needs exactly one Root track')
    p = np.broadcast_to(np.asarray(roots[0]['positions'], dtype=float), (count, 3))
    if not np.isfinite(p).all():
        raise ValueError('Nonfinite mantle Root trajectory')
    delta = p-p[0]; direction = delta[-1].copy(); direction[2] = 0
    distance = np.linalg.norm(direction)
    advance = delta@direction/distance if distance > 1e-6 else np.zeros(count)
    return advance, delta[:, 2]


def analysis(keys):
    times = np.array([k['timeSeconds'] for k in keys], dtype=float)
    advance = np.array([k['advanceCm'] for k in keys], dtype=float)
    flat = np.abs(np.diff(advance)) <= DISTANCE_EPSILON_CM
    plateaus = []; i = 0
    while i < len(flat):
        if not flat[i]:
            i += 1
            continue
        start = i
        while i < len(flat) and flat[i]:
            i += 1
        window = keys[start:i+1]
        plateaus.append(dict(startTimeSeconds=float(times[start]), endTimeSeconds=float(times[i]),
            advanceCm=float(advance[start]), liftChangeCm=float(keys[i]['liftCm']-keys[start]['liftCm']),
            leftHandSupportRange=float(np.ptp([k['leftHandSupport'] for k in window])),
            rightHandSupportRange=float(np.ptp([k['rightHandSupport'] for k in window]))))
    return dict(forwardStrictlyIncreasing=bool(np.all(np.diff(advance) > DISTANCE_EPSILON_CM)),
        forwardPlateaus=plateaus, distanceEpsilonCm=DISTANCE_EPSILON_CM,
        wholeClipDistanceTimeInversionSupported=False)


def validate(value, animation, trajectories):
    """Check semantics and original distances independently of the skin baker."""
    if value.get('curveContract') != contract():
        raise ValueError('Unsupported mantle curve semantics or playback/warping capability claim')
    sources = {r['raw']['source']: r['raw'] for r in animation['sequences']}
    roots = {r['source']: r for r in trajectories['sequences']}
    if len(sources) != len(animation['sequences']) or len(roots) != len(trajectories['sequences']):
        raise ValueError('Duplicate mantle input sequence')
    seen = set()
    for sequence in value['sequences']:
        source = sequence['source']
        if source in seen or source not in sources or source not in roots:
            raise ValueError('Foreign or duplicate mantle curve sequence')
        semantic = source.split('.')[-1].removeprefix('A_Als_')
        if semantic not in ('Mantle_High', 'Mantle_Low_Left', 'Mantle_Low_Right') or sequence.get('semanticName') != semantic:
            raise ValueError('Unsupported or mismatched mantle curve semantic identity')
        seen.add(source); raw = sources[source]; keys = sequence['keys']
        count = raw['sampledKeyCount']
        if count < 2 or len(keys) != count:
            raise ValueError('Mantle curve contract timeline differs from final pose')
        required = ('timeSeconds', 'rootAdvanceCm', 'advanceCm', 'liftCm', 'remainingLiftCm',
                    'leftHandSupport', 'rightHandSupport')
        if any(any(name not in k for name in required) for k in keys):
            raise ValueError('Missing separate authored distance or mantle control channel')
        channels = {name: np.array([k[name] for k in keys], dtype=float) for name in required}
        if not all(np.isfinite(v).all() for v in channels.values()):
            raise ValueError('Nonfinite mantle curve contract channel')
        times = np.arange(count)*raw['frameRateDenominator']/raw['frameRateNumerator']
        if (np.max(np.abs(channels['timeSeconds']-times)) > 1e-5 or
                abs(times[-1]-raw['playLength']) > 1e-5):
            raise ValueError('Mantle curve contract requires original key times')
        original, lift = root_progress(roots[source], count)
        # Match the existing bake's sub-millimeter codec-noise handling.
        if np.min(np.diff(original)) < -.001 or np.min(np.diff(lift)) < -.001 or lift[-1] <= 0:
            raise ValueError('Invalid mantle progress trajectory')
        original = np.maximum.accumulate(original); lift = np.maximum.accumulate(lift)
        if np.max(np.abs(channels['rootAdvanceCm']-original)) > .001:
            raise ValueError('Authored Root distance replaced by clearance progress')
        if np.max(np.abs(channels['liftCm']-lift)) > .001:
            raise ValueError('Mantle clearance control changed authored lift')
        advance = channels['advanceCm']
        if (np.min(advance) < -.001 or np.min(np.diff(advance)) < -.001 or
                np.max(advance-original) > .001 or abs(advance[0]) > .001 or
                abs(advance[-1]-original[-1]) > .001):
            raise ValueError('Mantle clearance progress must delay Root and preserve landing')
        if np.max(np.abs(channels['remainingLiftCm']-(lift-lift[-1]))) > .001:
            raise ValueError('Inconsistent mantle entry-height distance')
        for side in ('leftHandSupport', 'rightHandSupport'):
            if np.any((channels[side] < 0) | (channels[side] > 1)):
                raise ValueError('Invalid mantle contact weight')
        if sequence.get('curveAnalysis') != analysis(keys):
            raise ValueError('Missing or stale mantle forward plateau analysis')
        if sequence['semanticName'] == 'Mantle_High':
            body = sequence.get('bodyAdvanceControl') or {}
            if body.get('policy') != 'target-skin-front-clearance-v1':
                raise ValueError('High mantle requires target-skin clearance baking')
            if np.max(np.diff(advance)/np.diff(times)) > 200.+.001:
                raise ValueError('High mantle clearance speed exceeds bake contract')
        elif np.max(np.abs(advance-original)) > .001:
            raise ValueError('Low mantle clearance algorithm is not implemented')
    if seen != set(sources):
        raise ValueError('Missing mantle curve sequences')
    return dict(policy=POLICY, passed=True, sequences=len(seen),
        sequencesWithForwardPlateaus=sum(bool(s['curveAnalysis']['forwardPlateaus']) for s in value['sequences']),
        distanceMatching=contract()['distanceMatching'], motionWarping=contract()['motionWarping'])
