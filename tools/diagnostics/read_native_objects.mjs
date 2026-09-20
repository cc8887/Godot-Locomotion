import fs from 'node:fs';

// T3D declares objects once and serializes their contents later. Merge by full
// ExportPath, never by short name (nested graphs reuse the same node names).
export function readNativeObjects(file) {
  const stack = [], result = new Map();
  const bytes = fs.readFileSync(file);
  const content = bytes[0] === 0xff && bytes[1] === 0xfe ? bytes.subarray(2).toString('utf16le') : bytes.toString('utf8').replace(/^\uFEFF/, '');
  for (const line of content.split(/\r?\n/)) {
    if (/^\s*Begin Object/.test(line)) {
      stack.push({ path: line.match(/ExportPath="[^']*'([^']+)'"/)?.[1],
        name: line.match(/\bName="([^"]+)"/)?.[1], class: line.match(/\bClass=(\S+)/)?.[1], lines: [] });
    } else if (/^\s*End Object/.test(line)) {
      const item = stack.pop();
      if (!item?.path) throw new Error('Native object identity is missing.');
      const previous = result.get(item.path);
      result.set(item.path, { ...item, class: item.class ?? previous?.class, lines: [...previous?.lines ?? [], ...item.lines] });
    } else if (stack.length) stack.at(-1).lines.push(line.trim());
  }
  if (stack.length) throw new Error('Unbalanced native object export.');
  if (!result.size) throw new Error('No native objects parsed.');
  return [...result.values()];
}
