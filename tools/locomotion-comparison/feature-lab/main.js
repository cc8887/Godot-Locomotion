import * as THREE from "three";
import {
  FEATURES, ITEMS, alsCanChangeHips, alsMantleCheck, alsTurnChoice,
  clamp01, featureLabel, footWithLock, lyraTurnPose, smoothstep,
} from "./model.js";

const $ = (id) => document.getElementById(id);
const DEG = Math.PI / 180;
const UP = new THREE.Vector3(0, 1, 0);
const direction = new THREE.Vector3();
const GREEN = 0x118b75;
const ORANGE = 0xdf8350;
const duration = 3.2;
const assetRoot = "https://github.com/cc8887/Godot-Locomotion/blob/main/docs/verification/";
const epicAnimation = "https://dev.epicgames.com/documentation/unreal-engine/animation-in-lyra-sample-game-in-unreal-engine";

const LESSONS = {
  "foot-lock": {
    number: "01 / FOOT CONTACT", lead: "根部继续移动时，接地脚要保留自己的接触点；平台运动时，这个点还必须属于正确的基座。",
    problem: "一只脚进入支撑期后，角色胶囊、骨盆或平台仍可能移动。只播放原动画会让脚掌被根节点拖走。锁脚需要定义接触权重、捕获脚点，并在每帧把目标重新写回脚部控制；释放、换平台和瞬移都要处理历史。",
    alsFlow: ["FootLock_L/R", "捕获脚点", "根/基座补偿", "Foot IK"],
    als: "ALS V4 的脚锁曲线决定左右脚锁定权重，UpdateFootIK 与脚部控制消费已有姿态、角色位移和地面探测。切到移动平台时，本页展示 Refactored 基座空间锚点扩展；它与 V4 每帧满权重重捕获的规则有版本边界。",
    lyraFlow: ["原始接触姿态", "根部继续移动", "脚点漂移参照"],
    lyra: "右侧是未施加接触约束的通用输入姿态，用来量出问题，不是 Lyra 最终输出。Epic 的 Lyra 动画概览未公开可逐节点核对的 FootLock 曲线、捕获和释放规则；不能由此推断 Lyra 一定滑脚或没有脚部修正。",
    boundary: "绿色/橙色是左右脚，浅环是初始接触点。右侧刻意保留原始脚位置作为对照，标为“通用反例”。移动平台模式的左侧属于 Refactored 扩展示意；演示几何不是原资产的骨骼求值。",
    phases: ["捕获", "根部位移", "基座补偿", "释放"],
    sources: [["ALS 脚锁版本边界", assetRoot + "2026-09-13-based-foot-lock-model.md"], ["最终接触锚点验证", assetRoot + "2026-09-14-final-foot-contact-anchoring.md"], ["Lyra 动画官方概览", epicAnimation]],
  },
  turn: {
    number: "02 / TURN IN PLACE", lead: "镜头或控制器转向时，身体如何追上目标朝向，同时不让脚随胶囊原地打转。",
    problem: "若 Actor 立刻旋转，仍在地上的脚会绕角色中心扫过地面；若角色始终不转，瞄准与身体方向又会长期分离。原地转向必须决定何时起播、选多大的角度、由谁承担临时的朝向差。",
    alsFlow: ["目标 Yaw", "90°/180° 资产", "Turn Slot", "RotationAmount"],
    als: "ALS V4 的 TurnInPlace 先按目标角符号选左右，再以严格小于 130° 选 90° 或 180°，站/蹲各有资源。动态 Montage 经 Not Moving 内的 Turn/Rotate Slot 混入；站姿使用动画角度和播放倍率更新 RotationScale。",
    lyraFlow: ["Actor 跟 Controller", "Root Yaw Offset 抵消", "Idle 转身动画", "曲线释放 Offset"],
    lyra: "Lyra 的 Actor 朝向控制器，但 Idle 时 Rotate Root Bone 用 Root Yaw Offset 抵消这次旋转，让模型暂留原朝向。Idle 状态机按角度和等待时间选转身动画；转身曲线逐步释放偏移。各武器子 AnimBP 可改转身资源与延迟。",
    boundary: "右侧可直接核对 Epic 官方动画说明的 Accumulate / Hold / Blend Out 模式与 Turn Yaw 曲线。图中连续旋转是逻辑可视化，不代表两套原动画的逐帧脚部轨迹。",
    phases: ["输入朝向", "等待阈值", "播放转身", "对齐"],
    sources: [["ALS TurnInPlace 选择", assetRoot + "2026-09-12-turn-in-place-selection.md"], ["ALS Slot 位置", assetRoot + "2026-09-11-standing-turn-slot-placement.md"], ["Lyra 原地转向", epicAnimation]],
  },
  mantle: {
    number: "03 / MANTLE", lead: "攀爬先是空间查询，再是动作选择和根运动对齐；只播一段伸手动画无法保证胶囊落在平台上。",
    problem: "角色要判断前方是否可抓、顶部是否可站、身体穿越路径是否净空，还要根据台高与持物姿态选择动作。移动平台会让目标位置在攀爬期间持续变化。",
    alsFlow: ["前方/顶部/净空探测", "高度与 Overlay 选片", "Root Motion warp", "平台局部目标"],
    als: "本地 ALS Mantle 实现保留地面 50–225 cm、空中 50–150 cm 等源门槛；低/高台及持物 Overlay 选择不同 Montage/起始时间。RootMotionSource 将原始根轨迹对齐到真实边缘，移动平台目标保留在基座局部空间。",
    lyraFlow: ["Jump", "Fall", "Land / Idle"],
    lyra: "右侧只画 Epic 在 Lyra 动画概览中明确描述的 Jump→Fall→Idle/Jog 路径。公开页面未给出可复核的 Lyra Mantle 查询、选片和根运动链；这段跳跃参照不能作为“Lyra 不支持攀爬”的证据。",
    boundary: "障碍与探测光线是教学几何。左侧通过净空开关展示拒绝路径；右侧始终是已公开跳跃路径。高台动作按本地源设置选择，本页不模拟完整物理碰撞。",
    phases: ["寻找边缘", "判定净空", "抓边/撑起", "站上平台"],
    sources: [["ALS 普通 Demo 攀爬接入", assetRoot + "2026-09-26-mantling-demo.md"], ["Mantle 高度/Overlay 资源", assetRoot + "2026-09-24-mantle-inputs.md"], ["Lyra 动画状态概览", epicAnimation]],
  },
  "cross-step": {
    number: "04 / DIRECTION CHANGE", lead: "横移反向时，两腿可能暂时交叉；换向命令到来，并不代表换髋姿态可以立即切换。",
    problem: "从右横移突然改向左，当前循环可能正让一只脚越过另一只脚。若当帧切到相反方向的髋姿态，腿和脚会穿插或跳位。需要把输入方向与可换髋的动画窗口分开。",
    alsFlow: ["六方向状态", "Feet_Crossing = 0", "髋偏向/状态权重", "ChangeDirection"],
    als: "ALS V4 使用 F/B/LF/LB/RF/RB 六方向状态与四向 VelocityBlend。中性换髋要等 Feet_Crossing 恰为 0、源状态权重为 1、髋偏向绝对值小于 0.5，再沿 ChangeDirection 自定义曲线与腿部 BlendProfile 过渡。Feet_Crossing 不能替代停步用的 Feet_Position。",
    lyraFlow: ["四向 Strafe", "请求移动角", "Orientation Warping", "下身朝向修正"],
    lyra: "Epic 文档说明 Lyra 用四个基本方向的 Strafe 资产，并在覆盖不足的起步中用 Orientation Warping 程序化扭转下半身。公开说明未给出与 ALS Feet_Crossing 一一对应的交叉步门控；右侧仅展示方向适配原理。",
    boundary: "左侧脚越过中线的动作被夸张放大；门控状态与阈值依据 ALS V4 图。右侧是四向样本加下身旋转的程序化示意，不代表 Lyra 实际资产里的逐帧交叉步。",
    phases: ["右横移", "反向输入", "脚交叉/等待", "换髋或适配"],
    sources: [["ALS 交错步原图审计", assetRoot + "2026-09-09-locomotion-port-gap-audit.md"], ["ALS 换髋许可验证", assetRoot + "2026-09-10-direction-completeness.md"], ["Lyra Orientation Warping", epicAnimation]],
  },
  equipment: {
    number: "05 / EQUIPMENT ANIMATION", lead: "手里换了道具，改变的不只是手中网格，还可能是站姿、移动、瞄准和转身资源。",
    problem: "把步枪换成手枪或弓时，手部挂点、双手约束和动画资产都要一起切换。装备已变但动画层仍是旧武器，会出现穿模或空手握持；只换动画不换道具也一样错误。",
    alsFlow: ["Overlay 枚举", "姿态/上身分层", "虚拟手骨挂点", "道具实例"],
    als: "本地 ALS V4 导出包含 13 种 Overlay；Rifle/Pistol/Bow/Torch/Box 等选择对应姿态，道具挂在左右虚拟手骨并随最终骨骼更新。Bow_Draw 还可按曲线采样弓姿态。",
    lyraFlow: ["Quick Bar 装备", "EquipmentInstance", "Link Anim Class Layers", "武器子 AnimBP"],
    lyra: "Lyra 的装备系统由 Quick Bar 选中物品、EquipmentDefinition/Instance 生成持物 Actor；武器可关联继承 ABP_ItemAnimLayersBase 的子 AnimBP，按装备动态链接层。公开实例明确展示手枪，并说明各武器可覆盖移动、瞄准和骨骼修正。弓/火把/箱子在右侧标为需要自行扩展的示意。",
    boundary: "切换时序是教学过渡，不是两套项目的真实网络装备事务。Pistol/Rifle 用公开武器层机制示意；Bow/Torch/Box 不被写成 Lyra 自带内容。",
    phases: ["原装备", "请求切换", "姿态/层更新", "新装备生效"],
    sources: [["ALS 原道具挂点与 Overlay", assetRoot + "2026-09-20-overlay-props.md"], ["Lyra 装备系统", "https://dev.epicgames.com/documentation/unreal-engine/lyra-inventory-and-equipment-in-unreal-engine"], ["Lyra Linked Layers", epicAnimation]],
  },
};

