// Compare local poses only after the exporter has converted Godot to UE cm.
// A missing curve is different from a present zero. Do not align time by pose.
import fs from 'node:fs';
import assert from 'node:assert/strict';
const [godotPath, nativePath, reportPath, stage] = process.argv.slice(2);
const [godotStage,nativeStage=godotStage]=stage?.split(':') ?? [];
if (!godotPath || !nativePath || !reportPath) throw Error('Usage: node compare-full-graph-traces.mjs godot.json ue.json report.json');
const godot = JSON.parse(fs.readFileSync(godotPath, 'utf8'));
const native = JSON.parse(fs.readFileSync(nativePath, 'utf8'));
assert.equal(godot.schemaVersion, 1); assert.equal(native.schemaVersion, 1);
const index = new Map(native.names.map((name, i) => [name.toLowerCase(), i]));
assert.equal(index.size, native.names.length); assert.equal(godot.names.length, native.names.length);
const mapping = godot.names.map(name => { assert(index.has(name.toLowerCase()), `Missing bone ${name}`); return index.get(name.toLowerCase()); });
const limits = {positionCm: .001, rotationDegrees: .02, scale: .00001, curve: .0001};
const report = {schemaVersion: 1, inputs: {godotPath, nativePath}, limits, traces: [], frames: 0,
  poseFramesFailed: 0, curveFramesFailed: 0, signedZeroInputEncodings: 0,
  maxPositionCm: 0, maxRotationDegrees: 0, maxScale: 0, maxCurve: 0};
if(stage)report.stage=stage;
function sameInput(a, b, path = 'input') {
  if (typeof a === 'number' && typeof b === 'number') {
    assert(Number.isFinite(a) && a === b, `Different numeric input at ${path}: ${a} != ${b}`);
    // UE's JSON writer emits negative zero as 0. Record that serialization
    // distinction separately; no nonzero numeric input receives a tolerance.
    if (!Object.is(a, b)) report.signedZeroInputEncodings++;
  } else if (a && b && typeof a === 'object' && typeof b === 'object') {
    assert.deepEqual(Object.keys(a).sort(), Object.keys(b).sort(), path);
    for (const key of Object.keys(a)) sameInput(a[key], b[key], `${path}.${key}`);
  } else assert.equal(a, b, path);
}
assert.equal(godot.traces.length, native.traces.length);
for (let t = 0; t < godot.traces.length; t++) {
  const a = godot.traces[t], b = native.traces[t];
  assert.equal(a.name, b.name); assert.equal(a.frames.length, b.frames.length);
  const trace = {name: a.name, frames: a.frames.length, firstPoseFailure: null, firstCurveFailure: null,
    poseFramesFailed: 0, curveFramesFailed: 0};
  for (let f = 0; f < a.frames.length; f++) {
    const x = a.frames[f], y = b.frames[f];
    assert.equal(x.serial, y.serial); sameInput(x.input, y.input);
    if(stage){
      assert(x.stages?.[godotStage] && y.stages?.[nativeStage], `Missing stage ${stage}`);
      assert.equal(y.stages[nativeStage].evaluations,1,'Stage must come from one original evaluation');
      Object.assign(x,x.stages[godotStage]); Object.assign(y,y.stages[nativeStage]);
    }
    assert.equal(x.pose.length, godot.names.length); assert.equal(y.pose.length, native.names.length);
    const boneErrors = [];
    for (let i = 0; i < mapping.length; i++) {
      const p = x.pose[i], q = y.pose[mapping[i]];
      const distance = Math.hypot(...p.position.map((v, j) => v - q.position[j]));
      const dot = Math.abs(p.rotation.reduce((n, v, j) => n + v * q.rotation[j], 0));
      const norm = Math.hypot(...p.rotation) * Math.hypot(...q.rotation);
      const angle = 2 * Math.acos(Math.min(1, dot / norm)) * 180 / Math.PI;
      const scale = Math.max(...p.scale.map((v, j) => Math.abs(v - q.scale[j])));
      assert(Number.isFinite(distance) && Number.isFinite(angle) && Number.isFinite(scale));
      report.maxPositionCm = Math.max(report.maxPositionCm, distance);
      report.maxRotationDegrees = Math.max(report.maxRotationDegrees, angle);
      report.maxScale = Math.max(report.maxScale, scale);
      if (distance > limits.positionCm || angle > limits.rotationDegrees || scale > limits.scale)
        boneErrors.push({bone: godot.names[i], distanceCm: distance, rotationDegrees: angle, scale});
    }
    if (boneErrors.length) {
      trace.poseFramesFailed++; report.poseFramesFailed++;
      trace.firstPoseFailure ??= {serial: x.serial, bones: boneErrors};
    }
    const curveErrors = [];
    for (const name of new Set([...Object.keys(x.curves), ...Object.keys(y.curves)])) {
      const presentA = Object.hasOwn(x.curves, name), presentB = Object.hasOwn(y.curves, name);
      const error = Math.abs((x.curves[name] ?? 0) - (y.curves[name] ?? 0));
      assert(Number.isFinite(error)); report.maxCurve = Math.max(report.maxCurve, error);
      if (presentA !== presentB || error > limits.curve)
        curveErrors.push({name, godot: presentA ? x.curves[name] : null, native: presentB ? y.curves[name] : null, error});
    }
    if (curveErrors.length) {
      trace.curveFramesFailed++; report.curveFramesFailed++;
      trace.firstCurveFailure ??= {serial: x.serial, curves: curveErrors};
    }
    report.frames++;
  }
  report.traces.push(trace);
}
fs.writeFileSync(reportPath, JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify({...report, traces: report.traces.map(t => ({name: t.name,
  firstPoseFrame: t.firstPoseFailure?.serial, firstCurveFrame: t.firstCurveFailure?.serial,
  poseFramesFailed: t.poseFramesFailed, curveFramesFailed: t.curveFramesFailed}))}, null, 2));
process.exitCode = report.poseFramesFailed || report.curveFramesFailed ? 1 : 0;
