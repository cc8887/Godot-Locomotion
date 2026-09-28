import * as THREE from "three";
import {
  CLIP_DURATION,
  MOVING_SMOOTH_SPEED,
  STOP_CASES,
  alsStopBranch,
  distanceMatchedFrame,
  gaitFrame,
  phaseOffsetForCase,
  remainingAt,
  stopTime as modelStopTime,
  travelAt as modelTravelAt,
} from "./motion-model.js";

const $ = (id) => document.getElementById(id);
const elements = {
  play: $("play-button"), reset: $("reset-button"), scrub: $("time-scrub"),
  time: $("time-output"), initialSpeed: $("initial-speed"), braking: $("braking"),
  initialSpeedValue: $("initial-speed-value"), brakingValue: $("braking-value"),
  speed: $("speed-readout"), distance: $("distance-readout"),
  alsTime: $("als-time-readout"), dmTime: $("dm-time-readout"),
  alsState: $("als-state"), dmState: $("dm-state"),
  alsFoot: $("als-foot"), dmFoot: $("dm-foot"),
  feetPosition: $("feet-position-readout"), alsBranch: $("als-branch-readout"),
  phaseJump: $("phase-jump-readout"), phaseResult: $("phase-result"),
  caseExplanation: $("case-explanation"),
  alsCallout: $("als-callout"), dmCallout: $("dm-callout"),
  phaseButtons: [...document.querySelectorAll("[data-stop-case]")],
  modeButtons: [...document.querySelectorAll("[data-phase-mode]")],
};

const START_X = -4.5;
const SCENE_UNITS_PER_METER = 1.45;
// Authored right-support phase aligned to the default 5 m/s, 4 m/s² example.
const FIXED_CLIP_PHASE_OFFSET = 0.52125;
const UP = new THREE.Vector3(0, 1, 0);
const tmpDirection = new THREE.Vector3();
const clock = new THREE.Clock();

let initialSpeed = Number(elements.initialSpeed.value);
let braking = Number(elements.braking.value);
let elapsed = 0;
let playing = !window.matchMedia("(prefers-reduced-motion: reduce)").matches;
let stopCase = "cross";
let phaseMode = "distance";

function clamp(value, min, max) { return Math.max(min, Math.min(max, value)); }
function stopTime() { return modelStopTime(initialSpeed, braking); }
function duration() { return stopTime() + 0.65; }
function stopEntryTime() { return Math.max(0, (initialSpeed - MOVING_SMOOTH_SPEED) / braking); }
function travelAt(time) { return modelTravelAt(initialSpeed, braking, time); }
function sceneX(time) { return START_X + travelAt(time) * SCENE_UNITS_PER_METER; }

function material(color, roughness = 0.72, metalness = 0.04) {
  return new THREE.MeshStandardMaterial({ color, roughness, metalness });
}
function cylinderBetween(mesh, a, b) {
  tmpDirection.subVectors(b, a);
  mesh.position.copy(a).add(b).multiplyScalar(0.5);
  mesh.quaternion.setFromUnitVectors(UP, tmpDirection.clone().normalize());
  mesh.scale.y = tmpDirection.length();
}

