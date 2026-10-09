import { readFile } from 'node:fs/promises';
import assert from 'node:assert/strict';
import { test } from 'node:test';

// Load the browser's dependency-free geometry contract without needing Node
// package type settings or changing the viewer's CDN importmap.
const source = await readFile(new URL('./js/comparison-space.js', import.meta.url), 'utf8');
const { originMetrics, isRootBone } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));

test('constant pelvis offset remains visible even with zero animation range', () => {
  const m = originMetrics([0, 0, 0], [150, 90, 0]);
  assert.equal(m.pelvisRootHorizontalCm, 150);
  assert.equal(m.pelvisOriginHorizontalCm, 150);
  assert.deepEqual(m.pelvisCm, [150, 90, 0]);
});
test('root travel and body separation remain independently observable', () => {
  const m = originMetrics([300, 0, 400], [303, 95, 404]);
  assert.equal(m.rootOriginHorizontalCm, 500);
  assert.equal(m.pelvisRootHorizontalCm, 5);
});
test('binding alias retains root semantics; missing root is unknown', () => {
  assert.ok(isRootBone('__viewer_root__'));
  assert.ok(isRootBone('Root'));
  assert.equal(isRootBone('ik_foot_root'), false);
  assert.equal(originMetrics(null, [150, 90, 0]).pelvisRootHorizontalCm, null);
});
