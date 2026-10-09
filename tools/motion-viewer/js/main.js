import * as THREE from 'three';
import { Viewer, classifyBone, KIND_COLOR } from './viewport.js';
import { analyzeClip, displacementRows, fmtNum, humanSize } from './stats.js';
import { COMPARISON_SPACE } from './comparison-space.js';

const $ = (id) => document.getElementById(id);

const state = {
  stages: [], stageId: null, files: [], filterText: '',
  activeSlot: 'A', mode: 'single',
  loaded: { A: null, B: null },
  sidecar: { A: null, B: null },
  chain: { items: [], chainStages: [], short: {}, filterText: '', selectedKey: null, viewers: {}, loaded: {} },
};

const viewers = {};
let sharedCamera = null;
let copyingCamera = false;
function allViewers() { return [...Object.values(viewers), ...Object.values(state.chain.viewers)]; }
function shareCamera(camera) {
  if (copyingCamera) return;
  copyingCamera = true;
  sharedCamera = camera;
  for (const v of allViewers()) v.applyCameraState(camera);
  copyingCamera = false;
}

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
    onCameraChange: shareCamera,
    onFrame: (t, dur) => onFrame('A', t, dur),
  });
  viewers.B = new Viewer($('canvasB'), {
    onCameraChange: shareCamera,
    onFrame: (t, dur) => onFrame('B', t, dur),
  });
  bindUI();
  viewers.A.resetCamera();
  void boot().then(() => {
    // 默认进入链路比对模式（用户主用法）：等链路索引就绪后自动切换
    if (!state.batchId && state.mode === 'single' && state.chain.items.length) {
      ensureChainViewers();
      toggleChainMode();
    }
  });
}

async function boot(batchId = null) {
  const requestId = state.catalogRequest = (state.catalogRequest || 0) + 1;
  for (const id of ['batchSelect', 'stageSelect', 'refreshBtn', 'chainBtn', 'modeBtn']) $(id).disabled = true;
  try {
    const data = await fetchJSON('/api/stages'+(batchId ? '?batch='+encodeURIComponent(batchId) : ''));
    if (state.catalogRequest !== requestId) return;
    state.stages = data.stages;
    state.activePack = data.activePack;
    state.batchId = data.authoringBatch?.id || null;
    const batchSelect = $('batchSelect');
    batchSelect.hidden = !data.batches?.length;
    batchSelect.innerHTML = '';
    for (const batch of data.batches || []) {
      const option = document.createElement('option');
      option.value = batch.id; option.textContent = batch.label+' · '+batch.status;
      batchSelect.appendChild(option);
    }
    if (state.batchId) batchSelect.value = state.batchId;
    const comparison = $('compareBatch'), previousComparison = comparison.value;
    comparison.replaceChildren();
    for (const batch of data.batches || []) {
      const option = document.createElement('option'); option.value = batch.id; option.textContent = batch.label;
      comparison.appendChild(option);
    }
    comparison.value = data.batches?.some(b=>b.id===previousComparison) ? previousComparison
      : (data.batches || []).find(b=>b.id!==state.batchId)?.id || state.batchId;
    $('serverInfo').textContent = state.batchId
      ? `批次 · ${data.authoringBatch.status} · ${data.authoringBatch.label}`
      : `固定原点 · cm · 活动包 ${data.activePack?.pack || '原版'}`;
    $('serverInfo').title = $('serverInfo').textContent;
    const sel = $('stageSelect');
    sel.innerHTML = '';
    for (const s of state.stages) {
      const opt = document.createElement('option');
      opt.value = s.id;
      opt.textContent = s.name;
      sel.appendChild(opt);
    }
    sel.value = state.stages[0].id;
    await Promise.all([
      loadStage(state.stages[0].id),
      bootChain(),
    ]);
    if (state.batchId && state.files.length && state.mode !== 'chain') {
      viewers.A.setPlaying(false);
      $('playBtn').textContent = '播放';
      await loadFile(state.files[0], 'A');
      viewers.A.setTime(0);
    }
  } catch (e) {
    showError('无法获取阶段目录：' + e.message);
  } finally {
    if (state.catalogRequest === requestId) {
      for (const id of ['batchSelect', 'refreshBtn', 'chainBtn', 'modeBtn']) $(id).disabled = false;
      $('stageSelect').disabled = state.mode === 'chain';
    }
  }
}