function makeMannequin(scene, palette) {
  const root = new THREE.Group();
  scene.add(root);
  const suit = material(0x273a3c, 0.58);
  const limb = material(0x405458, 0.7);
  const skin = material(0xe2c6b3, 0.9);
  const accent = material(palette.accent, 0.48);
  const shoes = { [-1]: material(0x0b8f7b, 0.58), [1]: material(0xe9863e, 0.58) };

  const torso = new THREE.Mesh(new THREE.CapsuleGeometry(0.32, 0.52, 6, 12), suit);
  torso.position.y = 1.51;
  torso.rotation.z = -0.12;
  root.add(torso);
  const chest = new THREE.Mesh(new THREE.BoxGeometry(0.11, 0.48, 0.44), accent);
  chest.position.set(0.31, 1.56, 0);
  root.add(chest);
  const hips = new THREE.Mesh(new THREE.SphereGeometry(0.31, 12, 10), suit);
  hips.scale.set(1.1, 0.64, 0.85);
  hips.position.y = 1.06;
  root.add(hips);
  const head = new THREE.Mesh(new THREE.SphereGeometry(0.26, 18, 12), skin);
  head.position.set(0.1, 2.19, 0);
  root.add(head);
  const hair = new THREE.Mesh(new THREE.SphereGeometry(0.267, 18, 12, 0, Math.PI * 2, 0, Math.PI * 0.48), suit);
  hair.position.copy(head.position);
  root.add(hair);
  const visor = new THREE.Mesh(new THREE.BoxGeometry(0.055, 0.09, 0.29), suit);
  visor.position.set(0.34, 2.2, 0);
  root.add(visor);

  const limbs = [-1, 1].map((side) => {
    const upperLeg = new THREE.Mesh(new THREE.CylinderGeometry(0.115, 0.13, 1, 10), limb);
    const lowerLeg = new THREE.Mesh(new THREE.CylinderGeometry(0.085, 0.103, 1, 10), limb);
    const knee = new THREE.Mesh(new THREE.SphereGeometry(0.11, 10, 8), suit);
    const foot = new THREE.Mesh(new THREE.BoxGeometry(0.45, 0.15, 0.26), shoes[side]);
    const upperArm = new THREE.Mesh(new THREE.CylinderGeometry(0.073, 0.084, 1, 9), limb);
    const lowerArm = new THREE.Mesh(new THREE.CylinderGeometry(0.064, 0.072, 1, 9), limb);
    const elbow = new THREE.Mesh(new THREE.SphereGeometry(0.075, 9, 7), suit);
    const hand = new THREE.Mesh(new THREE.SphereGeometry(0.08, 10, 8), skin);
    const shoulder = new THREE.Mesh(new THREE.SphereGeometry(0.135, 10, 8), accent);
    root.add(upperLeg, lowerLeg, knee, foot, upperArm, lowerArm, elbow, hand, shoulder);
    return { side, upperLeg, lowerLeg, knee, foot, upperArm, lowerArm, elbow, hand, shoulder };
  });
  root.traverse((object) => { if (object.isMesh) object.castShadow = true; });

  function pose(position, left, right, sway = 0) {
    root.position.set(position, 0, 0);
    torso.rotation.z = -0.12 + sway * 0.08;
    for (const part of limbs) {
      const data = part.side < 0 ? left : right;
      const z = data.z ?? part.side * 0.19;
      const hip = new THREE.Vector3(0, 1.09, z);
      const footPoint = new THREE.Vector3(data.x, 0.115 + data.lift, z);
      const knee = new THREE.Vector3(0.12 + data.x * 0.46, 0.58 + data.lift * 0.3, z);
      cylinderBetween(part.upperLeg, hip, knee);
      cylinderBetween(part.lowerLeg, knee, footPoint);
      part.knee.position.copy(knee);
      part.foot.position.set(data.x + 0.115, 0.075 + data.lift, z);
      part.foot.rotation.z = data.lift * -0.35;
      const shoulder = new THREE.Vector3(0, 1.8, part.side * 0.38);
      const armSwing = -data.x * 0.44;
      const elbow = new THREE.Vector3(0.10 + armSwing, 1.38, part.side * 0.46);
      const hand = new THREE.Vector3(0.17 + armSwing * 1.4, 1.13, part.side * 0.47);
      cylinderBetween(part.upperArm, shoulder, elbow);
      cylinderBetween(part.lowerArm, elbow, hand);
      part.shoulder.position.copy(shoulder);
      part.elbow.position.copy(elbow);
      part.hand.position.copy(hand);
    }
  }

  return { pose };
}

function line(scene, points, color, opacity = 1) {
  const geometry = new THREE.BufferGeometry().setFromPoints(points.map((p) => new THREE.Vector3(...p)));
  const material = new THREE.LineBasicMaterial({ color, transparent: opacity < 1, opacity });
  const object = new THREE.Line(geometry, material);
  scene.add(object);
  return object;
}

