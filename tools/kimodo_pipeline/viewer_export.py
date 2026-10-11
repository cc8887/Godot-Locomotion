"""Register exact authoring receipts for the read-only Motion Viewer.

Preview files preserve every native frame and the source origin; they are not
retargeted assets or a production-pack activation.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path
import uuid

import numpy as np
from scipy.spatial.transform import Rotation

from .motion import load_motion, validate_motion

ROOT = Path(__file__).resolve().parents[2]


def _hash(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def _write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name+'.'+str(uuid.uuid4())+'.tmp')
    temporary.write_text(json.dumps(value, ensure_ascii=False, allow_nan=False)+'\n', encoding='utf-8')
    temporary.replace(path)


def register_index(root, folder, index, *, make_latest=True):
    viewer_root = Path(root)/'artifacts/kimodo_viewer'
    index_path = folder/'index.json'
    _write(index_path, index)
    registry_path = viewer_root/'current.json'
    registry = json.loads(registry_path.read_text(encoding='utf-8')) if registry_path.exists() else {'batches':[]}
    registered = dict(id=index['id'], label=index['label'], status=index['status'], createdUtc=index['createdUtc'],
        index=index_path.relative_to(viewer_root).as_posix(), sha256=_hash(index_path))
    registry.update(schemaVersion=1, latestBatch=index['id'] if make_latest else registry.get('latestBatch',index['id']),
        batches=[registered, *[b for b in registry['batches'] if b['id']!=index['id']]])
    _write(registry_path, registry)
    return dict(id=index['id'], index=str(index_path), entries=len(index['entries']))


def preview_motion(source, rig, name, provenance):
    validate_motion(source, rig)
    local = source['local_rot_mats']
    quaternions = Rotation.from_matrix(local.reshape(-1, 3, 3)).as_quat().reshape(len(local), -1, 4)
    return dict(format='kimodo-viewer-motion-v1', name=name, units='cm', up='y',
        fps=float(source['fps']), frameCount=len(local),
        jointNames=rig['jointNames'], parents=rig['parents'],
        offsetsCm=(np.asarray(rig['offsets'])*100).tolist(),
        rootPositionsCm=(source['root_positions']*100).tolist(),
        localQuaternions=quaternions.tolist(), source=provenance,
        displayRootOnly=True, productionReady=False)


def export_batch(graph, receipts, *, run_dir, label=None, status='authoring_candidates', root=ROOT, batch_id=None):
    root = Path(root).resolve()
    run_dir = Path(run_dir).resolve()
    # An unrelated temporary/test checkout must not change the project's Viewer.
    if not run_dir.is_relative_to(root/'artifacts'):
        return None
    rig_entry = receipts.get('rig:soma', {}).get('outputs', {}).get('rig.json')
    if not rig_entry:
        return None
    if _hash(rig_entry['path']) != rig_entry['sha256']:
        raise ValueError('Viewer rig receipt changed')
    rig = json.loads(Path(rig_entry['path']).read_text(encoding='utf-8'))
    names = {}
    for node in graph['nodes'].values():
        contract = node['params'].get('contract')
        if contract and (contract['semanticName'] not in names or contract['edition']=='v4'):
            names[contract['semanticName']] = contract['asset']
    selected = []
    for key, receipt in sorted(receipts.items()):
        if key not in graph['nodes'] or 'motion.npz' not in receipt['outputs']:
            continue
        if key.startswith('candidate:') and receipt['op']=='generate':
            if receipt.get('boundInput'):
                continue  # Reused input bindings are not fresh generation.
            constrained = receipts.get(key.removesuffix(':discovery'), {}) if key.endswith(':discovery') else {}
            if constrained and not constrained.get('boundInput') and 'motion.npz' in constrained.get('outputs', {}):
                continue  # The actual constrained output owns the generated phase.
            phase = 'generated'
        elif key.startswith('motion:'):
            source_key=graph['nodes'][key]['params'].get('source')
            if source_key and receipts.get(source_key,{}).get('boundInput'):
                continue
            phase = 'master'
        else:
            continue  # Constraint teachers are never presented as new outputs.
        selected.append((key, receipt, phase))
    identity = json.dumps([(k, r['identity'], p) for k, r, p in selected], sort_keys=True)
    batch_id = batch_id or run_dir.name+'-'+hashlib.sha256(identity.encode()).hexdigest()[:12]
    if Path(batch_id).name != batch_id or any(c not in 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-' for c in batch_id):
        raise ValueError('Invalid Viewer batch identity')
    viewer_root = root/'artifacts/kimodo_viewer'
    folder = viewer_root/'batches'/batch_id
    entries = []
    for key, receipt, phase in selected:
        entry = receipt['outputs']['motion.npz']
        if _hash(entry['path']) != entry['sha256']:
            raise ValueError('Viewer source receipt changed: '+key)
        semantic = graph['nodes'][key]['params'].get('semantic', key.split(':')[1])
        name = names.get(semantic, semantic)
        qualification = '未验收'
        gate_entry = receipt['outputs'].get('gait-validation.json') or receipt['outputs'].get('mantle-validation.json')
        gate = None
        if gate_entry:
            if _hash(gate_entry['path']) != gate_entry['sha256']:
                raise ValueError('Viewer gate receipt changed: '+key)
            gate = json.loads(Path(gate_entry['path']).read_text(encoding='utf-8'))
            qualification = '源检查通过' if gate.get('pass',gate.get('passed',False)) else '源检查未通过'
        source = dict(node=key, identity=receipt['identity'], motionPath=entry['path'],
            motionSha256=entry['sha256'], rigSha256=rig_entry['sha256'],
            phase=phase, qualification=qualification, batch=batch_id, runDirectory=str(run_dir))
        payload = preview_motion(load_motion(entry['path'], rig), rig, name, source)
        rel = Path('batches')/batch_id/phase/(hashlib.sha256(key.encode()).hexdigest()[:20]+'.motion.json')
        path = viewer_root/rel
        _write(path, payload)
        row = dict(path=rel.as_posix(), url='/authoring-file/'+rel.as_posix(),
            name=name, semantic=semantic, node=key, phase=phase, qualification=qualification,
            kind='source-motion', size=path.stat().st_size, sha256=_hash(path), sidecars=[],
            source=source, gatePass=None if gate is None else bool(gate.get('pass',gate.get('passed',False))))
        entries.append(row)
    index = dict(schemaVersion=1, id=batch_id, label=label or run_dir.name, status=status,
        createdUtc=datetime.now(timezone.utc).isoformat(), productionReady=False,
        runDirectory=str(run_dir), entries=entries)
    return register_index(root, folder, index)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run-dir', type=Path, required=True)
    parser.add_argument('--graph', type=Path, required=True)
    parser.add_argument('--label')
    parser.add_argument('--status', default='authoring_candidates')
    args = parser.parse_args()
    receipts = {}
    for path in sorted(args.run_dir.joinpath('nodes').rglob('receipt.json')):
        receipt = json.loads(path.read_text(encoding='utf-8'))
        if receipt['node'] in receipts and receipts[receipt['node']]['identity']!=receipt['identity']:
            raise ValueError('Ambiguous historical receipts; select an isolated run directory: '+receipt['node'])
        receipts[receipt['node']] = receipt
    result = export_batch(json.loads(args.graph.read_text(encoding='utf-8')), receipts,
        run_dir=args.run_dir, label=args.label, status=args.status)
    if result is None:
        raise ValueError('No project-local authoring rig/receipts to register')
    print(json.dumps(result, ensure_ascii=False))


if __name__=='__main__':
    main()
