import fs from 'node:fs';
import assert from 'node:assert/strict';

const [inputPath, outputPath] = process.argv.slice(2);
assert(outputPath, 'Usage: analyze_native_character_platform.mjs native-character.json report.json');
const native = JSON.parse(fs.readFileSync(inputPath, 'utf8'));
assert.equal(native.schemaVersion, 1); assert.equal(native.frames.length, 360);
assert.equal(native.locallyControlled, true);
const distance = (a, b) => Math.hypot(...a.map((v, i) => v - b[i]));
const normalize = q => { const n = Math.hypot(...q); assert(n > 0); return q.map(v => v / n); };
const rotationDifference = (a, b) => 2 * Math.acos(Math.min(1, Math.abs(
  normalize(a).reduce((s, v, i) => s + v * normalize(b)[i], 0)))) * 180 / Math.PI;
const tilt = q => { const n = normalize(q); return Math.acos(Math.max(-1, Math.min(1,
  1 - 2 * (n[0] ** 2 + n[1] ** 2)))) * 180 / Math.PI; };
const report = {schemaVersion: 1, inputPath, scope: native.scope, characterClass: native.characterClass,
  meshSource: native.meshSource, animationClass: native.animationClass,
  ignoreBaseRotation: native.ignoreBaseRotation, frames: native.frames.length,
  counts: {valid: 0, grounded: 0, onExpectedBase: 0, relativeRotation: 0},
  maximumBasePositionDifferenceCm: 0, maximumBaseRotationDifferenceDegrees: 0,
  maximumActorTiltDegrees: 0, maximumMeshTiltDegrees: 0, maximumPlatformTiltDegrees: 0, sides: {}};
for (const side of ['left', 'right']) report.sides[side] = {
  samples: native.frames[0].sole[side].length, allMinimumHeightMm: Infinity,
  lateLockedFrames: 0, lateLockedMinimumHeightMm: Infinity, latePenetratingLockedFrames: 0,
};
for (let f = 0; f < native.frames.length; f++) {
  const row = native.frames[f]; assert.equal(row.frame, f + 1);
  assert.equal(row.components.length, native.names.length);
  if (f) assert(row.worldTime > native.frames[f - 1].worldTime);
  assert(Number.isInteger(row.animationUpdateCounter));
  if (f) assert.equal(row.animationUpdateCounter, native.frames[f - 1].animationUpdateCounter + 1,
    'Original animation did not update exactly once per observed frame');
  for (const field of Object.keys(report.counts)) if (row[field]) report.counts[field]++;
  if (row.onExpectedBase && row.relativeLocation) {
    report.maximumBasePositionDifferenceCm = Math.max(report.maximumBasePositionDifferenceCm,
      distance(row.animationBase.position, row.platform.position));
    report.maximumBaseRotationDifferenceDegrees = Math.max(report.maximumBaseRotationDifferenceDegrees,
      rotationDifference(row.animationBase.rotation, row.platform.rotation));
  }
  report.maximumActorTiltDegrees = Math.max(report.maximumActorTiltDegrees, tilt(row.actor.rotation));
  report.maximumMeshTiltDegrees = Math.max(report.maximumMeshTiltDegrees, tilt(row.mesh.rotation));
  report.maximumPlatformTiltDegrees = Math.max(report.maximumPlatformTiltDegrees, tilt(row.platform.rotation));
  for (const side of ['left', 'right']) {
    const points = row.sole[side], summary = report.sides[side];
    assert.equal(points.length, summary.samples); assert(points.every(p => p.every(Number.isFinite)));
    const minimum = Math.min(...points.map(p => p[2])) * 10;
    assert(Math.abs(minimum - row.sole[side + 'MinimumCm'] * 10) < 1e-8);
    summary.allMinimumHeightMm = Math.min(summary.allMinimumHeightMm, minimum);
    // Keep frame 241-360 as the same late observation window, but count only
    // this original character's actual ground/base/valid/full-lock states.
    if (row.frame <= 240 || !row.grounded || !row.onExpectedBase || !row.valid || row[side].amount < .999) continue;
    summary.lateLockedFrames++;
    summary.lateLockedMinimumHeightMm = Math.min(summary.lateLockedMinimumHeightMm, minimum);
    if (minimum < -5) summary.latePenetratingLockedFrames++;
  }
}
assert(report.counts.valid >= 300 && report.counts.grounded >= 300 && report.counts.onExpectedBase >= 300,
  'Incomplete original character coverage');
assert(Object.values(report.sides).every(side => side.lateLockedFrames >= 60), 'Missing original lock window');
assert(report.maximumBasePositionDifferenceCm < .001 && report.maximumBaseRotationDifferenceDegrees < .02,
  'The animation did not read the actual current base');
fs.writeFileSync(outputPath, JSON.stringify(report, null, 2), {flag: 'wx'});
console.log(JSON.stringify(report));
