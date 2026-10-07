import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { FBXLoader } from '../vendor/FBXLoader.js';
import { BVHLoader } from 'three/addons/loaders/BVHLoader.js';

export const KIND_COLOR = {
  root: 0xff7a1a, pelvis: 0xff3d81, ik: 0xb44cff,
  foot: 0x35d96b, hand: 0xffd23d, head: 0x38a9ff,
};
const KIND_SIZE = { root: 16, pelvis: 13, ik: 13, foot: 11, hand: 11, head: 11 };

export function classifyBone(name) {
  if (/^root$/i.test(name)) return 'root';
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

export class Viewer {
  constructor(canvas, opts = {}) {
    this.canvas = canvas;
    this.onLoaded = opts.onLoaded || (() => {});
    this.onError = opts.onError || (() => {});
    this.onFrame = opts.onFrame || (() => {});

    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    this.renderer.setClearColor(0x0f1115);
    this.scene = new THREE.Scene();
    this.camera = new THREE.PerspectiveCamera(50, 1, 0.5, 20000);
    this.camera.position.set(150, 140, 260);

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
    this.controls.enableDamping = true;
    this.controls.dampingFactor = 0.08;

    this.pivot = null;
    this.model = null;
    this.mixer = null;
    this.action = null;
    this.clip = null;
    this.takes = [];
    this.skelHelper = null;
    this.trailLine = null;
    this.trailBoneName = null;
    this._texCache = {};
    this.playing = true;
    this.speed = 1;
    this.loop = true;
    this.upAxis = 'y';
    this._resize();
  }

  _resize() {
    const w = this.canvas.clientWidth || 2;
    const h = this.canvas.clientHeight || 2;
    this.renderer.setSize(w, h, false);
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
  }

  resize() { this._resize(); }

  resetCamera() {
    if (!this.model) {
      this.camera.position.set(150, 140, 260);
      this.controls.target.set(0, 90, 0);
      return;
    }
    this._frameCamera();
  }

  setUpAxis(axis) {
    this.upAxis = axis;
    if (this.pivot) this.pivot.rotation.x = axis === 'z' ? -Math.PI / 2 : 0;
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
    if (!this.playing) {
      this.action.paused = false;
      this.mixer.update(0);
      this.action.paused = true;
    }
  }

  clearScene() {
    if (this.mixer) { this.mixer.stopAllAction(); this.mixer = null; }
    this.action = null;
    this.clip = null;
    this.takes = [];
    if (this.skelHelper) {
      this.scene.remove(this.skelHelper);
      this.skelHelper.dispose?.();
      this.skelHelper = null;
    }
    if (this.trailLine) {
      this.trailLine.parent?.remove(this.trailLine);
      this.trailLine.geometry.dispose();
      this.trailLine.material.dispose();
      this.trailLine = null;
    }
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
    this.pivot = new THREE.Group();
    this.scene.add(this.pivot);
    this.setUpAxis(this.upAxis);

    let object;
    let takes = [];
    if (ext === '.bvh') {
      const res = await new BVHLoader().loadAsync(url);
      if (!res || !res.skeleton || !res.clip) throw new Error('BVH 解析失败');
      // BVHLoader returns { skeleton, clip }; skeleton itself is a
      // THREE.Skeleton (no .traverse). bones[0] is the root Bone with the
      // whole hierarchy already parented — use it as the mount object.
      object = res.skeleton.bones[0];
      if (!object) throw new Error('BVH 无骨骼');
      takes = [res.clip];
    } else {
      const buf = await new THREE.FileLoader()
        .setResponseType('arraybuffer')
        .loadAsync(url);
      object = new FBXLoader().parse(buf, '');
      takes = object.animations ? object.animations.slice() : [];
    }

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
      this._sampleTrail();
    }
    this._frameCamera();
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
    const box = new THREE.Box3();
    const v = new THREE.Vector3();
    let any = false;
    this.model.traverse((o) => {
      o.getWorldPosition(v);
      box.expandByPoint(v);
      any = true;
    });
    if (!any) return;
    const center = box.getCenter(new THREE.Vector3());
    const size = box.getSize(new THREE.Vector3());
    const maxDim = Math.max(size.x, size.y, size.z, 20);
    this.controls.target.copy(center);
    const dirv = new THREE.Vector3(1, 0.5, 1.35).normalize();
    this.camera.position.copy(center).addScaledVector(dirv, maxDim * 1.55);
    this.camera.near = Math.max(maxDim / 400, 0.1);
    this.camera.far = maxDim * 80;
    this.camera.updateProjectionMatrix();
    const gs = Math.max(1, maxDim / 200);
    this.grid.scale.setScalar(gs);
    this.axes.scale.setScalar(Math.max(1, maxDim / 120));
  }

  _candidateTrailBones() {
    const cands = [];
    this.model.traverse((o) => {
      if (!o.isBone || !o.name) return;
      if (/^root$/i.test(o.name) || /pelvis|hips/i.test(o.name)) cands.push(o);
    });
    return cands.slice(0, 2);
  }

  _sampleTrail() {
    if (!this.action || !this.clip || !this.model) return;
    const dur = this.clip.duration || 0;
    if (dur <= 0) return;
    const cands = this._candidateTrailBones();
    if (!cands.length) return;
    const N = Math.max(2, Math.min(120, Math.round(dur * 30)));
    const paths = cands.map(() => []);
    const wasPlaying = this.playing;
    const t0 = this.action.time;
    this.action.paused = false;
    const v = new THREE.Vector3();
    for (let i = 0; i < N; i++) {
      this.action.time = (dur * i) / (N - 1);
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

    let best = -1, bestLen = -1;
    paths.forEach((pts, ci) => {
      let len = 0;
      for (let i = 1; i < pts.length; i++) len += pts[i].distanceTo(pts[i - 1]);
      if (len > bestLen) { bestLen = len; best = ci; }
    });
    if (best < 0 || bestLen < 0.5) return;
    const geo = new THREE.BufferGeometry().setFromPoints(paths[best]);
    const mat = new THREE.LineBasicMaterial({ color: 0xffb02e, transparent: true, opacity: 0.85 });
    this.trailLine = new THREE.Line(geo, mat);
    this.trailLine.renderOrder = 1;
    this.pivot.add(this.trailLine);
    this.trailBoneName = cands[best].name;
  }

  update(dt) {
    if (this.mixer && this.playing) this.mixer.update(dt * this.speed);
    this.controls.update();
    this.renderer.render(this.scene, this.camera);
    if (this.action && this.clip) {
      const dur = this.clip.duration || 1;
      const t = this.action.time >= dur ? dur : this.action.time;
      this.onFrame(t, this.clip.duration || 0);
    }
  }
}