const options = {
  "foot-lock": { surface: "static", alpha: 1 },
  turn: { yaw: 115, stance: "standing" },
  mantle: { height: 1.35, blocked: false },
  "cross-step": { side: "left", bias: 0 },
  equipment: { item: "pistol" },
};
const controls = {
  "foot-lock": [{ label: "地面", key: "surface", choices: [["static", "固定"], ["moving", "移动平台"]] }, { label: "锁定权重", key: "alpha", min: 0, max: 1, step: 0.05, unit: "" }],
  turn: [{ label: "目标角度", key: "yaw", min: -170, max: 170, step: 5, unit: "°" }, { label: "姿态", key: "stance", choices: [["standing", "站立"], ["crouching", "蹲伏"]] }],
  mantle: [{ label: "台面高度", key: "height", choices: [[0.75, "低台 0.75m"], [1.35, "中台 1.35m"], [2, "高台 2.00m"]] }, { label: "顶部净空", key: "blocked", choices: [[false, "通畅"], [true, "阻挡"]] }],
  "cross-step": [{ label: "反向目标", key: "side", choices: [["left", "向左"], ["right", "向右"]] }, { label: "髋偏向", key: "bias", min: -1, max: 1, step: 0.1, unit: "" }],
  equipment: [{ label: "目标道具", key: "item", choices: Object.entries(ITEMS).map(([id, item]) => [id, item.label]) }],
};

