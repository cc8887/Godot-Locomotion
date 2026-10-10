"""Mathematical and DAG/cache regression tests; these are not visual acceptance."""
from __future__ import annotations

import copy
import json
import tempfile
import unittest
from pathlib import Path

import numpy as np
from scipy.spatial.transform import Rotation

from .graph import build_graph, digest, read_json, topological_order
from .motion import (complete, fk, freeze, loop_candidate, remove_planar_path, materialize_constraints, mirror,
                     offset, parameter, save_motion, validate_motion,load_motion)
from .runner import Runner, write_json

ROOT=Path(__file__).resolve().parents[2]


class CroppedCycleOriginTests(unittest.TestCase):
    def test_accelerating_start_removes_nonlinear_path_but_retains_bob(self):
        rig=rig_fixture();t=np.arange(90)/30;local=np.broadcast_to(np.eye(3),(90,len(rig['jointNames']),3,3)).copy()
        bob=.01*np.sin(t*7);root=np.stack([bob,np.ones(90),6*t*t/6+bob],axis=1)
        source=complete(local,root,rig,30)
        result=remove_planar_path(source,{'kind':'accelerate','velocity':6,'direction':[0,1]},3,rig)
        np.testing.assert_allclose(result['root_positions'][:,0],bob,atol=1e-12)
        np.testing.assert_allclose(result['root_positions'][:,2],bob,atol=1e-12)
        np.testing.assert_array_equal(source['root_positions'],root)

    def test_traveling_crop_and_derived_pose_share_character_origin(self):
        rig=rig_fixture();count=90
        local=np.broadcast_to(np.eye(3),(count,len(rig['jointNames']),3,3)).copy()
        root=np.stack([np.zeros(count),np.ones(count),np.arange(count)*6/30],axis=1)
        source=complete(local,root,rig,30)
        cycle=loop_candidate(source,.8,.35,rig,remove_translation=True)
        np.testing.assert_allclose(cycle['root_positions'][:,[0,2]],0,atol=1e-12)
        pose=freeze(cycle,6,1,rig)
        np.testing.assert_allclose(pose['root_positions'][0],[0,1,0],atol=1e-12)
        np.testing.assert_array_equal(source['root_positions'],root)


def rig_fixture():
    return {'jointNames':['Hips','Spine1','Spine2','Chest','LeftArm','RightArm','LeftLeg','RightLeg'],
            'parents':[-1,0,1,2,3,3,0,0],
            'offsets':[[0,0,0],[0,.2,0],[0,.2,0],[0,.2,0],[-.3,0,0],[.3,0,0],[-.1,-.5,0],[.1,-.5,0]]}


def clip(rig,count=8):
    local=np.broadcast_to(np.eye(3),(count,len(rig['jointNames']),3,3)).copy()
    local[:,4]=Rotation.from_euler('z',np.linspace(0,35,count)[:,None],degrees=True).as_matrix()
    root=np.stack([np.linspace(.2,.6,count),np.ones(count),np.linspace(0,.8,count)],axis=1)
    return complete(local,root,rig)


