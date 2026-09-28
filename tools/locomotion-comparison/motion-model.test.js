import test from "node:test";
import assert from "node:assert/strict";
import {
  AUTHORED_DISTANCE,
  CLIP_DURATION,
  MOVING_SMOOTH_SPEED,
  STOP_CASES,
  alsStopBranch,
  distanceCurve,
  distanceMatchedFrame,
  gaitFrame,
  matchDistance,
  phaseOffsetForCase,
  sampleDistanceCurve,
  stopDistance,
  stopTime,
  travelAt,
} from "./motion-model.js";

test("ALS V4 Stop boundaries select the documented Lock and Plant branches", () => {
  for (const [value, branch] of [
    [-0.5, "Lock Left"], [-0.49, "Plant Left"], [0, "Pre-Stop"],
    [0.01, "Plant Right"], [0.49, "Plant Right"], [0.5, "Lock Right"],
  ]) assert.equal(alsStopBranch(value), branch);
});

test("three entry phases share one stop distance while retaining distinct contacts", () => {
  const speed = 5;
  const braking = 4;
  const entry = (speed - MOVING_SMOOTH_SPEED) / braking;
  const expected = {
    cross: [true, true, "Plant Right"],
    left: [true, false, "Lock Left"],
    right: [false, true, "Lock Right"],
  };
  for (const [caseId, [left, right, branch]] of Object.entries(expected)) {
    const gait = gaitFrame(speed, braking, entry, entry, caseId);
    assert.equal(gait.left.contact, left, caseId);
    assert.equal(gait.right.contact, right, caseId);
    assert.equal(alsStopBranch(STOP_CASES[caseId].feetPosition), branch);
    const offset = phaseOffsetForCase(speed, braking, entry, caseId);
    const matched = distanceMatchedFrame(speed, braking, entry, offset);
    assert.ok(Math.abs(matched.left.localMeters - gait.left.localMeters) < 1e-12, caseId);
    assert.ok(Math.abs(matched.right.localMeters - gait.right.localMeters) < 1e-12, caseId);
    assert.equal(matched.left.contact, left, caseId);
    assert.equal(matched.right.contact, right, caseId);
  }
});

test("fixed and phase-adapted stop clips keep each planted foot at one world point", () => {
  const speed = 5;
  const braking = 4;
  const entry = (speed - MOVING_SMOOTH_SPEED) / braking;
  for (const offset of [0.52125, ...Object.keys(STOP_CASES).map((caseId) =>
    phaseOffsetForCase(speed, braking, entry, caseId))]) {
    for (const hz of [30, 60, 120]) {
      const previous = { left: null, right: null };
      let plantedPairs = 0;
      for (let index = Math.ceil(entry * hz); index <= Math.floor(stopTime(speed, braking) * hz); index++) {
        const frame = distanceMatchedFrame(speed, braking, index / hz, offset);
        for (const side of ["left", "right"]) {
          const foot = frame[side];
          const worldPoint = frame.worldMeters + foot.localMeters;
          if (foot.contact && previous[side]?.id === foot.contactId) {
            assert.ok(Math.abs(worldPoint - previous[side].worldPoint) < 1e-12);
            plantedPairs++;
          }
          previous[side] = foot.contact ? { id: foot.contactId, worldPoint } : null;
        }
      }
      assert.ok(plantedPairs > hz / 10);
    }
  }
});

test("baked stop curve is monotonic and its binary inverse returns the requested distance", () => {
  assert.equal(distanceCurve[0].value, -AUTHORED_DISTANCE);
  assert.equal(distanceCurve.at(-1).value, 0);
  for (let index = 1; index < distanceCurve.length; index++) {
    assert.ok(distanceCurve[index].time > distanceCurve[index - 1].time);
    assert.ok(distanceCurve[index].value > distanceCurve[index - 1].value);
  }
  for (let index = 0; index <= 320; index++) {
    const remaining = AUTHORED_DISTANCE * index / 320;
    const time = matchDistance(remaining);
    assert.ok(time >= 0 && time <= CLIP_DURATION);
    assert.ok(Math.abs(sampleDistanceCurve(time) + remaining) < 1e-12);
  }
  assert.equal(matchDistance(AUTHORED_DISTANCE + 0.01), null);
});

for (const [initialSpeed, braking] of [[5, 4], [6, 3], [3, 7], [4.5, 5.5]]) {
  test(`matched root and planted feet follow the world trajectory at ${initialSpeed} m/s, ${braking} m/s²`, () => {
    for (const hz of [30, 60, 120]) {
      const previousContact = new Map();
      let checkedContacts = 0;
      let checkedClipFrames = 0;
      for (let frameIndex = 0; frameIndex <= Math.ceil((stopTime(initialSpeed, braking) + 0.25) * hz); frameIndex++) {
        const time = frameIndex / hz;
        const frame = distanceMatchedFrame(initialSpeed, braking, time);
        assert.ok(Math.abs(frame.worldMeters - travelAt(initialSpeed, braking, time)) < 1e-12);
        if (frame.clipTime !== null) checkedClipFrames++;
        for (const side of ["left", "right"]) {
          const foot = frame[side];
          const worldFootMeters = frame.worldMeters + foot.localMeters;
          const previous = previousContact.get(side);
          if (foot.contact && previous?.id === foot.contactId) {
            assert.ok(Math.abs(worldFootMeters - previous.worldFootMeters) < 1e-12);
            checkedContacts++;
          }
          previousContact.set(side, foot.contact
            ? { id: foot.contactId, worldFootMeters }
            : null);
        }
      }
      assert.ok(checkedContacts > hz / 2);
      assert.ok(checkedClipFrames > hz / 2);
      assert.ok(Math.abs(distanceMatchedFrame(initialSpeed, braking, stopTime(initialSpeed, braking)).worldMeters
        - stopDistance(initialSpeed, braking)) < 1e-12);
    }
  });
}
