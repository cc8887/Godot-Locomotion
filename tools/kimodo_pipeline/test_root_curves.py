import copy
import tempfile
import unittest
from pathlib import Path
from .root_curves import bake,export
from .runner import file_hash

class RootCurveTests(unittest.TestCase):
    def inputs(self):
        raw=dict(source='/test',skeletonSource='/rig',sampledKeyCount=3,playLength=1,frameRateNumerator=2,frameRateDenominator=1,
                 tracks=[dict(bone='root',positions=[[0,0,0],[3,4,0],[0,0,12]],rotations=[[0,0,0,1]]*3)])
        job=dict(source='/test',edition='v4',bvhSha256='a'*64,rawSha256='b'*64,
                 contract=dict(asset='Walk',originalTiming=dict(interpolation='Linear'),originalEvaluation=dict(enableRootMotion=True)))
        return raw,job
    def test_distance_is_travel_not_net_displacement_and_ends_at_zero(self):
        raw,job=self.inputs();curve=bake(raw,job);keys=curve['keys']
        self.assertEqual([0,5,10],[k['cumulativePlanarDistanceCm'] for k in keys])
        self.assertEqual([10,5,0],[k['remainingPlanarDistanceCm'] for k in keys])
        self.assertEqual([-10,-5,0],[k['signedDistanceToEndCm'] for k in keys])
        self.assertEqual(18,keys[-1]['cumulativeSpatialDistanceCm']);self.assertEqual([0,.5,1],[k['timeSeconds'] for k in keys])
    def test_rotation_only_and_declared_static_clips_get_sidecars(self):
        raw,job=self.inputs();raw['tracks'][0]['positions']=[[0,0,0]]
        raw['tracks'][0]['rotations']=[[0,0,0,1],[0,0,1,0],[0,0,0,-1]]
        job['contract']['originalEvaluation']['enableRootMotion']=False
        curve=bake(raw,job);self.assertTrue(curve['containsRotation']);self.assertEqual(360,curve['keys'][-1]['cumulativeRotationDegrees'])
        raw['tracks'][0]['rotations']=[[0,0,0,1]];self.assertIsNone(bake(raw,job))
        job['contract']['originalEvaluation']['enableRootMotion']=True;self.assertIsNotNone(bake(raw,job))
    def test_export_is_repeatable_does_not_mutate_keys_and_rejects_stale_sidecar(self):
        raw,job=self.inputs();before=copy.deepcopy(raw)
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);first=export(root,raw,job);second=export(root,raw,job)
            self.assertEqual(first,second);self.assertEqual(first['sha256'],file_hash(root/first['file']));self.assertEqual(before,raw)
            raw['tracks'][0]['positions'][-1][0]=1
            with self.assertRaisesRegex(ValueError,'different inputs'):export(root,raw,job)

if __name__=='__main__':unittest.main()
