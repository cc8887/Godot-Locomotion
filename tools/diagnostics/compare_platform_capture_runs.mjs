import fs from 'node:fs';
import assert from 'node:assert/strict';

// Scene object IDs are process-local. Require a stable bijection for nonzero
// base IDs; compare every other field exactly, including all skin samples.
const [leftPath, rightPath, outputPath] = process.argv.slice(2);
assert(outputPath, 'Usage: node compare_platform_capture_runs.mjs left.json right.json report.json');
const read = path => JSON.parse(fs.readFileSync(path, 'utf8'));
const left = read(leftPath), right = read(rightPath);
assert(Array.isArray(left) && left.length > 0 && Array.isArray(right));
assert.equal(left.length, right.length);
const forward = new Map(), reverse = new Map();
const platformForward = new Map(), platformReverse = new Map();
const examples = [];
let differences = 0;
const mismatch = (path, a, b) => {
  differences++;
  if (examples.length < 10) examples.push({ path, left: a, right: b });
};
function compare(a, b, path, key) {
  const motorHit = /^frames\.\d+\.RigCapture\.MotorInput\.(Floor|LeftFootHit|RightFootHit)$/;
  if (motorHit.test(path)) {
    // Motor snapshots were added after the original capture comparator. Their
    // IDs are the SAME scene identity, with an int32 folded platform handle.
    for (const hit of [a, b]) if (hit.PlatformId >= 0) {
      assert(Number.isSafeInteger(hit.ColliderId) && hit.ColliderId > 0);
      const id = BigInt(hit.ColliderId);
      assert.equal(hit.PlatformId, Number((id ^ (id >> 32n)) & 0x7fffffffn), 'Invalid platform/collider identity pair');
    }
  }
  const hitField = motorHit.test(path.slice(0, path.lastIndexOf('.')));
  if (key === 'BaseIdentity' || key === 'ColliderIdentity' || (hitField && (key === 'ColliderId' || key === 'PlatformId'))) {
    assert(Number.isSafeInteger(a) && Number.isSafeInteger(b));
    const platform = key === 'PlatformId';
    const f = platform ? platformForward : forward, r = platform ? platformReverse : reverse;
    if (platform ? a < 0 || b < 0 : a <= 0 || b <= 0) {
      if (a !== b) mismatch(path, a, b);
    } else if ((f.has(a) && f.get(a) !== b) || (r.has(b) && r.get(b) !== a)) {
      mismatch(path, a, b);
    } else { f.set(a, b); r.set(b, a); }
    return;
  }
  if (a === null || b === null || typeof a !== 'object' || typeof b !== 'object') {
    if (a !== b) mismatch(path, a, b);
    return;
  }
  const ak = Object.keys(a).sort(), bk = Object.keys(b).sort();
  if (Array.isArray(a) !== Array.isArray(b) || JSON.stringify(ak) !== JSON.stringify(bk)) {
    mismatch(path + '.keys', ak, bk);
    return;
  }
  for (const k of ak) compare(a[k], b[k], path + '.' + k, k);
}
for (let i = 0; i < left.length; i++) {
  assert.equal(left[i].Frame, i + 1);
  assert.equal(right[i].Frame, i + 1);
  compare(left[i], right[i], 'frames.' + (i + 1), '');
}
const report = { leftPath, rightPath, frames: left.length, differences,
  baseIdentityMapping: [...forward], platformIdentityMapping: [...platformForward], examples, passed: differences === 0,
  scope: 'Exact capture equality modulo stable process-local base object identities; not a locomotion correctness gate' };
fs.writeFileSync(outputPath, JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report));
if (!report.passed) process.exitCode = 1;