const elements = {
  nav: [...document.querySelectorAll("[data-feature]")],
  title: $("feature-title"), number: $("feature-number"), lead: $("feature-lead"),
  controls: $("feature-controls"), problem: $("problem-copy"), alsFlow: $("als-flow"),
  lyraFlow: $("lyra-flow"), alsExplanation: $("als-explanation"),
  lyraExplanation: $("lyra-explanation"), boundary: $("boundary-copy"),
  sources: $("source-links"), phases: $("phase-track"),
  play: $("play-button"), reset: $("reset-button"), timeline: $("timeline"), time: $("time-value"),
  alsState: $("als-state"), alsValue: $("als-value"), alsOverlay: $("als-overlay"), alsBadge: $("als-badge"), alsHeading: $("als-heading"),
  lyraState: $("lyra-state"), lyraValue: $("lyra-value"), lyraOverlay: $("lyra-overlay"), lyraBadge: $("lyra-badge"), lyraHeading: $("lyra-heading"),
  metrics: [["metric-a-label", "metric-a"], ["metric-b-label", "metric-b"], ["metric-c-label", "metric-c"]].map(([label, value]) => [$(label), $(value)]),
};

function mat(color, roughness = 0.7) { return new THREE.MeshStandardMaterial({ color, roughness }); }
function rotateXZ(x, z, yaw) { const c = Math.cos(yaw), s = Math.sin(yaw); return { x: x * c + z * s, z: -x * s + z * c }; }
function cylinderBetween(mesh, a, b) {
  direction.subVectors(b, a);
  mesh.position.copy(a).add(b).multiplyScalar(0.5);
  mesh.quaternion.setFromUnitVectors(UP, direction.clone().normalize());
  mesh.scale.y = direction.length();
}
function line(scene, color, opacity = 1) {
  const geometry = new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(), new THREE.Vector3(1, 0, 0)]);
  const object = new THREE.Line(geometry, new THREE.LineBasicMaterial({ color, transparent: opacity < 1, opacity }));
  scene.add(object);
  return object;
}
function setLine(object, a, b) {
  object.geometry.setFromPoints([new THREE.Vector3(...a), new THREE.Vector3(...b)]);
}

