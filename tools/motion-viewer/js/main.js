import * as THREE from 'three';
import { Viewer, classifyBone, KIND_COLOR } from './viewport.js';
import { analyzeClip, displacementRows, fmtNum, humanSize } from './stats.js';

const $ = (id) => document.getElementById(id);

const state = {
  stages: [], stageId: null, files: [], filterText: '',
  activeSlot: 'A', mode: 'single',
  loaded: { A: null, B: null },
  sidecar: { A: null, B: null },
};

const viewers = {};

function showError(msg) {
  const bar = $('errBar');
  if (!msg) { bar.hidden = true; return; }
  bar.hidden = false;
  bar.textContent = '⚠ ' + msg;
}

async function fetchJSON(url) {
  const res = await fetch(url);
  if (!res.ok) throw new Error(`${url} → HTTP ${res.status}`);
  return res.json();
}

function init() {
  viewers.A = new Viewer($('canvasA'), {
    onFrame: (t, dur) => onFrame('A', t, dur),
  });
  viewers.B = new Viewer($('canvasB'), {
    onFrame: (t, dur) => onFrame('B', t, dur),
  });
  bindUI();
  void boot();
}

async function boot() {
  try {
    const data = await fetchJSON('/api/stages');
    state.stages = data.stages;
    const sel = $('stageSelect');
    sel.innerHTML = '';
    for (const s of state.stages) {
      const opt = document.createElement('option');
      opt.value = s.id;
      opt.textContent = s.name;
      sel.appendChild(opt);
    }
    sel.value = state.stages[0].id;
    await loadStage(state.stages[0].id);
  } catch (e) {
    showError('无法获取阶段目录：' + e.message);
  }
}

async function loadStage(stageId) {
  state.stageId = stageId;
  state.filterText = '';
  $('fileSearch').value = '';
  try {
    const data = await fetchJSON('/api/list?stage=' + encodeURIComponent(stageId));
    state.files = data.files;
    const st = state.stages.find((s) => s.id === stageId);
    $('stageMeta').textContent =
      `${st ? st.desc : ''} · 共 ${data.count} 个文件` + (st && st.expect ? ` · 预期：${st.expect}` : '');
    renderFileList();
  } catch (e) {
    state.files = [];
    renderFileList();
    showError('读取阶段清单失败：' + e.message);
  }
}

function renderFileList() {
  const ul = $('fileList');
  ul.innerHTML = '';
  const f = state.filterText.toLowerCase();
  const files = state.files.filter((x) => !f || x.path.toLowerCase().includes(f));
  const frag = document.createDocumentFragment();
  for (const file of files) {
    const li = document.createElement('li');
    const name = document.createElement('span');
    name.className = 'name';
    name.textContent = file.name;
    name.title = file.path;
    const size = document.createElement('span');
    size.className = 'size';
    size.textContent = humanSize(file.size);
    li.appendChild(name);
    if (file.sidecars && file.sidecars.length) {
      const s = document.createElement('span');
      s.className = 'side';
      s.textContent = 'Ⓢ';
      s.title = '存在溯源 JSON：' + file.sidecars.join(', ');
      li.appendChild(s);
    }
    li.appendChild(size);
    li.onclick = () => loadFile(file, state.activeSlot);
    li.oncontextmenu = (ev) => {
      ev.preventDefault();
      loadFile(file, state.activeSlot === 'A' ? 'B' : 'A');
    };
    frag.appendChild(li);
  }
  ul.appendChild(frag);
}

async function loadFile(file, slot) {
  const v = viewers[slot];
  const ext = file.path.slice(file.path.lastIndexOf('.')).toLowerCase();
  const url = '/file/' + encodeURIComponent(file.path);
  setActiveSlot(slot);
  showError('');
  if (ext === '.json') {
    v.clearScene();
    state.loaded[slot] = { file, takes: 0 };
    $('badge' + slot).textContent = `${file.name} · JSON 文本`;
    renderJsonFile(slot, file, url);
    return;
  }
  $('badge' + slot).textContent = '加载中…';
  try {
    const { takes } = await v.load(url, ext, file.name);
    state.loaded[slot] = { file, takes: takes.length };
    $('badge' + slot).textContent = `${file.name} · ${takes.length} take`;
    renderTabs(slot);
    renderClipSelect(slot, takes.length);
    renderSidecar(slot, file);
  } catch (e) {
    state.loaded[slot] = null;
    $('badge' + slot).textContent = '（加载失败）';
    showError(`加载失败 ${file.name}：${e.message}`);
  }
}

