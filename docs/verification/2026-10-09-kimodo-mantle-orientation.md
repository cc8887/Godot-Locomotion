# Kimodo Mantle 朝向与 Viewer 播放检查（2026-10-09）

**运行时结论修正**：下文的生成／Root 分解／FBX／Viewer 数值检查仍只证明资产阶段的方向一致。原 Godot 场景检查只看胶囊位移，没有测 actor 旋转，因此不能据此排除运行时 90° 偏转。后续确认 Root 关闭路径缺少 mesh 基底抵消，已修 Core；实际 actor yaw 由约 82.5° 恢复为 0°，穿模仍失败。见[实际姿态与完整蒙皮复核](2026-10-09-mantle-runtime-orientation-penetration.md)。

本次检查 `mantle-generation-20261009-v4` 的实际源候选、四个 Low 导出身份、Root/Pelvis／曲线、最终 FBX、Viewer 和 Godot。全程使用脚本与 Godot，不启动 UE。确认并修复两类 Viewer 错误；没有在本批生成／分解资产中复现固定 90° yaw。Godot 裸模型和角色前向之间存在既有 90° 基底差异，角色 presentation 已补偿。

## 源、目标与 Root 曲线

证据目录：`artifacts/kimodo_als/mantle-orientation-20261009/`。`audit-pipeline.py` 读取本批实际输出、不可变原版参考和发布包，结果见 `pipeline-orientation-audit.json`。

| 检查 | 结果 |
| --- | --- |
| High／Low Left／Low Right 源 Hips 对生成 guide 全帧旋转 | 81／60／60 帧，最大 1.9111°／1.6747°／1.3252° |
| 四个 V4／Refactored Low 目标 Pelvis 对原版参考 | 左最大 1.5148°、右最大 1.3057° |
| Root 分解前后 Pelvis 世界位置／旋转 | 最大 3.56e-15 cm／3.90e-14° |
| authored 与 runtime Pelvis 世界位置 | 误差 0 |
| Root 曲线对 raw 轨道 | 位置误差 0，yaw 最大 0°，终点 `(0,65,100)` cm |
| 发布载荷完整性 | 829 个索引载荷摘要不变 |

resource-pack 字节 SHA-256 保持 `cdb8302940e41603c326eca5feca547c53f92a05b25a76e31c8a31207f2b1906`。活动指针仍为 `als_nonprop_20261009_idle_arms_v2`；没有为朝向检查重写生成资产或切换活动包。

## Viewer 的真实缺陷与修复

第一处是空间转换：旧 Viewer 将 ALS component raw 和最终 FBX 都仅绕 X 转 -90°，忽略 FBX writer 已做 Y 反射。原生 raw 的 +Y 前向因此显示为 -Z，而 SOMA／FBX 显示为 +Z。该错误是反射／前向相反，不能据此断言源动作固定偏转 90°。修复后的共同点坐标分别为 SOMA `(X,Y,Z)`、component `(X,Z,Y)`、FBX `(X,Z,-Y)`。新增预览空间标记，旧预览兼容推断。

第二处是播放解析：writer 的内联 `AnimationLayer { }` 导致 Three.js ASCII parser 吞掉第一个 Root 模型；骨名加通道编号的曲线节点标签未被 AnimationParser 识别为 T/R/S。旧 Viewer 最终栏实际显示参考姿态。修复限定已知 Creator，在内存展开空块和转换曲线标签，保留连接／属性／数值／磁盘字节；增加缺 Root 或动画轨道的明确拒绝。

`audit-viewer.mjs` 使用实际生产 loader、绑定函数、显示变换与 Three.js 0.169，比较两种 V4 Low 的所有物理骨、所有作者关键时间。依赖只安装在本检查目录，不改变项目包依赖。