function makeStage(canvas, palette) {
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true, powerPreference: "high-performance" });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;

  const scene = new THREE.Scene();
  const camera = new THREE.OrthographicCamera(-6.5, 6.5, 3.9, -3.9, 0.1, 100);
  camera.position.set(3, 3.5, 10);
  camera.lookAt(0.6, 0.98, 0);
  scene.add(new THREE.HemisphereLight(0xffffff, 0x8a9c96, 2.0));
  const key = new THREE.DirectionalLight(0xffffff, 3.0);
  key.position.set(-3, 8, 6);
  key.castShadow = true;
  key.shadow.mapSize.set(1024, 1024);
  key.shadow.camera.left = -7;
  key.shadow.camera.right = 7;
  key.shadow.camera.top = 5;
  key.shadow.camera.bottom = -5;
  scene.add(key);

  const floor = new THREE.Mesh(new THREE.PlaneGeometry(23, 24), material(palette.floor, 0.95));
  floor.rotation.x = -Math.PI / 2;
  floor.position.y = -0.02;
  floor.receiveShadow = true;
  scene.add(floor);
  for (let x = -11; x <= 11; x += 1) line(scene, [[x, 0.005, -12], [x, 0.005, 12]], 0xb8c7c1, 0.29);
  for (let z = -12; z <= 12; z += 1) line(scene, [[-11.5, 0.006, z], [11.5, 0.006, z]], 0xb8c7c1, 0.29);
  line(scene, [[-5.3, 0.012, 0], [5.4, 0.012, 0]], palette.accent, 0.64);

  const marker = new THREE.Group();
  const markerMaterial = new THREE.MeshStandardMaterial({ color: palette.accent, transparent: true, opacity: 0.62, roughness: 0.5 });
  const ring = new THREE.Mesh(new THREE.TorusGeometry(0.48, 0.035, 8, 48), markerMaterial);
  ring.rotation.x = Math.PI / 2;
  ring.position.y = 0.025;
  const upright = new THREE.Mesh(new THREE.CylinderGeometry(0.018, 0.018, 1.2, 8), markerMaterial);
  upright.position.set(0, 0.61, -0.85);
  const cap = new THREE.Mesh(new THREE.SphereGeometry(0.075, 10, 8), markerMaterial);
  cap.position.set(0, 1.23, -0.85);
  marker.add(ring, upright, cap);
  scene.add(marker);

  const progress = new THREE.Mesh(new THREE.BoxGeometry(1, 0.018, 0.055), material(palette.accent, 0.5));
  progress.position.set(START_X, 0.026, 0);
  scene.add(progress);
  const figure = makeMannequin(scene, palette);
  const contactMarkers = [-1, 1].map((side) => {
    const contact = new THREE.Mesh(
      new THREE.RingGeometry(0.16, 0.23, 28),
      new THREE.MeshBasicMaterial({ color: side < 0 ? 0x0b8f7b : 0xe9863e, transparent: true, opacity: 0.95, side: THREE.DoubleSide }),
    );
    contact.rotation.x = -Math.PI / 2;
    contact.position.set(0, 0.014, side * 0.19);
    scene.add(contact);
    return contact;
  });
  const entryMarkers = [-1, 1].map((side) => {
    const marker = new THREE.Mesh(
      new THREE.RingGeometry(0.24, 0.27, 28),
      new THREE.MeshBasicMaterial({ color: side < 0 ? 0x0b8f7b : 0xe9863e, transparent: true, opacity: 0.42, side: THREE.DoubleSide }),
    );
    marker.rotation.x = -Math.PI / 2;
    marker.position.y = 0.016;
    scene.add(marker);
    return marker;
  });

  function resize() {
    const width = Math.max(1, canvas.clientWidth);
    const height = Math.max(1, canvas.clientHeight);
    renderer.setSize(width, height, false);
    const aspect = width / height;
    const visibleWidth = aspect < 1.2 ? 5.5 : 7;
    const visibleHeight = visibleWidth / aspect;
    camera.left = -visibleWidth / 2;
    camera.right = visibleWidth / 2;
    camera.top = visibleHeight / 2;
    camera.bottom = -visibleHeight / 2;
    camera.updateProjectionMatrix();
  }
  new ResizeObserver(resize).observe(canvas);
  resize();

  function update(position, target, left, right, sway, entryFeet) {
    figure.pose(position, left, right, sway);
    for (const [index, foot] of [left, right].entries()) {
      contactMarkers[index].visible = foot.contact;
      contactMarkers[index].position.x = position + foot.x;
      contactMarkers[index].position.z = foot.z ?? (index === 0 ? -0.19 : 0.19);
      entryMarkers[index].visible = Boolean(entryFeet);
      if (entryFeet) {
        entryMarkers[index].position.x = entryFeet.position + entryFeet.feet[index].x;
        entryMarkers[index].position.z = entryFeet.feet[index].z ?? (index === 0 ? -0.19 : 0.19);
      }
    }
    marker.position.x = target;
    camera.position.x = position + 3;
    camera.lookAt(position + 0.6, 0.98, 0);
    const length = Math.max(0.001, position - START_X);
    progress.scale.x = length;
    progress.position.x = START_X + length / 2;
    renderer.render(scene, camera);
  }
  return { update, renderer, canvas };
}

const alsStage = makeStage($("als-canvas"), { accent: 0x138a76, floor: 0xdcebe5 });
const dmStage = makeStage($("dm-canvas"), { accent: 0xd4744c, floor: 0xeee2db });

