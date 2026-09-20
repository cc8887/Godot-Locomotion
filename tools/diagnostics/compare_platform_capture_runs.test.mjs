import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const script = fileURLToPath(new URL('./compare_platform_capture_runs.mjs', import.meta.url));
const folded = id => Number((BigInt(id) ^ (BigInt(id) >> 32n)) & 0x7fffffffn);
const capture = id => [1, 2].map(Frame => ({ Frame, BaseIdentity: id, QueryHit: { ColliderIdentity: id }, Sole: { Y: -.001 },
  RigCapture: { MotorInput: { Floor: { ColliderId: id, PlatformId: folded(id), Normal: { Y: 1 } } } } }));

function run(left, right) {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'als-platform-comparison-'));
  try {
    const a = path.join(directory, 'left.json'), b = path.join(directory, 'right.json');
    const report = path.join(directory, 'report.json');
    fs.writeFileSync(a, JSON.stringify(left)); fs.writeFileSync(b, JSON.stringify(right));
    return spawnSync(process.execPath, [script, a, b, report], { encoding: 'utf8' }).status;
  } finally {
    // Exact directory created by this test only.
    fs.rmSync(directory, { recursive: true });
  }
}

test('stable identities can differ across processes; geometry stays exact', () => {
  assert.equal(run(capture(28940699191), capture(29494347321)), 0);
  const changed = capture(29494347321); changed[1].Sole.Y += .000001;
  assert.equal(run(capture(28940699191), changed), 1);
});
test('base and collider identities share one bijection', () => {
  const changed = capture(29494347321); changed[1].BaseIdentity++;
  assert.equal(run(capture(28940699191), changed), 1);
});
test('folded platform handles must match their own collider', () => {
  const changed = capture(29494347321); changed[1].RigCapture.MotorInput.Floor.PlatformId++;
  assert.equal(run(capture(28940699191), changed), 1);
});
test('missing collider and platform sentinels cannot become a live base', () => {
  const changed = capture(29494347321);
  changed[1].RigCapture.MotorInput.Floor = { ColliderId: -1, PlatformId: -1, Normal: { Y: 1 } };
  assert.equal(run(capture(28940699191), changed), 1);
});