async function renderJsonFile(slot, file, url) {
  const box = $('tab-sidecar');
  try {
    const data = await fetchJSON(url);
    box.innerHTML = '<pre>' + escapeHTML(JSON.stringify(data, null, 2)) + '</pre>';
    document.querySelector('.tabBtn[data-tab="sidecar"]').click();
    $('tab-summary').innerHTML =
      `<div class="kv"><b>文件</b><span>${escapeHTML(file.name)}</span>` +
      `<b>类型</b><span>JSON 文本（在“溯源 JSON”标签页查看内容）</span></div>`;
    $('tab-disp').innerHTML = '<div class="muted">JSON 文件无动画轨道</div>';
    $('tab-bones').innerHTML = '<div class="muted">JSON 文件无骨骼</div>';
  } catch (e) {
    box.innerHTML = '<div class="muted">JSON 读取失败：' + escapeHTML(e.message) + '</div>';
  }
}

function setActiveSlot(slot) {
  state.activeSlot = slot;
  $('vpA').classList.toggle('active', slot === 'A');
  $('vpB').classList.toggle('active', slot === 'B');
}

function renderClipSelect(slot, takeCount) {
  const sel = $('clipSelect');
  if (takeCount > 1) {
    sel.hidden = false;
    sel.innerHTML = '';
    takesOptions(sel, takeCount);
  } else {
    sel.hidden = true;
  }
}

function takesOptions(sel, n) {
  for (let i = 0; i < n; i++) {
    const o = document.createElement('option');
    o.value = i;
    o.textContent = `Take ${i + 1}`;
    sel.appendChild(o);
  }
}

function renderTabs(slot) {
  const v = viewers[slot];
  const clip = v.clip;
  const ana = analyzeClip(clip);
  const name = state.loaded[slot]?.file?.name || '—';
  const stage = state.stages.find((s) => s.id === state.stageId);

  let bones = 0;
  v.model?.traverse((o) => { if (o.isBone) bones++; });

  const rows = displacementRows(clip);
  const moving = rows.filter((r) => r.moving);
  const movingList = moving.map((r) => r.bone).join('、') || '（无）';
  const rootRow = rows.find((r) => /^root$/i.test(r.bone) || /__viewer_root__/.test(r.bone));
  const pelvisRow = rows.find((r) => /pelvis|hips/i.test(r.bone));

  const diag = diagnose(stage?.id, rows);
  const summaryHTML = `
    <div class="kv">
      <b>文件</b><span>${escapeHTML(name)}</span>
      <b>Take 数</b><span>${v.takes.length}</span>
      <b>时长</b><span>${fmtNum(clip?.duration ?? 0, 2)} s</span>
      <b>帧率</b><span>${ana.fps ? fmtNum(ana.fps, 1) + ' Hz' : '—'}</span>
      <b>帧数</b><span>${ana.frames || '—'}</span>
      <b>骨骼总数</b><span>${bones}</span>
      <b>动画骨骼</b><span>${ana.animatedNodes.length}</span>
      <b>旋转/位移轨道</b><span>${ana.quatTracks.length} / ${ana.posTracks.length}</span>
      <b>有位移的骨骼</b><span>${moving.length} 个：${escapeHTML(movingList)}</span>
    </div>
    <div class="muted small" style="margin-top:8px">root 位移范围：${rootRow ? `X ${fmtNum(rootRow.rangeX)} / Y ${fmtNum(rootRow.rangeY)} / Z ${fmtNum(rootRow.rangeZ)}` : '无位移轨道'} · pelvis/Hips 位移范围：${pelvisRow ? `X ${fmtNum(pelvisRow.rangeX)} / Y ${fmtNum(pelvisRow.rangeY)} / Z ${fmtNum(pelvisRow.rangeZ)}` : '无位移轨道'}</div>
    ${diag ? `<div class="${diag.ok ? 'ok' : 'bad'}" style="margin-top:8px">${diag.ok ? '✓ ' : '✗ '}${escapeHTML(diag.text)}</div>` : ''}
  `;
  $('tab-summary').innerHTML = summaryHTML;

  const dispTable = rows.length
    ? `<table class="dataTable"><tr><th>骨骼</th><th class="num">keys</th><th class="num">X min~max</th><th class="num">Y min~max</th><th class="num">Z min~max</th><th class="num">位移幅度</th></tr>` +
      rows.map((r) =>
        `<tr class="${r.moving ? '' : 'muted'}"><td>${escapeHTML(r.bone)}</td><td class="num">${r.keys}</td>` +
        `<td class="num">${fmtNum(r.x?.min)}~${fmtNum(r.x?.max)}</td>` +
        `<td class="num">${fmtNum(r.y?.min)}~${fmtNum(r.y?.max)}</td>` +
        `<td class="num">${fmtNum(r.z?.min)}~${fmtNum(r.z?.max)}</td>` +
        `<td class="num">${r.moving ? fmtNum(r.rangeX + r.rangeY + r.rangeZ) : '0'}</td></tr>`
      ).join('') + '</table>'
    : '<div class="muted">无位移轨道</div>';
  $('tab-disp').innerHTML = dispTable;

  renderBoneTab(v);
}

