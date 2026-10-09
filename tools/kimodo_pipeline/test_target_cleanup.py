"""Target cleanup must satisfy the unchanged original protocol and bone lengths."""
import copy
import unittest
import numpy as np

from .target_cleanup import cleanup_walk
from .target_gait import original_protocol, analyze_target_gait
from .publish import world_pose, assemble_overlay_lower_body, read_json, CONFIG


class TargetCleanupTests(unittest.TestCase):
    def test_moving_overlay_keeps_stand_legs_and_authored_upper_body(self):
        index=read_json(CONFIG/'refactored_animation_sources.json')
        names={'A_Als_Default_Poses':'Default_Poses','A_Als_Stand_Pose':'Stand_Pose','A_Als_Crouch_Pose':'Crouch_Pose'}
        jobs={};raws={};sources={}
        for row in index['assets']:
            name=row['source'].split('.')[-1]
            if name not in names:continue
            semantic=names[name];sources[semantic]=row['source']
            raws[row['source']]=read_json(CONFIG/row['file'])['raw']
            jobs[row['source']]=dict(edition='refactored',contract=dict(semanticName=semantic,
                family='pose_table' if semantic=='Default_Poses' else 'base_pose',originalEvaluation={'enableRootMotion':False}))
        table={t['bone']:t for t in raws[sources['Default_Poses']]['tracks']}
        from scipy.spatial.transform import Rotation
        table['thigh_r']['rotations']=[table['thigh_r']['rotations'][0]]*4
        table['thigh_r']['rotations'][1]=Rotation.from_euler('x',.8).as_quat().tolist()
        upper=copy.deepcopy(table['spine_02'])
        assemble_overlay_lower_body(jobs,raws,{'refactored':index['skeletons']['/ALS/ALS/Character/SK_Als.SK_Als']})
        fixed={t['bone']:t for t in raws[sources['Default_Poses']]['tracks']}
        stand={t['bone']:t for t in raws[sources['Stand_Pose']]['tracks']}
        np.testing.assert_allclose(fixed['thigh_r']['rotations'][1],stand['thigh_r']['rotations'][0],atol=1e-12)
        self.assertEqual(upper,fixed['spine_02'])

    def source(self):
        protocol = original_protocol()
        original = protocol['assets']['walk']
        values = np.asarray(original['curveValues'])
        contacts = np.zeros((len(values), 2, 2), int)
        contacts[values <= -.99, 0] = 1
        contacts[values >= .99, 1] = 1
        receipt = dict(pass_=True, outputFrameCount=len(values), policy={'version': 'original-protocol-test'},
                       transformedDetection={'sampleContacts': contacts.tolist()})
        receipt['pass'] = receipt.pop('pass_')
        return original, protocol['skeleton'], receipt

    def test_original_walk_remains_valid_and_limbs_do_not_stretch(self):
        original, skeleton, receipt = self.source()
        raw = original['raw']; before = copy.deepcopy(raw)
        fixed, _ = cleanup_walk(raw, skeleton, receipt, phase_shift=0)
        self.assertEqual(raw, before)
        report = analyze_target_gait(fixed, skeleton, original['source'])
        self.assertTrue(report['passed'], [c for c in report['checks'] if not c['passed']])
        a, b = world_pose(raw, skeleton), world_pose(fixed, skeleton)
        for thigh, calf, foot in [('thigh_l', 'calf_l', 'foot_l'), ('thigh_r', 'calf_r', 'foot_r')]:
            for parent, child in [(thigh, calf), (calf, foot)]:
                np.testing.assert_allclose(np.linalg.norm(a[child][1]-a[parent][1], axis=1),
                                           np.linalg.norm(b[child][1]-b[parent][1], axis=1), atol=1e-8)

    def test_failed_authoring_receipt_cannot_enter_target_cleanup(self):
        original, skeleton, receipt = self.source()
        receipt['pass'] = False
        with self.assertRaisesRegex(ValueError, 'passing source gate'):
            cleanup_walk(original['raw'], skeleton, receipt)


if __name__ == '__main__':
    unittest.main()
