import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { FBXLoader } from '../vendor/FBXLoader.js';
import { BVHLoader } from 'three/addons/loaders/BVHLoader.js';
import { COMPARISON_SPACE, isRootBone, originMetrics } from './comparison-space.js';
import { sourceMotionObject } from './source-motion.js';
import { normalizeOfflineFbxIndent } from './offline-fbx-text.js';

export const KIND_COLOR = {
  root: 0xff7a1a, pelvis: 0xff3d81, ik: 0xb44cff,
  foot: 0x35d96b, hand: 0xffd23d, head: 0x38a9ff,
};
const KIND_SIZE = { root: 16, pelvis: 13, ik: 13, foot: 11, hand: 11, head: 11 };

export function classifyBone(name) {
  if (isRootBone(name)) return 'root';
  if (/pelvis|hips/i.test(name)) return 'pelvis';
  if (/^(ik_|vb_)|^virtual/i.test(name)) return 'ik';
  if (/foot|ankle|ball|toe/i.test(name)) return 'foot';
  if (/hand|wrist|palm/i.test(name)) return 'hand';
  if (/^head$/i.test(name)) return 'head';
  return null;
}

// FBX track node names sometimes carry "Model::" / namespace prefixes that
// do not match the parsed bone names; strip them so PropertyBinding resolves.
function normalizeTrackNames(object, clips) {
  const names = new Set();
  object.traverse((o) => { if (o.name) names.add(o.name); });
  for (const clip of clips) {
    for (const track of clip.tracks) {
      const dot = track.name.indexOf('.');
      if (dot <= 0) continue;
      const node = track.name.slice(0, dot);
      if (names.has(node)) continue;
      const stripped = node.includes('::')
        ? node.slice(node.lastIndexOf('::') + 2) : null;
      if (stripped && names.has(stripped)) {
        track.name = stripped + track.name.slice(dot);
      }
    }
  }
}

// three.js PropertyBinding.findNode special-cases the literal name 'root' and
// would bind 'root.*' tracks to the mixer root object instead of the bone.
// Renaming bone+tracks to a safe alias keeps root-motion on the actual bone.
function aliasRootBone(object, clips) {
  let rootBone = null;
  object.traverse((o) => {
    if (!rootBone && o.isBone && /^root$/i.test(o.name)) rootBone = o;
  });
  if (!rootBone) return;
  const ALIAS = '__viewer_root__';
  let rewrote = false;
  for (const clip of clips) {
    for (const track of clip.tracks) {
      const dot = track.name.indexOf('.');
      if (dot <= 0) continue;
      if (/^root$/i.test(track.name.slice(0, dot))) {
        track.name = ALIAS + track.name.slice(dot);
        rewrote = true;
      }
    }
  }
  if (rewrote) rootBone.name = ALIAS;
}

// 管线 FBX 均为 ASCII 且 GlobalSettings 头部带 UpAxis 声明；
// 扫描文件头按文件自身约定处理（2 = Z-up，UE/ALS 导出；1 = Y-up）。
// 二进制 FBX 或读取异常时回退 Z-up（管线全部产物为 UE 约定）。
function detectFbxUpAxis(buf) {
  try {
    const n = Math.min(buf.byteLength, 8192);
    const head = new Uint8Array(buf, 0, n);
    let s = '';
    for (let i = 0; i < n; i++) s += String.fromCharCode(head[i]);
    const m = s.match(/P:\s*"UpAxis"[^0-9-]*(\d)/);
    if (m) return m[1] === '2' ? 'z' : 'y';
  } catch (e) { /* binary FBX 或读取异常，走回退 */ }
  return 'z';
}

