// All comparison viewports use centimetres, Y-up render space and a fixed
// source origin. Never subtract pelvis(0), animated bounds or root(t).
export const COMPARISON_SPACE = Object.freeze({
  schemaVersion: 1, id: 'fixed-source-origin-v1', units: 'cm',
  renderUp: 'Y', origin: [0, 0, 0], target: [0, 90, 0],
  cameraPosition: [400, 250, 450], verticalSpanCm: 800,
  recenter: false, follow: false, phaseAlignment: 'normalized-time-only',
});

export const isRootBone = (name) => /^(root|__viewer_root__)$/i.test(name);

export function originMetrics(root, pelvis) {
  const horizontal = (a, b) => Math.hypot(a[0] - b[0], a[2] - b[2]);
  return {
    rootCm: root, pelvisCm: pelvis,
    rootOriginHorizontalCm: root ? horizontal(root, [0, 0, 0]) : null,
    pelvisOriginHorizontalCm: pelvis ? horizontal(pelvis, [0, 0, 0]) : null,
    pelvisRootHorizontalCm: root && pelvis ? horizontal(pelvis, root) : null,
  };
}
