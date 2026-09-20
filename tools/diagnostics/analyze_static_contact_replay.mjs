import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';

const [beforeDir, afterDir, output] = process.argv.slice(2);
assert(output, 'Usage: node analyze_static_contact_replay.mjs before-dir after-dir report.json');
const read = (dir, file) => JSON.parse(fs.readFileSync(path.join(dir, file), 'utf8'));
const before = read(beforeDir, 'frames.json'), after = read(afterDir, 'frames.json');
const graph = read(afterDir, 'graph.json').traces[0].frames;
const metadataPath = path.join(afterDir, 'capture.json');
const hz = fs.existsSync(metadataPath) ? read(afterDir, 'capture.json').hz : 60;
assert([30,60,120].includes(hz));
assert.equal(after.length, hz*12); assert.equal(before.length, after.length); assert.equal(graph.length, after.length);
const upstream = ['Cycle', 'SourceSync', 'Standing', 'Movement'];
const point = p => [p.X, p.Y, p.Z];
const distance = (a, b) => Math.hypot(...a.map((x, i) => x - b[i]));
function world(p, t) {
  const v = point(p).map((x, i) => x * point(t.Scale)[i]), q = t.Rotation;
  const cross = (a, b) => [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]];
  const u = [q.X,q.Y,q.Z], c = cross(u, v).map(x => x*2), d = cross(u, c);
  return v.map((x, i) => x + q.W*c[i] + d[i] + point(t.Position)[i]);
}
const report = { beforeDir, afterDir, frames: after.length, hz, upstreamExact: upstream, toePinnedFootFrames: 0,
  lockedRuns: 0, lockedSamples: 0, maximumLockedDriftCm: 0,
  calibrationFootFrames: 0, releasingCorrectionFootFrames: 0, unlockedCorrectionFootFrames: 0,
  scope: 'Static final foot-bone endpoint during unchanged full-lock runs; not full material sole slip or force/contact validation', passed: true };
const active = { Left: null, Right: null };
for (let i = 0; i < after.length; i++) {
  const row = after[i]; assert.equal(row.Frame, i+1); assert.equal(before[i].Frame, row.Frame);
  assert(Math.abs(row.Input.DeltaTime-1/hz)<1e-7 && Math.abs(before[i].Input.DeltaTime-1/hz)<1e-7);
  for (const key of upstream) assert.deepEqual(before[i][key], row[key], `${key} changed at ${i+1}`);
  for (const side of ['Left', 'Right']) {
    const locked = row.RefactoredLocks[side], rig = row.RefactoredRig;
    assert.equal(locked.BaseIdentity, 0);
    const corrected = rig[`${side}ClearanceCorrected`], calibrated = rig[`${side}Calibrated`];
    if (rig[`${side}ToePinned`]) report.toePinnedFootFrames++;
    if (calibrated) report.calibrationFootFrames++;
    if (corrected) {
      assert(locked.Amount < .99999, 'Clearance must not rewrite a full contact');
      report[locked.Amount > .00001 ? 'releasingCorrectionFootFrames' : 'unlockedCorrectionFootFrames']++;
    }
    const eligible = row.Presentation?.PresentationPending !== true && locked.Amount >= .99999 &&
      rig[side].Location.ContactApplied && !rig[side].Location.ContactConstrained &&
      !locked.ThighConstrained && !calibrated && !corrected;
    if (!eligible) { active[side] = null; continue; }
    const key = JSON.stringify([point(locked.WorldLock.Position), row.RefactoredLocks.TeleportSequence]);
    const endpoint = world(row.RefactoredRigFeet[side].Position, graph[i].rigInput.ToWorld);
    if (!active[side] || active[side].key !== key) {
      active[side] = { key, endpoint }; report.lockedRuns++;
    } else {
      report.lockedSamples++;
      report.maximumLockedDriftCm = Math.max(report.maximumLockedDriftCm, distance(endpoint, active[side].endpoint));
    }
  }
}
assert(report.lockedSamples > 100*hz/60 && report.releasingCorrectionFootFrames > 0);
report.passed = report.maximumLockedDriftCm <= .0001; // 0.001 mm; same fixed endpoint, numerical transform tolerance only.
fs.writeFileSync(output, JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report));
if (!report.passed) process.exitCode = 1;