export class Viewer {
  constructor(canvas, opts = {}) {
    this.canvas = canvas;
    this.onLoaded = opts.onLoaded || (() => {});
    this.onError = opts.onError || (() => {});
    this.onFrame = opts.onFrame || (() => {});
    this.onCameraChange = opts.onCameraChange || (() => {});

    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    this.renderer.setClearColor(0x0f1115);
    this.scene = new THREE.Scene();
    this.verticalSpanCm = COMPARISON_SPACE.verticalSpanCm;
    this.camera = new THREE.OrthographicCamera(-400, 400, 400, -400, 0.1, 20000);
    this.camera.position.fromArray(COMPARISON_SPACE.cameraPosition);

    this.scene.add(new THREE.HemisphereLight(0xffffff, 0x28303c, 2.2));
    const dir = new THREE.DirectionalLight(0xffffff, 1.6);
    dir.position.set(120, 220, 160);
    this.scene.add(dir);

    this.grid = new THREE.GridHelper(600, 60, 0x2a3346, 0x1a2028);
    this.scene.add(this.grid);
    this.axes = new THREE.AxesHelper(60);
    this.scene.add(this.axes);

    this.controls = new OrbitControls(this.camera, canvas);
    this.controls.target.set(0, 90, 0);
    this.controls.enableDamping = false;
    this.controls.dampingFactor = 0.08;
    this.controls.enablePan = false;
    this.controls.addEventListener('change', () => {
      if (!this._copyingCamera) this.onCameraChange(this.cameraState());
    });
    const cross = new THREE.BufferGeometry().setFromPoints([
      new THREE.Vector3(-25, 0.2, 0), new THREE.Vector3(25, 0.2, 0),
      new THREE.Vector3(0, 0.2, -25), new THREE.Vector3(0, 0.2, 25),
    ]);
    this.scene.add(new THREE.LineSegments(cross, new THREE.LineBasicMaterial({ color: 0xffffff })));
    this.originGuide = new THREE.Line(new THREE.BufferGeometry().setFromPoints([
      new THREE.Vector3(), new THREE.Vector3(), new THREE.Vector3(),
    ]), new THREE.LineBasicMaterial({ color: KIND_COLOR.pelvis, depthTest: false }));
    this.scene.add(this.originGuide);
    this.metricsLabel = document.createElement('div');
    this.metricsLabel.className = 'vp-origin';
    canvas.parentElement.appendChild(this.metricsLabel);

    this.pivot = null;
    this.model = null;
    this.mixer = null;
    this.action = null;
    this.clip = null;
    this.takes = [];
    this.skelHelper = null;
    this.trailLine = null;
    this.trailBoneName = null;
    this.trailLines = [];
    this._texCache = {};
    this.playing = true;
    this.speed = 1;
    this.loop = true;
    // 显示约定：'z' = ALS 原版 Z-up（默认，Z-up 资产直立、轴指示 Z 朝上）；
    // 'y' = three.js 原生（按资产原始朝向显示，用于诊断轴问题）。
    this.upAxis = 'z';
    this.contentUp = null; // 'y' | 'z'：每次 load 检测的源数据朝向
    this._applyOrientation();
    this._resize();
  }

  _resize() {
    const w = this.canvas.clientWidth || 2;
    const h = this.canvas.clientHeight || 2;
    this._lastW = w;
    this._lastH = h;
    this.renderer.setSize(w, h, false);
    const half = this.verticalSpanCm / 2;
    this.camera.left = -half * w / h;
    this.camera.right = half * w / h;
    this.camera.top = half;
    this.camera.bottom = -half;
    this.camera.updateProjectionMatrix();
  }

  resize() { this._resize(); }

  resetCamera() {
    this._frameCamera();
  }

  cameraState() {
    return { position: this.camera.position.toArray(), target: this.controls.target.toArray(),
      zoom: this.camera.zoom, verticalSpanCm: this.verticalSpanCm };
  }

  applyCameraState(state) {
    this._copyingCamera = true;
    this.camera.position.fromArray(state.position);
    this.controls.target.fromArray(state.target);
    this.camera.zoom = state.zoom;
    this.verticalSpanCm = state.verticalSpanCm;
    this._resize();
    this.controls.update();
    this._copyingCamera = false;
  }

  setUpAxis(axis) {
    this.upAxis = axis;
    this._applyOrientation();
  }

