import unittest
import numpy as np
from .test_root_ownership import fixture,track
from .root_ownership import decompose
from .viewer_pack import preview_raw
from .publish import world_pose


class TargetPreviewTests(unittest.TestCase):
    def test_preview_preserves_authored_root_and_runtime_residual(self):
        raw,skeleton=fixture()
        track(raw,'pelvis')['positions']=np.column_stack((np.linspace(150,450,31),np.zeros(31),np.full(31,90))).tolist()
        authored,runtime,report=decompose(raw,skeleton,family='gait_cycle')
        for value in (raw,authored,runtime):
            preview=preview_raw(value,skeleton,'Walk',{})
            rebuilt=dict(value,tracks=[dict(bone=n,positions=np.asarray(preview['localPositionsCm'])[:,i].tolist(),
                rotations=np.asarray(preview['localQuaternions'])[:,i].tolist(),scales=np.asarray(preview['localScales'])[:,i].tolist())
                for i,n in enumerate(skeleton['rawBoneNames'])])
            expected=world_pose(value,skeleton);actual=world_pose(rebuilt,skeleton)
            for name in expected:np.testing.assert_allclose(actual[name][1],expected[name][1],atol=1e-10)
            self.assertFalse(preview['displayRootOnly']);self.assertEqual('z',preview['up'])
        self.assertGreater(preview_raw(authored,skeleton,'Walk',{})['source']['metrics']['maximumRootHorizontalCm'],290)
        metrics=preview_raw(runtime,skeleton,'Walk',{})['source']['metrics']
        self.assertEqual(0,metrics['maximumRootHorizontalCm'])
        self.assertLess(metrics['maximumPelvisRootHorizontalCm'],1e-8)

    def test_incompatible_track_timeline_is_rejected(self):
        raw,skeleton=fixture();track(raw,'pelvis')['positions']=[[0,0,90]]*2
        with self.assertRaisesRegex(ValueError,'incompatible timeline'):preview_raw(raw,skeleton,'Walk',{})

    def test_virtual_reference_tail_is_not_an_extra_raw_bone(self):
        raw,skeleton=fixture()
        skeleton['referencePose'].append(dict(position=[0,0,0],rotation=[0,0,0,1],scale=[1,1,1]))
        preview=preview_raw(raw,skeleton,'Walk',{})
        self.assertEqual(len(preview['jointNames']),len(preview['offsetsCm']))


if __name__=='__main__':unittest.main()
