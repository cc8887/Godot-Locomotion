# Kimodo Pipeline · Motion Viewer

基于 three.js 的管线产物可视化前端，用于浏览与检查 kimodo 动作转换管线每一步的产物。前端与管线完全解耦：服务器对 `assets/generated/` 只读，管线可以脱离前端独立运行。

有已登记生成批次时，页面默认展示“生成候选”，可通过“生成批次”选择历史批次、“周期处理／母动作”查看该批周期处理结果。“刷新资源”重新读取生产注册表并回到最近登记批次。源候选、源检查通过、Godot 已发布属于不同状态；未通过的试验也展示，空阶段不回退到旧资源。①②③ 明确标为历史中间目录，④仍展示当前活动包。

链路比对提供两种方式：**当前批次 · 各生产阶段** 显示生成候选、周期母动作、目标重定向 raw、水平分解 Root、ALS 原地 raw 和最终 FBX，另列活动包／原版作为独立对照；没有本批回执的阶段留空。**不同批次 · 同一生产阶段** 通过“对照批次”和“跨批次生产阶段”选择同名资产并排观察。切换批次保留链路模式和所选资产，共用固定原点和相机，从首帧暂停开始。概要显示目标制品整段 root→原点、pelvis→root 最大水平距离，溯源 JSON 记录实际文件、摘要及 ownership 回执。

目标包登记由 `publish stage` 和 `finalize-fbx` 自动触发；已有目标包可用 `python -m tools.kimodo_pipeline.viewer_pack --pack assets/generated/<version>/resource-pack.json --label "目标包批次"` 接入。默认不改变最近生成源批次；`--make-latest` 显式将其设为当前批次。注册只读取真实目标 raw 和已完成原生回读的最终 FBX，不对失败源批次制造后续阶段。目标预览保留完整局部平移、旋转和缩放，真实 root 轨道直接播放，使用源 Z-up/cm 约定。见[跨批次与阶段验证](../../docs/verification/2026-10-09-kimodo-viewer-batch-stage-chain.md)。

Pipeline Runner 在结束或门禁拒绝时，将本次已完成节点的原生 NPZ 导出为只读预览，并登记到 `artifacts/kimodo_viewer/current.json`。展示前验证 NPZ、rig 和门禁回执哈希；每个文件的“溯源 JSON”记录生产节点、批次和源 NPZ 摘要。预览保持所有原始帧、时长、朝向和原点，仅将米转为厘米，不经过 UE。新增静止 Root 仅供显示，不是资产的根运动烘焙。

已有独立试验可在具有 NumPy/SciPy 的 Kimodo Python 环境登记：

```powershell
python -m tools.kimodo_pipeline.viewer_export --run-dir artifacts/tests/<batch> --graph artifacts/tests/<batch>/graph.json --label "生成批次" --status "未发布"
```

`run-dir/nodes` 必须提供唯一版本的节点回执，脚本拒绝摘要变化或混合版本；旧输入绑定及约束教师不作为新产物展示。源骨架预览不代表最终 Godot 蒙皮、混合或脚锁验收。见[最新批次接入验证](../../docs/verification/2026-10-09-kimodo-viewer-latest-authoring.md)。

单栏、A/B 与链路视口统一以源资产原点 `(0,0,0)` 为参照，采用 cm、共同正交相机和比例尺；旋转/缩放联动，禁止按骨盆或包围盒自动构图。加载不会改变相机或清除资产的水平常量。参见 [空间契约](../../docs/architecture/kimodo-viewer-comparison-space.md) 与 [真实浏览器对照](../../docs/verification/2026-10-08-kimodo-viewer-origin-comparison.md)。

④ 栏的 FBX 及⑦ raw 文本读取普通 Demo 当前活动包；页首显示包路径，错误配置直接报错。①②③ 为现有中间目录，同名关联不保证是活动包的实际上游。

⑧ 水平轨迹分解对照读取专用 probe 输出：before／authored／inplace／original 可用 A/B 在同一原点比较。首帧按钮暂停并归零所有视口；A/B 同步由当前激活视口控制，拖动右栏时间也同步左栏。此阶段的候选不是活动包。见[分解设计及验证](../../docs/architecture/kimodo-planar-root-ownership.md)。

## 快速启动

```powershell
& 'C:\Users\yuchencui\AppData\Local\Programs\Python\Python314\python.exe' F:\Github\Godot-Locomotion\tools\motion-viewer\server.py
```

然后访问 http://127.0.0.1:10015/（任意 Python 3.8+ 均可运行，无需第三方依赖）。

## 功能

