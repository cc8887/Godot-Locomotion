import assert from 'node:assert/strict';
import test from 'node:test';
import { normalizeOfflineFbxIndent, validateOfflineFbxPlayback } from './js/offline-fbx-text.js';

const encode = (value) => new TextEncoder().encode(value).buffer;

test('normalizes the offline writer indentation expected by Three FBXLoader', () => {
  const source = encode([
    '; FBX 7.4.0 project file',
    'FBXHeaderExtension: {',
    ' Creator: "Kimodo ALS offline physical-track writer"',
    '}',
    'Connections: {',
    ' C: "OO",11,10',
    '}',
  ].join('\n'));
  const normalized = new TextDecoder().decode(normalizeOfflineFbxIndent(source));
  assert.match(normalized, /\n\tCreator:/);
  assert.match(normalized, /\nConnections: \{\n\tC: "OO",11,10\n\}/);
});

test('leaves other ASCII and binary FBX files untouched', () => {
  const thirdParty = encode('; FBX 7.4.0 project file\n  Objects: {\n    Model: 1\n  }');
  assert.equal(normalizeOfflineFbxIndent(thirdParty), thirdParty);

  const binary = encode('Kaydara FBX Binary  \u0000\u001a\u0000');
  assert.equal(normalizeOfflineFbxIndent(binary), binary);
});

test('inline animation layers close before the first root model', () => {
  const source = encode('; FBX 7.4.0 project file\n Creator: "Kimodo ALS offline physical-track writer"\nObjects: {\n AnimationLayer: 11, "AnimLayer::BaseLayer", "" { }\n Model: 1000, "Model::root", "LimbNode" {\n }\n}');
  const normalized = new TextDecoder().decode(normalizeOfflineFbxIndent(source));
  assert.match(normalized, /AnimationLayer:[^\n]+\{\n\t\}\n\tModel: 1000/);
});

test('producer transform curve labels map to T/R/S without changing connections', () => {
  const source = encode('; FBX 7.4.0 project file\n Creator: "Kimodo ALS offline physical-track writer"\n AnimationCurveNode: 10000, "AnimCurveNode::root_0", "" {\n }\n AnimationCurveNode: 10005, "AnimCurveNode::pelvis_1", "" {\n }\n AnimationCurveNode: 10010, "AnimCurveNode::hand_l_2", "" {\n }\n C: "OP",10000,1000,"Lcl Translation"');
  const normalized = new TextDecoder().decode(normalizeOfflineFbxIndent(source));
  assert.match(normalized, /10000, "AnimCurveNode::T"/);
  assert.match(normalized, /10005, "AnimCurveNode::R"/);
  assert.match(normalized, /10010, "AnimCurveNode::S"/);
  assert.match(normalized, /C: "OP",10000,1000,"Lcl Translation"/);
});

test('reference pose or partial root parsing cannot masquerade as final playback', () => {
  const scene = (hasRoot, tracks) => ({
    traverse(fn) { fn({isBone:hasRoot,name:'root'}); },
    animations:[{tracks}],
  });
  assert.throws(() => validateOfflineFbxPlayback(scene(false,['root.position'])), /播放数据不完整/);
  assert.throws(() => validateOfflineFbxPlayback(scene(true,[])), /播放数据不完整/);
  assert.doesNotThrow(() => validateOfflineFbxPlayback(scene(true,['root.position'])));
});
