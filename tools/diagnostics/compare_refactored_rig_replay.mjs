import fs from 'node:fs';
import assert from 'node:assert/strict';
const [capturePath, nativePath, outputPath] = process.argv.slice(2);
assert(outputPath, 'Usage: node compare_refactored_rig_replay.mjs capture.json native.json report.json');
const read = path => JSON.parse(fs.readFileSync(path, 'utf8'));
const rows = read(capturePath), native = read(nativePath);
assert.equal(native.schemaVersion, 2); assert.equal(rows.length, native.frames.length);
const names = rows[0].RigCapture.Skeleton.names;
assert.equal(new Set(names).size, names.length);
assert.equal(new Set(native.names).size, native.names.length);
assert.equal(new Set(names.map(name => name.toLowerCase())).size, names.length);
assert.equal(new Set(native.names.map(name => name.toLowerCase())).size, native.names.length);
const mapping = native.names.map(name => { const index = names.indexOf(name); assert(index >= 0, name); return index; });
const required = ['pelvis', 'thigh_l', 'calf_l', 'foot_l', 'thigh_r', 'calf_r', 'foot_r'];
required.forEach(name => assert(native.names.some(value => value.toLowerCase() === name), name));
assert.deepEqual([...native.missingInputNames].sort(), names.filter(name => !native.names.includes(name)).sort());
const maxima = {}, inputMaxima = {}, firstFailures = [], firstInputFailures = [];
let failures = 0, inputFailures = 0, evaluated = 0;
const distance = (a, b) => Math.hypot(...a.map((v, i) => v - b[i]));
const angle = (a, b) => {
  const na = Math.hypot(...a), nb = Math.hypot(...b);
  assert(na > .999 && nb > .999);
  const dot = Math.abs(a.reduce((s, v, i) => s + v * b[i], 0) / na / nb);
  return 2 * Math.acos(Math.min(1, dot)) * 180 / Math.PI;
};
const multiply = (a, b) => [
  a[3]*b[0]+a[0]*b[3]+a[1]*b[2]-a[2]*b[1],
  a[3]*b[1]-a[0]*b[2]+a[1]*b[3]+a[2]*b[0],
  a[3]*b[2]+a[0]*b[1]-a[1]*b[0]+a[2]*b[3],
  a[3]*b[3]-a[0]*b[0]-a[1]*b[1]-a[2]*b[2]];
const normalize = q => { const n = Math.hypot(...q); assert(n > 0); return q.map(v => v/n); };
const compose = (local, parent) => {
  if (!parent) return { ...local, rotation: normalize(local.rotation) };
  const q = parent.rotation, scaled = local.position.map((v, i) => v*parent.scale[i]);
  const rotated = multiply(multiply(q, [...scaled, 0]), [-q[0], -q[1], -q[2], q[3]]);
  return { position: parent.position.map((v,i) => v+rotated[i]),
    rotation: normalize(multiply(q, normalize(local.rotation))), scale: local.scale.map((v,i) => v*parent.scale[i]) };
};
const errorsFor = (a,b) => {
  assert([a,b].every(p => [p.position,p.rotation,p.scale].flat().every(Number.isFinite)));
  return {positionCm: distance(a.position,b.position), rotationDegrees: angle(a.rotation,b.rotation), scale: distance(a.scale,b.scale)};
};
for (let f = 0; f < rows.length; f++) {
  const row = rows[f], actual = native.frames[f];
  assert.equal(row.Frame, f + 1); assert.equal(actual.frame, row.Frame);
  assert.equal(actual.components.length, mapping.length);
  assert.equal(actual.inputComponents.length, mapping.length);
  assert.equal(row.RigCapture.Input.Identity.FrameId, row.Frame);
  const components = [], parents = row.RigCapture.Skeleton.parents;
  assert.equal(parents.length, names.length);
  row.RigCapture.Stages.PreFoot.pose.forEach((local,b) => {
    assert(parents[b] >= -1 && parents[b] < b);
    components.push(compose(local, parents[b] < 0 ? null : components[parents[b]]));
  });
  if (row.RigCapture.Input.FootTransformsValid && row.RigCapture.Input.ExecuteRig) evaluated++;
  for (let b = 0; b < mapping.length; b++) {
    const inputErrors = errorsFor(components[mapping[b]], actual.inputComponents[b]);
    for (const [key,value] of Object.entries(inputErrors))
      if (!inputMaxima[key] || value > inputMaxima[key].value) inputMaxima[key] = {value, frame:row.Frame, bone:native.names[b]};
    if (inputErrors.positionCm > 1e-6 || inputErrors.rotationDegrees > 1e-5 || inputErrors.scale > 1e-10) {
      inputFailures++;
      if (firstInputFailures.length < 12) firstInputFailures.push({frame:row.Frame, bone:native.names[b], ...inputErrors});
    }
    const expected = row.RigCapture.PostRigComponents[mapping[b]], result = actual.components[b];
    const errors = errorsFor(expected, result);
    for (const [key, value] of Object.entries(errors))
      if (!maxima[key] || value > maxima[key].value) maxima[key] = { value, frame: row.Frame, bone: native.names[b] };
    if (errors.positionCm > .001 || errors.rotationDegrees > .02 || errors.scale > 1e-5) {
      failures++;
      if (firstFailures.length < 12) firstFailures.push({ frame: row.Frame, bone: native.names[b], ...errors });
    }
  }
}
assert(evaluated > 0);
const report = { capturePath, nativePath, frames: rows.length, evaluated, comparedBones: mapping.length,
  missingInputNames: native.missingInputNames, inputFailures, firstInputFailures, inputMaxima,
  failures, firstFailures, maxima, passed: failures === 0 && inputFailures === 0,
  scope: native.closedFeedback
    ? 'Continuous original ALS foot state and CR_Als complete VM versus post-rig components; native prior socket/curve history, captured pre-rig animation and movement; not a full character simulation'
    : 'Original CR_Als complete Forwards Solve VM versus post-rig components for every mapped mesh bone; pre-rig pose, lock targets and pelvis allowance are captured inputs, not a closed UE character simulation' };
fs.writeFileSync(outputPath, JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report));
if (!report.passed) process.exitCode = 1;