class MotionTests(unittest.TestCase):
    def setUp(self):
        self.rig=rig_fixture();self.motion=clip(self.rig)
        self.base=freeze(self.motion,0,1,self.rig)

    def test_reflection_matches_fk_on_symmetric_reference(self):
        reflected=mirror(self.motion,self.base,self.rig)
        _,positions=fk(self.motion['local_rot_mats'],self.motion['root_positions'],self.rig)
        _,got=fk(reflected['local_rot_mats'],reflected['root_positions'],self.rig)
        permutation=[0,1,2,3,5,4,7,6];origin=self.base['root_positions'][0]
        expected=(positions[:,permutation]-origin)*[-1,1,1]+origin
        np.testing.assert_allclose(got,expected,atol=1e-12)

    def test_mirror_twice_recovers_asymmetric_base_and_motion(self):
        base=offset(self.base,{'LeftArm':27,'RightArm':-11},'x',self.rig)
        got=mirror(mirror(self.motion,base,self.rig),base,self.rig)
        np.testing.assert_allclose(got['local_rot_mats'],self.motion['local_rot_mats'],atol=1e-12)
        np.testing.assert_allclose(got['root_positions'],self.motion['root_positions'],atol=1e-12)

    def test_base_is_zero_residual_under_mirroring(self):
        base=offset(self.base,{'LeftArm':27,'RightArm':-11},'x',self.rig)
        np.testing.assert_allclose(mirror(base,base,self.rig)['local_rot_mats'],base['local_rot_mats'],atol=1e-12)

    def test_leg_mask_keeps_handed_upper_body(self):
        source=offset(self.motion,{'LeftLeg':15},'x',self.rig)
        reflected=mirror(source,self.base,self.rig,'legs')
        before,_=fk(source['local_rot_mats'],source['root_positions'],self.rig)
        after,_=fk(reflected['local_rot_mats'],reflected['root_positions'],self.rig)
        np.testing.assert_allclose(after[:,:6],before[:,:6],atol=1e-12)
        self.assertGreater(np.max(np.abs(after[:,7]-before[:,7])),.1)

    def test_parameter_neutral_exact_and_signed_targets(self):
        table=parameter(self.base,31,{'Chest':1},30,'yaw',self.rig)
        np.testing.assert_array_equal(table['local_rot_mats'][15],self.base['local_rot_mats'][0])
        left=freeze(table,0,1,self.rig);right=freeze(table,30,1,self.rig)
        np.testing.assert_allclose(mirror(left,self.base,self.rig)['local_rot_mats'],right['local_rot_mats'],atol=1e-12)

    def test_pitch_endpoints_point_up_then_down(self):
        table=parameter(self.base,31,{'Chest':1},30,'pitch',self.rig)
        g,_=fk(table['local_rot_mats'],table['root_positions'],self.rig)
        self.assertGreater(g[0,3,1,2],0)
        self.assertLess(g[-1,3,1,2],0)

    def test_fullbody_reference_has_same_root_path_at_endpoint(self):
        params={'seconds':3,'fps':30,'rootPath':{'kind':'linear','velocity':1.5,'direction':[0,1]},
                'anchors':[{'source':'base','sourceFrame':0,'at':0},{'source':'base','sourceFrame':0,'at':1}]}
        conditions=materialize_constraints(params,lambda key:self.base,self.rig)
        path,body=conditions
        self.assertEqual(body['frame_indices'],[0,89])
        np.testing.assert_allclose(np.asarray(body['root_positions'])[:,[0,2]],np.asarray(path['smooth_root_2d'])[[0,-1]])
        np.testing.assert_array_equal(path['smooth_root_2d'][0],[0,0])

    def test_turn_endpoint_orientation_matches_heading(self):
        params={'seconds':2,'fps':30,'rootPath':{'kind':'turn','headingDegrees':90},
                'anchors':[{'source':'base','sourceFrame':0,'at':1}]}
        conditions=materialize_constraints(params,lambda key:self.base,self.rig)
        np.testing.assert_allclose(conditions[1]['local_joints_rot'][0][0],[0,np.pi/2,0],atol=1e-12)

    def test_rejects_bad_rotation_and_invalid_frame(self):
        bad=copy.deepcopy(self.motion);bad['local_rot_mats'][0,0,0,0]=2
        with self.assertRaises(ValueError):validate_motion(bad,self.rig)
        with self.assertRaises(ValueError):freeze(self.motion,100,1,self.rig)

    def test_rejects_unpaired_hierarchy(self):
        rig=copy.deepcopy(self.rig);rig['jointNames'][5]='OtherArm'
        with self.assertRaises(ValueError):mirror(self.motion,self.base,rig)

    def test_loaded_float32_drift_is_projected_before_repeated_mirror(self):
        with tempfile.TemporaryDirectory() as temp:
            path=Path(temp)/'drift.npz'
            local=self.motion['local_rot_mats'].astype(np.float32)*(1+np.float32(1e-7))
            np.savez(path,local_rot_mats=local,root_positions=self.motion['root_positions'])
            loaded=load_motion(path,self.rig)
            basis=freeze(loaded,0,1,self.rig);twice=mirror(mirror(loaded,basis,self.rig),basis,self.rig)
            np.testing.assert_allclose(twice['local_rot_mats'],loaded['local_rot_mats'],atol=1e-12)
            np.savez(path,local_rot_mats=local*2,root_positions=self.motion['root_positions'])
            with self.assertRaises(ValueError):load_motion(path,self.rig)


class GraphTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.contracts=read_json(ROOT/'docs/architecture/kimodo-als-asset-contracts.json')
        cls.rules=read_json(ROOT/'tools/kimodo_pipeline/production_rules.json')
        cls.graph=build_graph(cls.contracts,cls.rules)

    def test_complete_coverage_and_acyclic_order(self):
        graph=self.graph;nodes=graph['nodes'];order=topological_order(nodes)
        self.assertEqual(253,sum(n['op'] in ('deliver','deliver_prop') for n in nodes.values()))
        self.assertEqual(len(nodes),len(order));positions={k:i for i,k in enumerate(order)}
        self.assertTrue(all(positions[d]<positions[k] for k,n in nodes.items() for d in n['deps']))

    def test_right_variants_no_inference_and_inverse_stance(self):
        nodes=self.graph['nodes']
        for name in ('Walk_Right_Forward','Run_Right_Forward','Jump_Run_Right','Turn_90_Right'):
            self.assertEqual('mirror',nodes['motion:'+name]['op'])
            self.assertNotIn('candidate:'+name,nodes)
        self.assertEqual('reverse',nodes['motion:CrouchToStand']['op'])

    def test_native_left_path_heading_and_lean_signs(self):
        nodes=self.graph['nodes']
        self.assertGreater(nodes['candidate:Walk_Left_Forward']['params']['rootPath']['direction'][0],0)
        self.assertEqual(90,nodes['candidate:Turn_90_Left']['params']['rootPath']['headingDegrees'])
        self.assertLess(nodes['motion:Lean_Left']['params']['angles']['Chest'],0)

    def test_constraint_sources_are_real_dependencies(self):
        for node in self.graph['nodes'].values():
            if node['op']!='generate':continue
            sources=[a['source'] for a in node['params']['anchors']]+[a['source'] for a in node['params'].get('endEffectors',[])]
            self.assertTrue(set(sources)<=set(node['deps']))

    def test_arms_and_no_curves_share_master(self):
        nodes=self.graph['nodes']
        self.assertEqual('motion:Rifle_Run',nodes['motion:Rifle_Run_Arms']['params']['source'])
        self.assertEqual('motion:Rifle_Poses',nodes['motion:Rifle_Poses_NoCurves']['params']['source'])
        self.assertEqual('copy',nodes['motion:Land_Heavy_Additive']['op'])

    def test_pose_frame_dependency_does_not_require_other_slots(self):
        order=topological_order(self.graph['nodes'],['reference:Rifle_Poses_NoCurves:9'])
        self.assertIn('pose:Rifle_Poses:9',order)
        self.assertNotIn('pose:Rifle_Poses:0',order)

    def test_missing_pose_layout_is_explicit(self):
        node=self.graph['nodes']['pose:Rifle_Poses:9']
        self.assertEqual('external',node['op']);self.assertIn('Rifle_Poses[9]',node['params']['reason'])

    def test_rejects_cycle_missing_dep_invalid_frame(self):
        with self.assertRaises(ValueError):topological_order({'a':{'deps':['b']},'b':{'deps':['a']}})
        with self.assertRaises(ValueError):topological_order({'a':{'deps':['missing']}})
        bad=copy.deepcopy(self.contracts)
        row=next(r for r in bad['assets'] if r['originalEvaluation']['baseAsset'])
        row['originalEvaluation']['baseFrame']=10000
        with self.assertRaises(ValueError):build_graph(bad,self.rules)

    def test_build_is_deterministic(self):
        self.assertEqual(digest(self.graph),digest(build_graph(self.contracts,self.rules)))


