import fs from 'node:fs';
import assert from 'node:assert/strict';
const [outputPath, ...inputs] = process.argv.slice(2);
assert(outputPath && inputs.length === 3, 'Usage: node analyze_platform_frequency_matrix.mjs report.json 30.json 60.json 120.json');
const report = { scope: 'Six-second real-physics tilt fixture; full support contact gate covers 4–6 seconds only', cases: [], passed: true };
for (const inputPath of inputs) {
  const rows = JSON.parse(fs.readFileSync(inputPath, 'utf8'));
  const hz = rows.length / 6;
  assert([30,60,120].includes(hz));
  const result = { inputPath, hz, frames: rows.length, lockedFrames: 0, calibrationFrames: 0,
    maximumCalibrationResidualCm: 0, firstFrameMinimumMeters: Math.min(rows[0].Sole.LeftMinimumHeight, rows[0].Sole.RightMinimumHeight),
    sides: {}, maximumPlatformPositionErrorMeters: 0, maximumPlatformQuaternionError: 0, passed: true };
  for (const side of ['Left','Right']) result.sides[side] = { minimumMeters: Infinity, maximumLowestMeters: -Infinity, penetratingFrames: 0, hoveringFrames: 0 };
  for (let i=0; i<rows.length; i++) {
    const row = rows[i], frame = i+1;
    assert.equal(row.Frame, frame);
    assert(Math.abs(row.RigCapture.MotorInput.DeltaTime - 1/hz) < 1e-7);
    const moving = frame > hz && frame <= hz*3;
    assert.equal(row.Command.MovementAxes.X, 0); assert.equal(row.Command.MovementAxes.Y, moving ? 1 : 0);
    // SyncToPhysics exposes the preceding command transform on this step.
    const t = Math.max(0, frame-1-hz/2)/hz;
    const position = [-2, -.5 + .15*Math.sin(t*.75), 0];
    const p = row.Platform.Position;
    result.maximumPlatformPositionErrorMeters = Math.max(result.maximumPlatformPositionErrorMeters,
      Math.hypot(p.X-position[0], p.Y-position[1], p.Z-position[2]));
    const x = .1*Math.sin(t)/2, z = .08*Math.sin(t*2/3)/2;
    const expected = [Math.sin(x)*Math.cos(z), -Math.sin(x)*Math.sin(z), Math.cos(x)*Math.sin(z), Math.cos(x)*Math.cos(z)];
    const q = row.Platform.Rotation, actual = [q.X,q.Y,q.Z,q.W];
    const difference = sign => Math.hypot(...actual.map((v,j)=>v-sign*expected[j]));
    result.maximumPlatformQuaternionError = Math.max(result.maximumPlatformQuaternionError, Math.min(difference(1),difference(-1)));
    const rig = row.RefactoredRig;
    if (rig.LeftCalibrated || rig.RightCalibrated) {
      result.calibrationFrames++;
      result.maximumCalibrationResidualCm = Math.max(result.maximumCalibrationResidualCm,
        rig.LeftCalibrated ? Math.abs(rig.LeftSupportDistance) : 0, rig.RightCalibrated ? Math.abs(rig.RightSupportDistance) : 0);
    }
    if (frame <= hz*4) continue;
    assert(row.Locked, 'Late support window must not be selectively excluded');
    result.lockedFrames++;
    for (const side of ['Left','Right']) {
      const support = row.Sole.FullSupport;
      assert(support && support[side+'Count'] >= row.Sole[side].length, 'Full support geometry observation is required');
      const value = support[side+'MinimumHeight'], state = result.sides[side];
      assert(Number.isFinite(value));
      state.minimumMeters = Math.min(state.minimumMeters,value); state.maximumLowestMeters = Math.max(state.maximumLowestMeters,value);
      if (value < -.005) state.penetratingFrames++;
      if (value > .005) state.hoveringFrames++;
    }
  }
  result.passed = result.lockedFrames === hz*2 && result.calibrationFrames > 0 && result.maximumCalibrationResidualCm <= .05 &&
    result.maximumPlatformPositionErrorMeters < 1e-6 && result.maximumPlatformQuaternionError < 1e-6 &&
    Object.values(result.sides).every(s => s.penetratingFrames === 0 && s.hoveringFrames === 0);
  report.cases.push(result); report.passed &&= result.passed;
}
assert.deepEqual(report.cases.map(c=>c.hz).sort((a,b)=>a-b), [30,60,120]);
fs.writeFileSync(outputPath,JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify(report,null,2));
if (!report.passed) process.exitCode = 1;