  // 显示朝向统一处理：
  // - 'z'（ALS 原版）：轴指示器旋转 -90°X，蓝色 Z 轴朝上（UE 约定）；
  //   Z-up 内容（FBX）pivot 旋转 -90°X 直立，Y-up 内容（BVH）保持直立。
  // - 'y'（原始数据）：一切归零，FBX 按原始 Z-up 躺平、BVH 直立，用于诊断。
  _applyOrientation() {
    this.axes.rotation.x = this.upAxis === 'z' ? -Math.PI / 2 : 0;
    if (!this.pivot) return;
    const standZup = this.upAxis === 'z' && this.contentUp === 'z';
    this.pivot.rotation.x = standZup ? -Math.PI / 2 : 0;
  }

  setPlaying(v) {
    this.playing = v;
    if (this.action) this.action.paused = !v;
  }

  setSpeed(v) { this.speed = v; }

  setLoop(v) {
    this.loop = v;
    if (this.action) {
      this.action.setLoop(v ? THREE.LoopRepeat : THREE.LoopOnce, Infinity);
      this.action.clampWhenFinished = true;
    }
  }

  setTime(t) {
    if (!this.action || !this.clip) return;
    const dur = this.clip.duration || 0;
    this.action.time = Math.max(0, Math.min(t, dur));
    const paused = this.action.paused;
    this.action.paused = false;
    this.mixer.update(0);
    this.action.paused = paused;
    this.model?.updateMatrixWorld(true);
  }

  clearScene() {
    this._loadGeneration = (this._loadGeneration || 0) + 1;
    if (this.mixer) { this.mixer.stopAllAction(); this.mixer = null; }
    this.action = null;
    this.clip = null;
    this.takes = [];
    if (this.skelHelper) {
      this.scene.remove(this.skelHelper);
      this.skelHelper.dispose?.();
      this.skelHelper = null;
    }
    for (const line of this.trailLines) {
      line.parent?.remove(line); line.geometry.dispose(); line.material.dispose();
    }
    this.trailLines = [];
    this.trailLine = null;
    this.trailBoneName = null;
    this.metricsLabel.textContent = '固定原点 O · cm';
    this.metricsLabel.classList.remove('bad');
    this.originGuide.visible = false;
    this._clearMarkers();
    if (this.pivot) {
      this.pivot.traverse((o) => {
        if (o.isSkinnedMesh) o.geometry?.dispose();
        if (o.material) {
          (Array.isArray(o.material) ? o.material : [o.material]).forEach((m) => m.dispose());
        }
      });
      this.scene.remove(this.pivot);
      this.pivot = null;
      this.model = null;
    }
  }

  async load(url, ext, fileName) {
    this.clearScene();
    const generation = this._loadGeneration;
    this.pivot = new THREE.Group();
    this.scene.add(this.pivot);

    let object;
    let takes = [];
    if (ext === '.source-motion') {
      const response = await fetch(url, {cache:'no-store'});
      if (!response.ok) throw new Error('源动画读取失败');
      const data = await response.json();
      const parsed = sourceMotionObject(data, THREE);
      object = parsed.object; takes = [parsed.clip]; this.contentUp = data.up;
    } else if (ext === '.bvh') {
      const res = await new BVHLoader().loadAsync(url);
      if (!res || !res.skeleton || !res.clip) throw new Error('BVH 解析失败');
      // BVHLoader returns { skeleton, clip }; skeleton itself is a
      // THREE.Skeleton (no .traverse). bones[0] is the root Bone with the
      // whole hierarchy already parented — use it as the mount object.
      object = res.skeleton.bones[0];
      if (!object) throw new Error('BVH 无骨骼');
      takes = [res.clip];
      this.contentUp = 'y'; // SOMA BVH 为 Y-up（骨骼沿 +Y 生长）
    } else {
      const buf = await new THREE.FileLoader()
        .setResponseType('arraybuffer')
        .loadAsync(url);
      this.contentUp = detectFbxUpAxis(buf);
      object = new FBXLoader().parse(normalizeOfflineFbxIndent(buf), '');
      takes = object.animations ? object.animations.slice() : [];
    }

    if (generation !== this._loadGeneration) throw new Error('Load superseded');
    this._applyOrientation();
    normalizeTrackNames(object, takes);
    aliasRootBone(object, takes);

    this.pivot.add(object);
    object.updateMatrixWorld(true);
    this.model = object;
    this.takes = takes;

    this._buildSkeletonHelper();
    this._buildMarkers();

    if (takes.length) {
      this.mixer = new THREE.AnimationMixer(this.pivot);
      this.playTake(0);
    }
    this.onLoaded({ fileName, takes });
    return { takes };
  }

