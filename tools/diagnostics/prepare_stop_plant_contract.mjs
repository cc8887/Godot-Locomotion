import fs from 'node:fs';
import assert from 'node:assert/strict';
import { readNativeObjects } from './read_native_objects.mjs';

const [nativeDirectory, v4Path, output] = process.argv.slice(2);
assert(nativeDirectory && v4Path && output, 'Expected native Standing directory, V4 graph JSON, fresh request output.');
const objects = readNativeObjects(nativeDirectory + '/refactored-standing.native.txt');
const baked = JSON.parse(fs.readFileSync(nativeDirectory + '/refactored-standing-machines.json', 'utf8'));
const v4 = JSON.parse(fs.readFileSync(v4Path, 'utf8'));
const one = values => { assert.equal(values.length, 1, 'Ambiguous native object or pin'); return values[0]; };
const pins = n => n.lines.filter(l=>l.startsWith('CustomProperties Pin')).map(l=>({
  name:l.match(/PinName="([^"]+)"/)?.[1], value:l.match(/DefaultValue="([^"]*)"/)?.[1],
  links:l.match(/LinkedTo=\(([^)]*)\)/)?.[1] ?? '' }));
const samples = [], plants = [];
for (const side of ['Left','Right']) {
  const graph = one(objects.filter(o=>o.path.includes(':AnimGraph.') && o.path.endsWith('.Plant '+side+' Foot')));
  const children = objects.filter(o=>o.path.startsWith(graph.path+'.') && !o.path.slice(graph.path.length+1).includes('.'));
  const evaluators = children.filter(o=>o.class==='/Script/AnimGraph.AnimGraphNode_SequenceEvaluator');
  assert.equal(evaluators.length,6);
  const layer = one(children.filter(o=>o.class==='/Script/AnimGraph.AnimGraphNode_LayeredBoneBlend'));
  const modify = one(children.filter(o=>o.class==='/Script/AnimGraph.AnimGraphNode_ModifyCurve'));
  plants.push({side,graph:graph.path,layerProperties:layer.lines.filter(l=>l.startsWith('Node=')),
    lockCurve:one(pins(modify).filter(p=>p.name==='CurveValues_0')),
    nodes:children.filter(o=>o.class.startsWith('/Script/AnimGraph.')).map(o=>({
      name:o.name,class:o.class,properties:o.lines.filter(l=>!l.startsWith('CustomProperties') && !l.startsWith('NodePos') && !l.startsWith('ShowPinForProperties')),
      pins:pins(o)}))});
  for (const node of evaluators) {
    const data=one(node.lines.filter(l=>l.startsWith('Node=')));
    assert(data.includes('bUseExplicitFrame=True'));
    const source=data.match(/Sequence="[^']*'([^']+)'"/)?.[1];
    const pin=one(pins(node).filter(p=>p.name==='ExplicitFrame'));
    assert(source?.startsWith('/ALS/') && !pin.links && /^\d+$/.test(pin.value));
    samples.push({version:'Refactored',side,node:node.name,source,mode:'frame',sample:Number(pin.value)});
  }
  const old=one(v4.graphs.filter(g=>g.name==='Plant '+side+' Foot'));
  for(const node of old.nodes.filter(n=>n.class==='AnimGraphNode_SequenceEvaluator')) {
    const pin=one(node.pins.filter(p=>p.name==='ExplicitTime'));
    assert.equal(pin.links.length,0);
    assert.equal(node.properties.Node.bUseExplicitFrame,false);
    samples.push({version:'V4',side,node:node.name,source:node.properties.Node.sequence,mode:'time',sample:Math.fround(Number(pin.value))});
  }
}
assert.equal(samples.length,24);
const stop=one(baked.bakedMachines.filter(m=>m.machineName==='Stop States'));
assert.deepEqual(stop.states.map(s=>s.stateName),['Entry','Lock Left Foot','Lock Right Foot','Plant Left Foot','Plant Right Foot']);
// Editor node suffixes and baked transition indices have different orders.
// Resolve endpoints through links before associating conditions with baked edges.
const stopGraph=one(objects.filter(o=>o.path.includes(':AnimGraph.') && o.path.endsWith('.Stop States')));
const directChildren=parent=>objects.filter(o=>o.path.startsWith(parent.path+'.') && !o.path.slice(parent.path.length+1).includes('.'));
const stopNodes=directChildren(stopGraph);
const boundName=node=>one(node.lines.filter(l=>l.startsWith('BoundGraph='))).match(/'([^']+)'/)?.[1];
const endpoint=(node,pinName)=>{
  const pin=one(pins(node).filter(p=>p.name===pinName));
  const links=pin.links.split(',').map(s=>s.trim()).filter(Boolean);
  assert.equal(links.length,1);
  const state=one(stopNodes.filter(n=>n.name===links[0].split(/\s+/)[0]));
  assert.equal(state.class,'/Script/AnimGraph.AnimStateNode');
  return boundName(state);
};
const editorTransitions=stopNodes.filter(n=>n.class==='/Script/AnimGraph.AnimStateTransitionNode');
assert.equal(editorTransitions.length,4);
const transitions=editorTransitions.map(node=>{
  const from=endpoint(node,'In'),to=endpoint(node,'Out');
  const graph=one(objects.filter(o=>o.path===node.path+'.'+boundName(node)));
  const children=directChildren(graph);
  const result=one(children.filter(o=>o.class==='/Script/AnimGraph.AnimGraphNode_TransitionResult'));
  const resultPin=one(pins(result).filter(p=>p.name==='bCanEnterTransition'));
  const operator=one(children.filter(o=>o.name===resultPin.links.trim().split(/\s+/)[0]));
  assert.equal(operator.class,'/Script/BlueprintGraph.K2Node_PromotableOperator');
  const a=one(pins(operator).filter(p=>p.name==='A'));
  const b=one(pins(operator).filter(p=>p.name==='B'));
  assert.equal(b.links,'');
  const input=one(children.filter(o=>o.name===a.links.trim().split(/\s+/)[0]));
  assert.equal(input.class,'/Script/PropertyAccessNode.K2Node_PropertyAccess');
  const inputPath=input.lines.filter(l=>/^Path\(\d+\)=/.test(l)).map(l=>l.match(/="([^"]+)"/)?.[1]);
  assert.deepEqual(inputPath,['GetParent','FeetState','FootPlantedAmount']);
  const operation=one(operator.lines.filter(l=>l.startsWith('FunctionReference='))).match(/MemberName="([^"]+)"/)?.[1];
  assert(['LessEqual_DoubleDouble','Greater_DoubleDouble'].includes(operation));
  const bakedIndex=stop.transitions.findIndex(t=>stop.states[t.previousState].stateName===from && stop.states[t.nextState].stateName===to);
  assert(bakedIndex>=0);
  const bakedEdge=stop.transitions[bakedIndex];
  const duration=Number(one(node.lines.filter(l=>l.startsWith('CrossfadeDuration='))).split('=')[1]);
  assert.equal(Math.fround(duration),bakedEdge.crossfadeDuration);
  return {node:node.path,from,to,bakedIndex,operation,inputPath,
    // A missing literal is retained explicitly; this diagnostic does not invent
    // an authored value or replace the engine's numeric pin default semantics.
    thresholdLiteral:b.value??null,crossfadeDuration:bakedEdge.crossfadeDuration,
    entryOrder:stop.states[0].transitions.findIndex(t=>t.transitionIndex===bakedIndex)};
}).sort((a,b)=>a.entryOrder-b.entryOrder);
assert.equal(new Set(transitions.map(t=>t.bakedIndex)).size,4);
const functions=objects.filter(o=>/:PlayStop(Left|Right)TransitionAnimation\.[^.]+$/.test(o.path));
const contract={schemaVersion:1,source:baked.source,scope:'Authored stop plant sampling and graph data; does not replace production V4 state rules',
  machine:stop,transitions,plants,functions,samples};
fs.writeFileSync(output,JSON.stringify(contract,null,2)+'\n',{flag:'wx'});
console.log(JSON.stringify({status:'STOP_PLANT_CONTRACT_REQUEST_OK',samples:samples.length,states:stop.states.length,transitions,output}));
