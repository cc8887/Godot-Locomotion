"""Diagnostic distinctions that the old endpoint/skin gates did not exercise."""
import copy
import importlib.util
from pathlib import Path
import unittest

SPEC = importlib.util.spec_from_file_location('mantle_audit', Path(__file__).parents[1]/'audit-mantle-style-quality.py')
AUDIT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(AUDIT)


def trace(straight_source=False, straight_target=True):
    def bones(straight):
        result = {}
        for side in 'lr':
            for name, position in [('upperarm', [0., 0., 0.]), ('lowerarm', [1., 0., 0.]),
                                   ('hand', [2., 0., 0.] if straight else [1., 1., 0.])]:
                result[name+'_'+side] = dict(world=position, rotation=[0., 0., 0., 1.],
                    localRotation=[0., 0., 0., 1.], restRotation=[0., 0., 0., 1.], restPosition=[1., 0., 0.])
        return result
    displayed = bones(straight_target)
    frame = dict(tick=1, montageTime=.5, bones=displayed, driverBones=bones(straight_source),
        handContacts=dict(LeftWeight=1., RightWeight=0., leftAnchor=dict(world=displayed['hand_l']['world'], rotation=[0., 0., 0., 1.])),
        modelContactRootCorrectionCm=dict(X=0., Y=0., Z=0.), penetration=dict(MaximumDepthMeters=0.))
    return dict(hz=60, fixtureHeightMeters=1.5, scenario='high', frames=[frame])


class AuditTests(unittest.TestCase):
    def test_detects_solver_introduced_singularity_even_with_perfect_contacts(self):
        result = AUDIT.audit(trace())
        self.assertTrue(result['existingSkinGate']['passed'])
        self.assertEqual(result['summary']['maximumExpectedAnchorErrorMeters'], 0)
        self.assertEqual(result['summary']['introducedCollinearSupportSamples'], 1)
        self.assertEqual(result['status'], 'diagnostic_rejected')

    def test_intentionally_straight_source_is_not_bent_source_regression(self):
        result = AUDIT.audit(trace(straight_source=True))
        self.assertEqual(result['summary']['straightSourceSupportSamples'], 1)
        self.assertTrue(result['knownAlgorithmDegradationPassed'])
        self.assertFalse(result['productionReady'])

    def test_free_arm_does_not_become_support_failure(self):
        value = trace(); value['frames'][0]['handContacts']['LeftWeight'] = 0
        self.assertTrue(AUDIT.audit(value)['knownAlgorithmDegradationPassed'])

    def test_independent_support_detects_unlocked_nonpenetrating_hand(self):
        expected = trace(straight_target=False)
        broken = copy.deepcopy(expected)
        broken['frames'][0]['handContacts']['LeftWeight'] = 0
        broken['frames'][0]['bones']['hand_l']['world'][2] = .2
        result = AUDIT.audit(broken, expected)
        self.assertTrue(result['existingSkinGate']['passed'])
        self.assertIn('existing_anchor_position_gate_failed', [f['code'] for f in result['failures']])

    def test_old_skin_failure_retained(self):
        value = trace(straight_target=False); value['frames'][0]['penetration']['MaximumDepthMeters'] = .02001
        self.assertIn('existing_skin_depth_gate_failed', [f['code'] for f in AUDIT.audit(value)['failures']])

    def test_wrist_axis_decomposition_and_missing_observation(self):
        import math
        bones = trace()['frames'][0]['bones']
        bones['hand_l']['localRotation'] = [math.sin(math.pi/8), 0., 0., math.cos(math.pi/8)]
        result = AUDIT.wrist(bones, 'l')
        self.assertAlmostEqual(result['wrappedTwistDegrees'], 45.)
        self.assertAlmostEqual(result['swingDegrees'], 0.)
        del bones['hand_l']['restPosition']
        self.assertEqual(AUDIT.wrist(bones, 'l')['status'], 'unsupported')

    def test_correction_derivatives_use_physics_ticks_not_frozen_montage_time(self):
        value = trace(straight_target=False)
        value['frames'] = [copy.deepcopy(value['frames'][0]) for _ in range(3)]
        for i, frame in enumerate(value['frames']):
            frame['tick'] = i+1; frame['modelContactRootCorrectionCm']['X'] = i
        result = AUDIT.audit(value)['bodyCorrection']
        self.assertAlmostEqual(result['maximumSpeedCmPerSecond'], 60.)
        self.assertAlmostEqual(result['maximumAccelerationCmPerSecondSquared'], 0.)

    def test_comparison_phase_mismatch_is_rejected(self):
        good = trace(); bad = copy.deepcopy(good); bad['frames'][0]['montageTime'] += .1
        with self.assertRaises(ValueError): AUDIT.audit(bad, good)


if __name__ == '__main__':
    unittest.main()
