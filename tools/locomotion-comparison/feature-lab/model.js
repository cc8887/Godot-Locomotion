export const FEATURES = ["foot-lock", "turn", "mantle", "cross-step", "equipment"];

export const ITEMS = Object.freeze({
  none: Object.freeze({ label: "徒手", overlay: "Default", prop: null, lyraLayer: "默认 Linked Layer" }),
  pistol: Object.freeze({ label: "手枪", overlay: "Pistol1H", prop: "M9", lyraLayer: "ABP_PistolAnimLayers" }),
  rifle: Object.freeze({ label: "步枪", overlay: "Rifle", prop: "M4A1", lyraLayer: "武器子 AnimBP" }),
  bow: Object.freeze({ label: "弓", overlay: "Bow", prop: "Bow", lyraLayer: null }),
  torch: Object.freeze({ label: "火把", overlay: "Torch", prop: "Torch", lyraLayer: null }),
  box: Object.freeze({ label: "箱子", overlay: "Box", prop: "Box", lyraLayer: null }),
});

export function clamp01(value) {
  return Math.max(0, Math.min(1, value));
}

export function smoothstep(value) {
  const t = clamp01(value);
  return t * t * (3 - 2 * t);
}

export function alsTurnChoice(yawDegrees, stance = "standing") {
  const magnitude = Math.abs(yawDegrees);
  return {
    direction: yawDegrees < 0 ? "Left" : "Right",
    assetAngle: magnitude < 130 ? 90 : 180,
    stance,
    scaleTurnAngle: stance === "standing",
  };
}

export function lyraTurnPose(targetYawDegrees, turnProgress) {
  const progress = smoothstep(turnProgress);
  return {
    actorYaw: targetYawDegrees,
    rootYawOffset: targetYawDegrees * (progress - 1),
    meshYaw: targetYawDegrees * progress,
  };
}

export function alsCanChangeHips(feetCrossing, sourceStateWeight, hipOrientationBias) {
  return feetCrossing === 0 && sourceStateWeight === 1 && Math.abs(hipOrientationBias) < 0.5;
}

export function alsMantleCheck(heightMeters, clear, approachDegrees = 0, airborne = false) {
  const min = 0.5;
  const max = airborne ? 1.5 : 2.25;
  if (!clear) return "blocked";
  if (Math.abs(approachDegrees) > 110) return "angle";
  if (heightMeters < min || heightMeters > max) return "height";
  return "accepted";
}

export function footWithLock(rootX, sourceLocalX, anchorX, platformX, alpha) {
  const sourceWorld = rootX + sourceLocalX;
  return sourceWorld + (anchorX + platformX - sourceWorld) * clamp01(alpha);
}

export function featureLabel(feature) {
  return ({
    "foot-lock": "锁脚",
    turn: "原地转向",
    mantle: "攀爬",
    "cross-step": "交叉步 / 换髋",
    equipment: "道具动画切换",
  })[feature];
}
