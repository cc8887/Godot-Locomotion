"""Static sampling and explicit source reuse must not certify failed gaits."""
import copy
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

import numpy as np

from .derived import POSE_POLICY, sample_pose, reuse_upstream, retain_base_deliveries, validate_pose_delivery
from .graph import build_graph, digest, load_rules, read_json, topological_order
from .runner import Runner, write_json
from .test_pipeline import rig_fixture, clip

ROOT=Path(__file__).resolve().parents[2]


class DerivedTests(unittest.TestCase):
    def test_target_pose_gate_rejects_changed_body_even_with_updated_file_hash(self):
        from .motion import freeze, save_motion
        from .runner import file_hash
        rig=rig_fixture();source=clip(rig,90)
        params=dict(policy=POSE_POLICY,semantic='Lean_Pose',parentSemantic='Run_Forward',phase=.25,
            periodSeconds=.8,samplingMode='conditioned-cycle',count=1,seconds=3,
            rootPath=dict(kind='linear',velocity=3.5,direction=[0,1]))
        sampled,receipt=sample_pose(source,rig,params)
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            save_motion(root/'source.npz',source,rig);save_motion(root/'pose.npz',sampled,rig)
            receipt['sourceMotion']=dict(path=str(root/'source.npz'),sha256=file_hash(root/'source.npz'))
            write_json(root/'sampling.json',receipt)
            delivered=freeze(sampled,0,25,rig);save_motion(root/'delivered.npz',delivered,rig)
            p=dict(file=str(root/'sampling.json'),sha256=file_hash(root/'sampling.json'),
                sourcePoseMotionFile=str(root/'pose.npz'),sourcePoseMotionSha256=file_hash(root/'pose.npz'),
                deliveredMotionSha256=file_hash(root/'delivered.npz'))
            job=dict(contract=dict(sampledPosePolicy=POSE_POLICY),poseSamplingReceipt=p,
                motionPath=str(root/'delivered.npz'),motionSha256=file_hash(root/'delivered.npz'))
            validate_pose_delivery(job)
            delivered['root_positions'][:,0]+=1
            save_motion(root/'delivered.npz',delivered,rig)
            job['motionSha256']=p['deliveredMotionSha256']=file_hash(root/'delivered.npz')
            with self.assertRaisesRegex(ValueError,'changed the sampled body'):validate_pose_delivery(job)

    def test_combined_pack_gets_separate_viewer_batch(self):
        from .full_pipeline import complete_run
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);pack=root/'pack';pack.mkdir()
            runner=SimpleNamespace(output=root,repo=root,python='python',graph=dict(pipeline='derived'),
                results={'asset':dict(op='deliver',identity='new',outputs={})})
            conversion=dict(viewer=dict(id='combined-target'),sourceClosureComplete=False,
                failedTargetGaits=[],failedTargetMantles=[],finalFbxReadback=dict(passed=True))
            with patch('tools.kimodo_pipeline.derived.retain_base_deliveries',side_effect=lambda p,a,r:(a,r)), \
                 patch('tools.kimodo_pipeline.offline_pipeline.convert_bundle',return_value=conversion) as convert, \
                 patch('tools.kimodo_pipeline.full_pipeline.verify_runtime',return_value=dict(status='runtime_passed')):
                result=complete_run(runner,dict(status='authoring_complete',viewerBatch=dict(id='new-source')),
                    base_pack=root/'base.json',pack=pack)
            self.assertIsNone(convert.call_args.kwargs['batch_id'])
            provenance=read_json(pack/'pipeline-provenance.json')
            self.assertIsNone(provenance['batchId'])
            self.assertEqual(provenance['targetViewerBatchId'],'combined-target')
            self.assertEqual(provenance['authoringBatchId'],'new-source')
            self.assertFalse(result['activated'])

    def test_base_pack_cannot_certify_zero_new_deliveries(self):
        from .full_pipeline import complete_run
        with tempfile.TemporaryDirectory() as directory:
            runner=SimpleNamespace(output=Path(directory),results={},graph=dict(pipeline='derived'))
            result=complete_run(runner,dict(status='authoring_rejected'),base_pack=Path(directory)/'missing-pack.json')
            self.assertEqual(result['failedPhase'],'source-quality')
            self.assertEqual(result['sourceDeliveries'],0)
            self.assertFalse(result['activated'])

    def test_retained_deliveries_merge_without_discarding_existing_sources(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            base=dict(node='existing',identity='base',outputs={})
            old=dict(node='replace',identity='old',outputs={})
            new=dict(node='replace',identity='new',outputs={})
            rig=dict(outputs={'rig.json':dict(sha256='same-rig')})
            write_json(root/'authoring.json',dict(assets=[base,old]))
            write_json(root/'receipts.json',{'rig:soma':rig,'existing':dict(identity='base',outputs={}),
                'replace':dict(identity='old',outputs={})})
            write_json(root/'retarget.json',dict(authoringBundle=str(root/'authoring.json')))
            write_json(root/'pack.json',dict(files=[],nativeRetargetBundle=str(root/'retarget.json')))
            assets,receipts=retain_base_deliveries(root/'pack.json',[new],{'rig:soma':rig,'replace':dict(identity='new')})
            self.assertEqual(assets,[base,new])
            self.assertEqual(receipts['existing']['identity'],'base')
            self.assertEqual(receipts['replace']['identity'],'new')

    def test_sample_removes_path_preserves_height_and_exact_rotations(self):
        rig=rig_fixture();source=clip(rig,90)
        source['root_positions'][:,2]=3.5*np.arange(90)/30+.03
        source['root_positions'][:,0]=.02*np.sin(np.arange(90))
        source['root_positions'][:,1]=1+.01*np.cos(np.arange(90))
        before=copy.deepcopy(source)
        p=dict(policy=POSE_POLICY,semantic='Lean_Pose',parentSemantic='Run_Forward',phase=.25,
            periodSeconds=.8,samplingMode='conditioned-cycle',count=1,seconds=3,
            rootPath=dict(kind='linear',velocity=3.5,direction=[0,1]))
        pose,receipt=sample_pose(source,rig,p)
        self.assertEqual(receipt['sourceFrame'],30)
        np.testing.assert_array_equal(pose['local_rot_mats'][0],source['local_rot_mats'][30])
        self.assertEqual(pose['root_positions'][0,1],source['root_positions'][30,1])
        self.assertAlmostEqual(pose['root_positions'][0,2],0)
        for key in source:np.testing.assert_array_equal(source[key],before[key])
        p['periodSeconds']=10
        with self.assertRaisesRegex(ValueError,'out of range'):sample_pose(source,rig,p)

    def test_default_samples_do_not_depend_on_failed_loop_but_gaits_keep_gates(self):
        graph=build_graph(read_json(ROOT/'docs/architecture/kimodo-als-asset-contracts.json'),
            load_rules(ROOT/'tools/kimodo_pipeline/directional_gait_rules.json'))
        order=topological_order(graph['nodes'],['asset:v4:ALS_N_Run_BasePose'])
        self.assertIn('candidate:Run_Forward',order)
        self.assertNotIn('motion:Run_Forward',order)
        self.assertNotIn('repair:Run_Forward:generated',order)
        self.assertEqual(graph['nodes']['motion:Run_Left_Forward']['op'],'gait_cycle')
        self.assertIsNone(graph['nodes']['asset:v4:ALS_N_Run_BasePose']['params']['targetPlanarOrigin'])
        contract=graph['nodes']['asset:v4:ALS_N_Run_BasePose']['params']['contract']
        self.assertEqual(contract['sampledPosePolicy'],POSE_POLICY)

    def test_reuse_changes_are_detected_and_unavailable_sources_never_generate(self):
        rig=rig_fixture()
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);write_json(root/'rig.json',rig)
            graph=dict(nodes={'rig:soma':dict(id='rig:soma',op='rig',deps=[],params={}),
                'candidate':dict(id='candidate',op='generate',deps=['rig:soma'],params=dict(semantic='Run',rootPath=None))},
                rules=dict(fps=30),rulesDigest='test')
            runner=Runner(graph,root/'first',bindings={'rig:soma':dict(path=str(root/'rig.json'))})
            runner.output.mkdir()
            runner.execute_node('rig:soma')
            # Use the runner's real content-addressed receipt structure.
            from .motion import save_motion
            from .runner import file_hash
            save_motion(root/'source.npz',clip(rig),rig)
            receipt=dict(node='candidate',op='generate',identity='source',boundInput=False,
                inputs=dict(node=graph['nodes']['candidate'],dependencyOutputs={'rig:soma':{'rig.json':runner.results['rig:soma']['outputs']['rig.json']['sha256']}}),
                outputs={'motion.npz':dict(path=str(root/'source.npz'),sha256=file_hash(root/'source.npz'))})
            receipt['identity']=digest(receipt['inputs'])
            write_json(root/'receipts.json',{'candidate':receipt})
            imported=reuse_upstream(graph,[root/'receipts.json'],derived_only=True)
            next_runner=Runner(imported,root/'next',bindings={'rig:soma':dict(path=str(root/'rig.json'))})
            next_runner.output.mkdir()
            next_runner.execute_node('rig:soma');next_runner.execute_node('candidate')
            self.assertEqual(next_runner.results['candidate']['op'],'reuse_motion')
            (root/'source.npz').write_bytes(b'changed')
            with self.assertRaisesRegex(ValueError,'Changed upstream'):reuse_upstream(graph,[root/'receipts.json'])
            missing=reuse_upstream(graph,[],derived_only=True)
            self.assertEqual(missing['nodes']['candidate']['op'],'external')


if __name__=='__main__':unittest.main()
