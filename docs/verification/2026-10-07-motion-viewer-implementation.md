# Motion Viewer 前端实现与验证记录

日期：2026-10-07
范围：`tools/motion-viewer/`（纯增量，无 pipeline 代码改动）；对应 ROADMAP 中的 kimodo 管线可视化检查目标。

## 实现内容

- `server.py`：Python stdlib（ThreadingHTTPServer）只读服务器，端口 10015。路由：`/`、`/api/stages`、`/api/list?stage=`、`/file/<rel>`（realpath 前缀校验防穿越）、`/js/*`、`/vendor/*`、`/style.css`。STAGES 覆盖 7 个阶段：① kimodo-bvh、② retarget-fbx、③ rootik-fbx、④ kimodo-godot、⑤ als-baseline、⑥ single-sample、⑦ raw-meta，每阶段带产物目录、扩展名过滤与预期说明。
- 前端：`index.html` / `style.css` / `js/viewport.js` / `js/stats.js` / `js/main.js`。three.js 0.169.0（CDN importmap）。
- `vendor/FBXLoader.js`：three@0.169.0 本地修补版（两处补丁，见下）；配套 `fflate.module.js`、`NURBSCurve.js`、`NURBSUtils.js` 同目录 vendor。

### three.js FBXLoader vendor 补丁（上游 0.169.0~0.180.0 均未修复）

1. `parseAnimationCurves` 的 X/Y/Z 分支原代码直接 `curveNodesMap.get(id).curves['x']`，UE5 导出 FBX 的自定义属性曲线挂在非 S/R/T AnimationCurveNode 上导致 `Cannot read properties of undefined (reading 'curves')` 崩溃；补丁为三分支加 `curveNodesMap.has()` 检查。
2. `parseAnimationLayers` 对无 model 父节点的曲线节点加守卫跳过（warn 后 return），避免 parents 空数组取 `[0].ID` 崩溃。

### 其他 three.js 兼容处理

- 字面骨名 `root` 被 PropertyBinding 特判导致 root 位移轨道绑到 mixer 根对象；加载时骨与轨道改名 `__viewer_root__` 规避（aliasRootBone）。
- BVHLoader 返回 `THREE.Skeleton` 非 Object3D，取 `bones[0]` 挂载。
- FBX 轨道 node 名含 `Model::` 前缀时剥离使 PropertyBinding 可解析（normalizeTrackNames）。

## 受控测试（浏览器自动化实测）

以下为自动化浏览器在 http://127.0.0.1:10015/ 的实测结果：

- BVH 播放：`ALS_N_Walk_F.bvh`（①）渲染播放正常，78 骨 / 30Hz，位移在 Hips（X 6.0 / Y 7.0 / Z 215.0），Root 静止；阶段诊断 ✓（截图 `artifacts/motion-viewer/bvh_walk_f_fixed.png`）。
- RootIK FBX 播放：`ALS_N_Transition_L_RootIK.fbx`（③）渲染播放正常，root 位移 X 18.4 / Y 16.9，pelvis 仅 Y 1.7 / XZ≈0，位移轨道含 root、ik_foot_l/r、ik_hand_*；阶段诊断 ✓（截图 `rootik_fbx_check.png`）。
- ④ 阶段抽查：`ALS_N_Transition_L.fbx`（1.37MB）加载播放正常（截图 `stage4_fbx_check.png`）。
- JSON 文本查看：`bake_report.json`（③）点击后 badge 显示"JSON 文本"、"溯源 JSON"tab 自动激活并 pretty-print 内容（total 20 / ok 20）。
- A/B 对比：开启后双视口，左键 Transition_L 到 A、右键 Transition_R 到 B，同步播放（双视口时间一致 0.51/2.33s），B 视口溯源自动刷新（截图 `ab_sync_check.png`）。
- 服务器路由：`/api/stages`、`/api/list`、`/file/`、`/js/*`、`/vendor/*`、`/style.css` 全部 curl/浏览器 200。

## 链路比对模式（2026-10-07 第二轮迭代）

### 实现内容

