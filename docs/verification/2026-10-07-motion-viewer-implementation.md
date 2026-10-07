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

## 验证边界（未执行项）

- ④⑤ 阶段仅各抽查播放与清单展示，未做逐条全量加载验证；两阶段的"与基线逐条对比"预期当前只展示清单，不做自动 diff。
- A/B 同步仅实测同阶段同名系资产（Transition_L vs Transition_R），跨阶段 BVH-vs-FBX 同名对比路径可用但未逐项实测。
- ② 重定向 FBX 阶段的诊断规则已实现（要求 pelvis 位移 root 静止），本轮会话未单独抽样实测（③ 的 pelvis/root 行为已覆盖相邻逻辑）。
- 速度 0.25×~2×、Z-up 修正、循环开关为编码实现，未逐项自动化实测。
- 本记录为前端工具验证，不构成管线产物本身的正确性证明；产物验收仍以 ROADMAP 当前目标的既有验证为准。

## 约束遵守

- 全程只读：服务器不写 `assets/generated/`，pipeline 零修改。
- 新增文件仅 `tools/motion-viewer/` 与本记录；截图存 `artifacts/motion-viewer/`（git 忽略区）。
