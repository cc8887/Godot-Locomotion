"""Reject unsupported distance/warping claims and preserve source distances."""
import copy
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from .mantle_curve_contract import CONTROL_FILE, analysis, contract, validate
from .publish import validate_mantle_curve_delivery
from .runner import file_hash, write_json
from .graph import read_json


def fixture():
    source='/Test.A_Als_Mantle_High'
    animation=dict(sequences=[dict(raw=dict(source=source, sampledKeyCount=5,
        frameRateNumerator=1, frameRateDenominator=1, playLength=4))])
    roots=dict(sequences=[dict(source=source, tracks=[dict(bone='Root',
        positions=[[0,10*i,20*i] for i in range(5)])])])
    keys=[dict(timeSeconds=float(i), rootAdvanceCm=10.*i, advanceCm=advance, liftCm=20.*i,
        remainingLiftCm=20.*i-80, leftHandSupport=1. if i in (1,2) else 0.,
        rightHandSupport=0.) for i,advance in enumerate([0.,5.,5.,20.,40.])]
    sequence=dict(source=source, semanticName='Mantle_High', keys=keys,
        bodyAdvanceControl=dict(policy='target-skin-front-clearance-v1'), curveAnalysis=analysis(keys))
    return dict(curveContract=contract(), sequences=[sequence]),animation,roots


