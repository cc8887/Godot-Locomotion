// Three.js FBXLoader's ASCII TextParser counts tab characters as nesting levels.
// Our offline writer emits the same FBX hierarchy with one space per level.
// Normalize only that known producer format; leave binary and third-party FBX bytes intact.
const OFFLINE_CREATOR = 'Creator: "Kimodo ALS offline physical-track writer"';

export function normalizeOfflineFbxIndent(buffer) {
  if (!(buffer instanceof ArrayBuffer) || buffer.byteLength === 0) return buffer;
  const header = new TextDecoder().decode(buffer.slice(0, Math.min(buffer.byteLength, 512)));
  if (!header.startsWith('; FBX') || !header.includes(OFFLINE_CREATOR)) return buffer;

  const text = new TextDecoder().decode(buffer);
  // TextParser cannot consume an opening and closing brace on one line.
  // In particular, an inline AnimationLayer swallowed the first root model
  // and left the displayed animation in its reference pose.
  const expanded = text.replace(/^( *)([^\r\n{}]+)\{\s*\}[ \t]*$/gm,
    (_, indent, declaration) => `${indent}${declaration}{\n${indent}}`);
  // FBXLoader identifies transform curves by the conventional T/R/S node
  // names; our writer identifies them by bone and channel number instead.
  // Connections retain the model/property identities, so changing only these
  // parser labels does not change the animation data or files on disk.
  const channels = expanded.replace(/(AnimationCurveNode:\s*\d+,\s*)"AnimCurveNode::[^"\r\n]+_([012])"/g,
    (_, declaration, channel) => `${declaration}"AnimCurveNode::${['T','R','S'][Number(channel)]}"`);
  const normalized = channels.replace(/^( +)/gm, (indent) => '\t'.repeat(indent.length));
  return new TextEncoder().encode(normalized).buffer;
}

export function validateOfflineFbxPlayback(object) {
  let root = false;
  object.traverse(node => { root ||= node.isBone && /^root$/i.test(node.name); });
  if (!root || !object.animations?.length || object.animations.some(clip => !clip.tracks.length)) {
    throw new Error('最终 FBX 播放数据不完整：缺少根骨骼或动画轨道');
  }
}
