"""Final-skin surface-relative skating regression gate for ordinary locomotion.

Near-ground samples are a skating proxy, not a contact-force measurement or
fully planted-foot certificate. Artistic acceptance remains separate.
"""
import math
import statistics

SCENARIOS = {f'{mode}-{direction}' for mode in
             ('walk', 'run', 'sprint', 'crouch', 'aim-walk', 'aim-run', 'aim-crouch')
             for direction in ('forward', 'back', 'left', 'right')}
POLICY = dict(schemaVersion=1, physicsHz=60, startPhase=90, endPhase=210,
              floorLocalTopCm=25, maximumSupportHeightCm=2,
              maximumCentroidHeightCm=5, minimumSamples=10,
              medianFloorCmPerSecond=25, originalMedianMultiplier=2)


def measure(data):
    rows = data['frames']
    if len(rows) != 6720 or {r['scenario'] for r in rows} != SCENARIOS:
        raise ValueError('Directional quality requires all 28 complete ordinary-input scenarios')
    result = {}
    for name in sorted(SCENARIOS):
        steady = [r for r in rows if r['scenario'] == name and 90 <= r['phase'] < 210]
        if [r['phase'] for r in steady] != list(range(90, 210)):
            raise ValueError('Incomplete steady-state phase: '+name)
        speeds = []
        for previous, current in zip(steady, steady[1:]):
            if current['tick'] != previous['tick']+1:
                raise ValueError('Nonconsecutive physics samples')
            for side in ('left', 'right'):
                a, b = previous[side], current[side]
                if not all(s['MinimumSupportHeight'] < 2 and
                           s['SoleCentroid']['Y']-25 < 5 for s in (a, b)):
                    continue
                p, q = a['SoleCentroid'], b['SoleCentroid']
                speed = math.hypot(q['X']-p['X'], q['Z']-p['Z'])*60
                if not math.isfinite(speed):
                    raise ValueError('Nonfinite sole sample')
                speeds.append(speed)
        result[name] = dict(samples=len(speeds), medianCmPerSecond=statistics.median(speeds) if speeds else None)
    return result


def compare(original, candidate):
    reference, observed = measure(original), measure(candidate)
    cases = []
    for name in sorted(SCENARIOS):
        a, b = reference[name], observed[name]
        enough = a['samples'] >= 10 and b['samples'] >= 10
        limit = max(25, 2*a['medianCmPerSecond']) if a['medianCmPerSecond'] is not None else None
        passed = enough and b['medianCmPerSecond'] <= limit
        cases.append(dict(scenario=name, original=a, candidate=b, limitCmPerSecond=limit,
                          passed=passed, reason='pass' if passed else 'near-ground skating regression' if enough else 'insufficient near-ground samples'))
    return dict(schemaVersion=1, policy=POLICY, passed=all(c['passed'] for c in cases), cases=cases,
                scope='Controlled flat floor, surface-relative final skin at 60 Hz. Near-ground proxy; no artistic or planted-foot acceptance.')
