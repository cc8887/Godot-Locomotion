"""The running lane must close and validate all six native directions."""
import unittest
from pathlib import Path

from .graph import build_graph, load_rules, read_json, topological_order
from .scope import run_targets
from .target_gait import analyze_target_gait, original_protocol, _sole_geometry
import numpy as np

ROOT = Path(__file__).resolve().parents[2]


class RunPipelineTests(unittest.TestCase):
    def test_independent_right_run_requires_native_conditions_and_gates(self):
        import copy
        contracts=read_json(ROOT/'docs/architecture/kimodo-als-asset-contracts.json')
        rules=load_rules(ROOT/'tools/kimodo_pipeline/run_rules.json')
        spec=copy.deepcopy(rules['gaitClosure']['Run_Left_Forward'])
        rules['gaitClosure']['Run_Right_Forward']=spec
        spec['independentGeneration']=True
        graph=build_graph(contracts,rules)
        self.assertEqual('gait_cycle',graph['nodes']['motion:Run_Right_Forward']['op'])
        self.assertEqual('reference:Run_Right_Forward:native-als',graph['nodes']['candidate:Run_Right_Forward']['params']['gaitReference'])
        self.assertEqual('periodic_repair',graph['nodes']['repair:Run_Right_Forward:generated']['op'])
        mirrored=copy.deepcopy(rules)
        mirrored['gaitClosure']['Run_Right_Forward'].update(constraintReference='generated_mirror',constraintSource='Run_Left_Forward')
        reflected=build_graph(contracts,mirrored)['nodes']['reference:Run_Right_Forward:generated-mirror']
        self.assertEqual('mirrored_gait_reference',reflected['op'])
        self.assertIn('motion:Run_Left_Forward',reflected['deps'])
        mirrored['gaitClosure']['Run_Right_Forward']['constraintSource']='Run_Left_Backward'
        with self.assertRaisesRegex(ValueError,'corresponding accepted'):build_graph(contracts,mirrored)
        for key,value in (('constraintReference',None),('repairAfterGeneration',False),('independentGeneration','true')):
            broken=copy.deepcopy(rules);broken['gaitClosure']['Run_Right_Forward'][key]=value
            with self.assertRaises(ValueError):build_graph(contracts,broken)

    def test_separated_guidance_rejects_invalid_values_and_preserves_default(self):
        from .graph import validate_cfg_weights
        self.assertIsNone(validate_cfg_weights(None))
        for invalid in ([2], [2, 4, 6], [True, 4], [2, float('inf')], [2, float('nan')], [2, -1], '2,4'):
            with self.assertRaises(ValueError):
                validate_cfg_weights(invalid)
        contracts=read_json(ROOT/'docs/architecture/kimodo-als-asset-contracts.json')
        rules=load_rules(ROOT/'tools/kimodo_pipeline/run_rules.json')
        self.assertNotIn('cfgWeights',build_graph(contracts,rules)['nodes']['candidate:Run_Forward']['params'])
        rules['generationCfgWeights']=[2,3]
        rules['gaitClosure']['Run_Forward']['cfgWeights']=[2,4]
        graph=build_graph(contracts,rules)
        self.assertEqual([2.,4.],graph['nodes']['candidate:Run_Forward']['params']['cfgWeights'])
        self.assertEqual([2.,3.],graph['nodes']['candidate:Run_Backward']['params']['cfgWeights'])

    def test_run_scope_rejects_a_profile_without_cycle_gates(self):
        from unittest.mock import patch
        from tools.produce_als_resources import main
        with patch('sys.argv',['produce_als_resources.py','inspect','--scope','run',
                               '--rules',str(ROOT/'tools/kimodo_pipeline/non_prop_rules.json')]):
            with self.assertRaisesRegex(ValueError,'gait gates for every direction'):
                main()

    def test_forefoot_target_repair_preserves_roll_and_rejects_excessive_correction(self):
        import copy
        from .target_cleanup import repair_periodic_gait
        from .publish import world_pose
        protocol=original_protocol();row=protocol['assets']['run_backward'];skeleton=protocol['skeleton']
        raw=copy.deepcopy(row['raw'])
        pelvis=next(t for t in raw['tracks'] if t['bone'].lower()=='pelvis')
        for position in pelvis['positions']:position[2]+=3.
        unchanged=copy.deepcopy(raw)
        fixed,phase,receipt=repair_periodic_gait(raw,skeleton,row['source'])
        gate=analyze_target_gait(fixed,skeleton,row['source'])
        self.assertTrue(gate['passed'],[c for c in gate['checks'] if not c['passed']])
        self.assertEqual('toe',receipt['contactMode']);self.assertEqual('toe',phase['contactMode'])
        self.assertEqual(unchanged,raw)
        before,after=world_pose(raw,skeleton),world_pose(fixed,skeleton)
        for suffix in ('l','r'):
            for bone in ('foot','ball'):
                np.testing.assert_allclose(before[bone+'_'+suffix][0],after[bone+'_'+suffix][0],atol=1e-9)
        self.assertLessEqual(receipt['maximumAnkleCorrectionCm'],8.)
        for position in pelvis['positions']:position[2]+=20.
        with self.assertRaisesRegex(ValueError,'correction exceeds bound'):
            repair_periodic_gait(raw,skeleton,row['source'])

    def test_scope_excludes_reference_poses_jumps_and_props(self):
        contracts = read_json(ROOT/'docs/architecture/kimodo-als-asset-contracts.json')
        graph = build_graph(contracts, load_rules(ROOT/'tools/kimodo_pipeline/run_rules.json'))
        targets = run_targets(contracts)
        self.assertEqual(12, len(targets))
        self.assertEqual(6, len({graph['nodes'][key]['params']['contract']['semanticName'] for key in targets}))
        order = topological_order(graph['nodes'], targets)
        for key in targets:
            node = graph['nodes'][key]
            self.assertEqual('als-forward-v1', node['params']['contract']['targetGaitPolicy'])
            master = graph['nodes'][node['params']['source']]
            self.assertIn(master['op'], ('gait_cycle', 'mirror'))
        self.assertFalse(any(graph['nodes'][key]['op']=='external' for key in order))
        self.assertFalse(any(key.startswith('candidate:Run_Right') for key in order))

    def test_backward_gate_measures_backward_travel_and_rejects_wrong_path(self):
        protocol = original_protocol()
        row = protocol['assets']['run_backward']
        report = analyze_target_gait(row['raw'], protocol['skeleton'], row['source'])
        self.assertTrue(report['passed'], [c for c in report['checks'] if not c['passed']])
        self.assertEqual((0., -1.), report['travelDirectionXY'])
        _, correct, _ = _sole_geometry(row['raw'], protocol['skeleton'], 350., (0., -1.))
        _, wrong, _ = _sole_geometry(row['raw'], protocol['skeleton'], 350., (0., 1.))
        support = np.asarray(report['contacts'], bool).T
        self.assertLess(np.median(np.linalg.norm(correct[:, :, 1, :2], axis=-1)[support]),
                        np.median(np.linalg.norm(wrong[:, :, 1, :2], axis=-1)[support]))


if __name__ == '__main__':
    unittest.main()
