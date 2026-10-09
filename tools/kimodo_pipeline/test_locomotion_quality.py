"""Release failures for skating, missing samples, and incomplete movement coverage."""
import copy
import unittest
from .locomotion_quality import SCENARIOS, compare


def trace(speed):
    rows = []
    for index, scenario in enumerate(sorted(SCENARIOS)):
        for phase in range(240):
            foot = dict(MinimumSupportHeight=0, SoleCentroid=dict(X=phase*speed/60, Y=26, Z=0))
            rows.append(dict(tick=index*240+phase+1, scenario=scenario, phase=phase,
                             left=foot, right=foot))
    return dict(frames=rows)


class LocomotionQualityTests(unittest.TestCase):
    def test_stationary_sole_reference_rejects_large_skating(self):
        report = compare(trace(0), trace(50))
        self.assertFalse(report['passed'])
        self.assertEqual(28, len(report['cases']))
        self.assertTrue(all(c['reason'] == 'near-ground skating regression' for c in report['cases']))

    def test_gate_is_relative_to_same_original_scenario(self):
        self.assertTrue(compare(trace(20), trace(35))['passed'])
        self.assertFalse(compare(trace(20), trace(45))['passed'])

    def test_absent_ground_samples_cannot_pass_as_zero_skating(self):
        airborne = copy.deepcopy(trace(0))
        for row in airborne['frames']:
            for side in ('left', 'right'):
                row[side]['SoleCentroid']['Y'] = 40
        report = compare(trace(0), airborne)
        self.assertFalse(report['passed'])
        self.assertTrue(all(c['reason'] == 'insufficient near-ground samples' for c in report['cases']))

    def test_missing_or_reordered_physics_samples_fail(self):
        incomplete = trace(0)
        incomplete['frames'].pop()
        with self.assertRaisesRegex(ValueError, 'complete'):
            compare(trace(0), incomplete)
        reordered = trace(0)
        reordered['frames'][90], reordered['frames'][91] = reordered['frames'][91], reordered['frames'][90]
        with self.assertRaisesRegex(ValueError, 'Incomplete steady-state'):
            compare(trace(0), reordered)
