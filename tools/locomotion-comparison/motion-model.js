export const AUTHORED_DISTANCE = 3.2;
export const CLIP_DURATION = 1.08;
export const MOVING_SMOOTH_SPEED = 1.5;
export const STEP_STRIDE = 0.8;

export const STOP_CASES = Object.freeze({
  cross: Object.freeze({ label: "双脚交叉", feetPosition: 0.18, phaseRoot: 0.40 }),
  left: Object.freeze({ label: "左脚支撑", feetPosition: -0.82, phaseRoot: 0.64 }),
  right: Object.freeze({ label: "右脚支撑", feetPosition: 0.82, phaseRoot: 0.24 }),
});

const CURVE_SAMPLE_RATE = 30;
const SWING_DISTANCE = 0.24;

export function alsStopBranch(feetPosition) {
  if (feetPosition <= -0.5) return "Lock Left";
  if (feetPosition < 0) return "Plant Left";
  if (feetPosition >= 0.5) return "Lock Right";
  if (feetPosition > 0) return "Plant Right";
  return "Pre-Stop";
}

export function gaitFrame(initialSpeed, braking, time, entryTime, caseId) {
  const scenario = STOP_CASES[caseId];
  const rootMeters = scenario.phaseRoot
    + travelAt(initialSpeed, braking, time)
    - travelAt(initialSpeed, braking, entryTime);
  return {
    left: authoredFootPose(rootMeters, -1),
    right: authoredFootPose(rootMeters, 1),
  };
}

export function phaseOffsetForCase(initialSpeed, braking, entryTime, caseId) {
  const matched = distanceMatchedFrame(initialSpeed, braking, entryTime);
  const difference = STOP_CASES[caseId].phaseRoot - matched.rootMeters;
  return ((difference % STEP_STRIDE) + STEP_STRIDE) % STEP_STRIDE;
}

function authoredRootAt(time) {
  const phase = Math.max(0, Math.min(1, time / CLIP_DURATION));
  return AUTHORED_DISTANCE * (2 * phase - phase * phase);
}

// The signed curve has the same convention as UE's stop-distance curves:
// negative distance before the stop target, zero at the final frame.
export const distanceCurve = Object.freeze([
  ...Array.from({ length: Math.ceil(CLIP_DURATION * CURVE_SAMPLE_RATE) }, (_, index) => {
    const time = index / CURVE_SAMPLE_RATE;
    return Object.freeze({ time, value: authoredRootAt(time) - AUTHORED_DISTANCE });
  }),
  Object.freeze({ time: CLIP_DURATION, value: 0 }),
]);

export function sampleDistanceCurve(time) {
  if (time <= 0) return distanceCurve[0].value;
  if (time >= CLIP_DURATION) return 0;
  let low = 0;
  let high = distanceCurve.length - 1;
  while (low + 1 < high) {
    const middle = (low + high) >>> 1;
    if (distanceCurve[middle].time <= time) low = middle;
    else high = middle;
  }
  const before = distanceCurve[low];
  const after = distanceCurve[high];
  const alpha = (time - before.time) / (after.time - before.time);
  return before.value + (after.value - before.value) * alpha;
}

export function matchDistance(remainingMeters) {
  if (remainingMeters > AUTHORED_DISTANCE) return null;
  const target = -Math.max(0, remainingMeters);
  let low = 1;
  let high = distanceCurve.length - 1;
  while (low < high) {
    const middle = (low + high) >>> 1;
    if (distanceCurve[middle].value < target) low = middle + 1;
    else high = middle;
  }
  const before = distanceCurve[low - 1];
  const after = distanceCurve[low];
  const alpha = (target - before.value) / (after.value - before.value);
  return before.time + (after.time - before.time) * alpha;
}

export function stopTime(initialSpeed, braking) {
  return initialSpeed / braking;
}

export function stopDistance(initialSpeed, braking) {
  return initialSpeed * initialSpeed / (2 * braking);
}

export function travelAt(initialSpeed, braking, time) {
  const movingTime = Math.max(0, Math.min(time, stopTime(initialSpeed, braking)));
  return initialSpeed * movingTime - 0.5 * braking * movingTime * movingTime;
}

export function remainingAt(initialSpeed, braking, time) {
  const speed = Math.max(0, initialSpeed - braking * time);
  return speed * speed / (2 * braking);
}

export function authoredFootPose(rootMeters, side) {
  const firstSwing = side < 0 ? 0.12 : 0.52;
  const firstAnchor = side < 0 ? -0.16 : 0.16;
  const stepIndex = Math.floor((rootMeters - firstSwing) / STEP_STRIDE);
  const swingStart = firstSwing + stepIndex * STEP_STRIDE;
  const oldAnchor = firstAnchor + stepIndex * STEP_STRIDE;
  const nextAnchor = oldAnchor + STEP_STRIDE;
  const swingPhase = (rootMeters - swingStart) / SWING_DISTANCE;
  const swinging = swingPhase < 1;
  const eased = swingPhase * swingPhase * (3 - 2 * swingPhase);
  const anchorMeters = swinging
    ? oldAnchor + (nextAnchor - oldAnchor) * eased
    : nextAnchor;
  return {
    localMeters: anchorMeters - rootMeters,
    liftMeters: swinging ? Math.sin(Math.PI * swingPhase) * 0.21 : 0,
    contact: !swinging,
    contactId: swinging ? null : stepIndex + 1,
  };
}

export function distanceMatchedFrame(initialSpeed, braking, time, phaseOffset = 0) {
  const travelMeters = travelAt(initialSpeed, braking, time);
  const remainingMeters = remainingAt(initialSpeed, braking, time);
  const clipTime = matchDistance(remainingMeters);
  const clipOriginMeters = stopDistance(initialSpeed, braking) - AUTHORED_DISTANCE;
  const rootMeters = clipTime === null
    ? travelMeters - clipOriginMeters
    : AUTHORED_DISTANCE + sampleDistanceCurve(clipTime);
  return {
    clipTime,
    rootMeters,
    worldMeters: clipOriginMeters + rootMeters,
    remainingMeters,
    left: authoredFootPose(rootMeters + phaseOffset, -1),
    right: authoredFootPose(rootMeters + phaseOffset, 1),
  };
}