class CacheTests(unittest.TestCase):
    def test_prop_uses_own_rig_and_motion_and_invalidates_referenced_file(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);write_json(root/'bow-rig.json',{'jointNames':['String']})
            (root/'bow-motion.bvh').write_text('authored prop candidate',encoding='utf-8')
            write_json(root/'input.json',{'rigPath':'bow-rig.json','motionPath':'bow-motion.bvh'})
            nodes={'prop':{'id':'prop','op':'external','deps':[],'params':{'payloadType':'prop','reason':'author bow'}},
                   'asset':{'id':'asset','op':'deliver_prop','deps':['prop'],
                            'params':{'source':'prop','contract':{'source':'/bow','asset':'Bow_Draw','edition':'v4'},'requires':['prop_rig_validation']}}}
            graph={'nodes':nodes,'rules':{'fps':30},'rulesDigest':'test'}
            bindings={'prop':{'path':str(root/'input.json')}}
            def run():return Runner(graph,root/'cache',bindings=bindings).run(['asset'])
            first=run();self.assertEqual(2,first['executed']);self.assertEqual(2,run()['cached'])
            self.assertIn('prop.motion.bvh',first['targets']['asset']['outputs'])
            (root/'bow-motion.bvh').write_text('updated authored prop candidate',encoding='utf-8')
            self.assertEqual(2,run()['executed'])

    def test_resume_corruption_and_transitive_input_invalidation(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);rig=rig_fixture();write_json(root/'rig.json',rig)
            source=clip(rig);base=freeze(source,0,1,rig)
            save_motion(root/'source.npz',source,rig);save_motion(root/'base.npz',base,rig)
            nodes={'rig:soma':{'id':'rig:soma','op':'rig','deps':[],'params':{}},
                   'base':{'id':'base','op':'external','deps':['rig:soma'],'params':{'reason':'base'}},
                   'source':{'id':'source','op':'external','deps':['rig:soma'],'params':{'reason':'source'}},
                   'mirror':{'id':'mirror','op':'mirror','deps':['rig:soma','source','base'],
                             'params':{'source':'source','base':'base','mask':'all'}}}
            graph={'nodes':nodes,'rules':{'fps':30},'rulesDigest':'test'}
            bindings={k:{'path':str(root/p)} for k,p in [('rig:soma','rig.json'),('base','base.npz'),('source','source.npz')]}
            def run():return Runner(graph,root/'cache',bindings=bindings).run(['mirror'])
            first=run();second=run();self.assertEqual(4,first['executed']);self.assertEqual(4,second['cached'])
            corrupt=Path(second['targets']['mirror']['outputs']['motion.npz']['path']);corrupt.write_bytes(b'broken')
            self.assertEqual(1,run()['executed']);self.assertTrue(list(corrupt.parent.parent.glob('*.damaged-*')))
            changed=offset(source,{'Chest':11},'y',rig);save_motion(root/'source.npz',changed,rig)
            self.assertEqual(2,run()['executed'])
            new_base=offset(base,{'RightArm':9},'x',rig);save_motion(root/'base.npz',new_base,rig)
            self.assertEqual(2,run()['executed'])

    def test_unbound_input_fails_and_preserves_evidence(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);rig=rig_fixture();write_json(root/'rig.json',rig)
            graph={'nodes':{'rig:soma':{'id':'rig:soma','op':'rig','deps':[],'params':{}},
                            'missing':{'id':'missing','op':'external','deps':['rig:soma'],'params':{'reason':'missing pose'}}},
                   'rules':{'fps':30},'rulesDigest':'test'}
            with self.assertRaisesRegex(ValueError,'Unbound authoring input'):
                Runner(graph,root/'cache',bindings={'rig:soma':{'path':str(root/'rig.json')}}).run(['missing'])
            self.assertTrue(list((root/'cache').glob('.building-*/failure.json')))
            self.assertFalse((root/'cache/running.lock').exists())


if __name__=='__main__':unittest.main()