function makeRig(scene, accent) {
  const body = new THREE.Group();
  scene.add(body);
  const charcoal = mat(0x263a3b, 0.6), cloth = mat(0x4b5f5e), skin = mat(0xe4c5ac), accentMat = mat(accent, 0.5);
  const torso = new THREE.Mesh(new THREE.CapsuleGeometry(0.31, 0.56, 7, 12), charcoal);
  torso.position.y = 1.51;
  body.add(torso);
  const chest = new THREE.Mesh(new THREE.BoxGeometry(0.12, 0.42, 0.42), accentMat);
  chest.position.set(0.32, 1.53, 0);
  body.add(chest);
  const head = new THREE.Mesh(new THREE.SphereGeometry(0.25, 16, 12), skin);
  head.position.set(0.08, 2.18, 0);
  body.add(head);
  const cap = new THREE.Mesh(new THREE.SphereGeometry(0.26, 16, 12, 0, Math.PI * 2, 0, Math.PI * .53), charcoal);
  cap.position.copy(head.position);
  body.add(cap);
  const nose = new THREE.Mesh(new THREE.BoxGeometry(0.08, 0.1, 0.12), charcoal);
  nose.position.set(.34, 2.16, 0);
  body.add(nose);
  const hips = new THREE.Mesh(new THREE.SphereGeometry(.29, 12, 10), charcoal);
  hips.scale.set(1.1, .65, .9);
  scene.add(hips);

  const legs = [-1, 1].map((side) => {
    const upper = new THREE.Mesh(new THREE.CylinderGeometry(.115, .13, 1, 9), cloth);
    const lower = new THREE.Mesh(new THREE.CylinderGeometry(.085, .10, 1, 9), cloth);
    const knee = new THREE.Mesh(new THREE.SphereGeometry(.115, 10, 8), charcoal);
    const shoe = new THREE.Mesh(new THREE.BoxGeometry(.42, .14, .25), mat(side < 0 ? GREEN : ORANGE, .54));
    const armUpper = new THREE.Mesh(new THREE.CylinderGeometry(.077, .087, 1, 9), cloth);
    const armLower = new THREE.Mesh(new THREE.CylinderGeometry(.065, .072, 1, 9), cloth);
    const hand = new THREE.Mesh(new THREE.SphereGeometry(.085, 10, 8), skin);
    const shoulder = new THREE.Mesh(new THREE.SphereGeometry(.13, 10, 8), accentMat);
    scene.add(upper, lower, knee, shoe, armUpper, armLower, hand, shoulder);
    return { side, upper, lower, knee, shoe, armUpper, armLower, hand, shoulder };
  });

  const propGroup = new THREE.Group();
  scene.add(propGroup);
  const props = {};
  const addProp = (name, meshes) => {
    const group = new THREE.Group();
    meshes.forEach((mesh) => group.add(mesh));
    propGroup.add(group);
    props[name] = group;
  };
  const steel = mat(0x2c3a3b, .5), wood = mat(0x805942), ember = mat(0xffb657, .3);
  const pistol = new THREE.Mesh(new THREE.BoxGeometry(.35, .13, .12), steel);
  const grip = new THREE.Mesh(new THREE.BoxGeometry(.1, .27, .11), steel); grip.position.set(-.08, -.14, 0);
  addProp("pistol", [pistol, grip]);
  const rifle = new THREE.Mesh(new THREE.BoxGeometry(.92, .13, .14), steel);
  const stock = new THREE.Mesh(new THREE.BoxGeometry(.25, .22, .12), wood); stock.position.x = -.37;
  const magazine = new THREE.Mesh(new THREE.BoxGeometry(.14, .25, .12), steel); magazine.position.set(.06, -.16, 0);
  addProp("rifle", [rifle, stock, magazine]);
  const bowArc = new THREE.Mesh(new THREE.TorusGeometry(.46, .035, 8, 24, Math.PI), wood);
  bowArc.rotation.z = Math.PI / 2;
  const bowString = new THREE.Mesh(new THREE.CylinderGeometry(.008, .008, .92, 6), mat(0xc7d3cd));
  addProp("bow", [bowArc, bowString]);
  const torch = new THREE.Mesh(new THREE.CylinderGeometry(.06, .08, .72, 10), wood);
  const flame = new THREE.Mesh(new THREE.ConeGeometry(.17, .35, 10), ember); flame.position.y = .53;
  addProp("torch", [torch, flame]);
  addProp("box", [new THREE.Mesh(new THREE.BoxGeometry(.58, .52, .56), wood)]);
  body.traverse((mesh) => { if (mesh.isMesh) mesh.castShadow = true; });
  scene.traverse((mesh) => { if (mesh.isMesh) mesh.castShadow = true; });

  function pose(frame) {
    const root = frame.root;
    const yaw = root.yaw || 0;
    const lowerYaw = frame.lowerYaw ?? yaw;
    body.position.set(root.x, root.y, root.z || 0);
    body.rotation.y = yaw;
    body.scale.y = frame.crouch ? .78 : 1;
    hips.position.set(root.x, root.y + (frame.crouch ? .85 : 1.04), root.z || 0);
    hips.rotation.y = lowerYaw;
    for (const part of legs) {
      const index = part.side < 0 ? 0 : 1;
      const foot = frame.feet[index];
      const offset = rotateXZ(0, part.side * .18, lowerYaw);
      const hip = new THREE.Vector3(root.x + offset.x, root.y + (frame.crouch ? .87 : 1.08), (root.z || 0) + offset.z);
      const ankle = new THREE.Vector3(foot.x, foot.y + .11, foot.z);
      const knee = hip.clone().lerp(ankle, .48);
      knee.x += .13;
      knee.y += .07;
      cylinderBetween(part.upper, hip, knee);
      cylinderBetween(part.lower, knee, ankle);
      part.knee.position.copy(knee);
      part.shoe.position.set(foot.x + .11, foot.y + .07, foot.z);
      part.shoe.rotation.y = lowerYaw;
      const poseName = frame.pose || "default";
      let hx = .18, hy = 1.2, hz = part.side * .43;
      if (poseName === "rifle") { hx = .78; hy = 1.51; hz = part.side * .20; }
      if (poseName === "pistol" && part.side > 0) { hx = .95; hy = 1.5; hz = .12; }
      if (poseName === "bow") { hx = part.side < 0 ? .86 : .48; hy = 1.55; hz = part.side * .20; }
      if (poseName === "torch" && part.side < 0) { hx = .58; hy = 1.62; hz = -.23; }
      if (poseName === "box") { hx = .65; hy = 1.5; hz = part.side * .3; }
      if (poseName === "mantle") { hx = .78; hy = 2.1; hz = part.side * .30; }
      const shoulderOffset = rotateXZ(0, part.side * .37, yaw);
      const handOffset = rotateXZ(hx, hz, yaw);
      const shoulder = new THREE.Vector3(root.x + shoulderOffset.x, root.y + (frame.crouch ? 1.46 : 1.8), (root.z || 0) + shoulderOffset.z);
      const hand = new THREE.Vector3(root.x + handOffset.x, root.y + hy * (frame.crouch ? .82 : 1), (root.z || 0) + handOffset.z);
      const elbow = shoulder.clone().lerp(hand, .55);
      elbow.x -= .12;
      cylinderBetween(part.armUpper, shoulder, elbow);
      cylinderBetween(part.armLower, elbow, hand);
      part.shoulder.position.copy(shoulder);
      part.hand.position.copy(hand);
    }
    for (const [name, prop] of Object.entries(props)) prop.visible = name === frame.prop;
    const propOffset = rotateXZ(frame.prop === "torch" ? .62 : .77, frame.prop === "torch" ? -.23 : 0, yaw);
    propGroup.position.set(root.x + propOffset.x, root.y + (frame.prop === "torch" ? 1.65 : 1.48), (root.z || 0) + propOffset.z);
    propGroup.rotation.y = yaw;
  }
  return { pose };
}