const CHAIN_STAGE_ORDER = ['kimodo-bvh', 'retarget-fbx', 'rootik-fbx', 'kimodo-godot', 'als-baseline'];
const CHAIN_COLORS = { 'authoring-generated':'#35d96b', 'authoring-master':'#64b7ff', 'kimodo-bvh': '#35d96b', 'retarget-fbx': '#3d8bff', 'rootik-fbx': '#b44cff', 'kimodo-godot': '#ff3d81', 'als-baseline': '#ffb02e' };

function assetGroupOf(key) {
  const m = key.match(/^ALS_([A-Za-z]+)_/);
  if (!m) return '其他';
  const tag = m[1];
  if (tag === 'CLF') return 'CLF 蹲';
  if (tag === 'CRF') return 'CRF 跑姿';
  if (tag === 'N') return 'N 站立';
  if (tag === 'Props') return 'Props 道具';
  if (tag === 'StanceVariation') return 'StanceVariation 姿态';
  return tag;
}

async function bootChain() {
  const requestId = state.chain.catalogRequest = (state.chain.catalogRequest || 0)+1;
  try {
    const query = new URLSearchParams({view:$('chainView').value, phase:$('comparePhase').value});
    if (state.batchId) query.set('batch',state.batchId);
    if ($('compareBatch').value) query.set('compare',$('compareBatch').value);
    const data = await fetchJSON('/api/chain?'+query);
    if (state.chain.catalogRequest !== requestId) return;
    state.chain.items = data.items;
    state.chain.chainStages = data.chainStages;
    state.chain.short = data.short || {};
    state.chain.columns = data.columns || [];
    const compare = $('chainView').value==='batches';
    $('compareBatch').hidden = $('comparePhase').hidden = !compare;
    renderChainList();
  } catch (e) {
    showError('无法获取链路索引：' + e.message);
  }
}

function renderChainList() {
  const ul = $('chainList');
  if (!ul) return;
  const c = state.chain;
  const f = c.filterText.toLowerCase();
  ul.innerHTML = '';
  const frag = document.createDocumentFragment();
  const groups = new Map();
  for (const item of c.items) {
    if (f && !item.key.toLowerCase().includes(f)) continue;
    const g = assetGroupOf(item.key);
    if (!groups.has(g)) groups.set(g, []);
    groups.get(g).push(item);
  }
  for (const [g, items] of groups) {
    const gh = document.createElement('div');
    gh.className = 'grp';
    gh.textContent = g + ' · ' + items.length + ' 条';
    frag.appendChild(gh);
    for (const item of items) {
      const li = document.createElement('li');
      li.dataset.key = item.key;
      const name = document.createElement('span');
      name.className = 'name';
      name.textContent = item.key;
      name.title = item.key;
      li.appendChild(name);
      const chips = document.createElement('span');
      chips.className = 'chips';
      for (const sid of c.chainStages) {
        const on = !!item.stages[sid];
        const chip = document.createElement('i');
        chip.className = 'chip ' + (on ? 'on' : 'off');
        chip.style.setProperty('--c', CHAIN_COLORS[sid] || '#888');
        chip.title = (c.short[sid] || sid) + (on ? ' · 有' : ' · 无');
        chips.appendChild(chip);
      }
      li.appendChild(chips);
      li.onclick = () => selectChainAsset(item.key);
      frag.appendChild(li);
    }
  }
  ul.appendChild(frag);
}

function toggleChainMode() {
  const chainOn = state.mode !== 'chain';
  if (chainOn) {
    if (state.mode === 'ab') toggleABMode();
    state.mode = 'chain';
    $('viewports').hidden = true;
    $('vpChain').hidden = false;
    $('chainBtn').classList.add('primary');
    $('modeBtn').classList.remove('primary');
    $('modeBtn').textContent = 'A/B 对比';
    $('syncWrap').hidden = false;
    $('syncLabel').textContent = ' 链路同步';
    $('chainCluster').hidden = false;
    $('fileSearch').hidden = true;
    $('stageSelect').disabled = true;
    $('fileList').hidden = true;
    $('chainList').hidden = false;
    $('stageMeta').hidden = true;
    onResize();
    if (!state.chain.selectedKey && state.chain.items.length) {
      selectChainAsset(state.chain.items[0].key);
    }
  } else {
    state.mode = 'single';
    $('viewports').hidden = false;
    $('vpChain').hidden = true;
    $('chainBtn').classList.remove('primary');
    $('chainCluster').hidden = true;
    $('fileSearch').hidden = false;
    $('stageSelect').disabled = false;
    $('fileList').hidden = false;
    $('chainList').hidden = true;
    $('stageMeta').hidden = false;
    $('syncLabel').textContent = ' A/B 同步';
    $('syncWrap').hidden = true;
    onResize();
  }
}

