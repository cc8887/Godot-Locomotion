import test from "node:test";
import assert from "node:assert/strict";
import {
  ITEMS, alsCanChangeHips, alsMantleCheck, alsTurnChoice,
  footWithLock, lyraTurnPose,
} from "./model.js";

test("ALS V4 turn asset boundary is strict at 130 degrees", () => {
  assert.equal(alsTurnChoice(129.999).assetAngle, 90);
  assert.equal(alsTurnChoice(130).assetAngle, 180);
  assert.equal(alsTurnChoice(-150, "crouching").scaleTurnAngle, false);
  assert.equal(alsTurnChoice(-150).direction, "Left");
});

test("Lyra root yaw offset counters actor yaw until the turn curve releases it", () => {
  const start = lyraTurnPose(110, 0);
  const end = lyraTurnPose(110, 1);
  assert.equal(start.actorYaw + start.rootYawOffset, start.meshYaw);
  assert.equal(start.meshYaw, 0);
  assert.equal(end.rootYawOffset, 0);
  assert.equal(end.meshYaw, 110);
});

test("neutral ALS hip change waits for uncrossed feet and a fully weighted source", () => {
  assert.equal(alsCanChangeHips(1, 1, 0), false);
  assert.equal(alsCanChangeHips(0, 0.99, 0), false);
  assert.equal(alsCanChangeHips(0, 1, 0.5), false);
  assert.equal(alsCanChangeHips(0, 1, -0.49), true);
});

test("ALS mantle gate distinguishes blocked clearance and airborne height range", () => {
  assert.equal(alsMantleCheck(1.35, true), "accepted");
  assert.equal(alsMantleCheck(1.35, false), "blocked");
  assert.equal(alsMantleCheck(2, true, 0, true), "height");
  assert.equal(alsMantleCheck(1, true, 111), "angle");
});

test("world foot anchor follows its platform while partial lock blends from the source", () => {
  for (const root of [-0.4, 0, 0.6, 1.2]) {
    assert.equal(footWithLock(root, -0.2, 0.3, 0.5, 1), 0.8);
    assert.equal(footWithLock(root, -0.2, 0.3, 0.5, 0), root - 0.2);
  }
});

test("ALS props retain distinct overlays and Lyra examples do not invent extra stock layers", () => {
  assert.equal(ITEMS.pistol.overlay, "Pistol1H");
  assert.equal(ITEMS.bow.prop, "Bow");
  assert.equal(ITEMS.bow.lyraLayer, null);
  assert.equal(ITEMS.pistol.lyraLayer, "ABP_PistolAnimLayers");
});
