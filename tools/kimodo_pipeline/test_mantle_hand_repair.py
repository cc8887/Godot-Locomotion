import hashlib
from pathlib import Path
import unittest
import numpy as np
from .mantle import reference_input,target
from .mantle_hand_repair import immutable_target_skin,repair,FINGER_LIMIT_DEGREES,validate_report
from .graph import read_json
from .root_ownership import semantic_hash

ROOT=Path(__file__).resolve().parents[2]


class MantleHandRepairTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        native,cls.skeleton,_=reference_input('Mantle_Low_Right')
        cls.skin=immutable_target_skin(cls.skeleton)
        cls.source=native['source']

    def candidate(self):
        path=ROOT/'assets/generated/als_nonprop_20261009_mantle_v4/target-mantle'/hashlib.sha256(self.source.encode()).hexdigest()/'after.raw.json'
        return read_json(path)

    def test_real_generated_hand_skin_repaired_without_replacing_body_or_support_wrist(self):
        raw=self.candidate();before=semantic_hash(raw)
        result,report=repair(raw,self.skeleton,'Mantle_Low_Right',self.skin)
        self.assertEqual(semantic_hash(raw),before)
        self.assertTrue(report['passed'],report)
        validate_report(report)
        repeated,repeated_report=repair(raw,self.skeleton,'Mantle_Low_Right',self.skin)
        self.assertEqual(semantic_hash(result),semantic_hash(repeated))
        self.assertEqual(report,repeated_report)
        self.assertGreater(max(r['beforeDepthCm'] for r in report['records']),8.)
        self.assertLess(max(r['afterDepthCm'] for r in report['records']),.6)
        self.assertLessEqual(max(r['maximumFingerCorrectionDegrees'] for r in report['records']),FINGER_LIMIT_DEGREES+1e-3)
        for r in report['records']:
            if r['support']:self.assertLess(r['wristDisplacementCm'],1e-5)
        transitions=[r for r in report['records'] if r['transitionAnchorChecked'] and not r['support']]
        self.assertEqual({r['frame'] for r in transitions},{10,23})
        self.assertLess(max(r['transitionAnchorDepthCm'] for r in transitions),.6)
        allowed={'upperarm_l','lowerarm_l','hand_l'}|{f'{f}_{p:02}_l' for f in ('thumb','index','middle','ring','pinky') for p in (1,2,3)}
        for old,new in zip(raw['tracks'],result['tracks']):
            self.assertEqual(old['positions'],new['positions']);self.assertEqual(old['scales'],new['scales'])
            if old['bone'] not in allowed:self.assertEqual(old,new)
        for key in ('sampledKeyCount','playLength','frameRateNumerator','frameRateDenominator'):
            self.assertEqual(raw[key],result[key])

    def test_impossible_fixed_wrist_contact_remains_rejected(self):
        raw=self.candidate()
        # Translate the body deep into the top. A support wrist cannot be
        # teleported out or bones stretched to make this bad input pass.
        pelvis=next(t for t in raw['tracks'] if t['bone']=='pelvis')
        pelvis['positions']=(np.asarray(pelvis['positions'])+[0,0,-30]).tolist()
        _,report=repair(raw,self.skeleton,'Mantle_Low_Right',self.skin)
        self.assertFalse(report['passed'])
        self.assertTrue(any(r['support'] and r['afterDepthCm']>2 for r in report['records']))

    def test_high_approach_can_escape_front_plane_with_fixed_support_wrist(self):
        raw,skeleton,_=reference_input('Mantle_High')
        corrected,_,_,gate=target(raw,skeleton,'Mantle_High')
        self.assertTrue(gate['passed'])
        result,report=repair(corrected,skeleton,'Mantle_High',self.skin)
        self.assertTrue(report['passed'],report)
        validate_report(report)
        for old,new in zip(corrected['tracks'],result['tracks']):
            self.assertEqual(old['positions'],new['positions'])
            self.assertEqual(old['scales'],new['scales'])
        self.assertTrue(all(r['wristDisplacementCm']<1e-5 for r in report['records'] if r['support']))

    def test_scaled_rig_is_rejected_before_editing(self):
        raw=self.candidate();raw['tracks'][1]['scales']=[[1.01]*3]
        with self.assertRaisesRegex(ValueError,'stretching'):
            repair(raw,self.skeleton,'Mantle_Low_Right',self.skin)

    def test_changed_finger_lengths_are_rejected_before_solving(self):
        raw=self.candidate();finger=next(t for t in raw['tracks'] if t['bone']=='middle_02_l')
        finger['positions']=(np.asarray(finger['positions'])+[1,0,0]).tolist()
        with self.assertRaisesRegex(ValueError,'immutable arm/finger offsets'):
            repair(raw,self.skeleton,'Mantle_Low_Right',self.skin)

    def test_claimed_pass_cannot_enlarge_budgets_or_omit_transition_gate(self):
        report=dict(policy='mantle-target-skin-hand-clearance-v1',passed=True,maximumDepthCm=2.,
                    maximumArmCorrectionCm=10.,maximumFingerCorrectionDegrees=45.,
                    records=[dict(support=False,afterDepthCm=0.,transitionAnchorChecked=True,
                        transitionAnchorDepthCm=2.1,wristDisplacementCm=0.,maximumFingerCorrectionDegrees=0.)])
        with self.assertRaisesRegex(ValueError,'budget exceeded'):validate_report(report)
        report['records'][0]['transitionAnchorDepthCm']=0.
        del report['records'][0]['transitionAnchorChecked']
        with self.assertRaisesRegex(ValueError,'transition checks'):validate_report(report)
        report['maximumDepthCm']=3.
        with self.assertRaisesRegex(ValueError,'Missing or failed'):validate_report(report)


if __name__=='__main__':unittest.main()