function sceneFoot(foot) {
  return {
    x: foot.localMeters * SCENE_UNITS_PER_METER,
    lift: foot.liftMeters * SCENE_UNITS_PER_METER * 1.8,
    contact: foot.contact,
  };
}
function gaitFeet(time, entry) {
  const frame = gaitFrame(initialSpeed, braking, time, entry, stopCase);
  return [sceneFoot(frame.left), sceneFoot(frame.right)].map((foot, index) => ({
    ...foot,
    z: stopCase === "cross" ? (index === 0 ? 0.22 : -0.22) : (index === 0 ? -0.19 : 0.19),
  }));
}
function stopFoot(time, entry, side, branch) {
  const entryX = sceneX(entry);
  const currentX = sceneX(time);
  const phase = clamp((time - entry) / Math.max(0.001, stopTime() - entry), 0, 1);
  const start = gaitFeet(entry, entry)[side < 0 ? 0 : 1];
  const supportSide = branch.endsWith("Left") ? -1 : 1;
  const isPlant = branch.startsWith("Plant");
  if (side === supportSide && !isPlant) {
    return { x: entryX + start.x - currentX, lift: 0, contact: true, z: start.z };
  }
  if (isPlant && side !== supportSide && phase < 0.64) {
    return { x: entryX + start.x - currentX, lift: 0, contact: true, z: start.z };
  }
  const localPhase = isPlant && side !== supportSide ? (phase - 0.64) / 0.36 : isPlant ? phase / 0.64 : phase;
  const progress = clamp(localPhase, 0, 1);
  const eased = progress * progress * (3 - 2 * progress);
  const finalX = sceneX(stopTime()) + side * 0.23;
  return {
    x: entryX + start.x + (finalX - entryX - start.x) * eased - currentX,
    lift: Math.sin(Math.PI * progress) * (isPlant && side === supportSide ? 1.05 : 0.72),
    contact: progress >= 1,
    z: start.z + (side * 0.19 - start.z) * eased,
  };
}

function contactText(left, right) {
  if (left.contact && right.contact) return "双脚接触";
  if (left.contact) return "左脚接触 · 右脚摆动";
  if (right.contact) return "右脚接触 · 左脚摆动";
  return "双脚摆动";
}

