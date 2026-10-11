"""Explicit reuse of upstream receipts and independent static gait sampling."""
from __future__ import annotations

import copy
from collections import Counter
from pathlib import Path

import numpy as np

from .graph import digest, read_json, topological_order
from .runner import file_hash, write_json

POSE_POLICY = 'generated-gait-phase-pose-v1'


def configure_pose_samples(nodes, rules):
    for name, parent in rules.get('sampledGaitPoses', {}).items():
        key, candidate = 'motion:' + name, 'candidate:' + parent
        if key not in nodes or nodes[key]['op'] != 'freeze' or candidate not in nodes:
            raise ValueError('A sampled pose requires an existing gait pose and generated candidate: ' + name)
        generator = nodes[candidate]
        parent_node = nodes['motion:' + parent]
        period = generator['params'].get('targetPeriodSeconds') or parent_node['params'].get('seconds')
        path = generator['params'].get('rootPath')
        if generator['op'] != 'generate' or not period or not path or path['kind'] != 'linear':
            raise ValueError('A sampled pose requires a linear gait candidate with a known period: ' + name)
        nodes[key] = dict(id=key, op='sample_pose', deps=['rig:soma', candidate], params=dict(
            source=candidate, semantic=name, parentSemantic=parent, policy=POSE_POLICY,
            periodSeconds=period, phase=.25,
            samplingMode='conditioned-cycle' if generator['params'].get('targetPeriodSeconds') else 'loop-crop',
            count=nodes[key]['params']['count'], seconds=generator['params']['seconds'], rootPath=path))
    # The right reference uses the same sampled left body, with the existing mirror.
    if 'Run_Left_Pose' in rules.get('sampledGaitPoses', {}):
        key = 'motion:Run_Right_Pose'
        nodes[key] = dict(id=key, op='mirror', deps=['rig:soma', 'motion:Run_Left_Pose', 'motion:Stand_Pose'],
            params=dict(source='motion:Run_Left_Pose', base='motion:Stand_Pose', mask='all'))


def reuse_upstream(graph, paths, *, derived_only=False):
    """Cut only explicitly supplied, byte-verified upstream source nodes."""
    graph = copy.deepcopy(graph)
    for path in paths:
        path = Path(path).resolve()
        incoming = read_json(path)
        if isinstance(incoming, list):
            incoming = {row['receipt']['node']: row['receipt'] for row in incoming if row.get('passed') is True}
        for key, receipt in incoming.items():
            if key not in graph['nodes'] or receipt['op'] not in ('generate', 'gait_cycle', 'mirror'):
                continue
            node = graph['nodes'][key]
            if node['op'] != receipt['op']:
                continue
            original = receipt['inputs']['node']
            if digest(receipt['inputs']) != receipt['identity']:
                raise ValueError('Upstream receipt identity changed: ' + key)
            if receipt['node'] != key or original['id'] != key or original['op'] != receipt['op']:
                raise ValueError('Upstream node identity differs: ' + key)
            if receipt.get('boundInput'):
                continue  # A binding alone is not evidence of Kimodo generation.
            if receipt['op'] == 'generate' and any(original['params'].get(field)!=node['params'].get(field)
                    for field in ('semantic','rootPath')):
                continue
            outputs = receipt['outputs']
            for payload in outputs.values():
                if file_hash(payload['path']) != payload['sha256']:
                    raise ValueError('Changed upstream output: ' + key)
            if 'motion.npz' not in outputs or 'constraint-reference.json' in outputs:
                raise ValueError('Upstream source is not generated motion: ' + key)
            if receipt['op'] != 'generate':
                gate = outputs.get('gait-validation.json')
                if gate is None or read_json(gate['path']).get('pass') is not True:
                    continue
                if 'authoring-trajectory.json' not in outputs:
                    raise ValueError('Upstream gait is missing its trajectory: ' + key)
            rig_hash = receipt['inputs']['dependencyOutputs']['rig:soma']['rig.json']
            graph['nodes'][key] = dict(id=key, op='reuse_motion', deps=['rig:soma'], params=dict(
                receiptFile=str(path), receiptSha256=file_hash(path), originalReceipt=receipt,
                originalNode=node, rigSha256=rig_hash))
            for sample in graph['nodes'].values():
                if sample['op']=='sample_pose' and sample['params']['source']==key:
                    p=original['params'];sample['params'].update(seconds=p['seconds'],rootPath=p['rootPath'])
                    if p.get('targetPeriodSeconds'):
                        sample['params'].update(periodSeconds=p['targetPeriodSeconds'],samplingMode='conditioned-cycle')
    if derived_only:
        for key, node in graph['nodes'].items():
            if node['op'] == 'generate':
                graph['nodes'][key] = dict(id=key, op='external', deps=['rig:soma'], params=dict(
                    reason='No matching completed upstream Kimodo receipt; derived scope never runs diffusion'))
    graph['order'] = topological_order(graph['nodes'])
    graph.setdefault('summary',{})['operations']=dict(sorted(Counter(n['op'] for n in graph['nodes'].values()).items()))
    graph['pipeline'] = 'derived' if derived_only else graph.get('pipeline', 'non-prop')
    return graph


