"""Content-addressed DAG runner with dependency hashes and fail-closed authoring inputs."""
from __future__ import annotations

import json
import os
import subprocess
import sys
import uuid
from importlib.metadata import version
from pathlib import Path

from .graph import digest, read_json, topological_order


def file_hash(path):
    import hashlib
    h=hashlib.sha256()
    with Path(path).open('rb') as f:
        for block in iter(lambda:f.read(1024*1024),b''): h.update(block)
    return h.hexdigest()


def write_json(path, value):
    Path(path).write_text(json.dumps(value,ensure_ascii=False,indent=2,allow_nan=False)+'\n',encoding='utf-8')


def resolve_bindings(path):
    if path is None:return {}
    path=Path(path).resolve();result=read_json(path)
    for key, entry in result.items():
        if isinstance(entry,str):entry={'path':entry};result[key]=entry
        entry['path']=str((path.parent/entry['path']).resolve())
    return result


class Runner:
    def __init__(self, graph, output, kimodo_repo=None, python=None, bindings=None):
        self.graph=graph;self.output=Path(output).resolve();self.repo=Path(kimodo_repo).resolve() if kimodo_repo else None
        self.python=str(Path(python).resolve()) if python else sys.executable
        self.bindings=bindings or {};self.results={};self.events=[]
        unknown=set(self.bindings)-set(graph['nodes'])
        if unknown:raise ValueError(f'Unknown bound node ids: {sorted(unknown)}')
        self.runtime={'python':sys.version,'numpy':version('numpy'),'scipy':version('scipy')}
        self.backend_env=os.environ.copy()
        if self.repo:
            self.runtime['torch']=version('torch')
            cache=self.repo/'.cache/huggingface/hub'
            if cache.is_dir():self.backend_env.setdefault('HF_HUB_CACHE',str(cache))
            llama=self.repo/'.cache/modelscope/Meta-Llama-3-8B-Instruct'
            if llama.is_dir():self.backend_env.setdefault('KIMODO_LLAMA_BASE_MODEL_PATH',str(llama))
            if graph['rules'].get('reuseModel',False):
                # HF cache constants are captured at first import, including
                # imports made by rig construction and checkpoint fingerprinting.
                self.backend_env.update(LOCAL_CACHE='True',HF_HUB_OFFLINE='1',TEXT_ENCODER_MODE='local',TEXT_ENCODER='llm2vec')
                self.backend_env.setdefault('TEXT_ENCODER_DEVICE','cpu')
                for name in ('HF_HUB_CACHE','KIMODO_LLAMA_BASE_MODEL_PATH','LOCAL_CACHE','HF_HUB_OFFLINE','TEXT_ENCODER_MODE','TEXT_ENCODER','TEXT_ENCODER_DEVICE'):
                    if name in self.backend_env:os.environ[name]=self.backend_env[name]
                os.environ.pop('CHECKPOINT_DIR',None)
        if any(p.name.lower() in ('assets','reference') for p in [self.output,*self.output.parents]):
            raise ValueError('Authoring cache must be outside production assets/reference directories')
        # Include implementation changes in every cache identity.
        self.implementation=digest({p.name:file_hash(p) for p in Path(__file__).parent.glob('*.py') if not p.name.startswith('test_') and p.name not in ('publish.py','root_curves.py')})
        if self.repo:
            self.backend=digest({str(p.relative_to(self.repo)):file_hash(p) for p in self.repo.joinpath('kimodo').rglob('*.py')})
        else:self.backend=None
        self.checkpoints={}
        self.models={}

    def checkpoint_identity(self,model):
        if model in self.checkpoints:return self.checkpoints[model]
        sys.path.insert(0,str(self.repo))
        from huggingface_hub import snapshot_download
        from kimodo.model.registry import MODEL_NAMES, resolve_model_name
        key=model if model in MODEL_NAMES else resolve_model_name(model,'Kimodo')
        repositories=[MODEL_NAMES[key],
                      'McGill-NLP/LLM2Vec-Meta-Llama-3-8B-Instruct-mntp',
                      'McGill-NLP/LLM2Vec-Meta-Llama-3-8B-Instruct-mntp-supervised']
        files={}
        for repository in repositories:
            path=Path(snapshot_download(repo_id=repository,local_files_only=True,cache_dir=self.backend_env.get('HF_HUB_CACHE')))
            for file in sorted(path.rglob('*')):
                if file.is_file():files[repository+'/'+file.relative_to(path).as_posix()]=file_hash(file)
        if self.backend_env.get('KIMODO_LLAMA_BASE_MODEL_PATH'):
            path=Path(self.backend_env['KIMODO_LLAMA_BASE_MODEL_PATH'])
            for file in sorted(path.rglob('*')):
                if file.is_file():files['llama-base/'+file.relative_to(path).as_posix()]=file_hash(file)
        self.checkpoints[model]=digest(files)
        return self.checkpoints[model]

    def output_of(self,key):
        return self.results[key]['outputs']

    def rig(self):
        return read_json(self.output_of('rig:soma')['rig.json']['path'])

    def motion(self,key):
        from .motion import load_motion
        output=self.output_of(key)
        if 'motion.npz' not in output:raise ValueError(f'{key} did not produce humanoid motion')
        return load_motion(output['motion.npz']['path'],self.rig(),self.graph['rules']['fps'])

    def run(self, targets=None):
        order=topological_order(self.graph['nodes'],targets)
        self.output.mkdir(parents=True,exist_ok=True)
        lock=self.output/'running.lock'
        with lock.open('x',encoding='utf-8') as f:f.write(str(os.getpid()))
        run_id=str(uuid.uuid4());run_log=self.output/f'run-{run_id}.json'
        try:
            for key in order:
                self.execute_node(key)
                write_json(run_log,{'status':'running','events':self.events,'targets':targets})
            bundle=self.output/f'bundle-{run_id}.json'
            assets=[{'node':key,'identity':receipt['identity'],'outputs':receipt['outputs']}
                    for key,receipt in self.results.items() if receipt['op'] in ('deliver','deliver_prop')]
            write_json(bundle,{'status':'authoring_candidates','productionReady':False,'assets':assets,
                               'graphDigest':digest(self.graph),'rulesDigest':self.graph['rulesDigest']})
            write_json(run_log,{'status':'authoring_complete','events':self.events,'targets':targets,
                                'productionReady':False,'bundle':str(bundle)})
        except Exception as error:
            write_json(run_log,{'status':'failed','events':self.events,'targets':targets,
                                'error':str(error),'productionReady':False})
            raise
        finally:
            # Register completed candidate nodes even when a downstream quality
            # gate rejects the batch. Viewing them never activates a game pack.
            try:
                from .viewer_export import export_batch
                self.viewer_batch=export_batch(self.graph,self.results,run_dir=self.output,
                    status='failed' if self.events and self.events[-1]['status']=='failed' else 'authoring_complete')
            except Exception as viewer_error:
                write_json(self.output/f'viewer-export-failure-{run_id}.json',{'error':str(viewer_error)})
                print(f'VIEWER_EXPORT_FAILED: {viewer_error}',file=sys.stderr,flush=True)
            lock.unlink()
        return {'status':'authoring_complete','productionReady':False,'runLog':str(run_log),
                'bundle':str(bundle),
                'executed':sum(e['status']=='built' for e in self.events),
                'cached':sum(e['status']=='cached' for e in self.events),
                'viewerBatch':getattr(self,'viewer_batch',None),
                'targets':{k:self.results[k] for k in (targets or order)}}

    def execute_node(self,key):
        node=self.graph['nodes'][key];binding=self.bindings.get(key)
        if binding and node['op'] not in ('rig','generate','external'):
            raise ValueError(f'Cannot replace derived node with a binding: {key}')
        if binding and not Path(binding['path']).is_file():raise ValueError(f'Bound input not found: {binding["path"]}')
        identity={'node':node,'rules':self.graph['rulesDigest'],'implementation':self.implementation,
                  'runtime':self.runtime,
                  'dependencies':{d:self.results[d]['identity'] for d in node['deps']},
                  'dependencyOutputs':{d:{n:o['sha256'] for n,o in self.output_of(d).items()} for d in node['deps']},
                  'binding':dict(binding,sha256=file_hash(binding['path'])) if binding else None,
                  'backend':self.backend if node['op'] in ('rig','generate') else None}
        if node['op']=='rig' and self.repo and not binding:
            identity['rigAssets']={str(p.relative_to(self.repo)):file_hash(p) for directory in ('somaskel30','somaskel77')
                                   for p in (self.repo/'kimodo/assets/skeletons'/directory).glob('*') if p.is_file()}
        if node['op']=='native_mantle_reference' or node['params'].get('contract',{}).get('family')=='mantle':
            from .mantle import reference_input
            _,_,reference_identity=reference_input(node['params'].get('semantic') or node['params']['contract']['semanticName'])
            identity['nativeMantleInputs']=reference_identity
        if node['op']=='native_gait_reference':
            from .als_reference import original_input
            index=Path(node['params']['indexPath'])
            if not index.is_absolute():index=Path(__file__).resolve().parents[2]/index
            _,_,payload=original_input(index,node['params']['source'])
            identity['nativeReferenceInputs']={str(index):file_hash(index),str(payload):file_hash(payload)}
        if node['op']=='native_air_reference' or node['params'].get('contract',{}).get('family') in ('fall','flail'):
            from .air import reference_input
            _,_,reference_identity=reference_input(node['params'].get('semantic') or node['params']['contract']['semanticName'])
            identity['nativeAirInputs']=reference_identity
        if node['op']=='generate' and self.repo and not binding:
            identity['checkpoints']=self.checkpoint_identity(node['params']['model'])
            identity['textEncoderDevice']=self.backend_env.get('TEXT_ENCODER_DEVICE','cpu')
            cache=self.graph['rules'].get('textEmbeddingCache')
            if cache:
                from .text_embeddings import FrozenTextEmbeddings
                frozen=FrozenTextEmbeddings(cache,identity['checkpoints'])
                identity['textEmbeddingCacheSha256']=file_hash(cache)
                identity['textEmbeddingPayloads']={text:entry['sha256'] for text,entry in frozen.manifest['prompts'].items()}
        if binding and node['params'].get('payloadType')=='prop':
            manifest=read_json(binding['path']);parent=Path(binding['path']).parent
            identity['propFiles']={name:file_hash(parent/manifest[name]) for name in ('rigPath','motionPath')}
        fingerprint=digest(identity);folder=self.output/'nodes'/digest(key)[:20]/fingerprint
        receipt_path=folder/'receipt.json'
        if receipt_path.exists():
            receipt=read_json(receipt_path)
            if receipt['identity']==fingerprint and all(Path(o['path']).is_file() and file_hash(o['path'])==o['sha256'] for o in receipt['outputs'].values()):
                self.results[key]=receipt;self.events.append({'node':key,'status':'cached','identity':fingerprint})
                print(f'CACHE {key}',flush=True);return
        building=self.output/f'.building-{uuid.uuid4()}';building.mkdir()
        print(f'BUILD {key} ({node["op"]})',flush=True)
        try:
            self.handle(node,building,binding)
            outputs={p.name:{'path':str(folder/p.name),'sha256':file_hash(p)} for p in building.iterdir() if p.is_file()}
            if not outputs:raise ValueError(f'No outputs from {key}')
            receipt={'node':key,'op':node['op'],'identity':fingerprint,'inputs':identity,'outputs':outputs,
                     'productionReady':False,'boundInput':bool(binding)}
            write_json(building/'receipt.json',receipt)
            folder.parent.mkdir(parents=True,exist_ok=True)
            if folder.exists():
                # Preserve the damaged cache for diagnosis; do not erase a previous run.
                folder.rename(folder.with_name(folder.name+'.damaged-'+str(uuid.uuid4())))
            building.rename(folder)
            self.results[key]=receipt;self.events.append({'node':key,'status':'built','identity':fingerprint})
        except Exception as error:
            write_json(building/'failure.json',{'node':key,'error':str(error),'identity':identity})
            self.events.append({'node':key,'status':'failed','evidence':str(building)})
            raise

    def handle(self,node,folder,binding):
        from . import motion as m
        import numpy as np
        p=node['params'];op=node['op']
        if op=='rig':
            if binding:
                rig=read_json(binding['path'])
            else:
                if not self.repo:raise ValueError('Supply --kimodo-repo or a rig:soma JSON binding')
                sys.path.insert(0,str(self.repo))
                from kimodo.skeleton import SOMASkeleton77
                skeleton=SOMASkeleton77()
                joints=skeleton.neutral_joints.cpu().numpy();parents=skeleton.joint_parents.cpu().tolist()
                offsets=[(joints[i]-joints[parent]).tolist() if parent>=0 else [0.,0.,0.] for i,parent in enumerate(parents)]
                rig={'jointNames':skeleton.bone_order_names,'parents':parents,'offsets':offsets,
                     'footJointNames':skeleton.foot_joint_names,'hipJointNames':skeleton.hip_joint_names,
                     'standardTPoseGlobalRotations':skeleton.global_rot_offsets.cpu().tolist(),
                     'basis':'native SOMA77 BONES-SEED','units':'meters','up':'Y',
                     'sourceAssets':{str(x):file_hash(x) for x in Path(skeleton.folder).glob('*') if x.is_file()}}
            names=rig['jointNames'];parents=rig['parents']
            if len(names)!=len(parents) or len(set(names))!=len(names) or parents[0]!=-1 or any(not 0<=v<i for i,v in enumerate(parents) if i):
                raise ValueError('Invalid topologically ordered rig')
            m.mirror_map(rig)
            write_json(folder/'rig.json',rig);return
        if op=='external' and p.get('payloadType')=='prop':
            if not binding:raise ValueError(f'Unbound authoring input {node["id"]}: {p["reason"]}')
            manifest=read_json(binding['path']);parent=Path(binding['path']).parent
            for name,label in (('rigPath','prop.rig'),('motionPath','prop.motion')):
                path=parent/manifest[name]
                (folder/(label+path.suffix)).write_bytes(path.read_bytes())
            write_json(folder/'prop-input.json',{'source':manifest,'productionReady':False});return
        if op=='deliver_prop':
            row=p['contract']
            for name,source in self.output_of(p['source']).items():
                (folder/name).write_bytes(Path(source['path']).read_bytes())
            write_json(folder/'asset.json',{'source':row['source'],'asset':row['asset'],'edition':row['edition'],
                        'motionEncoding':'external_prop_rig','productionReady':False,'requirements':p['requires']});return
        rig=self.rig()
        if binding:
            motion=m.load_motion(binding['path'],rig,binding.get('fps'))
            m.save_motion(folder/'motion.npz',motion,rig)
            write_json(folder/'bound-input.json',{'path':binding['path'],'sha256':file_hash(binding['path'])});return
        if op=='external':raise ValueError(f'Unbound authoring input {node["id"]}: {p["reason"]}')
        if op=='native_mantle_reference':
            from .mantle import native_reference
            index=Path(p['indexPath'])
            if not index.is_absolute():index=Path(__file__).resolve().parents[2]/index
            motion,receipt=native_reference(p['semantic'],rig,index)
            m.save_motion(folder/'motion.npz',motion,rig)
            write_json(folder/'constraint-reference.json',receipt)
            return
        if op=='native_air_reference':
            from .air import native_reference
            index=Path(p['indexPath'])
            if not index.is_absolute():index=Path(__file__).resolve().parents[2]/index
            motion,receipt=native_reference(p['semantic'],rig,index)
            m.save_motion(folder/'motion.npz',motion,rig)
            write_json(folder/'constraint-reference.json',receipt)
            return
        if op=='native_gait_reference':
            from .als_reference import native_gait_reference
            index=Path(p['indexPath'])
            if not index.is_absolute():index=Path(__file__).resolve().parents[2]/index
            motion,receipt=native_gait_reference(index,p['source'],rig,p['expectedSpeedMps'],p['direction'])
            m.save_motion(folder/'motion.npz',motion,rig)
            write_json(folder/'constraint-reference.json',receipt)
            return
        if op=='generate':
            if not self.repo:raise ValueError('Generation requires --kimodo-repo')
            if p.get('airReference'):
                from .air import materialize_constraints
                constraints,constraint_receipt=materialize_constraints(p,self.motion(p['airReference']),rig)
                write_json(folder/'air-constraints.json',constraint_receipt)
            elif p.get('mantleReference'):
                from .mantle import materialize_constraints
                constraints,constraint_receipt=materialize_constraints(p,self.motion(p['mantleReference']),rig)
                write_json(folder/'mantle-constraints.json',constraint_receipt)
            elif p.get('gaitReference'):
                from .gait_constraints import materialize_gait_constraints,GaitConstraintError
                try:
                    constraints, constraint_receipt=materialize_gait_constraints(p,self.motion(p['gaitReference']),rig)
                except GaitConstraintError as error:
                    write_json(folder/'gait-constraints.json',error.receipt)
                    raise
                write_json(folder/'gait-constraints.json',constraint_receipt)
            else:
                constraints=m.materialize_constraints(p,self.motion,rig)
            write_json(folder/'constraints.json',constraints)
            # CLI uses a path argument list, never shell interpolation.
            args=[self.python,'-m','kimodo.scripts.generate',p['prompt'],'--model',p['model'],
                  '--duration',str(p['seconds']),'--seed',str(p['seed']),
                  '--diffusion_steps',str(p['steps']),'--constraints',str(folder/'constraints.json'),
                  '--output',str(folder/'generated')]
            if p.get('postProcessing',True) is False:
                args.append('--no-postprocess')
            env=self.backend_env.copy();env.setdefault('TEXT_ENCODER_DEVICE','cpu')
            env.update(LOCAL_CACHE='True',HF_HUB_OFFLINE='1',TEXT_ENCODER_MODE='local',TEXT_ENCODER='llm2vec')
            # The cache identity hashes the resolved HF snapshots, so don't allow
            # a separate CHECKPOINT_DIR or encoder service to silently override them.
            env.pop('CHECKPOINT_DIR',None)
            if self.graph['rules'].get('reuseModel', False):
                # The same public inference API/settings as the CLI, with one
                # model load per process. Each job still resets its own seed.
                import contextlib
                os.environ.update(env)
                sys.path.insert(0,str(self.repo))
                import torch
                from kimodo import load_model
                from kimodo.constraints import load_constraints_lst
                from kimodo.exports.motion_io import save_kimodo_npz
                from kimodo.tools import seed_everything
                with (folder/'generation.log').open('w',encoding='utf-8') as log, contextlib.redirect_stdout(log), contextlib.redirect_stderr(log):
                    threads=self.graph['rules'].get('torchThreads')
                    if threads is not None:
                        if not isinstance(threads,int) or threads<1:raise ValueError('Invalid torchThreads')
                        torch.set_num_threads(threads)
                    if p['model'] not in self.models:
                        device=self.graph['rules'].get('inferenceDevice','cuda:0' if torch.cuda.is_available() else 'cpu')
                        if device not in ('cpu','cuda:0'):raise ValueError('Unsupported inferenceDevice')
                        print('KIMODO_MODEL_LOAD_BEGIN device='+device,flush=True)
                        frozen=None
                        if self.graph['rules'].get('textEmbeddingCache'):
                            from .text_embeddings import FrozenTextEmbeddings
                            frozen=FrozenTextEmbeddings(self.graph['rules']['textEmbeddingCache'],self.checkpoint_identity(p['model']))
                        self.models[p['model']]=load_model(p['model'],device=device,default_family='Kimodo',text_encoder=frozen)
                        print('KIMODO_MODEL_LOAD_OK',flush=True)
                    model=self.models[p['model']]
                    seed_everything(p['seed'])
                    constraints_lst=load_constraints_lst(str(folder/'constraints.json'),model.skeleton)
                    output=model([p['prompt']],[int(p['seconds']*p['fps'])],constraint_lst=constraints_lst,
                                 num_denoising_steps=p['steps'],num_samples=1,multi_prompt=True,
                                 num_transition_frames=5,post_processing=p.get('postProcessing',True),return_numpy=True)
                    sample={k:(v[0] if hasattr(v,'shape') and len(v.shape)>0 and v.shape[0]==1 else v) for k,v in output.items()}
                    save_kimodo_npz(str(folder/'generated.npz'),sample)
            else:
                with (folder/'generation.log').open('w',encoding='utf-8') as log:
                    result=subprocess.run(args,cwd=self.repo,env=env,stdout=log,stderr=subprocess.STDOUT)
                if result.returncode:raise RuntimeError(f'Kimodo exited {result.returncode}; see {folder / "generation.log"}')
            motion=m.load_motion(folder/'generated.npz',rig,p['fps'])
            if len(motion['root_positions'])!=int(p['seconds']*p['fps']):raise ValueError('Kimodo frame count/FPS differs from plan')
            m.save_motion(folder/'motion.npz',motion,rig)
            write_json(folder/'generation-settings.json',p);return
        if op=='air_cycle':
            from .air import author,AirError,POLICY
            if p['policy']!=POLICY:raise ValueError('Unknown Air authoring policy')
            source_outputs=self.output_of(p['source'])
            if 'constraint-reference.json' in source_outputs:raise ValueError('Constraint-only Air reference cannot become a delivery')
            try:
                motion,receipt,trajectory=author(self.motion(p['source']),self.motion(p['reference']),rig,p['semantic'])
            except AirError as error:
                write_json(folder/'air-validation.json',error.receipt)
                raise
            receipt['sourceMotionSha256']=source_outputs['motion.npz']['sha256']
            write_json(folder/'air-validation.json',receipt)
            write_json(folder/'air-trajectory.json',trajectory)
        elif op=='mantle':
            from .mantle import author,MantleError,POLICY
            if p['policy']!=POLICY:raise ValueError('Unknown mantle policy')
            source_outputs=self.output_of(p['source'])
            if 'constraint-reference.json' in source_outputs:
                raise ValueError('Constraint-only mantle reference cannot become a delivery')
            try:
                motion,receipt=author(self.motion(p['source']),self.motion(p['reference']),rig,p['semantic'])
            except MantleError as error:
                write_json(folder/'mantle-validation.json',error.receipt)
                raise
            receipt['sourceMotionSha256']=source_outputs['motion.npz']['sha256']
            write_json(folder/'mantle-validation.json',receipt)
        elif op=='rest':
            if 'standardTPoseGlobalRotations' not in rig:raise ValueError('T pose requires rig rest-basis conversion, not identity native rotations')
            global_rot=np.asarray(rig['standardTPoseGlobalRotations'])[None]
            motion=m.complete(m.local_from_global(global_rot,rig['parents']),np.zeros((1,3)),rig)
        elif op=='freeze':
            source=self.motion(p['source']);frame=p.get('frame',round(p.get('fraction',0)*(len(source['root_positions'])-1)))
            motion=m.freeze(source,frame,p['count'],rig)
            if p.get('canonical'):motion=m.canonicalize(motion,rig)
        elif op in ('copy','reverse'):
            source=self.motion(p['source'])
            motion=source if op=='copy' else m.complete(source['local_rot_mats'][::-1].copy(),source['root_positions'][::-1].copy(),rig,float(source['fps']))
        elif op=='upper_overlay':motion=m.upper_overlay(m.freeze(self.motion(p['base']),0,1,rig),self.motion(p['style']),self.motion(p['reference']),rig)
        elif op=='mirror':
            source_outputs=self.output_of(p['source'])
            gate=source_outputs.get('gait-validation.json')
            if gate:
                if p['mask']!='all':raise ValueError('A validated locomotion cycle requires a full-body mirror')
                from .gait import mirror_authored_cycle,GaitValidationError
                try:
                    motion,mirrored_receipt=mirror_authored_cycle(self.motion(p['source']),
                        self.motion(p['base']),rig,read_json(gate['path']),reverse_time=p.get('reverseTime',False),
                        repair_derived=p.get('repairDerived',False))
                except GaitValidationError as error:
                    write_json(folder/'gait-validation.json',error.receipt)
                    raise
                write_json(folder/'gait-validation.json',mirrored_receipt)
                write_json(folder/'authoring-trajectory.json',mirrored_receipt['removedPlanarTrajectory'])
            else:motion=m.mirror(self.motion(p['source']),self.motion(p['base']),rig,p['mask'])
        elif op=='offset':
            base=m.freeze(self.motion(p['base']),0,p['count'],rig)
            motion=m.offset(base,p['angles'],p['axis'],rig)
        elif op=='pack':
            parts=[self.motion(key) for key in p['parts']]
            if any(len(part['root_positions'])!=1 for part in parts):raise ValueError('Pose table inputs must be exactly one frame')
            motion=m.complete(np.concatenate([part['local_rot_mats'] for part in parts]),np.concatenate([part['root_positions'] for part in parts]),rig,p['fps'])
        elif op=='parameter':
            if p['mode']=='pitch_row':
                source=m.resample(self.motion(p['source']),p['count'],rig)
                motion=m.offset(source,{n:p['pitch']*w for n,w in p['distribution'].items()},'x',rig)
            else:motion=m.parameter(self.motion(p['base']),p['count'],p['distribution'],p['limit'],p['mode'],rig)
        elif op=='periodic_repair':
            from .periodic_repair import repair_periodic
            reference=p.get('contactReference')
            span=None
            if reference:
                period=p['targetPeriodFrames'];index=p['constrainedCycleIndex']
                if not isinstance(index,int) or index<0:raise ValueError('Constrained cycle index must be nonnegative')
                span=(index*period,(index+1)*period)
            motion,repair_report=repair_periodic(self.motion(p['source']),rig,p['expectedSpeedMps'],harmonics=p['harmonics'],
                target_period_frames=p.get('targetPeriodFrames'),constrained_cycle_span=span,
                contact_reference=None if reference is None else self.motion(reference),
                ramp_frames=p.get('rampFrames',3),minimum_knee_bend_degrees=p.get('minimumKneeBendDegrees',0),
                fit_regularization=p.get('fitRegularization',.002),enforce_sole_clearance=p.get('enforceSoleClearance',False))
            write_json(folder/'periodic-repair.json',repair_report)
            if not repair_report['after']['pass']:
                m.save_motion(folder/'rejected-repair.npz',motion,rig)
                raise ValueError('Periodic reference repair failed its independent authoring gate')
        elif op=='gait_selection':
            from .gait import audit_candidate
            choices=[]
            for source in p['sources']:
                value=self.motion(source);audit=audit_candidate(value,rig,p['expectedSpeedMps'])
                score=max(audit['detection']['measurements'][side]['nearGroundMedianSpeedMps'] or 0 for side in ('left','right'))
                choices.append((source,value,audit,score))
            valid=[choice for choice in choices if choice[2]['pass']]
            selected=min(valid,key=lambda choice:choice[3]) if valid else None
            write_json(folder/'selection.json',dict(passed=selected is not None,
                selected=None if selected is None else selected[0],
                policy='passing authoring gate first; minimum worst-foot near-ground median; reference wins ties',
                candidates=[dict(source=c[0],passed=c[2]['pass'],worstFootMedianMps=c[3],checks=c[2]['checks']) for c in choices],
                targetAndRuntimeValidationRequired=True))
            if selected is None:raise ValueError('No acceptable repaired/regenerated gait candidate')
            motion=selected[1]
        elif op=='gait_cycle':
            from .gait import author_cycle,GaitValidationError
            if 'constraint-reference.json' in self.output_of(p['source']):
                raise ValueError('An ALS pose constraint reference cannot be delivered as a generated gait')
            try:
                motion,gait_receipt=author_cycle(self.motion(p['source']),rig,p['targetCount'],p['expectedSpeedMps'],p.get('policy'))
            except GaitValidationError as error:
                write_json(folder/'gait-validation.json',error.receipt)
                raise
            write_json(folder/'gait-validation.json',gait_receipt)
            write_json(folder/'authoring-trajectory.json',gait_receipt['removedPlanarTrajectory'])
        elif op=='loop':motion=m.loop_candidate(self.motion(p['source']),p['seconds'],p['startFraction'],rig,p['removeHeading'],p['removeTranslation'])
        elif op=='in_place':motion=m.remove_planar_path(self.motion(p['source']),p['rootPath'],p['seconds'],rig)
        elif op=='deliver':
            row=p['contract'];timing=row['originalTiming'];source_motion=self.motion(p['source'])
            motion=source_motion if row['family'] in ('fall','flail') else m.resample(source_motion,timing['sampledKeyCount'],rig)
            if len(motion['root_positions'])!=timing['sampledKeyCount']:raise ValueError('Delivery key count differs')
            motion['fps']=np.asarray(timing['frameRateNumerator']/timing['frameRateDenominator'])
            asset_metadata={'source':row['source'],'asset':row['asset'],'edition':row['edition'],
                        'motionEncoding':'full_pose_native_soma77','productionReady':False,
                        'targetEvaluation':row['originalEvaluation'],'referenceNode':p['base'],
                        'targetTiming':timing,'requirements':p['requires'],
                        'curveNamesToAuthor':row['originalCurveNames'],
                        'curvePolicy':row['curveAndNotifyPolicy'],
                        'note':'Retarget full pose and reference together; compute additive once on target rig. Original curve keys/events were not copied.'}
            if row.get('targetGaitPolicy'):
                outputs=self.output_of(p['source'])
                gate=outputs.get('gait-validation.json');trajectory=outputs.get('authoring-trajectory.json')
                if gate is None or trajectory is None:raise ValueError('Gait delivery requires an executed authoring gate')
                receipt=read_json(gate['path'])
                if not receipt.get('pass') or receipt['outputFrameCount']!=timing['sampledKeyCount'] or receipt['fps']!=float(motion['fps']):
                    raise ValueError('Gait delivery differs from the validated final time domain')
                source_motion=self.motion(p['source'])
                if not np.allclose(motion['local_rot_mats'],source_motion['local_rot_mats'],atol=1e-12,rtol=0) or not np.array_equal(motion['root_positions'],source_motion['root_positions']):
                    raise ValueError('Gait delivery altered validated motion')
                asset_metadata['gaitValidationReceipt']=dict(file=gate['path'],sha256=gate['sha256'],
                    sourceMotionFile=outputs['motion.npz']['path'],sourceMotionSha256=outputs['motion.npz']['sha256'])
                asset_metadata['authoringTrajectory']=dict(file=trajectory['path'],sha256=trajectory['sha256'])
                upper=outputs.get('upper-body-validation.json')
                if upper:
                    if not read_json(upper['path'])['passed']:
                        raise ValueError('Gait delivery requires passing Idle-relative upper-body validation')
                    asset_metadata['upperBodyValidationReceipt']=dict(file=upper['path'],sha256=upper['sha256'])
            if row['family']=='mantle':
                outputs=self.output_of(p['source']);gate=outputs.get('mantle-validation.json')
                if gate is None:raise ValueError('Mantle delivery requires an executed authoring gate')
                receipt=read_json(gate['path']);source_motion=self.motion(p['source'])
                if (not receipt['passed'] or receipt['sampledKeyCount']!=timing['sampledKeyCount'] or
                    receipt['fps']!=float(motion['fps']) or
                    not np.allclose(motion['local_rot_mats'],source_motion['local_rot_mats'],atol=1e-12,rtol=0) or
                    not np.array_equal(motion['root_positions'],source_motion['root_positions'])):
                    raise ValueError('Mantle delivery altered validated motion/time domain')
                asset_metadata['mantleValidationReceipt']=dict(file=gate['path'],sha256=gate['sha256'],
                    sourceMotionFile=outputs['motion.npz']['path'],sourceMotionSha256=outputs['motion.npz']['sha256'])
                asset_metadata['note']='Generated mantle pose in validated native time domain; native PoseStanding/PoseGrounded semantics retained; contact metadata and root curves are authored on the target.'
            if row['family'] in ('fall','flail'):
                from .air import POLICY as AIR_POLICY
                from .gait import _array_hash
                outputs=self.output_of(p['source']);gate=outputs.get('air-validation.json');trajectory=outputs.get('air-trajectory.json')
                if row.get('targetAirPolicy')!=AIR_POLICY or gate is None or trajectory is None:
                    raise ValueError('Air delivery requires its dedicated authoring gate and trajectory')
                receipt=read_json(gate['path'])
                if (receipt.get('passed') is not True or receipt.get('policy')!=AIR_POLICY or
                    receipt.get('semanticName')!=row['semanticName'] or
                    receipt.get('sampledKeyCount')!=timing['sampledKeyCount'] or
                    receipt.get('outputMotionSha256')!=_array_hash(motion['local_rot_mats'],motion['root_positions'],motion['fps'])):
                    raise ValueError('Air delivery altered validated motion/time domain')
                asset_metadata['airValidationReceipt']=dict(file=gate['path'],sha256=gate['sha256'])
                asset_metadata['airTrajectory']=dict(file=trajectory['path'],sha256=trajectory['sha256'])
            if p['base']:m.save_motion(folder/'additive-reference-full-pose.npz',self.motion(p['base']),rig)
        else:raise ValueError(f'Unknown operation {op}')
        if op in ('gait_cycle', 'mirror'):
            idle = p.get('upperBodyIdleReference')
            limits = p.get('upperBodyPolicy', {})
            inherited = self.output_of(p['source']).get('upper-body-validation.json')
            if not idle and inherited:
                previous = read_json(inherited['path'])
                idle, limits = previous['idleNode'], previous['limits']
            if idle:
                from .upper_body import audit_idle_relative
                upper = audit_idle_relative(motion, self.motion(idle), rig, limits)
                upper['idleNode'] = idle
                write_json(folder/'upper-body-validation.json', upper)
                if not upper['passed']:
                    m.save_motion(folder/'rejected-upper-body.npz', motion, rig)
                    raise ValueError('Walk upper-body posture exceeds its generated Idle reference limits')
        m.save_motion(folder/'motion.npz',motion,rig)
        if op=='deliver':
            if asset_metadata.get('airValidationReceipt'):
                asset_metadata['airValidationReceipt']['deliveredMotionSha256']=file_hash(folder/'motion.npz')
            if 'gaitValidationReceipt' in asset_metadata:
                asset_metadata['gaitValidationReceipt']['deliveredMotionSha256']=file_hash(folder/'motion.npz')
            if 'mantleValidationReceipt' in asset_metadata:
                asset_metadata['mantleValidationReceipt']['deliveredMotionSha256']=file_hash(folder/'motion.npz')
            write_json(folder/'asset.json',asset_metadata)
        if op=='deliver' and self.repo:
            import torch
            sys.path.insert(0,str(self.repo))
            from kimodo.skeleton import SOMASkeleton77
            from kimodo.exports.bvh import save_motion_bvh
            save_motion_bvh(str(folder/'motion.bvh'),torch.from_numpy(motion['local_rot_mats']),
                            torch.from_numpy(motion['root_positions']),skeleton=SOMASkeleton77(),
                            fps=float(motion['fps']),standard_tpose=True)
