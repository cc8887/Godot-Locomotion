import fs from 'node:fs';
import path from 'node:path';
import { readNativeObjects } from './read_native_objects.mjs';

const [directory, output, writersOutput] = process.argv.slice(2);
if (!directory || !output) throw new Error('Expected native graph directory and report path.');
const manifest = JSON.parse(fs.readFileSync(path.join(directory, 'sources.json'), 'utf8'));
const sources = manifest.sources.map(source => {
  const objects = readNativeObjects(path.join(directory, source.file));
  const nodes = objects.filter(o => /AnimGraphNode_/.test(o.class)).map(o => ({
    path: o.path, class: o.class,
    properties: o.lines.filter(l => !l.startsWith('CustomProperties') && !l.startsWith('ShowPinForProperties')),
    bindings: objects.filter(b => b.path.startsWith(o.path + '.') && /AnimGraphNodeBinding/.test(b.class))
      .map(b => ({ path: b.path, class: b.class, properties: b.lines })),
    pins: o.lines.filter(l => l.startsWith('CustomProperties Pin')).map(l => ({
      name: l.match(/PinName="([^"]+)"/)?.[1],
      id: l.match(/PinId=([^,]+)/)?.[1],
      links: l.match(/LinkedTo=\(([^)]+)\)/)?.[1] ?? '',
      value: l.match(/DefaultValue="((?:[^"\\]|\\.)*)"/)?.[1] ?? null,
    })),
  }));
  return { source: source.source, nodes };
});
fs.writeFileSync(output, JSON.stringify({ schemaVersion: 1, sources }, null, 2) + '\n');
const pattern = /PoseMoving|PoseGrounded|PoseInAir|FootLeftIk|FootRightIk/;
if (writersOutput) fs.writeFileSync(writersOutput, JSON.stringify({ schemaVersion: 1,
  writers: sources.flatMap(s => s.nodes.filter(n => !n.path.includes(':ExecuteUbergraph') && pattern.test(JSON.stringify(n.properties)))),
}, null, 2) + '\n');
console.log(JSON.stringify(sources.map(s => ({ source: s.source, nodes: s.nodes.length,
  writers: s.nodes.filter(n => !n.path.includes(':ExecuteUbergraph') && pattern.test(JSON.stringify(n.properties))).map(n => ({
    path: n.path, class: n.class, properties: n.properties.filter(p => pattern.test(p)),
    bindings: n.bindings,
    pins: n.pins.filter(p => /Curve|Alpha|Pose|Source/.test(p.name)),
  })),
})), null, 2));