function makeStage(canvas, accent) {
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true, powerPreference: "high-performance" });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  const scene = new THREE.Scene();
  const camera = new THREE.OrthographicCamera(-4, 4, 3, -1, .1, 100);
  scene.add(new THREE.HemisphereLight(0xffffff, 0x8ba69c, 2));
  const sun = new THREE.DirectionalLight(0xffffff, 3);
  sun.position.set(-2, 8, 6); sun.castShadow = true; sun.shadow.mapSize.set(1024, 1024);
  sun.shadow.camera.left = -7; sun.shadow.camera.right = 7; sun.shadow.camera.top = 6; sun.shadow.camera.bottom = -6;
  scene.add(sun);
  const floor = new THREE.Mesh(new THREE.PlaneGeometry(14, 14), mat(accent === GREEN ? 0xe0ede6 : 0xf1e8e1, 1));
  floor.rotation.x = -Math.PI / 2; floor.position.y = -.03; floor.receiveShadow = true; scene.add(floor);
  for (let value = -7; value <= 7; value++) {
    const a = line(scene, 0xb2c2bb, .24), b = line(scene, 0xb2c2bb, .24);
    setLine(a, [value, .002, -7], [value, .002, 7]);
    setLine(b, [-7, .003, value], [7, .003, value]);
  }
  const rig = makeRig(scene, accent);
  const contact = [-1, 1].map((side) => {
    const ring = new THREE.Mesh(new THREE.RingGeometry(.21, .27, 30), new THREE.MeshBasicMaterial({ color: side < 0 ? GREEN : ORANGE, side: THREE.DoubleSide, transparent: true, opacity: .94 }));
    ring.rotation.x = -Math.PI / 2; scene.add(ring); return ring;
  });
  const anchor = new THREE.Mesh(new THREE.RingGeometry(.28, .31, 30), new THREE.MeshBasicMaterial({ color: GREEN, side: THREE.DoubleSide, transparent: true, opacity: .46 }));
  anchor.rotation.x = -Math.PI / 2; scene.add(anchor);
  const platform = new THREE.Mesh(new THREE.BoxGeometry(2.3, .18, 1.35), mat(0x93b4a3));
  platform.position.y = -.12; platform.receiveShadow = true; scene.add(platform);
  const wall = new THREE.Mesh(new THREE.BoxGeometry(1.6, 1, 2.5), mat(0x9aa9a1, .9));
  wall.receiveShadow = true; wall.castShadow = true; scene.add(wall);
  const wallTop = new THREE.Mesh(new THREE.BoxGeometry(1.66, .07, 2.57), mat(0x7c9a8a));
  scene.add(wallTop);
  const probe = [line(scene, GREEN), line(scene, GREEN), line(scene, GREEN)];
  const arrow = new THREE.ArrowHelper(new THREE.Vector3(1, 0, 0), new THREE.Vector3(-.2, .08, -.95), 1.5, accent, .25, .12);
  scene.add(arrow);
  let activeFeature = "foot-lock";
  function resize() {
    const width = Math.max(1, canvas.clientWidth), height = Math.max(1, canvas.clientHeight);
    renderer.setSize(width, height, false);
    const visibleHeight = activeFeature === "mantle" ? 5.7 : 4.35;
    const visibleWidth = visibleHeight * width / height;
    camera.left = -visibleWidth / 2; camera.right = visibleWidth / 2;
    camera.top = visibleHeight / 2; camera.bottom = -visibleHeight / 2;
    camera.updateProjectionMatrix();
  }
  new ResizeObserver(resize).observe(canvas);
  resize();
  function update(feature, frame) {
    if (activeFeature !== feature) { activeFeature = feature; resize(); }
    platform.visible = feature === "foot-lock" && options["foot-lock"].surface === "moving";
    platform.position.x = frame.platformX || 0;
    wall.visible = wallTop.visible = feature === "mantle";
    if (wall.visible) {
      const height = options.mantle.height;
      wall.scale.y = height;
      wall.position.set(1.35, height / 2 - .03, 0);
      wallTop.position.set(1.35, height + .015, 0);
    }
    for (const ray of probe) ray.visible = feature === "mantle" && accent === GREEN;
    if (probe[0].visible) {
      const endColor = options.mantle.blocked ? ORANGE : GREEN;
      for (const ray of probe) ray.material.color.setHex(endColor);
      setLine(probe[0], [-.8, 1.25, -.25], [.55, 1.25, -.25]);
      setLine(probe[1], [.53, options.mantle.height + .55, -.2], [.53, options.mantle.height, -.2]);
      setLine(probe[2], [.5, options.mantle.height + 1.5, .35], [1.4, options.mantle.height + 1.5, .35]);
    }
    arrow.visible = feature === "turn" || feature === "cross-step";
    if (arrow.visible) {
      const angle = feature === "turn" ? options.turn.yaw * DEG : (options["cross-step"].side === "left" ? -65 : 65) * DEG;
      arrow.position.set(feature === "turn" ? 0 : -.3, .1, -.95);
      arrow.setDirection(new THREE.Vector3(Math.cos(angle), 0, -Math.sin(angle)).normalize());
    }
    anchor.visible = feature === "foot-lock";
    if (anchor.visible) anchor.position.set(frame.anchorX ?? -.45, .015, -.19);
    rig.pose(frame);
    frame.feet.forEach((foot, index) => {
      contact[index].visible = Boolean(foot.contact);
      contact[index].position.set(foot.x, foot.y + .008, foot.z);
    });
    camera.position.set(feature === "mantle" ? 4.7 : 3.8, feature === "mantle" ? 5.2 : 3.7, 8.4);
    camera.lookAt(feature === "mantle" ? .45 : 0, feature === "mantle" ? 1.85 : 1.18, 0);
    renderer.render(scene, camera);
  }
  return { update };
}