async function selectChainAsset(key) {
  showError('');
  state.chain.selectedKey = key;
  document.querySelectorAll('#chainList li[data-key]').forEach((li) => {
    li.classList.toggle('sel', li.dataset.key === key);
  });
  const item = state.chain.items.find((it) => it.key === key);
  if (!item) return;
  renderChainSummary(item);
  const c = state.chain;
  const requestId = c.requestId = (c.requestId || 0) + 1;
  for (const sid of c.chainStages) {
    c.viewers[sid]?.clearScene();
    c.loaded[sid] = null;
  }
  $('chainMeta').textContent = '加载链路…';
  for (const id of ['timeSlider', 'firstFrameBtn', 'playBtn', 'exportOriginBtn']) $(id).disabled = true;
  for (const sid of c.chainStages) {
    const vp = c.viewers[sid];
    const host = $('vpchain-' + sid);
    if (!vp || !host) continue;
    const entry = item.stages[sid];
    if (!entry) {
      vp.clearScene();
      c.loaded[sid] = null;
      const badge = host.querySelector('.vp-badge');
      badge.textContent = '（本批未产出此阶段）';
      badge.classList.add('muted');
      continue;
    }
    const badge = host.querySelector('.vp-badge');
    badge.classList.remove('muted');
    badge.textContent = '加载中…';
    const ext = entry.path.slice(entry.path.lastIndexOf('.')).toLowerCase();
    const url = entry.url || '/file/' + encodeURIComponent(entry.path);
    try {
      const { takes } = await vp.load(url, entry.kind==='source-motion' ? '.source-motion' : ext, entry.name);
      if (c.requestId !== requestId) return;
      c.loaded[sid] = { file: entry, takes: takes.length };
      badge.textContent = `${entry.name} · ${entry.qualification || takes.length+' take'}`;
      vp.setPlaying(false); vp.setTime(0);
    } catch (e) {
      if (c.requestId !== requestId) return;
      c.loaded[sid] = null;
      badge.textContent = '（加载失败）';
      showError(`加载失败 ${entry.name}：${e.message}`);
    }
  }
  c.master = c.chainStages.find(sid=>c.loaded[sid]) || c.chainStages[0];
  for (const sid of c.chainStages) $('vpchain-'+sid)?.classList.toggle('active',sid===c.master);
  $('playBtn').textContent = '播放';
  if ($('syncAB').checked) syncChainTo(0);
  $('chainMeta').textContent = `链路就绪 · ${Object.values(c.loaded).filter(Boolean).length} 栏`;
  for (const id of ['timeSlider', 'firstFrameBtn', 'playBtn', 'exportOriginBtn']) $(id).disabled = false;
}

function renderChainSummary(item) {
  const rows = state.chain.chainStages.map((sid) => {
    const e = item.stages[sid];
    if (!e) return `<tr><td>${escapeHTML(state.chain.short[sid] || sid)}</td><td class="muted">无</td><td class="muted">—</td></tr>`;
    const metrics = e.source?.metrics;
    const measurement = metrics ? `root→O 最大 ${metrics.maximumRootHorizontalCm.toFixed(2)} cm · pelvis→root 最大 ${metrics.maximumPelvisRootHorizontalCm.toFixed(2)} cm` : '';
    return `<tr><td>${escapeHTML(state.chain.short[sid] || sid)}</td><td>${escapeHTML(e.name)}<br><span class="muted">${escapeHTML(e.qualification || '')} ${measurement}</span></td><td class="num">${humanSize(e.size)}</td></tr>`;
  }).join('');
  $('tab-summary').innerHTML =
    `<div class="muted small" style="margin-bottom:6px">链路资源 <b>${escapeHTML(item.key)}</b> 在各阶段的产物：</div>` +
    `<table class="dataTable"><tr><th>阶段</th><th>文件</th><th class="num">大小</th></tr>${rows}</table>` +
    `<div class="muted small" style="margin-top:8px">固定原点 · cm · 同一相机。当前批次的空阶段表示尚未产出／门禁阻断；独立对照栏不是本批上游。生成候选的静止 Root 只供显示，水平分解栏才有真实 root 轨道；ALS 原地栏锁定 root，保留 pelvis 自然残差。同步按时长比例，尚非支撑相位对齐。</div>`;
  $('tab-sidecar').innerHTML = '<pre>'+escapeHTML(JSON.stringify(Object.fromEntries(Object.entries(item.stages).map(([sid,e])=>[sid,{file:e.path,sha256:e.sha256,source:e.source || null}])),null,2))+'</pre>';
  document.querySelector('.tabBtn[data-tab="summary"]').click();
}

