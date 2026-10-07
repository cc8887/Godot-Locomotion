// Clip inspection helpers: track breakdown + per-bone displacement ranges.
// These power the "displacement track" and "summary" tabs used for error checks.

function trackKind(name) {
  if (/\.quaternion$/.test(name)) return 'quat';
  if (/\.position$/.test(name) || /\.scale$/.test(name)) return 'vec';
  return 'other';
}

export function analyzeClip(clip) {
  const posTracks = [];
  const quatTracks = [];
  const otherTracks = [];
  let fps = null;
  let frames = 0;
  if (!clip) {
    return { posTracks, quatTracks, otherTracks, fps, frames, animatedNodes: [] };
  }
  const animatedNodes = new Set();
  for (const t of clip.tracks) {
    const kind = trackKind(t.name);
    if (kind === 'quat') quatTracks.push(t);
    else if (kind === 'vec') posTracks.push(t);
    else otherTracks.push(t);
    const dot = t.name.indexOf('.');
    if (dot > 0) animatedNodes.add(t.name.slice(0, dot));
    if (fps === null && t.times.length >= 2) {
      const dt = t.times[1] - t.times[0];
      if (dt > 1e-6) fps = 1 / dt;
    }
    if (t.times.length > frames) frames = t.times.length;
  }
  return {
    posTracks, quatTracks, otherTracks, fps, frames,
    animatedNodes: [...animatedNodes],
  };
}

function acc(rec, axis, v) {
  if (!rec[axis]) rec[axis] = { min: v, max: v };
  else {
    if (v < rec[axis].min) rec[axis].min = v;
    if (v > rec[axis].max) rec[axis].max = v;
  }
}

// Rows: per bone with position tracks -> min/max/range per axis + key count.
export function displacementRows(clip) {
  const map = new Map();
  if (!clip) return [];
  for (const t of clip.tracks) {
    let axis = null;
    let stride = 0;
    if (/\.position$/.test(t.name)) { axis = 'xyz'; stride = 3; }
    else if (/\.position\.[xyz]$/.test(t.name)) { axis = t.name.slice(-1); stride = 1; }
    else continue;
    const dot = t.name.indexOf('.position');
    const bone = t.name.slice(0, dot);
    if (!map.has(bone)) map.set(bone, { x: null, y: null, z: null, keys: 0 });
    const rec = map.get(bone);
    rec.keys = Math.max(rec.keys, t.times.length);
    const vals = t.values;
    if (axis === 'xyz') {
      for (let i = 0; i < t.times.length; i++) {
        acc(rec, 'x', vals[i * 3]);
        acc(rec, 'y', vals[i * 3 + 1]);
        acc(rec, 'z', vals[i * 3 + 2]);
      }
    } else {
      for (let i = 0; i < t.times.length; i++) acc(rec, axis, vals[i]);
    }
  }
  const rows = [...map.entries()].map(([bone, r]) => {
    const rangeOf = (a) => (r[a] ? Math.abs(r[a].max - r[a].min) : 0);
    return {
      bone,
      keys: r.keys,
      x: r.x, y: r.y, z: r.z,
      rangeX: rangeOf('x'), rangeY: rangeOf('y'), rangeZ: rangeOf('z'),
      moving: rangeOf('x') > 0.01 || rangeOf('y') > 0.01 || rangeOf('z') > 0.01,
    };
  });
  rows.sort((a, b) => (b.rangeX + b.rangeY + b.rangeZ) - (a.rangeX + a.rangeY + a.rangeZ));
  return rows;
}

export function fmtNum(v, digits = 1) {
  if (v === null || v === undefined || Number.isNaN(v)) return '—';
  return v.toFixed(digits);
}

export function humanSize(bytes) {
  if (bytes >= 1024 * 1024) return (bytes / 1024 / 1024).toFixed(2) + ' MB';
  if (bytes >= 1024) return (bytes / 1024).toFixed(1) + ' KB';
  return bytes + ' B';
}