| 检查 | 旧代码重放 | 修复后 |
| --- | --- | --- |
| 每个 Low 物理骨数 | 67，缺 Root | 68，含 Root |
| 每个 Low 动画轨道 | 0 | 204 |
| 每个 Low 比较时间 | 不可正常播放 | 46 个关键时间 |
| raw／最终 FBX 骨骼世界位置最大误差 | 不成立 | 3.18e-14 cm |
| raw／最终 FBX 身体 heading 最大误差 | 不成立 | 7.64e-14° |

结果见 `viewer-pose-audit.json`。ufbx 最终回读原先通过仍有效，它不保证另一解析器的播放正确。

实际浏览器使用最新 v4 的 Low Right 链路，固定共同相机，源时间 0.983 s／1.967 s，各目标栏按归一化时间同步。活动旧包是独立对照，不能当本批上游；此截图是骨架播放检查，不能替代最终蒙皮验收。

![修复后 Low Right 各阶段共同空间播放](../../artifacts/kimodo_als/mantle-orientation-20261009/viewer-fixed-low-right.png)

## Godot 模型基底与实际运行

`audit-godot.gd` 在真实 Godot 4.7.2 中读取原版和新 Low FBX，实际求值 AnimationPlayer；另将本批 raw 局部通道应用于原版 mesh 骨架，分别检查裸模型和带现有 profile presentation 的模型。

原版和新 FBX 裸模型 Root 终点均沿 Godot -X。当前骨架父级基底把 FBX X／Y／Z 分别映射为 Godot +Z／+X／+Y；加上 writer 的 Y 反射，原生 `(X,Y,Z)` 变为 Godot `(-Y,Z,X)`。`assets/config/p3_locomotion_profile.json` 既有 `yawRadians=-pi/2` 将裸模型 -X 对齐角色前方 -Z。两种 Low 带 profile 的 Root 位移均为 `(0,1,-0.65)` m，误差小于 1e-5 m。

`godot-import-audit.json`／`.log` 记录 4 个真实导入资产、2 个 raw→原版 mesh 绑定、2 个 profile 挂载绑定，成功标记 `GODOT_MANTLE_ORIENTATION_AUDIT_OK`。

通过环境覆盖选择本批四替换诊断包，运行两个已有真实场景：

- `p3_presentation_smoke.tscn`：`GODOT_ALS_P3_PRESENTATION_OK yaws=3 identity=1`，日志 `godot-presentation.log`。
- `mantling_demo_smoke.tscn -- --mantle-case=low --mantle-hz=60`：实际选择本批 pack，frame31 启动；frame120 胶囊位置 `(-4.0000005,1.9010313,-0.7499999)`，前进沿 -Z；`MANTLE_DEMO_OK active=59 starts=1`，日志 `godot-mantle-low.log`。

本轮没有修改 C#、Godot runtime presentation 或资产 Root yaw。挂载／胶囊轨迹检查当时未发现偏转，但没有检查 actor yaw；该运行时结论已由后续复核纠正。

## 回归与边界

9 项 Node 空间／解析／拒绝回归和 8 项 Python Viewer 导出／登记回归通过，日志 `viewer-tests.log`、`viewer-python-tests.log`；真实 all-key 审计见上述 JSON。复现命令（项目主目录）：

```powershell
node --test tools/motion-viewer/comparison-space.test.mjs tools/motion-viewer/offline-fbx-text.test.mjs
& 'G:/Github/kimodo/.venv/Scripts/python.exe' -m unittest tools.kimodo_pipeline.test_viewer_pack tools.kimodo_pipeline.test_viewer_export
node artifacts/kimodo_als/mantle-orientation-20261009/audit-viewer.mjs
& 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path . --script artifacts/kimodo_als/mantle-orientation-20261009/audit-godot.gd
```

本次是已有生成资产的诊断和 Viewer 修复，没有重新生成。High 源路径仍在原门禁上失败，没有新的 High 目标交付；未重跑完整运行矩阵、长期／性能／全蒙皮或人工视觉验收。Low 逐键姿态与实际场景局部通过不能升级为完整新包验收。既有失败和缺失清单保留，见[实际生成记录](2026-10-09-kimodo-mantle-generation.md)。
