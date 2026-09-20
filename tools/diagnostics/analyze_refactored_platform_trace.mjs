import fs from 'node:fs';

const paths = process.argv.slice(2);
if (paths.length === 0) throw new Error('Pass one or more platform trace JSON paths.');
const sub = (a, b) => ({ X: a.X-b.X, Y: a.Y-b.Y, Z: a.Z-b.Z });
const length = v => Math.hypot(v.X, v.Y, v.Z);
const scaled = (v, k) => ({ X: v.X*k, Y: v.Y*k, Z: v.Z*k });
for (const path of paths) {
  const rows = JSON.parse(fs.readFileSync(path, 'utf8'));
  const maxima = {};
  const record = (key, delta, row, side) => {
    const error = length(delta);
    if (!(key in maxima) || maxima[key].meters < error)
      maxima[key] = { meters: error, delta, frame: row.Frame, side };
  };
  let start;
  for (const row of rows) {
    if (!row.Locked) { start = undefined; continue; }
    start ??= row;
    for (const side of ['Left', 'Right']) {
      const leg = row.RefactoredRig[side];
      const lock = row.RefactoredLocks[side];
      record('physical_drift', sub(row[side], start[side]), row, side);
      record('rig_drift', sub(row['Rig'+side], start['Rig'+side]), row, side);
      record('write_error', sub(row['World'+side], row['RigWorld'+side]), row, side);
      record('solver_error', scaled(sub(row.RefactoredRigFeet[side].Position, leg.Location.FootLocation), .01), row, side);
      const horizontal = sub(leg.Location.FootLocation, lock.FinalComponent.Position);
      horizontal.Z = 0;
      record('location_horizontal', scaled(horizontal, .01), row, side);
    }
  }
  const samples = rows.filter(r => [241, 260, 300, 360].includes(r.Frame)).map(row => ({
    frame: row.Frame, physical: row.Left, rig: row.RigLeft,
    offset: row.RefactoredRig.LeftOffset, spring: row.RefactoredRig.Left.Location,
    pelvis: row.RefactoredRig.PelvisOffset, footWeight: row.LeftFootIkWeight,
    locked: row.RefactoredLocks.Left.FinalComponent.Position,
  }));
  process.stdout.write(JSON.stringify({ path, frames: rows.length, maxima, samples }, null, 2)+'\n');
}
