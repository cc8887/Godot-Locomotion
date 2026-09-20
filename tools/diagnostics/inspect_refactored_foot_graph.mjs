import fs from 'node:fs';
import path from 'node:path';
import { readNativeObjects as objects } from './read_native_objects.mjs';

const [directory, output, environmentOutput, rigOutput] = process.argv.slice(2);
if (!directory || !output) throw new Error('Expected native settings directory and report path.');
const animation = objects(path.join(directory, 'AB_Als.native.txt'));
const root = animation.filter(o => /:AnimGraph\.AnimGraphNode_[^.]+$/.test(o.path)).map(o => ({
  name: o.name, class: o.class,
  properties: o.lines.filter(l => !l.startsWith('CustomProperties') && !l.startsWith('ShowPinForProperties')),
  pins: o.lines.filter(l => l.startsWith('CustomProperties Pin')).map(l => ({
    name: l.match(/PinName="([^"]+)"/)?.[1],
    links: l.match(/LinkedTo=\(([^)]+)\)/)?.[1] ?? '',
  })),
}));
const rig = objects(path.join(directory, 'CR_Als.native.txt'));
const functions = rig.filter(o => /:RigVMFunctionLibrary\.[^.]+$/.test(o.path)).map(o => ({ name: o.name, class: o.class }));
function functionNodes(name) {
const graph = rig.filter(o => o.class === '/Script/RigVMDeveloper.RigVMGraph' && o.path.includes(':RigVMFunctionLibrary.' + name + '.'));
if (graph.length !== 1) throw new Error('Function graph is ambiguous: ' + name);
return graphNodes(graph[0].path); }
function graphNodes(graphPath) {
const prefix = graphPath + '.';
return rig.filter(o => o.path.startsWith(prefix) && !o.path.slice(prefix.length).includes('.')).map(o => ({
    name: o.name, class: o.class, properties: o.lines,
    pins: rig.filter(p => p.path.startsWith(o.path + '.'))
      .map(p => ({ name: p.path.slice(o.path.length + 1), class: p.class, properties: p.lines })),
  })); }
const feet = functionNodes('RefreshFootIk'), applyFoot = functionNodes('ApplyFootIk');
const pelvis = functionNodes('RefreshPelvisOffset'), offsets = functionNodes('RefreshFootOffset'), traceOffsets = functionNodes('TraceFootOffset');
const rigModels = rig.filter(o => o.class === '/Script/RigVMDeveloper.RigVMGraph' && o.path.endsWith(':RigVMModel'));
if (rigModels.length !== 1) throw new Error('Top-level Rig VM graph is ambiguous.');
const rigModel = graphNodes(rigModels[0].path);
const rigVariables = rig.filter(o => !o.path.includes(':')).flatMap(o => o.lines.filter(l => l.startsWith('NewVariables(')));
fs.writeFileSync(output, JSON.stringify({ root, functions, feet, applyFoot, pelvis, offsets, traceOffsets, rigModel, rigVariables }, null, 2) + '\n');
if (environmentOutput) fs.writeFileSync(environmentOutput, JSON.stringify({
  schemaVersion: 1, source: '/ALS/ALS/Character/CR_Als.CR_Als', pelvis, offsets, traceOffsets,
}, null, 2) + '\n');
if (rigOutput) fs.writeFileSync(rigOutput, JSON.stringify({
  schemaVersion: 1, source: '/ALS/ALS/Character/CR_Als.CR_Als', rigModel, rigVariables, feet, applyFoot,
}, null, 2) + '\n');
console.log(JSON.stringify({ root: root.map(n => ({ name: n.name, pins: n.pins })), functions, footNodes: feet.length }));