- 管线阶段浏览：覆盖 8 个阶段（① Kimodo BVH / ② 重定向 FBX / ③ RootIK 烘焙 FBX / ④ 当前项目中的 FBX / ⑤ 原版 ALS v4 基线 / ⑥ 单样本链与冒烟 / ⑦ Raw 源与计划 JSON / ⑧ 水平轨迹分解对照），每阶段标注该步骤的产物形态与"预期应有的特征"。
- BVH / FBX 播放：BVHLoader 与修补版 FBXLoader 解析，AnimationMixer 驱动播放，支持暂停、进度条、速度 0.25×~2×、循环、Up 轴切换（Y-up / Z-up 修正）、视角重置。
- 关键骨骼标记：视口中以彩色标记点标出根骨骼（root/Root）、骨盆、IK/虚拟骨（ik_*/VB_*）、脚、手、头共 6 类，鼠标悬停骨骼线可见名称。
- 根/骨盆运动轨迹：分别画出 root 橙线和 pelvis/Hips 粉线，白十字为原点；实时显示两骨到原点及 pelvis-root 的水平距离，避免只看运动幅度漏掉恒定偏移。root 绑定别名也保留标记与轨迹。
- 位移轨道分析表（"位移轨道" tab）：逐骨列出 X/Y/Z 位移范围与幅度，按总幅度降序排列——用于直接看出"位移到底在 root 还是 pelvis/Hips"。
- 阶段形态提示：概要 tab 检查① Hips 位移/Root 静止、② pelvis 位移/root 静止、③ root 运动。这些只检查轨迹分布，不代表资产质量通过；视口红色距离也只是观察提示，真实生成/发布门禁独立执行。
- 导出原点对照：保存各栏的实际文件、活动包字节哈希、时间、源 up/显示约定、root/pelvis 坐标和相机状态，供截图追溯。
- A/B 对比：点击"A/B 对比"开启双视口，左键文件加载到 A、右键加载到 B（可跨阶段），A 驱动 B 按时长比例同步播放——用于同名资产跨阶段对比（如 ①BVH vs ③RootIK FBX）。
- 链路比对：点击"链路比对"进入多阶段关联比对模式——侧栏按 ALS 命名前缀聚类（CLF 蹲 / CRF 跑姿 / N 站立 / Props 道具 / StanceVariation / Mannequin / 其他），每条资源右侧以彩色芯片标出五个动画阶段（①BVH / ②重定向 / ③RootIK / ④Kimodo最终 / ⑤ALS原版）的有无；点击资源即在右侧一排视口中同时加载同一 clip 的各阶段产物（无该阶段时显示占位），开启"链路同步"后由 master 视口按播放比例驱动其余视口，点击任一视口可切换 master。同一资源按"去扩展名 + 去 `_RootIK` 后缀"归一化关联。
- 溯源 JSON：自动探测 `.source.json` / `.verification.json` sidecar 并在"溯源 JSON" tab 展示；列表中的 JSON 文件（bake_report、manifest、export_plan 等）点击即按文本查看。
- 文件过滤：侧栏顶部输入框支持模糊过滤（如输入 `Transition_L` 快速定位）。

## 检查错误的典型用法

1. 阶段诊断不通过 → 打开"位移轨道" tab，看位移挂在哪根骨骼、幅度是否合理（例如 ② 阶段若发现 root 有大位移，说明重定向未按预期把位移留在 pelvis）。
2. 跨阶段对比 → A/B 模式分别加载同名资产（如 ① 的 `ALS_N_Walk_F.bvh` 与 ③ 的 `ALS_N_Walk_F_RootIK.fbx`），同步播放观察两者姿势/轨迹差异。
3. 溯源追踪 → "溯源 JSON" tab 展示该文件的 schemaVersion、rootPolicy、ikBindings、fbxSha256 等生成参数，用于核对生成配置。

## HTTP API

- `GET /api/stages[?batch=<id>]` — 阶段定义、生成批次与当前活动包来源
- `GET /api/list?stage=<id>[&batch=<id>]` — 阶段文件清单（含 sidecars 存在性）
- `GET /api/chain[?batch=<id>]` — 跨阶段同名关联，包含选定批次的源候选与母动作
- `GET /authoring-file/<预览相对路径>` — 仅限 `artifacts/kimodo_viewer` 内 `.motion.json`，不开放其他测试目录
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
- 链路比对阶段芯片仅反映产物存在性；RootIK 阶段仅覆盖 InAir/Transitions 的 20 条，其余资源显示"本阶段无此资源"属预期。
- 链路同步按播放比例进行（各阶段 clip 时长可能不同），不做帧级对齐。