function ensureChainViewers() {
  const c = state.chain;
  const signature = c.chainStages.join('|');
  if (c.viewerSignature === signature) {
    for (const sid of c.chainStages) $('vpchain-'+sid).querySelector('.vp-label').textContent = c.short[sid] || sid;
    return;
  }
  for (const viewer of Object.values(c.viewers)) {
    viewer.clearScene(); viewer.controls.dispose(); viewer.renderer.dispose(); viewer.renderer.forceContextLoss();
  }
  $('vpChain').replaceChildren(); c.viewers = {}; c.loaded = {}; c.master = null; c.viewerSignature = signature;
  for (const sid of c.chainStages) {
    const host = document.createElement('div');
    host.className = 'vp';
    host.id = 'vpchain-' + sid;
    const canvas = document.createElement('canvas');
    host.appendChild(canvas);
    const label = document.createElement('div');
    label.className = 'vp-label';
    label.textContent = c.short[sid] || sid;
    host.appendChild(label);
    const badge = document.createElement('div');
    badge.className = 'vp-badge';
    badge.textContent = '（未加载）';
    host.appendChild(badge);
    $('vpChain').appendChild(host);
    const v = new Viewer(canvas, {
      onCameraChange: shareCamera,
      onFrame: (t, dur) => {
        if (state.mode === 'chain' && state.chain.master === sid) updateTransport(t, dur);
      },
    });
    host.addEventListener('mousedown', () => {
      if (state.chain.master !== sid) {
        state.chain.master = sid;
        for (const s in c.viewers) {
          const h = $('vpchain-' + s);
          if (h) h.classList.toggle('active', s === sid);
        }
      }
    });
    c.viewers[sid] = v;
    if (sharedCamera) v.applyCameraState(sharedCamera);
    state.chain.master = state.chain.master || sid;
  }
  const first = c.chainStages[0];
  if (first && $('vpchain-' + first)) $('vpchain-' + first).classList.add('active');
}

function syncChainTo(t) {
  const c = state.chain;
  const m = c.viewers[c.master];
  if (!m?.action || !m?.clip) return;
  const mDur = m.clip.duration || 1;
  m.setTime(t);
  syncChainToRatio(m.action.time / mDur);
}

function syncChainToRatio(ratio) {
  const c = state.chain;
  for (const sid of c.chainStages) {
    if (sid === c.master) continue;
    const v = c.viewers[sid];
    if (!v?.action || !v?.clip) continue;
    const dur = v.clip.duration || 1;
    const target = Math.max(0, Math.min(ratio * dur, dur));
    v.setTime(target);
  }
}

async function loadStage(stageId) {
  const requestId = state.listRequest = (state.listRequest || 0) + 1;
  state.stageId = stageId;
  state.filterText = '';
  $('fileSearch').value = '';
  try {
    const data = await fetchJSON('/api/list?stage=' + encodeURIComponent(stageId)+(state.batchId ? '&batch='+encodeURIComponent(state.batchId) : ''));
    if (state.listRequest !== requestId) return;
    state.files = data.files;
    const st = state.stages.find((s) => s.id === stageId);
    $('stageMeta').textContent =
      `${st ? st.desc : ''} · 共 ${data.count} 个文件` + (st && st.expect ? ` · 预期：${st.expect}` : '');
    renderFileList();
  } catch (e) {
    if (state.listRequest !== requestId) return;
    state.files = [];
    renderFileList();
    showError('读取阶段清单失败：' + e.message);
  }
}

