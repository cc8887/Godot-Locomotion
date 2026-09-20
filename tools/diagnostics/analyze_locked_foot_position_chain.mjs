import fs from 'node:fs';
import assert from 'node:assert/strict';
const [inputPath, outputPath] = process.argv.slice(2);
assert(outputPath, 'Usage: node analyze_locked_foot_position_chain.mjs capture.json report.json');
const rows = JSON.parse(fs.readFileSync(inputPath, 'utf8'));
const v = p => [p.X, p.Y, p.Z];
const add = (a, b) => a.map((x, i) => x + b[i]);
const sub = (a, b) => a.map((x, i) => x - b[i]);
const norm = a => Math.hypot(...a);
const cross = (a, b) => [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]];
const rotate = (a, q) => {
  const xyz = [q.X, q.Y, q.Z], t = cross(xyz, a).map(x => 2*x);
  return add(a, add(t.map(x => q.W*x), cross(xyz, t)));
};
const inverse = q => ({ X: -q.X, Y: -q.Y, Z: -q.Z, W: q.W });
const fromNative = a => [a[1]*.01, a[2]*.01, -a[0]*.01];
const reachable = (input, offset) => {
  const delta = sub(add(v(input.TargetLocation), [0, 0, offset]), v(input.ThighLocation));
  const limit = Math.fround(Math.fround(input.LegLength) * Math.fround(input.MaxLegStretchRatio));
  return limit >= .0001 && norm(delta) <= limit;
};
const report = { inputPath, frames: rows.length, scope: 'Locked, thigh-unconstrained and reach-unconstrained windows only; full vector decomposition in actual platform space', sides: {} };
for (const side of ['Left', 'Right']) {
  const result = report.sides[side] = { samples: 0, pairs: 0, maximumEquationErrorMeters: 0,
    maximumAnchorChangeMeters: 0, maximumFinalDriftMeters: 0, maximumOffsetDriftMeters: 0,
    maximumTangentOffsetDriftMeters: 0, maximumDriftResidualMeters: 0, maximumFrame: null };
  let previous = false, start;
  for (const row of rows) {
    // The scene gate classifies a PAIR as free only when both legs are free.
    const free = row.Locked && row.Based.ThighConstrained === 0 && ['Left','Right'].every(s =>
      reachable(row.RefactoredRigFeet[s+'Input'], row.RefactoredRig[s].Location.OffsetZ));
    if (!free) { previous = false; continue; }
    const q = inverse(row.Platform.Rotation);
    const anchor = fromNative(v(row.RefactoredLocks[side].BaseLock.Position));
    const offset = row.RefactoredRig[side].Location.OffsetZ;
    const worldOffset = fromNative(rotate([0,0,offset], row.Queries.Request.ToWorld.Rotation));
    const baseOffset = rotate(worldOffset, q);
    const final = v(row[side]);
    const error = norm(sub(final, add(anchor, baseOffset)));
    result.samples++; result.maximumEquationErrorMeters = Math.max(result.maximumEquationErrorMeters, error);
    if (!previous) start = { anchor, baseOffset, final };
    else {
      result.pairs++;
      const delta = sub(final, start.final), correction = sub(baseOffset, start.baseOffset);
      result.maximumAnchorChangeMeters = Math.max(result.maximumAnchorChangeMeters, norm(sub(anchor, start.anchor)));
      const drift = norm(delta);
      if (drift > result.maximumFinalDriftMeters) { result.maximumFinalDriftMeters = drift; result.maximumFrame = row.Frame; }
      result.maximumOffsetDriftMeters = Math.max(result.maximumOffsetDriftMeters, norm(correction));
      result.maximumTangentOffsetDriftMeters = Math.max(result.maximumTangentOffsetDriftMeters, Math.hypot(correction[0],correction[2]));
      result.maximumDriftResidualMeters = Math.max(result.maximumDriftResidualMeters, norm(sub(delta, correction)));
    }
    previous = true;
  }
  assert(result.pairs > 10, 'Insufficient unconstrained locked window');
}
report.passed = Object.values(report.sides).every(s => s.maximumEquationErrorMeters < .00001 &&
  s.maximumAnchorChangeMeters < .00001 && s.maximumDriftResidualMeters < .00001);
fs.writeFileSync(outputPath, JSON.stringify(report, null, 2)+'\n');
console.log(JSON.stringify(report,null,2));
if (!report.passed) process.exitCode = 1;
