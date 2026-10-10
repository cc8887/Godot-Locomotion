import copy
import tempfile
import unittest
from pathlib import Path
import numpy as np
from .mantle_contacts import ROOT, bake, export
from .graph import read_json
from .runner import file_hash, write_json


class MantleContactBakeTests(unittest.TestCase):
    def test_fixed_anchors_independent_release_and_original_key_times(self):
        config=ROOT/'assets/config'; value=bake(config)
        high=next(s for s in value['sequences'] if s['semanticName']=='Mantle_High')
        self.assertEqual(high['keys'][45]['leftHandSupport'],1)
        self.assertEqual(high['keys'][45]['rightHandSupport'],0)
        control=high['bodyAdvanceControl']
        self.assertEqual(control['policy'],'target-skin-front-clearance-v1')
        self.assertGreater(control['maximumAdvanceDelayCm'],0)
        self.assertLessEqual(high['bodyBlendReadyTimeSeconds'],17/30)
        advance=np.array([k['advanceCm'] for k in high['keys']])
        self.assertTrue(np.all(np.diff(advance)>=-1e-7))
        self.assertLessEqual(np.max(np.diff(advance)),200/30+1e-5)
        self.assertAlmostEqual(advance[-1],65,places=5)
        original=np.array([k['rootAdvanceCm'] for k in high['keys']])
        self.assertGreater(np.max(original-advance),0)
        self.assertAlmostEqual(original[-1],advance[-1],places=5)
        self.assertTrue(high['curveAnalysis']['forwardPlateaus'])
        self.assertFalse(high['curveAnalysis']['wholeClipDistanceTimeInversionSupported'])
        self.assertEqual(value['curveContract']['playbackDriver'],'montage-time')
        self.assertFalse(value['curveContract']['distanceMatching']['continuousPlayback'])
        self.assertFalse(value['curveContract']['motionWarping']['rootMotionWarping'])
        from .mantle import reference_input
        from .publish import world_pose
        raw,skeleton,_=reference_input('Mantle_High')
        wrist=world_pose(raw,skeleton)['hand_l'][1][18:59].mean(axis=0)
        np.testing.assert_allclose(high['anchors']['left']['positionCm'],wrist-[0,50,199],atol=1e-6)
        for sequence in value['sequences']:
            self.assertEqual(set(sequence['anchors']),{'left','right'})
            times=[k['timeSeconds'] for k in sequence['keys']]
            np.testing.assert_allclose(times,np.arange(len(times))/30,atol=1e-6)
            for side,anchor in sequence['anchors'].items():
                self.assertEqual(np.asarray(anchor['positionCm']).shape,(3,))
                self.assertAlmostEqual(np.linalg.norm(anchor['quaternionXYZW']),1)
            self.assertEqual(sequence['keys'][0]['remainingLiftCm'],-sequence['keys'][-1]['liftCm'])
        left=next(s for s in value['sequences'] if s['semanticName']=='Mantle_Low_Left')
        self.assertEqual(left['keys'][15]['leftHandSupport'],0)
        self.assertEqual(left['keys'][15]['rightHandSupport'],1)

    def test_deterministic_bake_refuses_overwrite_and_keeps_source_bytes(self):
        config=ROOT/'assets/config'; source=config/'refactored_mantle_animation_inputs.json'; before=file_hash(source)
        with tempfile.TemporaryDirectory() as folder:
            output=Path(folder)/'contact.json'; first=export(config,output); second=export(config,output)
            self.assertEqual(first,second)
            broken=read_json(output);broken['sequences'][0]['anchors']['left']['positionCm'][0]+=1
            write_json(output,broken)
            with self.assertRaisesRegex(ValueError,'Changed mantle contact bake'):export(config,output)
        self.assertEqual(before,file_hash(source))

    def test_reversing_root_is_rejected_before_runtime(self):
        with tempfile.TemporaryDirectory() as folder:
            config=Path(folder)
            for name in ['refactored_mantle_animation_inputs','refactored_mantle_root_tracks']:
                value=copy.deepcopy(read_json(ROOT/'assets/config'/f'{name}.json'))
                if name.endswith('root_tracks'):
                    root=next(t for t in value['sequences'][0]['tracks'] if t['bone'].lower()=='root')
                    root['positions'][40][2]=-100
                write_json(config/f'{name}.json',value)
            with self.assertRaisesRegex(ValueError,'Invalid mantle progress'):bake(config)


if __name__=='__main__':unittest.main()
