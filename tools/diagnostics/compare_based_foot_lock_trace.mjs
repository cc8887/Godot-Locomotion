import fs from 'node:fs';

const [capturePath, nativePath, reportPath] = process.argv.slice(2);
if (!capturePath || !nativePath || !reportPath) throw new Error('Expected capture, native output, and report paths.');
const frames = JSON.parse(fs.readFileSync(capturePath, 'utf8'));
const native = JSON.parse(fs.readFileSync(nativePath, 'utf8'));
if (native.schemaVersion !== 1 || native.results.length !== frames.length * 2) throw new Error('Native case coverage differs.');
const captures = new Map(frames.map(frame => [frame.Frame, frame.BasedFootLockTrace]));
const seen = new Set();
const limits = { positionCm: .001, rotationDegrees: .02, amount: 1e-6 };
const worst = { positionCm: 0, rotationDegrees: 0, amount: 0 };
const rigVerified = !!native.initializedRig;
if (rigVerified) { limits.referenceAxis = 1e-6; worst.referenceAxis = 0; }
const failures = [];
const nearStop = [];
const previousNative = new Map();
const qmul = (a, b) => ({ X: a.W*b.X+a.X*b.W+a.Y*b.Z-a.Z*b.Y,
  Y: a.W*b.Y-a.X*b.Z+a.Y*b.W+a.Z*b.X, Z: a.W*b.Z+a.X*b.Y-a.Y*b.X+a.Z*b.W,
  W: a.W*b.W-a.X*b.X-a.Y*b.Y-a.Z*b.Z });
const angle = (a, b) => {
  const dot = a.X*b.X+a.Y*b.Y+a.Z*b.Z+a.W*b.W;
  const norm = Math.hypot(a.X,a.Y,a.Z,a.W)*Math.hypot(b.X,b.Y,b.Z,b.W);
  if (!Number.isFinite(norm) || Math.abs(norm-1) > .002) throw new Error('Nonunit or nonfinite quaternion in trace.');
  return 2*Math.acos(Math.min(1, Math.abs(dot/norm)))*180/Math.PI;
};
const distance = (a, b) => Math.hypot(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
const conjugate = q => ({ X: -q.X, Y: -q.Y, Z: -q.Z, W: q.W });
const rotate = (q, v) => qmul(qmul(q, { ...v, W: 0 }), conjugate(q));
const relativePosition = (world, component) => {
  const v = rotate(conjugate(component.Rotation), {
    X: world.X-component.Position.X, Y: world.Y-component.Position.Y, Z: world.Z-component.Position.Z });
  return { X: v.X/component.Scale.X, Y: v.Y/component.Scale.Y, Z: v.Z/component.Scale.Z };
};
// Geometric diagnostics only. The pass/fail oracle above remains compiled C++.
const signedAngleXY = (a, b) => {
  const norm = Math.hypot(a.X, a.Y)*Math.hypot(b.X, b.Y);
  if (norm <= 1e-8) return null;
  return Math.acos(Math.max(-1, Math.min(1, (a.X*b.X+a.Y*b.Y)/norm))) *
    Math.sign(a.X*b.Y-a.Y*b.X)*180/Math.PI;
};
for (const row of native.results) {
  const key = `${row.Frame}:${row.Side}`;
  if (seen.has(key) || !['Left','Right'].includes(row.Side)) throw new Error('Duplicate or unknown native case.');
  seen.add(key);
  const frame = captures.get(row.Frame);
  if (!frame || frame.Identity.FrameId !== row.Frame) throw new Error('Uncommitted or mismatched trace identity.');
  const trace = frame[row.Side];
  const expected = trace.Result;
  const actual = row.Native;
  const diff = { Frame: row.Frame, Side: row.Side, positionCm: 0, rotationDegrees: 0, amount: Math.abs(actual.Amount-expected.Amount) };
  if (rigVerified) diff.referenceAxis = distance(native.initializedRig[row.Side], trace.Input.ThighAxisPelvisSpace);
  for (const field of ['WorldLock','BaseLock','ComponentLock','FinalComponent']) {
    const position = distance(actual[field].Position, expected[field].Position);
    const rotation = angle(actual[field].Rotation, expected[field].Rotation);
    if (!Number.isFinite(position) || !Number.isFinite(rotation)) throw new Error('Nonfinite native pose.');
    if (position > diff.positionCm) { diff.positionCm = position; diff.positionField = field; }
    if (rotation > diff.rotationDegrees) { diff.rotationDegrees = rotation; diff.rotationField = field; }
  }
  for (const name of Object.keys(worst)) worst[name] = Math.max(worst[name], diff[name]);
  if (Object.keys(limits).some(name => !Number.isFinite(diff[name]) || diff[name] > limits[name])) failures.push(diff);
  const rotation = qmul(trace.Input.ComponentTransform.Rotation, actual.FinalComponent.Rotation);
  const prior = previousNative.get(row.Side);
  if (row.Frame >= 610 && row.Frame <= 620 && prior?.frame === row.Frame-1) {
    const result = { Frame: row.Frame, Side: row.Side, amount: actual.Amount,
      nativeStepDegrees: angle(rotation, prior.rotation), godotThigh: expected.ThighConstrained, godotFoot: expected.FootConstrained };
    for (const variant of ['Unconstrained','ThighOnly','FootOnly'])
      result[`${variant}StepDegrees`] = angle(qmul(trace.Input.ComponentTransform.Rotation, row[variant].FinalComponent.Rotation), prior.rotation);
    const thigh = rotate(trace.Input.PelvisRotation, trace.Input.ThighAxisPelvisSpace);
    const target = relativePosition(trace.Input.TargetWorld.Position, trace.Input.ComponentTransform);
    result.captureInputs = {
      previousAmount: trace.Previous.Amount, requestedAmount: trace.Input.LockAmount,
      thighAxisComponent: { X: thigh.X, Y: thigh.Y, Z: thigh.Z },
      previousFinalComponent: trace.Previous.FinalComponent.Position, currentTargetComponent: target,
      previousFinalThighAngleDegrees: signedAngleXY(thigh, trace.Previous.FinalComponent.Position),
      currentTargetThighAngleDegrees: signedAngleXY(thigh, target),
      targetToPreviousFinalCm: distance(target, trace.Previous.FinalComponent.Position)
    };
    nearStop.push(result);
  }
  previousNative.set(row.Side, { frame: row.Frame, rotation });
}
const report = { scope: native.scope, frames: frames.length, cases: seen.size, limits, worst,
  initializedRig: native.initializedRig ?? null, referenceAxesVerified: rigVerified,
  passed: failures.length === 0, failedCases: failures.length, failures, nearStop };
fs.writeFileSync(reportPath, JSON.stringify(report, null, 2)+'\n');
console.log(JSON.stringify({ frames: report.frames, cases: report.cases, limits, worst, passed: report.passed,
  failedCases: failures.length, firstFailures: failures.slice(0, 3), stopFrame: nearStop.filter(row => row.Frame === 616) }));
if (!report.passed) process.exitCode = 1;
