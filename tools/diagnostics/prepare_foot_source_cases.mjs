import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';

const [capturePath, auditPath, prefix] = process.argv.slice(2);
assert(prefix && path.isAbsolute(prefix), 'Usage: prepare_foot_source_cases.mjs capture.json audit.json absolute-output-prefix');
const captured = JSON.parse(fs.readFileSync(capturePath, 'utf8'));
const audit = JSON.parse(fs.readFileSync(auditPath, 'utf8'));
assert.equal(audit.schemaVersion, 1);
assert.equal(captured.length, 360);
const skeleton = captured[0].RigCapture.Skeleton;
const key = name => name.toLowerCase(); // Match native FName identity, retaining original spelling.
const targetIndices = new Map(skeleton.names.map((name, i) => [key(name), i]));
assert.equal(targetIndices.size, skeleton.names.length);
const sources = [
  ['v4-stand', '/ALS_N_Pose.ALS_N_Pose'],
  ['refactored-stand', '/A_Als_Stand_Pose.A_Als_Stand_Pose'],
];
const manifestPath = prefix + '-manifest.json';
const outputPaths = sources.flatMap(([label]) => [prefix + '-' + label + '-input.json', prefix + '-' + label + '-native.json']);
assert([manifestPath, ...outputPaths].every(file => !fs.existsSync(file)), 'Fresh paths required');
const manifest = {schemaVersion: 1, capturePath: path.resolve(capturePath), auditPath: path.resolve(auditPath),
  captureSha256: crypto.createHash('sha256').update(fs.readFileSync(capturePath)).digest('hex'),
  auditSha256: crypto.createHash('sha256').update(fs.readFileSync(auditPath)).digest('hex'),
  scope: 'Controlled held non-additive native source pose on the same V4 mesh, captured environment, and curve producers. NOT a complete Refactored AnimBP/Character or production animation replacement.',
  cases: []};
for (const [label, suffix] of sources) {
  const asset = audit.assets.find(a => a.source.endsWith(suffix));
  assert(asset, 'Missing explicit native source');
  assert.equal(asset.metadata.additiveType, 'AAT_None', 'Never treat additive output as a full pose');
  const sample = asset.samples.find(s => s.time === 0);
  assert.equal(sample.animation.evaluatedAdditive, false);
  assert.deepEqual(sample.raw.names, asset.skeleton.logicalBoneNames);
  const mapped = [];
  for (const name of asset.skeleton.rawBoneNames) {
    const source = sample.raw.names.findIndex(n => key(n) === key(name));
    const target = targetIndices.get(key(name));
    assert(source >= 0 && target !== undefined, 'Missing physical bone: ' + name);
    const sourceParent = asset.skeleton.logicalParents[source];
    const targetParent = skeleton.parents[target];
    assert.equal(sourceParent < 0 ? null : key(sample.raw.names[sourceParent]),
      targetParent < 0 ? null : key(skeleton.names[targetParent]), 'Parent mismatch: ' + name);
    mapped.push({source, target});
  }
  assert.equal(mapped.length, 68);
  const rows = captured.map(row => {
    const c = row.RigCapture;
    const pose = structuredClone(c.Stages.PreFoot.pose);
    for (const {source, target} of mapped) pose[target] = sample.raw.pose[source];
    // Only the 68 physical local poses differ. Keep the existing native curve
    // producers and all physical environment history. Unmapped virtual bones
    // remain explicit and are excluded by the existing native mesh replay.
    return {Frame: row.Frame, Platform: row.Platform, RigCapture: {
      Skeleton: c.Skeleton, Input: c.Input, MotorInput: c.MotorInput, Movement: c.Movement,
      Stages: {PreFoot: {pose, curves: c.Stages.PreFoot.curves}},
    }};
  });
  const input = prefix + '-' + label + '-input.json';
  const output = prefix + '-' + label + '-native.json';
  fs.writeFileSync(input, JSON.stringify(rows), {flag: 'wx'});
  manifest.cases.push({label, source: asset.source, heldTimeSeconds: 0, mappedPhysicalBones: mapped.length,
    input, output, closedFeedback: true});
}
fs.writeFileSync(manifestPath, JSON.stringify(manifest, null, 2), {flag: 'wx'});
console.log(JSON.stringify({manifestPath, cases: manifest.cases.length, framesPerCase: captured.length}));