function render() {
  const t = Math.min(elapsed, stopTime());
  const speed = Math.max(0, initialSpeed - braking * t);
  const remaining = remainingAt(initialSpeed, braking, t);
  const position = sceneX(t);
  const target = sceneX(stopTime());
  const entry = stopEntryTime();
  const scenario = STOP_CASES[stopCase];
  const branch = alsStopBranch(scenario.feetPosition);
  const entryFeet = gaitFeet(entry, entry);
  const entrySnapshot = { position: sceneX(entry), feet: entryFeet };

  let alsLeft, alsRight;
  if (t < entry) {
    [alsLeft, alsRight] = gaitFeet(t, entry);
    elements.alsState.textContent = "移动循环";
    elements.alsFoot.textContent = contactText(alsLeft, alsRight);
  } else {
    alsLeft = stopFoot(t, entry, -1, branch);
    alsRight = stopFoot(t, entry, 1, branch);
    elements.alsState.textContent = speed > 0.01 ? `Stop · ${branch}` : "Not Moving · 静止";
    elements.alsFoot.textContent = branch.startsWith("Plant") ? "先补脚，再转移支撑" : `${branch.endsWith("Left") ? "左脚" : "右脚"}锁定`;
  }
  alsStage.update(position, target, alsLeft, alsRight, speed / initialSpeed, t >= entry ? entrySnapshot : null);

  const phaseOffset = phaseMode === "adapted"
    ? phaseOffsetForCase(initialSpeed, braking, entry, stopCase)
    : FIXED_CLIP_PHASE_OFFSET;
  const matched = distanceMatchedFrame(initialSpeed, braking, t, phaseOffset);
  const matchedEntry = distanceMatchedFrame(initialSpeed, braking, entry, phaseOffset);
  const entryDmFeet = [sceneFoot(matchedEntry.left), sceneFoot(matchedEntry.right)];
  const jumpCm = Math.max(...entryDmFeet.map((foot, index) => Math.hypot(
    foot.x - entryFeet[index].x,
    (foot.lift - entryFeet[index].lift) / 1.8,
    (phaseMode === "distance" && stopCase === "cross")
      ? (index === 0 ? -0.19 : 0.19) - entryFeet[index].z : 0,
  ) / SCENE_UNITS_PER_METER * 100));
  let dmLeft, dmRight;
  if (t < entry) {
    [dmLeft, dmRight] = gaitFeet(t, entry);
    elements.dmState.textContent = "移动循环 · 保留原相位";
  } else {
    [dmLeft, dmRight] = [sceneFoot(matched.left), sceneFoot(matched.right)];
    if (phaseMode === "adapted" && stopCase === "cross") {
      dmLeft.z = 0.22;
      dmRight.z = -0.22;
    }
    elements.dmState.textContent = speed > 0.01 ? "距离曲线反查" : "片段末帧 · 静止";
  }
  dmStage.update(position, target, dmLeft, dmRight, speed / initialSpeed, t >= entry ? entrySnapshot : null);

  elements.speed.textContent = `${speed.toFixed(2)} m/s`;
  elements.distance.textContent = `${remaining.toFixed(2)} m`;
  elements.alsTime.textContent = t < entry ? "未进入" : `${Math.max(0, t - entry).toFixed(2)} s`;
  elements.dmTime.textContent = matched.clipTime === null
    ? "未进入"
    : `${matched.clipTime.toFixed(2)} s / ${CLIP_DURATION.toFixed(2)} s`;
  elements.feetPosition.textContent = `${scenario.feetPosition > 0 ? "+" : ""}${scenario.feetPosition.toFixed(2)}`;
  elements.alsBranch.textContent = branch;
  elements.phaseJump.textContent = `${(phaseMode === "adapted" ? 0 : jumpCm).toFixed(0)} cm`;
  elements.phaseResult.textContent = phaseMode === "adapted"
    ? "教学假设：先取得同相位片段，再做距离反查"
    : jumpCm > 15
      ? "固定片段与入口相位冲突；距离相同仍会换脚"
      : "此入口与固定片段接近；距离匹配不会自行选脚";
  elements.caseExplanation.textContent = stopCase === "cross"
    ? "交叉时 p = +0.18：右脚侧尚未压实，ALS 走 Plant Right；固定距离片段会跳到自己的脚位。"
    : stopCase === "left"
      ? "左脚承重且 p = −0.82：ALS 锁左脚；同一距离不会告诉固定片段应保留左脚。"
      : "右脚承重且 p = +0.82：ALS 锁右脚；固定片段若刚好同相位，入口冲突就小。";
  elements.alsCallout.textContent = t < entry ? contactText(alsLeft, alsRight)
    : branch.startsWith("Plant") ? "左脚撑住 · 右脚跨步" : `${branch.endsWith("Left") ? "左" : "右"}脚世界点固定`;
  elements.dmCallout.textContent = t < entry ? contactText(dmLeft, dmRight)
    : phaseMode === "adapted" ? "片段相位已适配" : `固定片段 · ${contactText(dmLeft, dmRight)}`;
  elements.time.textContent = `${elapsed.toFixed(2)} s`;
  elements.scrub.value = Math.round(elapsed / duration() * 1000);
  elements.dmFoot.textContent = contactText(dmLeft, dmRight);
}

function setPlaying(next) {
  playing = next;
  elements.play.textContent = playing ? "暂停" : "播放";
  elements.play.setAttribute("aria-label", playing ? "暂停动画" : "播放动画");
}
elements.play.addEventListener("click", () => {
  if (!playing && elapsed >= duration()) elapsed = 0;
  setPlaying(!playing);
  render();
});
elements.reset.addEventListener("click", () => { elapsed = 0; setPlaying(false); render(); });
elements.scrub.addEventListener("input", () => {
  elapsed = Number(elements.scrub.value) / 1000 * duration();
  setPlaying(false);
  render();
});
function updateParameters() {
  initialSpeed = Number(elements.initialSpeed.value);
  braking = Number(elements.braking.value);
  elements.initialSpeedValue.textContent = `${initialSpeed.toFixed(1)} m/s`;
  elements.brakingValue.textContent = `${braking.toFixed(1)} m/s²`;
  elapsed = 0;
  setPlaying(false);
  render();
}
elements.initialSpeed.addEventListener("input", updateParameters);
elements.braking.addEventListener("input", updateParameters);
for (const button of elements.phaseButtons) button.addEventListener("click", () => {
  stopCase = button.dataset.stopCase;
  for (const item of elements.phaseButtons) item.setAttribute("aria-pressed", String(item === button));
  elapsed = playing ? Math.max(0, stopEntryTime() - 0.28) : stopEntryTime() + 0.05;
  render();
});
for (const button of elements.modeButtons) button.addEventListener("click", () => {
  phaseMode = button.dataset.phaseMode;
  for (const item of elements.modeButtons) item.setAttribute("aria-pressed", String(item === button));
  render();
});

function animate() {
  const delta = Math.min(clock.getDelta(), 0.06);
  if (playing) {
    elapsed += delta * 0.65;
    if (elapsed >= duration()) elapsed = 0;
  }
  render();
  requestAnimationFrame(animate);
}
setPlaying(playing);
animate();