const alsStage = makeStage($("als-canvas"), GREEN);
const lyraStage = makeStage($("lyra-canvas"), ORANGE);
let feature = FEATURES.includes(location.hash.slice(1)) ? location.hash.slice(1) : "foot-lock";
let elapsed = 0;
let playing = !matchMedia("(prefers-reduced-motion: reduce)").matches;
const clock = new THREE.Clock();

function foot(x, y, z, contact = y < .02) { return { x, y, z, contact }; }
function standardFeet(root, lift = 0) {
  return [foot(root.x - .21, root.y, -.19), foot(root.x + .17, root.y + lift, .19, lift < .02)];
}
function crossFeet(u, sideSign) {
  const swingIn = smoothstep((u - .31) / .25);
  const swingOut = smoothstep((u - .68) / .13);
  const crossAmount = swingIn * (1 - swingOut);
  const lift = .33 * crossAmount;
  return sideSign < 0
    ? [foot(-.24, 0, -.2), foot(.24 - .83 * crossAmount, lift, .2 - .63 * crossAmount)]
    : [foot(-.24 + .83 * crossAmount, lift, -.2 + .63 * crossAmount), foot(.24, 0, .2)];
}

function sample(featureId, side, u) {
  const p = options[featureId];
  if (featureId === "foot-lock") {
    const platformX = p.surface === "moving" ? .62 * smoothstep(u) : 0;
    const root = { x: -.7 + .75 * smoothstep(u) + platformX, y: 0, z: 0, yaw: 0 };
    const anchorX = -.91 + platformX;
    const sourceLeft = root.x - .21;
    const release = smoothstep((u - .64) / .2);
    const leftLock = p.alpha * (1 - release);
    const leftX = side === "als" ? footWithLock(root.x, -.21, -.91, platformX, leftLock) : sourceLeft;
    const left = foot(leftX, side === "als" ? .34 * Math.sin(Math.PI * release) : 0, -.19, release === 0 || release === 1);
    const rightAnchor = -.7 + .75 * smoothstep(.38) + .18 + platformX;
    const right = side === "als" && u >= .38
      ? foot(rightAnchor, 0, .19, true)
      : foot(root.x + .18, .25 * Math.sin(Math.PI * clamp01(u / .38)), .19, u === 0 || u >= .38);
    const drift = Math.abs(sourceLeft - anchorX) * 100;
    return {
      frame: { root, feet: [left, right], anchorX, platformX },
      state: side === "als" ? release > 0 ? "左脚释放 · 右脚接替支撑" : p.alpha > .95 ? "左脚锁定 · 右脚摆动" : "源姿态与锚点混合" : "未约束输入姿态 · 通用反例",
      value: side === "als" ? release > 0 ? `${leftLock.toFixed(2)} 左脚锁权重` : `${Math.abs(leftX - anchorX).toFixed(2)} m 偏移` : `${drift.toFixed(0)} cm 原始漂移`,
      overlay: side === "als" ? p.alpha === 0 ? "锁定权重为 0：保留原始脚位" : release > 0 ? "左脚抬起后释放旧锚点，右脚保持接触" : p.surface === "moving" ? "基座局部锚点随平台移动" : "左脚世界接触点固定" : "仅展示未约束输入；非 Lyra 最终脚部输出",
      metrics: [["左脚锁权重", leftLock.toFixed(2)], ["初始脚点漂移", `${drift.toFixed(0)} cm`], ["接触基准", p.surface === "moving" ? "移动基座" : "世界地面"]],
    };
  }
  if (featureId === "turn") {
    const choice = alsTurnChoice(p.yaw, p.stance);
    const turnProgress = clamp01((u - .32) / .56);
    const eased = smoothstep(turnProgress);
    const lyra = lyraTurnPose(p.yaw, turnProgress);
    const yaw = side === "als" ? p.yaw * eased : lyra.meshYaw;
    const root = { x: 0, y: 0, z: 0, yaw: yaw * DEG };
    const left = foot(-.2 + .17 * eased * Math.sign(p.yaw), Math.sin(Math.PI * clamp01((u - .36) / .36)) * .25, -.2, u < .36 || u > .72);
    const right = foot(.2 - .12 * eased * Math.sign(p.yaw), Math.sin(Math.PI * clamp01((u - .56) / .35)) * .25, .2, u < .56 || u > .91);
    return {
      frame: { root, feet: [left, right], crouch: p.stance === "crouching" },
      state: side === "als" ? p.yaw === 0 ? "朝向已对齐" : u < .32 ? "等待 TurnInPlace" : `Turn Slot · ${choice.direction}` : u < .32 ? "Accumulate · 抵消 Actor 旋转" : "转身曲线释放 Root Yaw Offset",
      value: side === "als" ? p.yaw === 0 ? "无需选片" : `${choice.assetAngle}° ${choice.direction}` : `${lyra.rootYawOffset.toFixed(0)}° Root Yaw Offset`,
      overlay: side === "als" ? "Actor 随 Montage 逐步旋转" : "Actor 已朝控制器；模型先保留原朝向",
      metrics: [["目标 Yaw", `${p.yaw}°`], ["ALS 片段档位", p.yaw === 0 ? "无需选片" : `${choice.assetAngle}°`], ["Lyra Root Yaw Offset", `${lyra.rootYawOffset.toFixed(0)}°`]],
    };
  }
  if (featureId === "mantle") {
    const gate = alsMantleCheck(p.height, !p.blocked);
    const climb = smoothstep((u - .35) / .55);
    const approach = smoothstep(u / .35);
    const accepted = gate === "accepted";
    const root = side === "als"
      ? accepted ? { x: -1.35 + .75 * approach + 2.63 * climb, y: p.height * smoothstep((u - .43) / .4), z: 0, yaw: 0 } : { x: -1.35 + .6 * approach, y: 0, z: 0, yaw: 0 }
      : { x: -1.35 + .72 * approach, y: .62 * Math.sin(Math.PI * clamp01((u - .3) / .6)), z: 0, yaw: 0 };
    const feet = standardFeet(root, side === "als" && accepted ? Math.sin(Math.PI * climb) * .22 : 0);
    return {
      frame: { root, feet, pose: side === "als" && accepted && u > .37 && u < .9 ? "mantle" : "default" },
      state: side === "als" ? accepted ? u < .35 ? "前向/顶部/净空探测" : "RootMotionSource · 对齐台面" : "净空受阻 · 拒绝攀爬" : "Jump → Fall → Idle 参照路径",
      value: side === "als" ? accepted ? `${p.height.toFixed(2)} m 台面` : "0 次起播" : "未核验 Mantle 图",
      overlay: side === "als" ? accepted ? "目标跟随可攀爬台面" : "阻挡命中，动作不开始" : "此处只画公开的基础跳跃状态",
      metrics: [["台面高度", `${p.height.toFixed(2)} m`], ["ALS 判定", accepted ? "允许" : "拒绝"], ["Lyra Mantle 原图", "未核验"]],
    };
  }
  if (featureId === "cross-step") {
    const sign = p.side === "left" ? -1 : 1;
    const crossing = u >= .31 && u < .81 ? 1 : 0;
    const requested = u >= .42;
    const allowed = requested && alsCanChangeHips(crossing, 1, p.bias);
    const alsProgress = allowed ? smoothstep((u - .81) / .18) : 0;
    const lyraProgress = requested ? smoothstep((u - .42) / .32) : 0;
    const root = { x: -.45 + .65 * Math.sin(Math.PI * u), y: 0, z: 0, yaw: Math.PI / 2 };
    const lowerYaw = root.yaw + (side === "als" ? alsProgress : lyraProgress) * sign * .52;
    return {
      frame: { root, lowerYaw, feet: crossFeet(u, sign) },
      state: side === "als" ? requested ? allowed ? "ChangeDirection · 换髋" : crossing ? "等待 Feet_Crossing = 0" : "等待髋偏向回中" : "原方向循环" : requested ? "四向 Strafe + Orientation Warping" : "基本方向 Strafe",
      value: side === "als" ? crossing ? "Feet_Crossing = 1" : `Feet_Crossing = 0 · Bias ${p.bias.toFixed(1)}` : `${(lyraProgress * 30).toFixed(0)}° 下身适配`,
      overlay: side === "als" ? "交叉窗口内保持原髋姿态" : "方向适配展示下身旋转，不等同换髋门控",
      metrics: [["Feet_Crossing", String(crossing)], ["髋偏向", p.bias.toFixed(1)], ["ALS 换髋许可", allowed ? "开放" : "等待"]],
    };
  }
  const item = ITEMS[p.item];
  const switched = u > .38;
  const activeItem = switched ? p.item : "none";
  const active = ITEMS[activeItem];
  const supported = active.lyraLayer !== null;
  const displayItem = side === "als" || supported ? activeItem : "none";
  const root = { x: -.1, y: 0, z: 0, yaw: -.23 };
  return {
    frame: { root, feet: standardFeet(root), pose: displayItem, prop: displayItem === "none" ? null : displayItem },
    state: side === "als" ? `Overlay = ${active.overlay}` : supported ? "Equipment → Linked Anim Layer" : "需新增武器/道具子层",
    value: side === "als" ? active.prop || "空手" : active.lyraLayer || "非示例自带资产",
    overlay: side === "als" ? "Overlay 姿态与手部挂点同步切换" : supported ? "装备驱动层类替换" : "此侧保留基础姿态；扩展方式示意",
    metrics: [["目标道具", item.label], ["ALS Overlay", active.overlay], ["Lyra 层", active.lyraLayer || "需自行扩展"]],
  };
}