function diagnose(stageId, rows) {
  const rootRow = rows.find((r) => /^root$/i.test(r.bone) || /__viewer_root__/.test(r.bone));
  const pelvisRow = rows.find((r) => /pelvis|hips/i.test(r.bone));
  const rootMoving = rootRow && (rootRow.rangeX + rootRow.rangeY + rootRow.rangeZ) > 0.01;
  const pelvisMoving = pelvisRow && (pelvisRow.rangeX + pelvisRow.rangeY + pelvisRow.rangeZ) > 0.01;
  if (stageId === 'kimodo-bvh') {
    if (pelvisMoving && !rootMoving) return { ok: true, text: 'BVH 预期校验通过：位移在 Hips，Root 静止' };
    return { ok: false, text: 'BVH 位移分布异常：预期 Hips 位移、Root 静止' };
  }
  if (stageId === 'retarget-fbx') {
    if (pelvisMoving && !rootMoving) return { ok: true, text: '重定向预期校验通过：位移在 pelvis，root 静止' };
    return { ok: false, text: '重定向位移分布异常：预期 pelvis 位移、root 静止' };
  }
  if (stageId === 'rootik-fbx') {
    if (rootMoving) return { ok: true, text: '根烘焙预期校验通过：位移已提取到 root' };
    return { ok: false, text: '根烘焙位移异常：root 位移轨道接近 0，疑似未烘焙' };
  }
  return null;
}

function renderBoneTab(v) {
  const bones = v.keyBones();
  if (!bones.length) {
    $('tab-bones').innerHTML = '<div class="muted">未识别到关键骨骼</div>';
    return;
  }
  const tmp = new THREE.Vector3();
  const rowsHTML = bones.map((b) => {
    b.getWorldPosition(tmp);
    const kind = classifyBone(b.name);
    const hex = '#' + KIND_COLOR[kind]?.toString(16).padStart(6, '0');
    return `<tr><td><i class="dot" style="background:${hex || '#888'};display:inline-block;width:9px;height:9px;border-radius:50%;margin-right:6px"></i>${escapeHTML(b.name)}</td>` +
      `<td class="num">${fmtNum(tmp.x, 0)}</td><td class="num">${fmtNum(tmp.y, 0)}</td><td class="num">${fmtNum(tmp.z, 0)}</td></tr>`;
  }).join('');
  $('tab-bones').innerHTML =
    `<div class="muted small" style="margin-bottom:6px">当前帧关键骨骼世界坐标（每帧刷新需切标签页触发）</div>` +
    `<table class="dataTable"><tr><th>骨骼</th><th class="num">X</th><th class="num">Y</th><th class="num">Z</th></tr>${rowsHTML}</table>`;
}

async function renderSidecar(slot, file) {
  state.sidecar[slot] = null;
  const box = $('tab-sidecar');
  const candidates = (file.sidecars || []).map((s) => '/file/' + encodeURIComponent(file.path + s));
  const nearby = [
    '/file/' + encodeURIComponent(file.path.replace(/\.fbx$/, '') + '.source.json'),
    '/file/' + encodeURIComponent(file.path.replace(/\.fbx$/, '') + '.verification.json'),
  ];
  const urls = [...candidates, ...nearby];
  for (const url of urls) {
    try {
      const data = await fetchJSON(url);
      state.sidecar[slot] = data;
      box.innerHTML = '<pre>' + escapeHTML(JSON.stringify(data, null, 2)) + '</pre>';
      return;
    } catch (e) { /* try next */ }
  }
  box.innerHTML = '<div class="muted">无溯源 JSON</div>';
}

function escapeHTML(s) {
  return String(s).replace(/[&<>"']/g, (c) => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
  }[c]));
}

