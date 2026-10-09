"""Offline SOMA repair through Kimodo's bundled MotionCorrection backend.

Produces diagnostic candidates and before/after independent gait audits. It
does not publish assets or certify Blender, target-skin or Godot quality.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import sys

import numpy as np

from . import gait, motion


def correction_contacts(values, frames):
    """The native ABI reads exactly [L heel, L toe, R heel, R toe]."""
    values = np.asarray(values, dtype=np.float32)
    if values.ndim == 3 and values.shape[0] == 1:
        values = values[0]
    if values.shape not in ((frames, 4), (frames, 6)):
        raise ValueError('Contacts must have four native channels or six expanded SOMA77 channels')
    if not np.isfinite(values).all() or np.any((values < 0) | (values > 1)):
        raise ValueError('Contact confidence must be finite and between zero and one')
    if values.shape[1] == 6:
        # The public SOMA30 -> SOMA77 converter duplicates the toe channel.
        # Independent toe-end tracks need an explicit authoring decision.
        if not np.array_equal(values[:, 1], values[:, 2]) or not np.array_equal(values[:, 4], values[:, 5]):
            raise ValueError('Independent toe-end contacts require an explicit four-channel contact plan')
        values = values[:, [0, 1, 3, 4]]
    return np.ascontiguousarray(values)


def restore_travel(source, rig, space, trajectory=None):
    if space not in ('travelling', 'in-place'):
        raise ValueError('Declare the motion coordinate space')
    roots = source['root_positions'].copy()
    if space == 'in-place':
        if trajectory is None:
            raise ValueError('In-place repair requires the removed authoring trajectory')
        travel = np.asarray(trajectory['positions'], dtype=float)
        if (trajectory.get('units') != 'meters' or trajectory.get('up') != 'Y' or
                travel.shape != roots.shape or not np.isfinite(travel).all() or
                float(trajectory['fps']) != float(source['fps'])):
            raise ValueError('Authoring trajectory does not match the motion time/coordinate domain')
        roots[:, [0, 2]] += travel[:, [0, 2]]
    elif trajectory is not None:
        raise ValueError('Do not add a second trajectory to travelling motion')
    return motion.complete(source['local_rot_mats'].copy(), roots, rig, float(source['fps']))


def file_hash(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', type=Path, required=True, help='Native NPZ, preferably generated.npz with model foot_contacts')
    parser.add_argument('--rig', type=Path, required=True)
    parser.add_argument('--kimodo-repo', type=Path, required=True)
    parser.add_argument('--space', choices=('travelling', 'in-place'), required=True)
    parser.add_argument('--trajectory', type=Path)
    parser.add_argument('--contacts', type=Path, help='Explicit JSON array of four-channel contact confidences')
    parser.add_argument('--constraints', type=Path, help='Optional already corrected, compatible Kimodo constraints')
    parser.add_argument('--speed', type=float, required=True, help='Expected authored travel speed in meters/second')
    parser.add_argument('--backend', choices=('native', 'periodic'), default='native')
    parser.add_argument('--harmonics', type=int, default=4, help='Periodic fit harmonic count')
    parser.add_argument('--root-margin', type=float, default=.04)
    parser.add_argument('--feedback', action='store_true', help='Emit periodic regeneration constraints only after the repair passes')
    parser.add_argument('--output', type=Path, required=True, help='New output directory; never overwrites an existing run')
    args = parser.parse_args()
    if not np.isfinite(args.root_margin) or args.root_margin < 0:
        raise ValueError('Invalid root correction margin')
    rig = json.loads(args.rig.read_text(encoding='utf-8-sig'))
    if args.backend == 'periodic' and (args.space != 'travelling' or args.contacts or args.constraints):
        raise ValueError('Periodic backend currently takes travelling motion and measured source contact intervals')
    source = motion.load_motion(args.input, rig)
    trajectory = json.loads(args.trajectory.read_text(encoding='utf-8-sig')) if args.trajectory else None
    travelling = restore_travel(source, rig, args.space, trajectory)
    if args.backend == 'periodic':
        contacts = None
        contact_provenance = dict(kind='independent_measured_source_intervals')
    elif args.contacts:
        contacts = json.loads(args.contacts.read_text(encoding='utf-8-sig'))
        contact_provenance = dict(kind='explicit_authoring_plan', file=str(args.contacts.resolve()), sha256=file_hash(args.contacts))
    else:
        with np.load(args.input, allow_pickle=False) as data:
            if 'foot_contacts' not in data:
                raise ValueError('No model foot_contacts; supply a reviewed contact plan, not a minimum-height guess')
            contacts = data['foot_contacts'].copy()
        contact_provenance = dict(kind='model_prediction_not_ground_truth', originalChannels=int(np.asarray(contacts).shape[-1]))
    if contacts is not None:
        contacts = correction_contacts(contacts, len(source['root_positions']))
    args.output.mkdir(parents=True, exist_ok=False)
    report = dict(schemaVersion=1, status='repair_running', productionReady=False,
                  input=str(args.input.resolve()), inputSha256=file_hash(args.input), rigSha256=file_hash(args.rig),
                  inputSpace=args.space, outputSpace=args.space, contactSource=contact_provenance,
                  nativeContactOrder=['left_heel', 'left_toe', 'right_heel', 'right_toe'],
                  rootMarginMeters=args.root_margin, speedMps=args.speed,
                  before=gait.audit_candidate(travelling, rig, args.speed))
    report_path = args.output / 'repair-report.json'

    def save():
        report_path.write_text(json.dumps(report, indent=2, allow_nan=False) + '\n', encoding='utf-8')

    save()
    try:
        if args.backend == 'periodic':
            from .periodic_repair import repair_periodic
            repaired, details = repair_periodic(travelling, rig, args.speed, harmonics=args.harmonics)
            report['periodicRepair'] = details
        else:
            repaired = native_repair(args, travelling, rig, contacts, report)
        report['backend'] = args.backend
        report['after'] = gait.audit_candidate(repaired, rig, args.speed)
        if args.backend == 'native':
            report['maximumHipCorrectionMeters'] = float(np.max(np.linalg.norm(
                repaired['root_positions'] - travelling['root_positions'], axis=-1)))
        report['repairAcceptedByAuthoringGate'] = report['after']['pass']
        report['status'] = 'authoring_gate_passed_target_validation_required' if report['after']['pass'] else 'repair_rejected'
        output_motion = repaired
        if args.space == 'in-place':
            roots = repaired['root_positions'].copy()
            roots[:, [0, 2]] -= np.asarray(trajectory['positions'])[:, [0, 2]]
            output_motion = motion.complete(repaired['local_rot_mats'], roots, rig, float(source['fps']))
        path = args.output / 'repaired.npz'
        motion.save_motion(path, output_motion, rig)
        report['outputSha256'] = file_hash(path)
        save()
        if args.feedback and report['after']['pass']:
            from .gait_constraints import materialize_gait_constraints
            cycle = report['after']['selectedCycle']
            delta = repaired['root_positions'][cycle['endInclusive'], [0, 2]] - repaired['root_positions'][cycle['start'], [0, 2]]
            direction = delta / np.linalg.norm(delta)
            period = cycle['durationSeconds']
            feedback_frames = max(round(2 * float(source['fps'])), 3 * (cycle['endInclusive']-cycle['start']) + 1)
            params = dict(seconds=min(10., (feedback_frames + 1e-6) / float(source['fps'])),
                          fps=float(source['fps']), targetPeriodSeconds=period, boundaryContextFrames=1,
                          rootPath=dict(kind='linear', velocity=args.speed, direction=direction.tolist(), headingDegrees=0))
            conditions, condition_receipt = materialize_gait_constraints(params, repaired, rig)
            condition_receipt['sourceRepairAccepted'] = True
            condition_receipt['sourceRepairInputSha256'] = report['inputSha256']
            condition_receipt['sourceRepairOutputSha256'] = report['outputSha256']
            (args.output / 'feedback-constraints.json').write_text(json.dumps(conditions, indent=2) + '\n', encoding='utf-8')
            (args.output / 'feedback-receipt.json').write_text(json.dumps(condition_receipt, indent=2) + '\n', encoding='utf-8')
            report['feedbackConstraintSha256'] = file_hash(args.output / 'feedback-constraints.json')
        save()
        print(json.dumps(dict(status=report['status'], report=str(report_path.resolve()))))
        return 0 if report['after']['pass'] else 2
    except Exception as error:
        report.update(status='repair_failed', error=str(error))
        save()
        raise


def native_repair(args, travelling, rig, contacts, report):
    sys.path.insert(0, str(args.kimodo_repo.resolve()))
    import torch
    from kimodo.constraints import Root2DConstraintSet, load_constraints_lst
    from kimodo.postprocess import post_process_motion
    from kimodo.skeleton import SOMASkeleton77
    torch.set_num_threads(2)
    skeleton = SOMASkeleton77()
    if rig['jointNames'] != list(skeleton.bone_order_names):
        raise ValueError('Offline backend requires the exact SOMA77 bone order')
    constraints = load_constraints_lst(str(args.constraints), skeleton) if args.constraints else []
    if args.constraints:
        report['constraintsSha256'] = file_hash(args.constraints)
    else:
        # Preserve the existing global path; do not pin travelling feet to
        # component-space coordinates or freeze the hips' vertical bob.
        path = travelling['root_positions'][:, [0, 2]]
        with np.load(args.input, allow_pickle=False) as data:
            if args.space == 'travelling' and 'smooth_root_pos' in data:
                smooth = np.asarray(data['smooth_root_pos'])
                if smooth.ndim == 3 and smooth.shape[0] == 1:
                    smooth = smooth[0]
                if smooth.shape == travelling['root_positions'].shape:
                    path = smooth[:, [0, 2]]
        constraints = [Root2DConstraintSet(skeleton, torch.arange(len(path)), torch.tensor(path, dtype=torch.float32))]
    result = post_process_motion(torch.tensor(travelling['local_rot_mats'], dtype=torch.float32)[None],
                                 torch.tensor(travelling['root_positions'], dtype=torch.float32)[None],
                                 torch.tensor(contacts, dtype=torch.float32)[None], skeleton,
                                 constraints, root_margin=args.root_margin)
    repaired = motion.complete(result['local_rot_mats'][0].cpu().numpy(),
                               result['root_positions'][0].cpu().numpy(), rig, float(travelling['fps']))
    return repaired

if __name__ == '__main__':
    raise SystemExit(main())