- `server.py` 新增 `GET /api/chain`：按归一化键（去扩展名 + 去 `_RootIK` 后缀）聚合五个动画阶段（①BVH / ②重定向 / ③RootIK / ④Kimodo最终 / ⑤ALS原版）的产物，返回 126 条链路资源；`CHAIN_STAGE_IDS` 固定阶段顺序。
- `index.html` / `js/main.js` / `style.css` 新增链路比对模式：
  - 侧栏聚类列表：按 ALS 命名前缀分组（CLF 蹲 15 / CRF 跑姿 5 / N 站立 66 / Props 道具 / StanceVariation / Mannequin / 其他），每条右侧 5 个彩色芯片标记各阶段有无，支持前缀过滤。
  - 一排链路视口：点击资源同时加载同一 clip 五个阶段产物（无该阶段显示"本阶段无此资源"占位），视口行横向排布可滚动。
  - master 机制：点击任一视口切换时间轴主控；开启"链路同步"后 master 按播放比例驱动其余视口（各阶段 clip 时长可能不同，按比例不做帧级对齐）；播放/暂停/速度/循环/滑杆/Up 轴/重置视角在链路模式下统一作用于全部视口。
  - 概要 tab 显示选中资源在各阶段的文件名与大小对照表。

### 受控测试（浏览器自动化实测）

- `/api/chain` 返回 126 条；`ALS_N_Falling_LeanPose_0` 等 InAir/Transitions 类为完整五阶段链（RootIK 有），`ALS_CLF_*` 等为四阶段链（RootIK 无，符合 20 条 RootIK 覆盖范围）。
- 点击 `ALS_N_Falling_LeanPose_0`：五个视口同时加载成功（各 1 take，badge 分别为 BVH 77.1KB / 重定向 1.31MB / RootIK 1.31MB / 最终 923.5KB / 原版 640KB），骨骼渲染与轨迹线正常，时间轴由 master（①BVH）驱动 0.28/0.97s。
- 点击 `ALS_CLF_GetUp_Back`：RootIK 视口显示"本阶段无此资源"占位，其余四视口正常加载。
- 退出链路模式：单视图与阶段文件列表正常恢复，全程无控制台报错。

### 验证边界（链路模式未执行项）

- 126 条链路资源仅实测上述 2 条（五阶段 1 条 + 四阶段 1 条），未全量逐条加载。
- 链路同步仅验证 master 播放比例驱动逻辑（编码实现 + 无报错），未逐帧核对各视口姿势一致性。
- master 切换（点击视口）实测路径未逐一触发；链接视口横向滚动在 5 视口下未溢出，未测试更窄窗口。
- Props / StanceVariation 聚类的实际展示未单独截图核对（数据来自同一 `/api/chain` 索引）。

## 链路视口渲染修复（2026-10-07 第三轮迭代）

### 问题

用户实测反馈：链路比对模式中视口只显示纯色色块，无骨骼动画。且旧快照中五视口 badge 显示 take 数正常但画面空白。

### 根因

链路视口在 `#vpChain` 容器处于 `hidden` 状态时创建（`ensureChainViewers` 在模式切换前执行），此时 `canvas.clientWidth = 0`，Viewer 构造函数 `_resize()` 回退为 2×2 像素 WebGL 绘制缓冲；容器显示后没有任何代码重新设置画布尺寸（`onResize()` 只处理 A/B 视口），2×2 缓冲被 CSS `width/height:100%` 拉伸铺满整个视口区域，形成纯色块。clearColor 深灰 #0f1115 即所见色块。

### 修复

- `js/viewport.js`：`_resize()` 记录 `_lastW/_lastH`；`update()` 每帧自检 `clientWidth/clientHeight` 与上次记录不一致即自动 `_resize()`，容器从 hidden 转显示后画布自愈恢复正确渲染尺寸。
- `js/main.js`：`onResize()` 扩展为链路模式下同时 resize 全部链路视口（窗口缩放、模式切换调用路径全覆盖）。

### 受控测试（浏览器自动化实测）

