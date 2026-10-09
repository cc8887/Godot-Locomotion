"""Native-export positives and damaged target negatives, without generation."""
import copy
import unittest

import numpy as np

from .target_gait import (analyze_target_gait, original_protocol,
                          selected_stop_rule, validate_als_protocol)


class TargetGaitTests(unittest.TestCase):
    def test_native_directional_and_impulse_geometry_contracts(self):
        for kind in self.protocol['assets']:
            with self.subTest(kind=kind):
                _,report=self.original(kind)
                self.assertTrue(report['passed'],[c for c in report['checks'] if not c['passed']])
        _,impulse=self.original('sprint_acceleration')
        self.assertEqual(500,impulse['expectedSpeedCmS'])
        self.assertGreaterEqual(np.asarray(impulse['geometry']['soleHeightsCm'])[:,:,0].min(),3.5)

    def test_native_lateral_velocity_is_not_forward_velocity(self):
        from .target_gait import _sole_geometry
        row=self.protocol['assets']['walk_left_forward']
        _,proper,_=_sole_geometry(row['raw'],self.protocol['skeleton'],150,(1,0))
        _,wrong,_=_sole_geometry(row['raw'],self.protocol['skeleton'],150,(0,1))
        self.assertLess(np.median(np.linalg.norm(proper[...,:2],axis=-1)),
                        np.median(np.linalg.norm(wrong[...,:2],axis=-1)))

    @classmethod
    def setUpClass(cls):
        cls.protocol = original_protocol()

    def original(self, kind="walk"):
        row = self.protocol["assets"][kind]
        report = analyze_target_gait(row["raw"], self.protocol["skeleton"], row["source"])
        return row, report

    def test_original_three_forward_assets_pass_independent_geometry_and_protocol(self):
        for kind in ("walk", "run", "crouch"):
            with self.subTest(kind=kind):
                row, report = self.original(kind)
                self.assertTrue(report["passed"], [c for c in report["checks"] if not c["passed"]])
                metadata = validate_als_protocol(row["payload"], report, row["markers"])
                self.assertTrue(metadata["passed"], metadata)
                events = {e["side"]: e for e in report["events"]}
                self.assertAlmostEqual(.5, abs(events["right"]["time"]-events["left"]["time"])/row["raw"]["playLength"], delta=.025)
        _, walk = self.original()
        self.assertEqual("left_stable", walk["stages"][4])
        self.assertEqual("right_stable", walk["stages"][21])
        self.assertEqual("LockLeft", selected_stop_rule(walk["footPlanted"][4]))
        self.assertEqual("LockRight", selected_stop_rule(walk["footPlanted"][21]))

    def test_native_threshold_boundary_keeps_plant_distinct_from_lock(self):
        expected = [(-1, "LockLeft"), (-.5, "LockLeft"), (-.25, "PlantLeft"),
                    (0, "PlantLeft"), (.25, "PlantRight"), (.5, "PlantRight"), (1, "LockRight")]
        self.assertEqual([r for _, r in expected], [selected_stop_rule(v) for v, _ in expected])

    def test_sign_flip_and_half_amplitude_are_independently_rejected(self):
        row, report = self.original()
        for factor in (-1, .5, -.5):
            with self.subTest(factor=factor):
                payload = copy.deepcopy(row["payload"])
                curve = next(c for c in payload["curves"]["curves"] if c["name"] == "FootPlanted")
                for key in curve["keys"]:
                    key["value"] *= factor
                check = validate_als_protocol(payload, report, row["markers"])
                self.assertFalse(check["passed"])
                self.assertTrue(any(not c["passed"] and "full_support_encoding" in c["check"] for c in check["checks"]))

    def test_missing_walk_curve_and_invented_crouch_curve_fail(self):
        row,report=self.original()
        payload=copy.deepcopy(row['payload'])
        support=next(c for c in payload['curves']['curves'] if c['name']=='FootPlanted')
        payload['curves']['curves'].remove(support)
        self.assertFalse(validate_als_protocol(payload,report,row['markers'])['passed'])
        row,report=self.original('crouch')
        payload=copy.deepcopy(row['payload'])
        payload['curves']['curves'].append(support)
        self.assertFalse(validate_als_protocol(payload,report,row['markers'])['passed'])

    def test_repeated_side_and_half_cycle_marker_shift_fail(self):
        row, report = self.original()
        markers = copy.deepcopy(row["markers"])
        markers[1]["name"] = markers[0]["name"]
        self.assertFalse(validate_als_protocol(row["payload"], report, markers)["passed"])
        markers = copy.deepcopy(row["markers"])
        for marker in markers:
            marker["time"] = (marker["time"]+row["raw"]["playLength"]/2) % row["raw"]["playLength"]
        self.assertFalse(validate_als_protocol(row["payload"], report, markers)["passed"])

    def test_shifted_body_rejected_even_when_root_is_zero(self):
        row, _ = self.original()
        raw = copy.deepcopy(row["raw"])
        pelvis = next(t for t in raw["tracks"] if t["bone"].lower() == "pelvis")
        for point in pelvis["positions"]:
            point[1] += 350
        report = analyze_target_gait(raw, self.protocol["skeleton"], row["source"])
        self.assertFalse(next(c for c in report["checks"] if c["check"] == "body_origin")["passed"])

    def test_no_ground_samples_fails_instead_of_certifying_zero_sliding(self):
        row, _ = self.original()
        raw = copy.deepcopy(row["raw"])
        pelvis = next(t for t in raw["tracks"] if t["bone"].lower() == "pelvis")
        for point in pelvis["positions"]:
            point[2] += 100
        report = analyze_target_gait(raw, self.protocol["skeleton"], row["source"])
        for side in ("left", "right"):
            check = next(c for c in report["checks"] if c["check"] == side+"_independent_near_ground_sliding")
            self.assertFalse(check["passed"])
            self.assertEqual(0, check["samples"])
            self.assertIsNone(check["medianCmS"])

    def test_stationary_pose_cannot_claim_valid_in_place_walk(self):
        row, _ = self.original()
        raw = copy.deepcopy(row["raw"])
        for track in raw["tracks"]:
            for field in ("positions", "rotations", "scales"):
                if track[field]:
                    track[field] = [copy.deepcopy(track[field][4 if len(track[field]) > 4 else 0])]
        report = analyze_target_gait(raw, self.protocol["skeleton"], row["source"])
        self.assertFalse(report["passed"])
        check = next(c for c in report["checks"] if c["check"] == "left_independent_near_ground_sliding")
        self.assertFalse(check["passed"])
        self.assertAlmostEqual(150., check["medianCmS"])

    def test_other_actions_cannot_silently_acquire_pilot_gait_protocol(self):
        row, _ = self.original()
        with self.assertRaisesRegex(ValueError, "Unsupported native target gait"):
            analyze_target_gait(row["raw"], self.protocol["skeleton"], "/ALS/A_Als_Roll.A_Als_Roll")


if __name__ == "__main__":
    unittest.main()