let frameCounter = 0;
function onFrame(slot, t, dur) {
  if (state.mode === 'ab') {
    if (state.syncAB && slot === 'A') {
      const other = viewers.B;
      if (other.action && other.clip) {
        const oDur = other.clip.duration || dur || 1;
        other.action.time = (t / (dur || 1)) * oDur;
        if (!other.playing) {
          other.action.paused = false;
          other.mixer.update(0);
          other.action.paused = true;
        }
      }
    }
    if (slot === state.activeSlot) updateTransport(t, dur);
  } else if (slot === 'A') {
    updateTransport(t, dur);
  }
  frameCounter++;
  if (frameCounter % 30 === 0 && !$('tab-bones').hidden) {
    renderBoneTab(viewers[state.activeSlot]);
  }
}

function updateTransport(t, dur) {
  const slider = $('timeSlider');
  if (document.activeElement !== slider) {
    slider.max = dur || 1;
    slider.value = t;
  }
  $('timeText').textContent = `${t.toFixed(2)} / ${(dur || 0).toFixed(2)} s`;
}

function bindUI() {
  $('stageSelect').onchange = (e) => loadStage(e.target.value);
  $('fileSearch').oninput = (e) => {
    state.filterText = e.target.value;
    renderFileList();
  };
  $('modeBtn').onclick = toggleABMode;
  $('playBtn').onclick = () => {
    const v = viewers[state.activeSlot];
    const play = !v.playing;
    viewers.A.setPlaying(play);
    viewers.B.setPlaying(play);
    $('playBtn').textContent = play ? '暂停' : '播放';
  };
  $('timeSlider').oninput = (e) => {
    viewers[state.activeSlot].setTime(parseFloat(e.target.value));
    if (state.mode === 'ab' && state.syncAB) syncTimeToB(parseFloat(e.target.value));
  };
  $('speedSelect').onchange = (e) => {
    const s = parseFloat(e.target.value);
    viewers.A.setSpeed(s);
    viewers.B.setSpeed(s);
  };
  $('loopChk').onchange = (e) => {
    viewers.A.setLoop(e.target.checked);
    viewers.B.setLoop(e.target.checked);
  };
  $('upSelect').onchange = (e) => {
    viewers[state.activeSlot].setUpAxis(e.target.value);
    viewers[state.activeSlot].resetCamera();
  };
  $('resetCamBtn').onclick = () => viewers[state.activeSlot].resetCamera();
  $('clipSelect').onchange = (e) => {
    viewers[state.activeSlot].playTake(parseInt(e.target.value, 10));
    renderTabs(state.activeSlot);
  };

  for (const id of ['vpA', 'vpB']) {
    $(id).addEventListener('mousedown', () => setActiveSlot(id.slice(-1)));
  }

  document.querySelectorAll('.tabBtn').forEach((btn) => {
    btn.onclick = () => {
      document.querySelectorAll('.tabBtn').forEach((b) => b.classList.remove('active'));
      btn.classList.add('active');
      document.querySelectorAll('.tab').forEach((t) => (t.hidden = true));
      $('tab-' + btn.dataset.tab).hidden = false;
      if (btn.dataset.tab === 'bones') renderBoneTab(viewers[state.activeSlot]);
    };
  });

  window.addEventListener('resize', onResize);
}

function syncTimeToB(t) {
  const a = viewers.A, b = viewers.B;
  if (!a.action || !b.action || !a.clip || !b.clip) return;
  const ratio = a.clip.duration ? t / a.clip.duration : 0;
  b.action.time = ratio * (b.clip.duration || 0);
  if (!b.playing) {
    b.action.paused = false;
    b.mixer.update(0);
    b.action.paused = true;
  }
}

function toggleABMode() {
  state.mode = state.mode === 'ab' ? 'single' : 'ab';
  const ab = state.mode === 'ab';
  $('vpB').hidden = !ab;
  $('syncWrap').hidden = !ab;
  $('modeBtn').textContent = ab ? '退出 A/B' : 'A/B 对比';
  $('badgeB').textContent = ab && !state.loaded.B ? '（未加载）' : $('badgeB').textContent;
  onResize();
  if (ab && !state.loaded.B) {
    showError('A/B 模式已开启：请先点 A 视口激活，再右键文件列表项加载到 B 视口');
  } else {
    showError('');
  }
}

function onResize() {
  viewers.A.resize();
  viewers.B.resize();
}

const clock = new THREE.Clock();
function tick() {
  const dt = Math.min(clock.getDelta(), 0.1);
  viewers.A.update(dt);
  if (state.mode === 'ab') viewers.B.update(dt);
  requestAnimationFrame(tick);
}

init();
tick();
