# Kimodo Pipeline · Motion Viewer

基于 three.js 的管线产物可视化前端，用于浏览与检查 kimodo 动作转换管线每一步的产物。前端与管线完全解耦：服务器对 `assets/generated/` 只读，管线可以脱离前端独立运行。

## 快速启动

```powershell
& 'C:\Users\yuchencui\AppData\Local\Programs\Python\Python314\python.exe' F:\Github\Godot-Locomotion\tools\motion-viewer\server.py
```

然后访问 http://127.0.0.1:10015/（任意 Python 3.8+ 均可运行，无需第三方依赖）。

## 功能

- 管线阶段浏览：覆盖 7 个阶段（① Kimodo BVH / ② 重定向 FBX / ③ RootIK 烘焙 FBX / ④ Kimodo 最终 Godot 树 / ⑤ 原版 ALS v4 基线 / ⑥ 单样本链与冒烟 / ⑦ Raw 源与计划 JSON），每阶段标注该步骤的产物形态与"预期应有的特征"。
- BVH / FBX 播放：BVHLoader 与修补版 FBXLoader 解析，AnimationMixer 驱动播放，支持暂停、进度条、速度 0.25×~2×、循环、Up 轴切换（Y-up / Z-up 修正）、视角重置。
- 关键骨骼标记：视口中以彩色标记点标出根骨骼（root/Root）、骨盆、IK/虚拟骨（ik_*/VB_*）、脚、手、头共 6 类，鼠标悬停骨骼线可见名称。
- 根/骨盆运动轨迹：自动采样运动幅度更大的那根骨骼，画出橙色轨迹线。
- 位移轨道分析表（"位移轨道" tab）：逐骨列出 X/Y/Z 位移范围与幅度，按总幅度降序排列——用于直接看出"位移到底在 root 还是 pelvis/Hips"。
- 阶段预期自动诊断：概要 tab 依据阶段预期自动判定通过/失败。① 要求 Hips 位移、Root 静止；② 要求 pelvis 位移、root 静止；③ 要求位移已提取到 root。命中显示绿色 ✓，不命中显示红色 ✗ 并附原因。
- A/B 对比：点击"A/B 对比"开启双视口，左键文件加载到 A、右键加载到 B（可跨阶段），A 驱动 B 按时长比例同步播放——用于同名资产跨阶段对比（如 ①BVH vs ③RootIK FBX）。
- 溯源 JSON：自动探测 `.source.json` / `.verification.json` sidecar 并在"溯源 JSON" tab 展示；列表中的 JSON 文件（bake_report、manifest、export_plan 等）点击即按文本查看。
- 文件过滤：侧栏顶部输入框支持模糊过滤（如输入 `Transition_L` 快速定位）。

## 检查错误的典型用法

1. 阶段诊断不通过 → 打开"位移轨道" tab，看位移挂在哪根骨骼、幅度是否合理（例如 ② 阶段若发现 root 有大位移，说明重定向未按预期把位移留在 pelvis）。
2. 跨阶段对比 → A/B 模式分别加载同名资产（如 ① 的 `ALS_N_Walk_F.bvh` 与 ③ 的 `ALS_N_Walk_F_RootIK.fbx`），同步播放观察两者姿势/轨迹差异。
3. 溯源追踪 → "溯源 JSON" tab 展示该文件的 schemaVersion、rootPolicy、ikBindings、fbxSha256 等生成参数，用于核对生成配置。

## HTTP API

- `GET /api/stages` — 阶段定义列表
- `GET /api/list?stage=<id>` — 阶段文件清单（含 sidecars 存在性）
- `GET /file/<assets/generated 下相对路径>` — 文件内容（realpath 防路径穿越，仅限 assets/generated 内）

## 技术说明

- three.js 0.169.0（CDN importmap）+ OrbitControls + AnimationMixer；`vendor/` 内 FBXLoader 为本地修补版。
- vendor 补丁（上游 0.169.0~0.180.0 均未修复，影响所有 UE5 导出的 FBX）：
  1. `parseAnimationCurves` X/Y/Z 分支补 `curveNodesMap.has()` 检查——UE5 把自定义属性曲线挂在非 S/R/T 的 AnimationCurveNode 上，否则解析直接抛 `Cannot read properties of undefined (reading 'curves')`。
  2. `parseAnimationLayers` 对无 model 父节点的曲线节点加守卫跳过，避免 undefined 访问。
- `root` 骨骼及轨道在加载时改名 `__viewer_root__`，规避 three.js PropertyBinding 对字面名 `root` 的特判（否则 root 位移轨道会绑到 mixer 根对象上失效）。
- BVHLoader 返回的 skeleton 是 THREE.Skeleton，需取 `bones[0]` 作为根 Object3D 挂载。

## 边界

- 仅只读展示，不写入、不修改任何管线产物。
- 诊断规则当前覆盖 ①②③ 三个阶段；④⑤ 阶段的预期（最终树与原版基线逐条对比）当前只展示清单，不做逐条 diff。