- 修复前快照复现：色块问题确认为 hidden 创建时机；修复后刷新页面进入链路模式，`ALS_CLF_GetUp_Back`（四阶段）五个视口骨骼、标记点与轨迹线正常渲染，RootIK 占位正常。
- `ALS_N_Falling_LeanPose_0`（五阶段完整链）加载正常，master 时间轴 0.00/1.47s 正常驱动，滑杆拖动同步生效。
- 全程控制台无报错。

## 默认 Z-up 显示与默认链路模式（2026-10-07 第四轮迭代）

### 需求

用户要求默认显示坐标轴按 ALS 原版（Z 轴向上），且启动即进入链路比对模式。

### 资产朝向约定核实（字节级读取文件头）

- BVH（`kimodo_ue/als_regen/*.bvh`）：Y-up（Root→Hips OFFSET 为 `0 100 0`，骨骼沿 +Y 生长）。
- FBX 四个阶段全部 `P: "UpAxis", ... 2`（Z-up，UE 导出约定）：重定向 `fbx/*.fbx`、RootIK `fbx_rootik/*.fbx`、Kimodo 最终 `kimodo_als/animations/**/*.fbx`、ALS 原版 `als_v4/animations/**/*.fbx`。

### 实现

- `js/viewport.js`：Viewer 默认 `upAxis='z'`；新增 `detectFbxUpAxis(buf)` 扫描 FBX 头部 `GlobalSettings` 的 `UpAxis` 声明，`load()` 时按扩展名（BVH=Y-up）或文件头检测 `contentUp`；`_applyOrientation()` 统一处理：Z-up 显示约定下轴指示器旋转 -90°X（蓝 Z 朝上，UE 观感）、Z-up 内容 pivot 旋转 -90°X 直立，Y-up 内容保持直立；Y-up 显示约定下全部归零（FBX 躺平、BVH 直立），供诊断轴问题。
- `index.html`：Up 轴下拉改为"显示约定"，默认 `Z-up（ALS 原版）`，次选 `Y-up（原始数据）`。
- `js/main.js`：`boot()` 并行加载阶段目录与链路索引，完成后自动 `ensureChainViewers()` + 切换链路模式，启动默认进入链路比对并自动选中首条资源。

### 受控测试（浏览器自动化实测）

- 页面加载后默认进入链路模式，左侧为链路列表（89 条分组），五视口自动加载 `ALS_CLF_GetUp_Back`，无需手动点击"链路比对"。
- 播放中两次截图（1.22s 与 0.55s 两个姿势）：BVH 与三个 FBX 视口全部直立渲染（Z-up 内容已立起，Y-up BVH 保持直立），骨骼、标记与轨迹正常。
- 控制台无报错。

### 验证边界

- Z-up/Y-up 切换实测了默认 Z-up 单向；Y-up（原始数据）选项切换路径为编码实现 + 控制台无报错，未逐一截图核对 FBX 躺平效果。
- 二进制 FBX 的 `detectFbxUpAxis` 回退路径（回退 Z-up）未实测——管线全部产物为 ASCII FBX。
- BVH 若未来出现 Z-up 变体，当前按扩展名固定 Y-up 处理，未做逐文件检测。

## 验证边界（未执行项）

- ④⑤ 阶段仅各抽查播放与清单展示，未做逐条全量加载验证；两阶段的"与基线逐条对比"预期当前只展示清单，不做自动 diff。
- A/B 同步仅实测同阶段同名系资产（Transition_L vs Transition_R），跨阶段 BVH-vs-FBX 同名对比路径可用但未逐项实测。
- ② 重定向 FBX 阶段的诊断规则已实现（要求 pelvis 位移 root 静止），本轮会话未单独抽样实测（③ 的 pelvis/root 行为已覆盖相邻逻辑）。
- 速度 0.25×~2×、Z-up 修正、循环开关为编码实现，未逐项自动化实测。
- 本记录为前端工具验证，不构成管线产物本身的正确性证明；产物验收仍以 ROADMAP 当前目标的既有验证为准。

## 约束遵守

- 全程只读：服务器不写 `assets/generated/`，pipeline 零修改。
- 新增文件仅 `tools/motion-viewer/` 与本记录；截图存 `artifacts/motion-viewer/`（git 忽略区）。
