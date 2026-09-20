import fs from 'node:fs';
import path from 'node:path';

const [repository, sourceDirectory, reportPath] = process.argv.slice(2);
if (!repository || !sourceDirectory || !reportPath) throw new Error('Expected repository, native source directory, report.');
const read = name => JSON.parse(fs.readFileSync(path.join(repository, 'assets/config', name), 'utf8'));
const inventory = read('v4_anim_graph_inventory.json');
const native = read('v4_overlay_transition_inputs.json');
const bank = read('v4_movement_source_inputs.json');
const stopIndex = path.join(repository, 'assets/config/v4_stop_source_inputs.json');
const stopBank = fs.existsSync(stopIndex) ? read('v4_stop_source_inputs.json') : null;
const eventGraph = inventory.graphs.find(g => g.name === 'EventGraph');
const nodes = new Map(eventGraph.nodes.map(n => [n.name, n]));
const blocks = new Map();
const stack = [];
for (const line of native.graphs.find(g => g.name === 'EventGraph').nativeText.split(/\r?\n/)) {
  const begin = line.match(/^\s*Begin Object.*?Name="([^"]+)"/);
  if (begin) stack.push({ name: begin[1], lines: [] });
  else if (/^\s*End Object/.test(line)) {
    const item = stack.pop();
    if (item.lines.some(l => l.includes('CustomProperties Pin'))) blocks.set(item.name, item.lines);
  } else if (stack.length) stack.at(-1).lines.push(line);
}
if (stack.length) throw new Error('Unbalanced native graph export.');
const consumers = [];
for (const side of ['L', 'R']) {
  const name = `->N Stop ${side}`;
  const entry = eventGraph.nodes.find(n => n.properties.CustomFunctionName === `AnimNotify_${name}`);
  const links = entry.pins.find(p => p.name === 'then').links;
  if (links.length !== 1) throw new Error('Ambiguous Stop notify execution.');
  const play = nodes.get(links[0].node);
  if (play.properties.FunctionReference.memberName !== 'PlayTransition') throw new Error('Unexpected Stop consumer.');
  const animation = blocks.get(play.name).filter(l => l.includes('PinName="Parameters_Animation_'));
  if (animation.length !== 1) throw new Error('Missing original animation pin.');
  const asset = animation[0].match(/DefaultObject="([^"]+)"/)?.[1];
  if (!asset) throw new Error('Missing original Stop animation object.');
  const parameters = {};
  for (const key of ['BlendInTime', 'BlendOutTime', 'PlayRate', 'StartTime']) {
    const pin = play.pins.find(p => p.name.startsWith(`Parameters_${key}_`));
    if (!pin || pin.links.length || !Number.isFinite(Number(pin.value))) throw new Error('Nonliteral Stop parameter.');
    parameters[key] = Number(pin.value);
  }
  consumers.push({ notify: name, eventNode: entry.name, playNode: play.name, asset, parameters,
    registeredInMovementRawBank: bank.request.rootAssets.some(a => a.source === asset),
    registeredInStopRawBank: stopBank?.request.rootAssets.some(a => a.source === asset) ?? false });
}
const sources = JSON.parse(fs.readFileSync(path.join(sourceDirectory, 'stop-sources.json'), 'utf8'));
if (sources.schemaVersion !== 1 || sources.assets.length !== 4) throw new Error('Missing native Stop asset coverage.');
const pairs = [];
for (let side = 0; side < 2; side++) {
  const v4 = sources.assets[side], refactored = sources.assets[side+2];
  if (v4.version !== 'V4' || refactored.version !== 'Refactored' || v4.length !== refactored.length ||
      v4.frames.length !== refactored.frames.length) throw new Error('Stop source time coverage differs.');
  let maxLockCurveDifference = 0;
  let worstSample = null;
  for (let i = 0; i < v4.frames.length; i++) {
    if (v4.frames[i].time !== refactored.frames[i].time) throw new Error('Unpaired sample time.');
    for (const [a, b] of [['FootLock_L', 'FootLeftLock'], ['FootLock_R', 'FootRightLock']]) {
      const delta = Math.abs(v4.frames[i].curves[a]-refactored.frames[i].curves[b]);
      if (!Number.isFinite(delta)) throw new Error('Missing/nonfinite native lock curve.');
      if (delta > maxLockCurveDifference) {
        maxLockCurveDifference = delta;
        worstSample = { time: v4.frames[i].time, v4Curve: a, refactoredCurve: b,
          v4Value: v4.frames[i].curves[a], refactoredValue: refactored.frames[i].curves[b] };
      }
    }
    for (const asset of [v4, refactored]) for (const pose of Object.values(asset.frames[i].component)) {
      if (!pose.position.every(Number.isFinite) || !pose.rotation.every(Number.isFinite) ||
          Math.abs(Math.hypot(...pose.rotation)-1) > .001) throw new Error('Invalid native component pose.');
    }
  }
  pairs.push({ v4: v4.source, refactored: refactored.source, samples: v4.frames.length,
    maxLockCurveDifference, worstSample, curvesWithinTolerance: maxLockCurveDifference <= 1e-6 });
}
const report = { scope: 'Authored Stop notify consumers and individual native source curves; not full graph/scene parity',
  consumers, pairs, sampleCount: sources.assets.reduce((n, a) => n+a.frames.length, 0) };
fs.writeFileSync(reportPath, JSON.stringify(report, null, 2)+'\n');
console.log(JSON.stringify(report));