def handle_reuse(runner, node, folder):
    import shutil
    from .motion import load_motion
    from .gait import _array_hash, revalidate_authored_cycle
    p = node['params']
    if file_hash(p['receiptFile']) != p['receiptSha256']:
        raise ValueError('Upstream receipt changed')
    if runner.output_of('rig:soma')['rig.json']['sha256'] != p['rigSha256']:
        raise ValueError('Upstream rig differs from the selected SOMA rig')
    outputs = p['originalReceipt']['outputs']
    for payload in outputs.values():
        if file_hash(payload['path']) != payload['sha256']:
            raise ValueError('Upstream payload changed')
    motion = load_motion(outputs['motion.npz']['path'], runner.rig())
    if float(motion['fps']) != runner.graph['rules']['fps']:
        raise ValueError('Upstream motion FPS differs from the production time domain')
    if 'gait-validation.json' in outputs:
        gate = read_json(outputs['gait-validation.json']['path'])
        expected = _array_hash(motion['local_rot_mats'], motion['root_positions'], motion['fps'])
        if not gate.get('pass') or gate.get('outputMotionSha256') != expected:
            raise ValueError('Upstream gait receipt does not bind its motion')
        target = p['originalNode']['params'].get('targetCount')
        if target is not None and len(motion['root_positions']) != target:
            raise ValueError('Upstream gait time domain differs')
        current_validation = revalidate_authored_cycle(motion, runner.rig(), gate)
    for name in ('motion.npz', 'gait-validation.json', 'authoring-trajectory.json', 'upper-body-validation.json'):
        if name in outputs:
            shutil.copy2(outputs[name]['path'], folder / name)
    if 'gait-validation.json' in outputs:
        gate.update(policy=current_validation['policy'], policySha256=current_validation['policySha256'],
                    cycleAnchorSide=current_validation['cycleAnchorSide'],
                    reuseValidation=current_validation)
        write_json(folder / 'gait-validation.json', gate)
    write_json(folder / 'reuse-provenance.json', p)


