"""Viewer previews must preserve native FK and identify the exact authoring run."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

import numpy as np
from scipy.spatial.transform import Rotation

from .motion import complete, fk, save_motion
from .test_gait import walking_fixture
from .viewer_export import preview_motion, export_batch


class ViewerExportTests(unittest.TestCase):
    def test_mantle_source_gate_is_visible_and_hash_checked(self):
        source,rig=walking_fixture(seconds=2.)
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);run=root/'artifacts/run';run.mkdir(parents=True)
            rig_file=run/'rig.json';rig_file.write_text(json.dumps(rig),encoding='utf-8')
            motion_file=run/'motion.npz';save_motion(motion_file,source,rig)
            gate_file=run/'mantle-validation.json';gate_file.write_text(json.dumps(dict(passed=True)),encoding='utf-8')
            def output(path):return dict(path=str(path),sha256=hashlib.sha256(path.read_bytes()).hexdigest())
            graph={'nodes':{'motion:Mantle_High':{'params':{'semantic':'Mantle_High','source':'candidate:Mantle_High'}}}}
            receipts={'rig:soma':{'outputs':{'rig.json':output(rig_file)}},
                'motion:Mantle_High':dict(op='mantle',identity='one',outputs={'motion.npz':output(motion_file),'mantle-validation.json':output(gate_file)})}
            result=export_batch(graph,receipts,run_dir=run,root=root)
            entry=json.loads(Path(result['index']).read_text(encoding='utf-8'))['entries'][0]
            self.assertTrue(entry['gatePass'])
            self.assertEqual('源检查通过',entry['qualification'])
            gate_file.write_text(json.dumps(dict(passed=False)),encoding='utf-8')
            with self.assertRaisesRegex(ValueError,'gate receipt changed'):
                export_batch(graph,receipts,run_dir=run,root=root)

    def test_preview_preserves_first_frame_origin_timing_and_physical_fk(self):
        source,rig=walking_fixture(seconds=2.)
        source['root_positions'] += [2.,0.,-3.]
        payload=preview_motion(source,rig,'Walk',{'motionSha256':'test'})
        np.testing.assert_array_equal(payload['rootPositionsCm'],source['root_positions']*100)
        local=Rotation.from_quat(np.asarray(payload['localQuaternions']).reshape(-1,4)).as_matrix().reshape(source['local_rot_mats'].shape)
        _,expected=fk(source['local_rot_mats'],source['root_positions'],rig)
        _,actual=fk(local,np.asarray(payload['rootPositionsCm'])/100,rig)
        np.testing.assert_allclose(actual,expected,atol=1e-12)
        self.assertEqual(float(source['fps']),payload['fps'])
        self.assertEqual(len(expected),payload['frameCount'])
        self.assertFalse(payload['productionReady'])

    def test_registry_keeps_batches_and_rejects_changed_receipts(self):
        source,rig=walking_fixture(seconds=2.)
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);run=root/'artifacts/run';run.mkdir(parents=True)
            rig_file=run/'rig.json';rig_file.write_text(json.dumps(rig),encoding='utf-8')
            source_file=run/'motion.npz';save_motion(source_file,source,rig)
            def output(path):return {'path':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest()}
            receipts={'rig:soma':{'outputs':{'rig.json':output(rig_file)}},
                'candidate:Walk':{'identity':'one','op':'generate','outputs':{'motion.npz':output(source_file)}}}
            graph={'nodes':{'candidate:Walk':{'params':{'semantic':'Walk'}}}}
            first=export_batch(graph,receipts,run_dir=run,root=root,status='failed')
            registry=json.loads((root/'artifacts/kimodo_viewer/current.json').read_text(encoding='utf-8'))
            self.assertEqual(first['id'],registry['latestBatch'])
            self.assertEqual('failed',registry['batches'][0]['status'])
            receipts['candidate:Walk']['identity']='two'
            second=export_batch(graph,receipts,run_dir=run,root=root)
            registry=json.loads((root/'artifacts/kimodo_viewer/current.json').read_text(encoding='utf-8'))
            self.assertEqual(second['id'],registry['latestBatch'])
            self.assertEqual(2,len(registry['batches']))
            source_file.write_bytes(b'changed')
            with self.assertRaisesRegex(ValueError,'source receipt changed'):
                export_batch(graph,receipts,run_dir=run,root=root)

    def test_bound_inputs_and_constraint_teachers_are_not_new_generation(self):
        source,rig=walking_fixture(seconds=2.)
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);run=root/'artifacts/run';run.mkdir(parents=True)
            path=run/'rig.json';path.write_text(json.dumps(rig),encoding='utf-8')
            receipt={'outputs':{'rig.json':{'path':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest()}}}
            graph={'nodes':{'candidate:Walk':{'params':{}},'reference:Walk':{'params':{}}}}
            receipts={'rig:soma':receipt,'candidate:Walk':{'op':'generate','boundInput':True,'outputs':{'motion.npz':{}}},
                'reference:Walk':{'op':'native_gait_reference','outputs':{'motion.npz':{}}}}
            exported=export_batch(graph,receipts,run_dir=run,root=root)
            self.assertEqual(0,exported['entries'])

    def test_incremental_export_updates_one_batch_and_rejects_unsafe_identity(self):
        source,rig=walking_fixture(seconds=2.)
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);run=root/'artifacts/run';run.mkdir(parents=True)
            rig_file=run/'rig.json';rig_file.write_text(json.dumps(rig),encoding='utf-8')
            motion_file=run/'motion.npz';save_motion(motion_file,source,rig)
            def output(path):return dict(path=str(path),sha256=hashlib.sha256(path.read_bytes()).hexdigest())
            receipts={'rig:soma':{'outputs':{'rig.json':output(rig_file)}}}
            graph={'nodes':{'candidate:Walk':{'params':{'semantic':'Walk'}}}}
            export_batch(graph,receipts,run_dir=run,root=root,batch_id='fresh-run')
            receipts['candidate:Walk']=dict(op='generate',identity='one',outputs={'motion.npz':output(motion_file)})
            result=export_batch(graph,receipts,run_dir=run,root=root,batch_id='fresh-run')
            registry=json.loads((root/'artifacts/kimodo_viewer/current.json').read_text(encoding='utf-8'))
            self.assertEqual(1,len(registry['batches']))
            self.assertEqual('fresh-run',registry['latestBatch'])
            self.assertEqual(1,result['entries'])
            graph['nodes']['candidate:Walk:discovery']={'params':{'semantic':'Walk'}}
            receipts['candidate:Walk:discovery']=dict(op='generate',identity='discovery',outputs={'motion.npz':output(motion_file)})
            result=export_batch(graph,receipts,run_dir=run,root=root,batch_id='fresh-run')
            self.assertEqual(1,result['entries'])
            index=json.loads(Path(result['index']).read_text(encoding='utf-8'))
            self.assertEqual('candidate:Walk',index['entries'][0]['node'])
            with self.assertRaisesRegex(ValueError,'Invalid Viewer batch identity'):
                export_batch(graph,receipts,run_dir=run,root=root,batch_id='../outside')


if __name__=='__main__':unittest.main()
