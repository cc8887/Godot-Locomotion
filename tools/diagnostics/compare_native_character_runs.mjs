import fs from 'node:fs';
import assert from 'node:assert/strict';
const [leftPath, rightPath, outputPath] = process.argv.slice(2);
assert(outputPath, 'Usage: compare_native_character_runs.mjs cold.json editor.json report.json');
const read = p => JSON.parse(fs.readFileSync(p, 'utf8'));
const a = read(leftPath), b = read(rightPath);
assert.equal(a.schemaVersion, 1); assert.equal(b.schemaVersion, 1);
assert.equal(a.frames.length, 360); assert.equal(b.frames.length, 360);
assert.deepEqual(a.names.map(n => n.toLowerCase()), b.names.map(n => n.toLowerCase()));
for (const key of Object.keys(a).filter(k => !['names', 'frames'].includes(k))) assert.equal(a[key], b[key], key);
const report = {leftPath, rightPath, frames: a.frames.length, bones: a.names.length, maxima: {}, failures: [], failureCount: 0,
  scope: 'Full native character cold/Editor repeat; FName bone identity, all poses/states/curves, unordered sole geometry; not byte equality or Godot whole-character parity'};
function check(frame, field, error, limit) {
  assert(Number.isFinite(error));
  if (!report.maxima[field] || error > report.maxima[field].error) report.maxima[field] = {frame, error, limit};
  if (error > limit) { report.failureCount++; if (report.failures.length < 12) report.failures.push({frame, field, error, limit}); }
}
const distance = (a, b) => Math.hypot(...a.map((v, i) => v - b[i]));
const normalize = q => {const n = Math.hypot(...q); assert(n > 0); return q.map(v => v / n);};
function pose(frame, field, a, b) {
  check(frame, field + '.positionCm', distance(a.position, b.position), .001);
  const qa = normalize(a.rotation), qb = normalize(b.rotation);
  check(frame, field + '.rotationDegrees', 2 * Math.acos(Math.min(1, Math.abs(qa.reduce((s, v, i) => s + v * qb[i], 0)))) * 180 / Math.PI, .02);
  check(frame, field + '.scale', Math.max(...a.scale.map((v, i) => Math.abs(v - b.scale[i]))), 1e-5);
}
for (let i = 0; i < a.frames.length; i++) {
  const x = a.frames[i], y = b.frames[i], f = i + 1;
  assert.equal(x.frame, f); assert.equal(y.frame, f);
  for (const field of ['worldTime', 'animationUpdateCounter', 'relativeLocation', 'relativeRotation', 'baseChanged', 'onExpectedBase', 'grounded', 'valid'])
    assert.equal(x[field], y[field], `frame ${f} ${field}`);
  for (const field of ['actor', 'mesh', 'platform', 'animationBase']) pose(f, field, x[field], y[field]);
  check(f, 'velocityCmPerSecond', distance(x.velocity, y.velocity), .001);
  for (let bone = 0; bone < a.names.length; bone++) pose(f, 'bone.' + a.names[bone], x.components[bone], y.components[bone]);
  assert.deepEqual(Object.keys(x.curves).sort(), Object.keys(y.curves).sort(), `frame ${f} curve presence`);
  for (const name of Object.keys(x.curves)) check(f, 'curve.' + name, Math.abs(x.curves[name] - y.curves[name]), 1e-4);
  for (const side of ['left', 'right']) {
    check(f, side + '.amount', Math.abs(x[side].amount - y[side].amount), 1e-6);
    for (const field of ['targetWorld', 'worldLock', 'baseLock', 'componentLock', 'finalComponent'])
      pose(f, side + '.' + field, x[side][field], y[side][field]);
    const p = x.sole[side], q = y.sole[side];
    assert.equal(p.length, q.length);
    // Mesh build/load can reorder vertices. Verify the actual point sets in
    // both directions instead of assuming CPU buffer indices are identities.
    const directed = (p, q) => Math.max(...p.map(v => Math.min(...q.map(w => distance(v, w)))));
    check(f, side + '.soleHausdorffCm', Math.max(directed(p, q), directed(q, p)), .01);
    check(f, side + '.soleMinimumCm', Math.abs(x.sole[side + 'MinimumCm'] - y.sole[side + 'MinimumCm']), .01);
  }
}
report.passed = report.failureCount === 0;
fs.writeFileSync(outputPath, JSON.stringify(report, null, 2), {flag: 'wx'});
console.log(JSON.stringify({frames: report.frames, bones: report.bones, passed: report.passed, failureCount: report.failureCount, failures: report.failures}));
if (!report.passed) process.exitCode = 1;
