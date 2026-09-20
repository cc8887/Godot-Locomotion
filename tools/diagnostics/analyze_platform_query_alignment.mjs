import fs from 'node:fs';
import assert from 'node:assert/strict';

// Independent geometry check for MovementPlatformGraphSmoke's unit-scale
// 100 x 1 x 100 box. Reconstruct its top plane from the scene transform,
// without reading the foot solver's expected offsets or output poses.
const [inputPath, outputPath] = process.argv.slice(2);
assert(outputPath, 'Usage: node analyze_platform_query_alignment.mjs capture.json report.json');
const rows = JSON.parse(fs.readFileSync(inputPath, 'utf8'));
assert(rows.length > 2);
rows.forEach((row, i) => assert.equal(row.Frame, i + 1));
const vector = v => [v.X, v.Y, v.Z];
const add = (a, b) => a.map((v, i) => v + b[i]);
const sub = (a, b) => a.map((v, i) => v - b[i]);
const scale = (a, b) => a.map(v => v * b);
const dot = (a, b) => a.reduce((sum, v, i) => sum + v * b[i], 0);
const cross = (a, b) => [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]];
const rotate = (v, q) => {
  const u = vector(q), t = scale(cross(u, v), 2);
  return add(add(v, scale(t, q.W)), cross(u, t));
};
const fromNative = v => [v.Y / 100, v.Z / 100, -v.X / 100];
const passes = [];
for (const lag of [0, 1, 2]) {
  const result = { lag, samples: 0, maximumPlaneErrorMeters: 0,
    maximumRayIntersectionErrorMeters: 0, maximumNormalError: 0, worstPlaneFrame: null };
  for (let i = 2; i < rows.length; i++) {
    const row = rows[i], platform = rows[i - lag].Platform;
    assert(Math.abs(Math.hypot(...vector(platform.Rotation), platform.Rotation.W) - 1) < 1e-6);
    const normal = rotate([0, 1, 0], platform.Rotation);
    const top = add(vector(platform.Position), scale(normal, .5));
    for (const side of ['Left', 'Right']) {
      const request = row.Queries.Request;
      if (!request[side + 'Enabled']) continue;
      const hit = row.Queries.Response[side];
      assert(hit.Blocking, 'Expected the large platform box to cover each enabled query');
      const point = fromNative(hit.Impact), start = fromNative(request[side].Start);
      const delta = sub(fromNative(request[side].End), start);
      const denominator = dot(delta, normal);
      assert(Math.abs(denominator) > 1e-8);
      const t = dot(sub(top, start), normal) / denominator;
      assert(t >= 0 && t <= 1, 'Platform plane lies outside the query segment');
      const expected = add(start, scale(delta, t));
      const q = platform.Rotation;
      const local = rotate(sub(expected, vector(platform.Position)), { X: -q.X, Y: -q.Y, Z: -q.Z, W: q.W });
      assert(Math.abs(local[0]) < 50 && Math.abs(local[2]) < 50,
        'Intersection falls outside the finite platform top face');
      const planeError = Math.abs(dot(sub(point, top), normal));
      const normalError = Math.hypot(...sub([hit.Normal.Y, hit.Normal.Z, -hit.Normal.X], normal));
      const rayError = Math.hypot(...sub(point, expected));
      assert([planeError, normalError, rayError].every(Number.isFinite));
      if (planeError > result.maximumPlaneErrorMeters) {
        result.maximumPlaneErrorMeters = planeError; result.worstPlaneFrame = row.Frame;
      }
      result.maximumNormalError = Math.max(result.maximumNormalError, normalError);
      result.maximumRayIntersectionErrorMeters = Math.max(result.maximumRayIntersectionErrorMeters, rayError);
      result.samples++;
    }
  }
  passes.push(result);
}
const current = passes[0];
assert(current.samples > 0);
const passed = current.maximumPlaneErrorMeters < .0001 &&
  current.maximumRayIntersectionErrorMeters < .0001 && current.maximumNormalError < .00001;
const report = { inputPath, frames: rows.length, passed, passes,
  scope: 'Same-frame box/query alignment; older transforms are diagnostic comparisons, never accepted substitutes' };
fs.writeFileSync(outputPath, JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report));
if (!passed) process.exitCode = 1;