def sample_pose(source, rig, params):
    from .motion import freeze, remove_planar_path
    if params['policy'] != POSE_POLICY or not 0 <= params['phase'] < 1:
        raise ValueError('Invalid sampled pose policy/phase')
    period=round(params['periodSeconds']*float(source['fps']))
    if params['samplingMode']=='conditioned-cycle':
        frame=period+round(params['phase']*period)
    elif params['samplingMode']=='loop-crop':
        count=period+1
        start=max(0,min(round((len(source['root_positions'])-1)*.35),len(source['root_positions'])-count))
        frame=start+round(params['phase']*period)
    else:raise ValueError('Unknown pose sampling mode')
    in_place = remove_planar_path(source, params['rootPath'], params['seconds'], rig)
    result = freeze(in_place, frame, params['count'], rig)
    if not np.array_equal(result['local_rot_mats'][0], source['local_rot_mats'][frame]):
        raise ValueError('Pose extraction changed local body rotations')
    receipt = dict(policy=POSE_POLICY, passed=True, semanticName=params['semantic'],
        parentSemantic=params['parentSemantic'], sourceFrame=frame, sourceTimeSeconds=frame/float(source['fps']),
        sourceFrameCount=len(source['root_positions']), outputFrameCount=params['count'],
        removedPlanarTranslation=(source['root_positions'][frame]-in_place['root_positions'][frame]).tolist(),
        validationScope='static full pose only; no gait cycle acceptance', productionReady=False)
    return result, receipt


def retain_base_deliveries(pack_path, assets, receipts):
    from .publish import ROOT
    pack=read_json(pack_path)
    for entry in pack['files']:
        if file_hash(ROOT/entry['path'].removeprefix('res://'))!=entry['sha256']:
            raise ValueError('Base pack payload changed: '+entry['path'])
    retarget=read_json(pack['nativeRetargetBundle'])
    author=Path(retarget['authoringBundle']);base=read_json(author)
    incoming=read_json(author.parent/'receipts.json')
    if incoming['rig:soma']['outputs']['rig.json']['sha256']!=receipts['rig:soma']['outputs']['rig.json']['sha256']:
        raise ValueError('Base and derived deliveries use different SOMA rigs')
    selected={a['node']:a for a in base['assets']}
    combined={key:incoming[key] for key in selected}
    for asset in base['assets']:
        if incoming[asset['node']]['identity']!=asset['identity']:
            raise ValueError('Base delivery identity differs')
        if incoming[asset['node']]['outputs']!=asset['outputs']:
            raise ValueError('Base delivery outputs differ from their receipt')
        for payload in asset['outputs'].values():
            if file_hash(payload['path'])!=payload['sha256']:raise ValueError('Base authoring output changed')
    selected.update({a['node']:a for a in assets});combined.update(receipts)
    return list(selected.values()),combined


def validate_pose_delivery(job):
    p=job.get('poseSamplingReceipt')
    if job['contract'].get('sampledPosePolicy')!=POSE_POLICY or not p:
        raise ValueError('Missing independent pose sampling evidence')
    for key,hash_key in (('file','sha256'),('sourcePoseMotionFile','sourcePoseMotionSha256')):
        if file_hash(p[key])!=p[hash_key]:raise ValueError('Pose sampling evidence changed')
    receipt=read_json(p['file'])
    source=receipt['sourceMotion']
    if receipt.get('passed') is not True or receipt['policy']!=POSE_POLICY or file_hash(source['path'])!=source['sha256']:
        raise ValueError('Invalid upstream pose sample')
    if file_hash(job['motionPath'])!=job['motionSha256'] or p['deliveredMotionSha256']!=job['motionSha256']:
        raise ValueError('Pose sampling does not bind the delivered motion')
    with np.load(job['motionPath'],allow_pickle=False) as data, np.load(p['sourcePoseMotionFile'],allow_pickle=False) as pose:
        for key in ('local_rot_mats','root_positions'):
            if not np.allclose(data[key],pose[key][0],atol=1e-12,rtol=0):
                raise ValueError('Static pose delivery changed the sampled body')


def targets(graph, contracts, pack):
    from .scope import is_prop
    missing = set(pack['missingNonPropSources'])
    return ['asset:' + row['edition'] + ':' + row['asset'] for row in contracts['assets']
            if not is_prop(row) and row['source'] in missing and
            graph['nodes']['motion:' + row['semanticName']]['op'] in ('sample_pose', 'freeze', 'offset', 'mirror', 'reuse_motion')]
