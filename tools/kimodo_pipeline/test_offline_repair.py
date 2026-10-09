"""ABI and coordinate safeguards for offline repair; no synthetic asset delivery."""
import unittest
import numpy as np

from .offline_repair import correction_contacts, restore_travel
from .test_gait import walking_fixture


class OfflineRepairTests(unittest.TestCase):
    def test_native_four_stride_preserves_left_right_over_multiple_frames(self):
        expanded = np.array([[1, 0, 0, 0, 1, 1], [0, 1, 1, 1, 0, 0]], dtype=float)
        result = correction_contacts(expanded, 2).ravel()
        # These indices are the C++ binding's 4*frame + side offsets.
        self.assertEqual([1, 0, 0, 1], result[:4].tolist())
        self.assertEqual([0, 1, 1, 0], result[4:8].tolist())

    def test_independent_six_channel_plan_requires_explicit_authoring(self):
        with self.assertRaisesRegex(ValueError, 'Independent toe-end'):
            correction_contacts([[1, 0, 1, 0, 1, 1]], 1)

    def test_missing_or_nonfinite_contacts_rejected(self):
        for values in [np.zeros((3, 2)), np.full((3, 4), np.nan), np.full((3, 4), 1.5)]:
            with self.assertRaises(ValueError):
                correction_contacts(values, 3)

    def test_in_place_cannot_be_repaired_as_world_stationary_feet_without_travel(self):
        source, rig = walking_fixture()
        with self.assertRaisesRegex(ValueError, 'requires the removed'):
            restore_travel(source, rig, 'in-place')
        with self.assertRaisesRegex(ValueError, 'second trajectory'):
            restore_travel(source, rig, 'travelling', {})


if __name__ == '__main__':
    unittest.main()