function setText(id, value) { elements[id].textContent = value; }
function renderFlow(container, steps) {
  container.replaceChildren();
  steps.forEach((step, index) => {
    if (index) { const arrow = document.createElement("i"); arrow.textContent = "→"; container.append(arrow); }
    const label = document.createElement("span"); label.textContent = step; container.append(label);
  });
}
function renderControls() {
  elements.controls.replaceChildren();
  for (const control of controls[feature]) {
    const group = document.createElement("div"); group.className = "control-group";
    const title = document.createElement("span"); title.textContent = control.label; group.append(title);
    if (control.choices) {
      const segmented = document.createElement("div"); segmented.className = "segmented"; segmented.setAttribute("role", "group"); segmented.setAttribute("aria-label", control.label);
      for (const [value, label] of control.choices) {
        const button = document.createElement("button"); button.type = "button"; button.textContent = label;
        button.setAttribute("aria-pressed", String(options[feature][control.key] === value));
        button.addEventListener("click", () => {
          options[feature][control.key] = value;
          for (const choice of segmented.children) choice.setAttribute("aria-pressed", String(choice === button));
          elapsed = playing ? 0 : duration * .56;
          render();
        });
        segmented.append(button);
      }
      group.append(segmented);
    } else {
      const input = document.createElement("input"); input.type = "range"; input.min = control.min; input.max = control.max; input.step = control.step; input.value = options[feature][control.key];
      input.setAttribute("aria-label", control.label);
      const output = document.createElement("output");
      const update = () => { options[feature][control.key] = Number(input.value); output.textContent = `${input.value}${control.unit}`; render(); };
      input.addEventListener("input", update); update(); group.append(input, output);
    }
    elements.controls.append(group);
  }
}
function selectFeature(next) {
  feature = FEATURES.includes(next) ? next : FEATURES[0];
  const lesson = LESSONS[feature];
  elapsed = 0;
  for (const tab of elements.nav) tab.setAttribute("aria-current", tab.dataset.feature === feature ? "page" : "false");
  setText("title", featureLabel(feature)); setText("number", lesson.number); setText("lead", lesson.lead);
  setText("problem", lesson.problem); setText("alsExplanation", lesson.als); setText("lyraExplanation", lesson.lyra); setText("boundary", lesson.boundary);
  renderFlow(elements.alsFlow, lesson.alsFlow); renderFlow(elements.lyraFlow, lesson.lyraFlow);
  elements.sources.replaceChildren();
  for (const [label, url] of lesson.sources) {
    const anchor = document.createElement("a"); anchor.href = url; anchor.target = "_blank"; anchor.rel = "noopener noreferrer"; anchor.textContent = label + " ↗"; elements.sources.append(anchor);
  }
  elements.phases.replaceChildren();
  for (const phase of lesson.phases) { const span = document.createElement("span"); span.textContent = phase; elements.phases.append(span); }
  elements.alsHeading.textContent = feature === "foot-lock" && options["foot-lock"].surface === "moving" ? "本地扩展 / 三维示意" : "原图规则 / 三维示意";
  elements.alsBadge.textContent = feature === "foot-lock" && options["foot-lock"].surface === "moving" ? "本地扩展" : "已核对";
  elements.lyraHeading.textContent = feature === "foot-lock" ? "通用未约束参照" : feature === "mantle" ? "已公开的跳跃路径" : "Epic 公开机制";
  elements.lyraBadge.textContent = feature === "foot-lock" ? "通用反例" : feature === "mantle" ? "范围说明" : "官方文档";
  renderControls(); render();
}

