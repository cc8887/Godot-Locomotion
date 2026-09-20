import fs from 'node:fs';
import assert from 'node:assert/strict';

// Node-output parity, not a full UE character/AnimBP comparison. Native replay
// reads only the captured inputs; expected outputs are compared here afterwards.
const [capturePath, nativePath] = process.argv.slice(2);
assert(capturePath && nativePath, 'Pass the Godot capture and native replay paths.');
const capture = JSON.parse(fs.readFileSync(capturePath, 'utf8'));
const native = JSON.parse(fs.readFileSync(nativePath, 'utf8'));
assert.equal(native.schemaVersion, 1);
assert.equal(native.frames.length, capture.length);
const xyz = v => [v.X, v.Y, v.Z];
const distance = (a, b) => Math.hypot(...a.map((v, i) => v - b[i]));
const maxima = {};
const record = (key, value, frame, side) => {
  assert(Number.isFinite(value), `${key}: nonfinite value at ${frame}/${side}`);
  if (!maxima[key] || value > maxima[key].value) maxima[key] = { value, frame, side };
};
let evaluated = 0, constrained = 0, lockedConstrained = 0;
const freeStarts = {};
let freePairs = 0;
for (let index = 0; index < capture.length; index++) {
  const row = capture[index], actual = native.frames[index];
  assert.equal(row.Frame, index + 1);
  assert.equal(actual.frame, row.Frame);
  assert.equal(actual.evaluated, row.RefactoredRigFeet.LocationEvaluated);
  if (!actual.evaluated) continue;
  for (const side of ['Left', 'Right']) {
    const input = row.RefactoredRigFeet[side + 'Input'];
    const expected = row.RefactoredRig[side].Location;
    const result = actual[side.toLowerCase()];
    assert.equal(result.valid, expected.Spring.Valid);
    record('location_cm', distance(result.location, xyz(expected.FootLocation)), row.Frame, side);
    record('offset_cm', Math.abs(result.offset - expected.OffsetZ), row.Frame, side);
    record('velocity_cm_per_second', Math.abs(result.velocity - expected.Spring.Velocity), row.Frame, side);
    record('previous_target_cm', Math.abs(result.previousTarget - expected.Spring.PreviousTarget), row.Frame, side);
    const beforeClamp = xyz(input.TargetLocation);
    beforeClamp[2] += result.offset;
    const clampDistance = distance(beforeClamp, result.location);
    record('native_clamp_cm', clampDistance, row.Frame, side);
    if (clampDistance > 1e-5) {
      constrained++;
      if (row.Locked) lockedConstrained++;
    }
    // Preserve all-frame drift reporting in the original scene smoke. This
    // additional metric identifies continuous per-foot unconstrained windows
    // using the independently executed UE node, not the Godot expected output.
    const free = row.Locked && row.Based.ThighConstrained === 0 && clampDistance <= 1e-5;
    if (free && freeStarts[side]) {
      freePairs++;
      record('unconstrained_platform_drift_m', distance(xyz(row[side]), freeStarts[side]), row.Frame, side);
    } else if (free) freeStarts[side] = xyz(row[side]);
    else delete freeStarts[side];
    evaluated++;
  }
}
assert(evaluated > 0, 'No evaluated samples.');
process.stdout.write(JSON.stringify({ capturePath, nativePath, frames: capture.length,
  evaluated, constrained, lockedConstrained, freePairs, maxima }, null, 2) + '\n');
// 1 micrometer in component-space position, 0.001 cm/s in spring velocity.
// These compare original UE node output, and do not relax platform drift gates.
for (const key of ['location_cm', 'offset_cm', 'previous_target_cm'])
  assert(maxima[key].value <= 1e-4, `${key}: native parity failed`);
assert(maxima.velocity_cm_per_second.value <= 1e-3, 'Native spring velocity parity failed');
process.stdout.write('NATIVE_FOOT_LOCATION_PARITY_PASS\n');
