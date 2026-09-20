import fs from 'node:fs';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';

const [manifestPath, outputPath] = process.argv.slice(2);
assert(outputPath, 'Usage: analyze_foot_source_cases.mjs manifest.json report.json');
const read = file => JSON.parse(fs.readFileSync(file, 'utf8'));
const sha = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
// The generated JSON round trip normalizes numeric -0 to 0. Compare its
// canonical representation, without tolerances or omitted environment fields.
const sameJson = (a, b, label) => assert(JSON.stringify(a) === JSON.stringify(b), label);
const manifest = read(manifestPath);
assert.equal(manifest.captureSha256, sha(manifest.capturePath));
assert.equal(manifest.auditSha256, sha(manifest.auditPath));
const captured = read(manifest.capturePath);
const results = manifest.cases.map(item => ({item, input: read(item.input), native: read(item.output)}));
const report = {schemaVersion: 1, manifestPath, scope: manifest.scope, cases: [],
  pairedMaximumSolePointDistanceMm: 0};
for (const {item, input, native} of results) {
  assert.equal(native.schemaVersion, 2); assert.equal(native.closedFeedback, true);
  assert.equal(native.frames.length, captured.length);
  assert.equal(native.names.length, item.mappedPhysicalBones);
  const result = {label: item.label, source: item.source, frames: native.frames.length,
    lockedWindowFrames: 0, sides: {}};
  for (const side of ['left', 'right']) result.sides[side] = {
    allMinimumHeightMm: Infinity, lockedMinimumHeightMm: Infinity,
    penetratingLockedFrames: 0, samples: native.frames[0].sole[side].length,
  };
  for (let i = 0; i < captured.length; i++) {
    const original = captured[i], request = input[i], actual = native.frames[i];
    assert.equal(actual.frame, original.Frame);
    assert.equal(request.Frame, original.Frame);
    sameJson(request.Platform, original.Platform, 'Platform input changed');
    for (const field of ['Skeleton', 'Input', 'MotorInput', 'Movement'])
      sameJson(request.RigCapture[field], original.RigCapture[field], field + ' input changed');
    sameJson(request.RigCapture.Stages.PreFoot.curves, original.RigCapture.Stages.PreFoot.curves, 'Curve input changed');
    if (original.Locked) result.lockedWindowFrames++;
    for (const side of ['left', 'right']) {
      const points = actual.sole[side], summary = result.sides[side];
      assert.equal(points.length, summary.samples);
      assert(points.every(p => p.length === 3 && p.every(Number.isFinite)));
      const minimum = Math.min(...points.map(p => p[2])) * 10;
      assert(Math.abs(minimum - actual.sole[side + 'MinimumCm'] * 10) < 1e-8);
      summary.allMinimumHeightMm = Math.min(summary.allMinimumHeightMm, minimum);
      if (!original.Locked) continue;
      assert(actual.feedback.valid && actual.feedback[side].amount > .999,
        'The source substitution did not preserve native lock coverage');
      summary.lockedMinimumHeightMm = Math.min(summary.lockedMinimumHeightMm, minimum);
      if (minimum < -5) summary.penetratingLockedFrames++;
    }
  }
  assert.equal(result.lockedWindowFrames, 120);
  report.cases.push(result);
}
assert.equal(results.length, 2);
assert.deepEqual(results[0].native.names, results[1].native.names);
for (let i = 0; i < captured.length; i++) for (const side of ['left', 'right']) {
  const a = results[0].native.frames[i].sole[side], b = results[1].native.frames[i].sole[side];
  assert.equal(a.length, b.length); // Identical native mesh, LOD, and selected vertices.
  for (let p = 0; p < a.length; p++) report.pairedMaximumSolePointDistanceMm = Math.max(
    report.pairedMaximumSolePointDistanceMm, Math.hypot(...a[p].map((v, j) => v - b[p][j])) * 10);
}
fs.writeFileSync(outputPath, JSON.stringify(report, null, 2), {flag: 'wx'});
console.log(JSON.stringify(report));
