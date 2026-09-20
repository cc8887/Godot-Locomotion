import fs from 'node:fs';
import path from 'node:path';

const [basedDirectory, v4Directory, reportPath] = process.argv.slice(2);
if (!basedDirectory || !v4Directory || !reportPath) throw new Error('Expected based capture, V4 capture, and report path.');
function inspect(directory) {
  const frames = JSON.parse(fs.readFileSync(path.join(directory, 'frames.json'), 'utf8'));
  const violations = JSON.parse(fs.readFileSync(path.join(directory, 'violations.json'), 'utf8'));
  if (frames.length !== 720 || frames.some((f, i) => f.Frame !== i + 1)) throw new Error('Incomplete 720-frame capture.');
  const slot = f => f.Standing.TurnSlot.Evaluations.filter(e => e.Slot.Id === 3);
  const active = frames.filter(f => slot(f).length);
  if (!active.length) throw new Error('No physical Grounded Slot playback in capture.');
  const checkpoints = [...new Set([active[0].Frame, 615, 616, 621, 642])];
  return {
    activeFrames: active.length, firstFrame: active[0].Frame, lastFrame: active.at(-1).Frame,
    animationIds: [...new Set(active.flatMap(f => slot(f).map(e => e.AnimationId)))], violations,
    checkpoints: checkpoints.map(i => ({ frame: i, stop: frames[i - 1].Standing.Stop,
      evaluations: slot(frames[i - 1]) })),
  };
}
const report = {
  scope: 'Actual default Overlay stop playback and version-specific rotation guard; not UE scene or contact parity',
  based: inspect(basedDirectory), v4: inspect(v4Directory),
};
fs.writeFileSync(reportPath, JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify({ based: { activeFrames: report.based.activeFrames, firstFrame: report.based.firstFrame,
  animations: report.based.animationIds, violations: report.based.violations },
  v4: { activeFrames: report.v4.activeFrames, firstFrame: report.v4.firstFrame,
    animations: report.v4.animationIds, violations: report.v4.violations } }));
