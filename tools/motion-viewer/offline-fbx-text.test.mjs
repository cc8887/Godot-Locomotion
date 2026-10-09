import assert from 'node:assert/strict';
import test from 'node:test';
import { normalizeOfflineFbxIndent } from './js/offline-fbx-text.js';

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