  playTake(index) {
    if (!this.mixer || !this.takes[index]) return;
    if (this.action) this.action.stop();
    this.clip = this.takes[index];
    this.action = this.mixer.clipAction(this.clip);
    this.action.setLoop(this.loop ? THREE.LoopRepeat : THREE.LoopOnce, Infinity);
    this.action.clampWhenFinished = true;
    this.action.paused = !this.playing;
    this.action.time = 0;
    this.action.play();
    this.mixer.update(0);
    this._sampleTrail();
  }

  _buildSkeletonHelper() {
    if (!this.model) return;
    this.skelHelper = new THREE.SkeletonHelper(this.model);
    this.skelHelper.material.linewidth = 2;
    this.scene.add(this.skelHelper);
  }

  _markerTexture(kind) {
    if (this._texCache[kind]) return this._texCache[kind];
    const color = '#' + KIND_COLOR[kind].toString(16).padStart(6, '0');
    const c = document.createElement('canvas');
    c.width = c.height = 64;
    const ctx = c.getContext('2d');
    ctx.beginPath();
    ctx.arc(32, 32, 21, 0, Math.PI * 2);
    ctx.fillStyle = color;
    ctx.fill();
    ctx.lineWidth = 6;
    ctx.strokeStyle = 'rgba(0,0,0,0.55)';
    ctx.stroke();
    const tex = new THREE.CanvasTexture(c);
    this._texCache[kind] = tex;
    return tex;
  }

  _buildMarkers() {
    if (!this.model) return;
    this.model.traverse((o) => {
      if (!o.isBone || !o.name) return;
      const kind = classifyBone(o.name);
      if (!kind) return;
      const mat = new THREE.SpriteMaterial({
        map: this._markerTexture(kind), depthTest: false, transparent: true,
      });
      const spr = new THREE.Sprite(mat);
      spr.scale.setScalar(KIND_SIZE[kind]);
      spr.renderOrder = 999;
      spr.userData = { boneName: o.name, kind };
      o.add(spr);
    });
  }

  _clearMarkers() {
    if (!this.model) return;
    this.model.traverse((o) => {
      if (o.isSprite && o.userData.boneName) {
        o.parent?.remove(o);
        o.material.dispose();
      }
    });
    for (const k in this._texCache) this._texCache[k].dispose();
    this._texCache = {};
  }

  keyBones() {
    const out = [];
    if (!this.model) return out;
    this.model.traverse((o) => {
      if (o.isBone && o.name) {
        const kind = classifyBone(o.name);
        if (kind) out.push(o);
      }
    });
    return out;
  }

  _frameCamera() {
    this.controls.target.fromArray(COMPARISON_SPACE.target);
    this.camera.position.fromArray(COMPARISON_SPACE.cameraPosition);
    this.camera.zoom = 1;
    this.camera.updateProjectionMatrix();
    this.controls.update();
  }

  _candidateTrailBones() {
    const cands = [];
    this.model.traverse((o) => {
      if (!o.isBone || !o.name) return;
      if (isRootBone(o.name) || /pelvis|hips/i.test(o.name)) cands.push(o);
    });
    return cands.slice(0, 2);
  }

