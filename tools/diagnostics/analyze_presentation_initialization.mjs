import fs from 'node:fs';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

// This fixture has one cold owner and six seconds of uninterrupted animation.
// Preserve every unshown pose in the trace; readiness is not a contact metric.
const [baselinePath, currentPath, outputPath] = process.argv.slice(2);
assert(outputPath, 'Usage: node analyze_presentation_initialization.mjs baseline.json current.json report.json');
const baseline = JSON.parse(fs.readFileSync(baselinePath, 'utf8'));
const current = JSON.parse(fs.readFileSync(currentPath, 'utf8'));
assert.equal(current.length, baseline.length);
assert(current.length >= 180);
for (let i = 0; i < current.length; i++) {
  assert.equal(current[i].Frame, i + 1);
  assert(!Object.hasOwn(baseline[i], 'Presentation'), 'Baseline must precede readiness diagnostics');
  const pending = i === 0;
  assert.deepEqual(current[i].Presentation, {
    PresentationPending: pending, IsVisible: !pending, IsVisualReady: !pending,
  }, `Unexpected visibility at frame ${i + 1}`);
  const input = current[i].RigCapture?.Input;
  assert(input, 'Require the actual production rig input');
  assert.equal(input.ExecuteRig && !input.FootTransformsValid, pending);
}
// Only remove the newly introduced presentation fields. The existing exact
// comparator checks all other fields, normalizing process-local object IDs.
const comparisonInput = outputPath + '.comparison-input.json';
const comparisonReport = outputPath + '.pose-comparison.json';
fs.writeFileSync(comparisonInput, JSON.stringify(current.map(({ Presentation, ...pose }) => pose)));
execFileSync(process.execPath, [fileURLToPath(new URL('./compare_platform_capture_runs.mjs', import.meta.url)),
  baselinePath, comparisonInput, comparisonReport], { stdio: 'pipe' });
const comparison = JSON.parse(fs.readFileSync(comparisonReport, 'utf8'));
assert.equal(comparison.passed, true);
const report = { baselinePath, currentPath, frames: current.length,
  hiddenCommittedFrames: [1], firstVisibleFrame: 2, identicalAnimationAndContact: comparison.passed,
  firstHiddenSole: current[0].Sole.FullSupport, firstVisibleSole: current[1].Sole.FullSupport,
  passed: true, scope: 'Presentation initialization only; unshown initial penetration remains observable, not physically repaired' };
fs.writeFileSync(outputPath, JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report));