function renderFileList() {
  const ul = $('fileList');
  ul.innerHTML = '';
  const f = state.filterText.toLowerCase();
  const files = state.files.filter((x) => !f || [x.path,x.name,x.semantic,x.node].join(' ').toLowerCase().includes(f));
  const frag = document.createDocumentFragment();
  for (const file of files) {
    const li = document.createElement('li');
    const name = document.createElement('span');
    name.className = 'name';
    name.textContent = file.qualification ? `${file.name} · ${file.node.endsWith(':discovery') ? '发现候选' : file.qualification}` : file.name;
    name.title = file.node ? file.node+' · '+file.path : file.path;
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
  const url = file.url || '/file/' + encodeURIComponent(file.path);
  setActiveSlot(slot);
  showError('');
  if (ext === '.json' && file.kind!=='source-motion') {
    v.clearScene();
    state.loaded[slot] = { file, takes: 0 };
    $('badge' + slot).textContent = `${file.name} · JSON 文本`;
    renderJsonFile(slot, file, url);
    return;
  }
  $('badge' + slot).textContent = '加载中…';
  try {
    const { takes } = await v.load(url, file.kind==='source-motion' ? '.source-motion' : ext, file.name);
    state.loaded[slot] = { file, takes: takes.length };
    $('badge' + slot).textContent = `${file.name} · ${file.qualification || takes.length+' take'}`;
    renderTabs(slot);
    renderClipSelect(slot, takes.length);
    if (file.source) $('tab-sidecar').innerHTML = '<pre>'+escapeHTML(JSON.stringify(file.source,null,2))+'</pre>';
    else renderSidecar(slot, file);
  } catch (e) {
    if (e.message === 'Load superseded') return;
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
  $('chainBtn').onclick = () => { ensureChainViewers(); toggleChainMode(); };
  $('chainSearch').oninput = (e) => {
    state.chain.filterText = e.target.value;
    renderChainList();
  };
  $('syncAB').onchange = (e) => {
    state.syncAB = e.target.checked;
  };
  state.syncAB = $('syncAB').checked;
  $('exportOriginBtn').onclick = () => {
    const entries = state.mode === 'chain'
      ? Object.entries(state.chain.viewers).filter(([sid]) => state.chain.loaded[sid])
      : Object.entries(viewers).filter(([slot]) => state.loaded[slot]);
    const report = { ...COMPARISON_SPACE, activePack: state.activePack, authoringBatch:state.batchId, mode: state.mode,
      asset: state.mode === 'chain' ? state.chain.selectedKey : null,
      stages: entries.map(([id, v]) => ({ id, file: state.mode === 'chain'
        ? state.chain.loaded[id].file.path : state.loaded[id].file.path,
        source:state.mode==='chain' ? state.chain.loaded[id].file.source : state.loaded[id].file.source,
        ...v.originSnapshot() })) };
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([JSON.stringify(report, null, 2)], { type: 'application/json' }));
    a.download = 'motion-origin-comparison.json'; a.click(); URL.revokeObjectURL(a.href);
  };
  $('stageSelect').onchange = (e) => loadStage(e.target.value);
  async function refreshBatch(batchId) {
    const chainOn = state.mode==='chain', selectedKey = state.chain.selectedKey;
    state.chain.requestId=(state.chain.requestId || 0)+1;
    for (const viewer of allViewers()) viewer.clearScene();
    state.loaded = {A:null,B:null}; state.chain.loaded = {}; state.chain.selectedKey = null;
    $('badgeA').textContent = $('badgeB').textContent = '（未加载）';
    for (const id of ['tab-summary', 'tab-bones', 'tab-disp', 'tab-sidecar']) $(id).textContent = '正在读取所选批次…';
    $('fileList').replaceChildren(); $('chainList').replaceChildren();
    $('clipSelect').hidden = true;
    $('timeSlider').value = 0;
    await boot(batchId);
    if (chainOn) {
      ensureChainViewers();
      await selectChainAsset(state.chain.items.some(i=>i.key===selectedKey) ? selectedKey : state.chain.items[0]?.key);
      onResize();
    }
  }
  $('refreshBtn').onclick = () => refreshBatch(null);
  $('batchSelect').onchange = (event) => refreshBatch(event.target.value);
  async function refreshChain() {
    const c = state.chain, key = c.selectedKey;
    c.requestId = (c.requestId || 0)+1;
    await bootChain(); ensureChainViewers();
    await selectChainAsset(c.items.some(i=>i.key===key) ? key : c.items[0]?.key);
    onResize();
  }
  for (const id of ['chainView','compareBatch','comparePhase']) $(id).onchange = refreshChain;
  $('firstFrameBtn').onclick = () => {
    const active = state.mode === 'chain' ? Object.values(state.chain.viewers) : Object.values(viewers);
    for (const v of active) { v.setPlaying(false); v.setTime(0); }
    $('playBtn').textContent = '播放';
    $('timeSlider').value = 0;
  };
  $('fileSearch').oninput = (e) => {
    state.filterText = e.target.value;
    renderFileList();
  };
  $('modeBtn').onclick = () => {
    if (state.mode === 'chain') toggleChainMode();
    toggleABMode();
  };
  $('playBtn').onclick = () => {
    if (state.mode === 'chain') {
      const play = !Object.values(state.chain.viewers).some((v) => v.action && v.playing);
      for (const sid in state.chain.viewers) state.chain.viewers[sid].setPlaying(play);
      $('playBtn').textContent = play ? '暂停' : '播放';
    } else {
      const v = viewers[state.activeSlot];
      const play = !v.playing;
      viewers.A.setPlaying(play);
      viewers.B.setPlaying(play);
      $('playBtn').textContent = play ? '暂停' : '播放';
    }
  };
  $('timeSlider').oninput = (e) => {
    const t = parseFloat(e.target.value);
    if (state.mode === 'chain') {
      syncChainTo(t);
      return;
    }
    viewers[state.activeSlot].setTime(t);
    if (state.mode === 'ab' && state.syncAB) syncABTo(state.activeSlot, t);
  };
  $('speedSelect').onchange = (e) => {
    const s = parseFloat(e.target.value);
    if (state.mode === 'chain') {
      for (const sid in state.chain.viewers) state.chain.viewers[sid].setSpeed(s);
      return;
    }
    viewers.A.setSpeed(s);
    viewers.B.setSpeed(s);
  };
  $('loopChk').onchange = (e) => {
    if (state.mode === 'chain') {
      for (const sid in state.chain.viewers) state.chain.viewers[sid].setLoop(e.target.checked);
      return;
    }
    viewers.A.setLoop(e.target.checked);
    viewers.B.setLoop(e.target.checked);
  };
  $('upSelect').onchange = (e) => {
    const axis = e.target.value;
    if (state.mode === 'chain') {
      for (const sid in state.chain.viewers) {
        state.chain.viewers[sid].setUpAxis(axis);
        state.chain.viewers[sid].resetCamera();
      }
      return;
    }
    viewers[state.activeSlot].setUpAxis(axis);
    viewers[state.activeSlot].resetCamera();
  };
  $('resetCamBtn').onclick = () => {
    viewers.A.resetCamera();
  };
  $('clipSelect').onchange = (e) => {
    if (state.mode === 'chain') return;
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

function syncABTo(sourceSlot, t) {
  const source = viewers[sourceSlot], other = viewers[sourceSlot === 'A' ? 'B' : 'A'];
  if (!source.action || !other.action || !source.clip || !other.clip) return;
  const ratio = source.clip.duration ? t / source.clip.duration : 0;
  other.setTime(ratio * (other.clip.duration || 0));
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
  if (state.mode === 'chain') {
    for (const sid in state.chain.viewers) state.chain.viewers[sid].resize();
  }
}

const clock = new THREE.Clock();
function tick() {
  const dt = Math.min(clock.getDelta(), 0.1);
  if (state.mode === 'chain') {
    if (!state.chain.viewers[state.chain.master]?.clip)
      state.chain.master = Object.keys(state.chain.viewers).find(sid => state.chain.viewers[sid].clip);
    const masterV = state.chain.viewers[state.chain.master];
    if (masterV) masterV.update(dt);
    const syncOn = $('syncAB').checked;
    if (syncOn && masterV?.action && masterV?.clip) {
      const ratio = masterV.action.time / (masterV.clip.duration || 1);
      for (const sid in state.chain.viewers) {
        if (sid === state.chain.master) continue;
        const v = state.chain.viewers[sid];
        if (v.action && v.clip) {
          const dur = v.clip.duration || 1;
          const target = ratio * dur;
          v.setTime(target);
        }
        v.update(0);
      }
    } else {
      for (const sid in state.chain.viewers) {
        if (sid === state.chain.master) continue;
        state.chain.viewers[sid].update(dt);
      }
    }
  } else if (state.mode === 'ab' && state.syncAB) {
    const master = viewers[state.activeSlot], other = viewers[state.activeSlot === 'A' ? 'B' : 'A'];
    master.update(dt);
    if (master.action && master.clip) syncABTo(state.activeSlot, master.action.time);
    const playing = other.playing;
    other.playing = false;
    other.update(0);
    other.playing = playing;
  } else {
    viewers.A.update(dt);
    if (state.mode === 'ab') viewers.B.update(dt);
  }
  requestAnimationFrame(tick);
}

init();
tick();