  _sampleTrail() {
    if (!this.action || !this.clip || !this.model) return;
    const dur = this.clip.duration || 0;
    if (dur <= 0) return;
    const cands = this._candidateTrailBones();
    if (!cands.length) return;
    for (const line of this.trailLines) {
      line.parent?.remove(line); line.geometry.dispose(); line.material.dispose();
    }
    this.trailLines = [];
    // Preserve authored key times as well as 60 Hz subframes and both endpoints.
    const times = [...new Set([0, dur, ...this.clip.tracks.flatMap(t => Array.from(t.times)),
      ...Array.from({ length: Math.ceil(dur * 60) + 1 }, (_, i) => Math.min(i / 60, dur))])].sort((a, b) => a - b);
    const paths = cands.map(() => []);
    const wasPlaying = this.playing;
    const t0 = this.action.time;
    this.action.paused = false;
    const v = new THREE.Vector3();
    for (const time of times) {
      this.action.time = time;
      this.mixer.update(0);
      this.model.updateMatrixWorld(true);
      cands.forEach((b, ci) => {
        b.getWorldPosition(v);
        this.pivot.worldToLocal(v);
        paths[ci].push(v.clone());
      });
    }
    this.action.time = t0;
    this.mixer.update(0);
    this.action.paused = !wasPlaying;

    paths.forEach((pts, ci) => {
      const geo = new THREE.BufferGeometry().setFromPoints(pts);
      const mat = new THREE.LineBasicMaterial({ color: KIND_COLOR[classifyBone(cands[ci].name)],
        transparent: true, opacity: 0.85 });
      const line = new THREE.Line(geo, mat);
      line.userData.boneName = cands[ci].name;
      this.pivot.add(line); this.trailLines.push(line);
    });
    this.model.updateMatrixWorld(true);
  }

  originSnapshot() {
    this.model?.updateMatrixWorld(true);
    const position = b => b ? b.getWorldPosition(new THREE.Vector3()).toArray() : null;
    const bones = this.keyBones();
    return { timeSeconds: this.action?.time ?? null, durationSeconds: this.clip?.duration ?? null,
      sourceUp: this.contentUp, displayConvention: this.upAxis,
      ...originMetrics(position(bones.find(b => isRootBone(b.name))),
        position(bones.find(b => classifyBone(b.name) === 'pelvis'))), camera: this.cameraState() };
  }

  update(dt) {
    // Self-heal: viewers created while hidden get a 2x2 fallback buffer;
    // once the container becomes visible clientWidth/Height recover, so
    // re-sync the drawing buffer every frame when the layout size changes.
    const cw = this.canvas.clientWidth || 2;
    const ch = this.canvas.clientHeight || 2;
    if (cw !== this._lastW || ch !== this._lastH) this._resize();

    if (this.mixer && this.playing) this.mixer.update(dt * this.speed);
    this.controls.update();
    const m = this.originSnapshot();
    this.originGuide.visible = !!(m.rootCm && m.pelvisCm);
    if (this.originGuide.visible) {
      const vertices = this.originGuide.geometry.attributes.position;
      vertices.setXYZ(0, 0, .2, 0);
      vertices.setXYZ(1, m.rootCm[0], .2, m.rootCm[2]);
      vertices.setXYZ(2, m.pelvisCm[0], .2, m.pelvisCm[2]);
      vertices.needsUpdate = true;
      this.originGuide.geometry.computeBoundingSphere();
    }
    const fmt = v => v == null ? '未知' : v.toFixed(1);
    this.metricsLabel.textContent = `固定原点 O · cm\nroot→O ${fmt(m.rootOriginHorizontalCm)}\npelvis→O ${fmt(m.pelvisOriginHorizontalCm)}\npelvis→root ${fmt(m.pelvisRootHorizontalCm)}`;
    // Observation only: a source locomotion clip may intentionally travel.
    // Red highlights separation, not a universal animation acceptance verdict.
    this.metricsLabel.classList.toggle('bad', m.pelvisRootHorizontalCm > 50);
    this.renderer.render(this.scene, this.camera);
    if (this.action && this.clip) {
      const dur = this.clip.duration || 1;
      const t = this.action.time >= dur ? dur : this.action.time;
      this.onFrame(t, this.clip.duration || 0);
    }
  }
}
