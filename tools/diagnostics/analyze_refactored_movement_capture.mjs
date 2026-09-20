import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';

const [directory, prefix] = process.argv.slice(2);
assert(directory && prefix, 'Expected capture directory and fresh output prefix.');
const rows = JSON.parse(fs.readFileSync(path.join(directory, 'frames.json'), 'utf8'));
assert.equal(rows.length, 720);
const quaternion = q => [q.X, q.Y, q.Z, q.W];
const angle = (a, b) => {
  const x = quaternion(a), y = quaternion(b), norm = Math.hypot(...x) * Math.hypot(...y);
  assert(Number.isFinite(norm) && Math.abs(norm - 1) < .002, 'Invalid rotation');
  return 2 * Math.acos(Math.min(1, Math.abs(x.reduce((sum, v, i) => sum + v * y[i], 0) / norm))) * 180 / Math.PI;
};
const multiply = (a, b) => ({ X: a.W*b.X+a.X*b.W+a.Y*b.Z-a.Z*b.Y,
  Y: a.W*b.Y-a.X*b.Z+a.Y*b.W+a.Z*b.X, Z: a.W*b.Z+a.X*b.Y-a.Y*b.X+a.Z*b.W,
  W: a.W*b.W-a.X*b.X-a.Y*b.Y-a.Z*b.Z });
const rotate = (q, v) => multiply(multiply(q, {...v, W: 0}), {X:-q.X,Y:-q.Y,Z:-q.Z,W:q.W});
const signedXY = (a,b) => Math.atan2(a.X*b.Y-a.Y*b.X, a.X*b.X+a.Y*b.Y)*180/Math.PI;
const requests = [], excluded = [], stages = [], constrained = [], hipStarts = [];
const maxima = {};
for (let i = 0; i < rows.length; i++) {
  const row = rows[i], trace = row.BasedFootLockTrace, rig = row.RefactoredRigFeet;
  assert.equal(row.Frame, i + 1);
  assert(trace && rig, 'Enable --based-foot-lock-trace and use the stage-aware capture.');
  assert.equal(trace.Identity.FrameId, row.Frame);
  if (trace.Left.Input.Valid && trace.Right.Input.Valid)
    requests.push({ Frame: row.Frame, BasedFootLockTrace: trace });
  else excluded.push(row.Frame); // Native callable probe requires valid feet; never fabricate validity.
  if (row.Cycle.HipTransition && row.Cycle.TransitionsStartedThisFrame > 0)
    hipStarts.push({frame:row.Frame, crossing:row.Cycle.Crossing, direction:row.Cycle.Direction});
  for (const side of ['Left', 'Right']) {
    assert.deepEqual(rig['Target'+side].Location, trace[side].Result.FinalComponent.Position);
    assert.deepEqual(rig['Target'+side].Rotation, trace[side].Result.FinalComponent.Rotation);
    if (!i) continue;
    const before = rows[i-1], current = trace[side];
    const item = {frame:row.Frame, side,
      preFootComponentDegrees:angle(rig['Source'+side].Rotation, before.RefactoredRigFeet['Source'+side].Rotation),
      lockTargetComponentDegrees:angle(rig['Target'+side].Rotation, before.RefactoredRigFeet['Target'+side].Rotation),
      postRigComponentDegrees:angle(rig[side].Rotation, before.RefactoredRigFeet[side].Rotation),
      finalWorldDegrees:angle(row.FootPose[side+'FootWorldRotation'], before.FootPose[side+'FootWorldRotation']),
      previousAmount:current.Previous.Amount, inputAmount:current.Input.LockAmount, amount:current.Result.Amount,
      thighConstrained:current.Result.ThighConstrained, footConstrained:current.Result.FootConstrained,
      previousFinalThighAngleDegrees:signedXY(rotate(current.Input.PelvisRotation,current.Input.ThighAxisPelvisSpace),
        current.Previous.FinalComponent.Position)};
    for (const field of ['preFootComponentDegrees','lockTargetComponentDegrees','postRigComponentDegrees','finalWorldDegrees'])
      if (!maxima[field] || maxima[field].degrees < item[field]) maxima[field] = {frame:row.Frame,side,degrees:item[field]};
    if (item.finalWorldDegrees > 30 || item.lockTargetComponentDegrees > 30) stages.push(item);
    if (item.thighConstrained) constrained.push(item);
  }
}
const report = {
  scope:'Actual production capture; component-stage rotation attribution, not UE whole-character or sole-contact acceptance',
  frames:rows.length, nativeRequestFrames:requests.length, nativeExcludedInvalidFrames:excluded,
  waitingForFeetFrames:rows.filter(r=>r.Cycle.WaitingForFeet).length, hipStarts, maxima,
  discontinuities:stages, thighConstraints:constrained,
};
for (const [suffix, value] of [['.report.json',report],['.request.json',requests]]) {
  const output = prefix + suffix;
  assert(!fs.existsSync(output), 'Refusing to overwrite evidence: '+output);
  fs.writeFileSync(output,JSON.stringify(value,null,2)+'\n',{flag:'wx'});
}
console.log(JSON.stringify(report,null,2));