class MantleCurveContractTests(unittest.TestCase):
    def setUp(self):
        self.value,self.animation,self.roots=fixture()

    def check(self):
        return validate(self.value,self.animation,self.roots)

    def test_forward_plateau_preserves_lifting_time_and_authored_distance(self):
        result=self.check(); row=self.value['sequences'][0]
        self.assertEqual([k['rootAdvanceCm'] for k in row['keys']],[0,10,20,30,40])
        plateau=row['curveAnalysis']['forwardPlateaus']
        self.assertEqual(len(plateau),1)
        self.assertEqual((plateau[0]['startTimeSeconds'],plateau[0]['endTimeSeconds']),(1,2))
        self.assertEqual(plateau[0]['liftChangeCm'],20)
        self.assertFalse(row['curveAnalysis']['forwardStrictlyIncreasing'])
        self.assertFalse(row['curveAnalysis']['wholeClipDistanceTimeInversionSupported'])
        self.assertEqual(result['sequencesWithForwardPlateaus'],1)
        self.assertTrue(result['distanceMatching']['entryHeightMatch'])
        self.assertFalse(result['distanceMatching']['continuousPlayback'])
        self.assertFalse(result['motionWarping']['rootMotionWarping'])

    def test_strict_forward_curve_does_not_certify_unimplemented_playback(self):
        row=self.value['sequences'][0]
        row['source']='/Test.A_Als_Mantle_Low_Left';row['semanticName']='Mantle_Low_Left'
        self.animation['sequences'][0]['raw']['source']=row['source']
        self.roots['sequences'][0]['source']=row['source']
        del row['bodyAdvanceControl']
        for key in row['keys']:key['advanceCm']=key['rootAdvanceCm']
        row['curveAnalysis']=analysis(row['keys'])
        self.assertTrue(row['curveAnalysis']['forwardStrictlyIncreasing'])
        self.assertFalse(row['curveAnalysis']['wholeClipDistanceTimeInversionSupported'])
        self.assertTrue(self.check()['passed'])

    def test_false_distance_matching_or_warping_claims_are_rejected(self):
        for group,field in [('distanceMatching','continuousPlayback'),
                ('distanceMatching','advanceCurveTimeInversion'),('motionWarping','rootMotionWarping'),
                ('motionWarping','warpWindowsExported')]:
            with self.subTest(field=field):
                changed=copy.deepcopy(self.value);changed['curveContract'][group][field]=True
                with self.assertRaisesRegex(ValueError,'capability claim'):
                    validate(changed,self.animation,self.roots)

    def test_clearance_curve_cannot_be_declared_as_distance_time_lookup(self):
        self.value['curveContract']['curveRoles']['advanceCm']='distance-to-animation-time'
        with self.assertRaisesRegex(ValueError,'semantics'):self.check()

    def test_replacing_authored_distance_with_delayed_curve_fails(self):
        for key in self.value['sequences'][0]['keys']:key['rootAdvanceCm']=key['advanceCm']
        with self.assertRaisesRegex(ValueError,'Authored Root distance'):self.check()

    def test_missing_original_distance_is_not_reconstructed_from_clearance(self):
        del self.value['sequences'][0]['keys'][1]['rootAdvanceCm']
        with self.assertRaisesRegex(ValueError,'Missing separate authored distance'):self.check()

    def test_stale_plateau_analysis_is_rejected(self):
        self.value['sequences'][0]['curveAnalysis']['forwardPlateaus']=[]
        with self.assertRaisesRegex(ValueError,'plateau analysis'):self.check()

    def test_clearance_cannot_modify_lift_or_landing(self):
        for field,index in [('liftCm',2),('advanceCm',4)]:
            with self.subTest(field=field):
                changed=copy.deepcopy(self.value);changed['sequences'][0]['keys'][index][field]-=1
                with self.assertRaisesRegex(ValueError,'authored lift|preserve landing'):
                    validate(changed,self.animation,self.roots)

    def test_high_requires_skin_control_and_cannot_be_relabeled(self):
        row=self.value['sequences'][0];del row['bodyAdvanceControl']
        with self.assertRaisesRegex(ValueError,'target-skin clearance'):self.check()
        row['semanticName']='Mantle_Low_Left'
        with self.assertRaisesRegex(ValueError,'semantic identity'):self.check()

    def test_resampling_and_contact_corruption_are_rejected(self):
        for field,bad in [('timeSeconds',1.25),('leftHandSupport',1.01),('advanceCm',float('nan'))]:
            with self.subTest(field=field):
                changed=copy.deepcopy(self.value);changed['sequences'][0]['keys'][1][field]=bad
                with self.assertRaisesRegex(ValueError,'original key times|contact weight|Nonfinite'):
                    validate(changed,self.animation,self.roots)

    def test_pack_gate_binds_manifest_inputs_and_semantics(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder).resolve();pack=root/'pack';config=pack/'config';config.mkdir(parents=True)
            pose=config/'refactored_mantle_animation_inputs.json';roots=config/'refactored_mantle_root_tracks.json'
            controls=pack/CONTROL_FILE
            write_json(pose,self.animation);write_json(roots,self.roots)
            value=copy.deepcopy(self.value)
            value.update(animationInputsSha256=file_hash(pose),rootInputsSha256=file_hash(roots))
            write_json(controls,value)
            manifest=dict(mantleCurveContract=dict(controlFile=CONTROL_FILE,**contract()),
                files=[dict(path='res://'+p.relative_to(root).as_posix(),sha256=file_hash(p)) for p in (controls,pose,roots)])
            with patch('tools.kimodo_pipeline.publish.ROOT',root):
                self.assertTrue(validate_mantle_curve_delivery(pack/'resource-pack.json',manifest)['passed'])
                broken=copy.deepcopy(manifest);del broken['mantleCurveContract']
                with self.assertRaisesRegex(ValueError,'stage a fresh pack'):
                    validate_mantle_curve_delivery(pack/'resource-pack.json',broken)
                value['curveContract']['motionWarping']['rootMotionWarping']=True
                write_json(controls,value);manifest['files'][0]['sha256']=file_hash(controls)
                with self.assertRaisesRegex(ValueError,'capability claim'):
                    validate_mantle_curve_delivery(pack/'resource-pack.json',manifest)
                value=copy.deepcopy(self.value)
                value.update(animationInputsSha256='stale',rootInputsSha256=file_hash(roots))
                write_json(controls,value);manifest['files'][0]['sha256']=file_hash(controls)
                with self.assertRaisesRegex(ValueError,'different final inputs'):
                    validate_mantle_curve_delivery(pack/'resource-pack.json',manifest)

    def test_runtime_preflight_preserves_rejection_before_starting_godot(self):
        from .full_pipeline import verify_runtime
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);pack=root/'resource-pack.json';output=root/'runtime'
            write_json(pack,dict(targetMantleChecks=[dict(source='/Test.A_Als_Mantle_High')]))
            with patch('tools.kimodo_pipeline.full_pipeline.subprocess.run') as process:
                with self.assertRaisesRegex(ValueError,'preflight failed'):
                    verify_runtime(pack,output,'godot','dotnet','python')
            process.assert_not_called()
            failure=read_json(output/'runtime-result.json')
            self.assertEqual(failure['status'],'runtime_failed')
            self.assertEqual(failure['checks'][0]['name'],'mantle-curve-contract')
            self.assertFalse(failure['checks'][0]['passed'])


if __name__=='__main__':unittest.main()