function render() {
  const u = clamp01(elapsed / duration);
  const left = sample(feature, "als", u), right = sample(feature, "lyra", u);
  alsStage.update(feature, left.frame); lyraStage.update(feature, right.frame);
  setText("alsState", left.state); setText("alsValue", left.value); setText("alsOverlay", left.overlay);
  setText("lyraState", right.state); setText("lyraValue", right.value); setText("lyraOverlay", right.overlay);
  elements.alsHeading.textContent = feature === "foot-lock" && options["foot-lock"].surface === "moving" ? "本地扩展 / 三维示意" : "原图规则 / 三维示意";
  elements.alsBadge.textContent = feature === "foot-lock" && options["foot-lock"].surface === "moving" ? "本地扩展" : "已核对";
  for (let index = 0; index < 3; index++) {
    elements.metrics[index][0].textContent = left.metrics[index][0];
    elements.metrics[index][1].textContent = left.metrics[index][1];
  }
  [...elements.phases.children].forEach((phase, index) => phase.classList.toggle("active", index === Math.min(3, Math.floor(u * 4))));
  elements.timeline.value = Math.round(u * 1000);
  elements.time.textContent = `${elapsed.toFixed(2)} s`;
}
function setPlaying(next) { playing = next; elements.play.textContent = playing ? "暂停" : "播放"; elements.play.setAttribute("aria-label", playing ? "暂停动画" : "播放动画"); }
elements.play.addEventListener("click", () => { if (!playing && elapsed >= duration) elapsed = 0; setPlaying(!playing); render(); });
elements.reset.addEventListener("click", () => { elapsed = 0; setPlaying(false); render(); });
elements.timeline.addEventListener("input", () => { elapsed = Number(elements.timeline.value) / 1000 * duration; setPlaying(false); render(); });
for (const tab of elements.nav) tab.addEventListener("click", () => { location.hash = tab.dataset.feature; selectFeature(tab.dataset.feature); });
window.addEventListener("hashchange", () => { if (location.hash.slice(1) !== feature) selectFeature(location.hash.slice(1)); });
function animate() {
  const delta = Math.min(clock.getDelta(), .06);
  if (playing) { elapsed += delta * .72; if (elapsed > duration + .4) elapsed = 0; }
  render(); requestAnimationFrame(animate);
}
setPlaying(playing);
selectFeature(feature);
animate();
