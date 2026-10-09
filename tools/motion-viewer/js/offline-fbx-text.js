// Three.js FBXLoader's ASCII TextParser counts tab characters as nesting levels.
// Our offline writer emits the same FBX hierarchy with one space per level.
// Normalize only that known producer format; leave binary and third-party FBX bytes intact.
const OFFLINE_CREATOR = 'Creator: "Kimodo ALS offline physical-track writer"';

export function normalizeOfflineFbxIndent(buffer) {
  if (!(buffer instanceof ArrayBuffer) || buffer.byteLength === 0) return buffer;
  const header = new TextDecoder().decode(buffer.slice(0, Math.min(buffer.byteLength, 512)));
  if (!header.startsWith('; FBX') || !header.includes(OFFLINE_CREATOR)) return buffer;

  const text = new TextDecoder().decode(buffer);
  const normalized = text.replace(/^( +)/gm, (indent) => '\t'.repeat(indent.length));
  return new TextEncoder().encode(normalized).buffer;
}
