# Kimodo 资产替换后表现异常诊断与 Stage3 重定向姿态对齐修复

2026-10-07。承接 [Kimodo 验收](2026-09-30-kimodo-acceptance.md)。用户目验替换资产后的 als_demo 表现与骨骼动作本体差距明显，要求检查两大嫌疑：BVH 转换坐标轴错误、骨骼重定向错误。本批结论为：**嫌疑1（BVH 坐标轴）排除；嫌疑2（重定向）确认在 Stage3（Manny→ALS），根因是 RTG 缺失 retarget pose 对齐导致 FK 直传把绑定姿态差作为恒定偏移烘焙进每条动画；已在 UE 侧修复并全链重出、Godot 侧复验全绿。**

## 诊断方法

两个探针脚本位于 `artifacts/kimodo_als/`：

- `probe_pose_chain.py`：内在关节角探针（8 组 elbow/knee/shoulder/hip ×L/R），对比相邻骨段方向夹角。该指标与 rest pose、全局朝向无关，对轴错敏感（轴错不可能得到 0.00° 的肘部误差）。本次修复了 OP 连接解析缺失：ascii FBX 7.7 中 curveNode→model 绑定走 `C: "OP",<curveNodeId>,<modelId>, "Lcl Translation"/"Lcl Rotation"`，原解析器只在 OO 分支处理该情形。
- `probe_curve_offset.py`：曲线级分析，逐帧四元数夹角 mean/std/min/max，区分 rest pose 差与曲线动态差（std≈0 即恒定偏移、FK 直传）。

三组对照：`rest_s1_vs_s2`（一段 BVH→Manny 前后绑定姿态差）、`curve_s1_vs_s2`（一段重定向前后曲线差）、`curve_s2_vs_orig`（二段 Manny→ALS vs 原版 ALS FBX）。

## 嫌疑1 判定：排除

证据（probe_pose_chain_report.json，P1 段）：

| 探针 | mean | max | 判定 |
| --- | ---: | ---: | --- |
| elbow L/R | 0.00° | 0.00° | 无轴错（轴错不可能有此精度） |
| shoulder L/R | <1.1° | — | 无轴错 |
| knee R | 15.61° | — | 归因一段 Mocara 腿 IK/FloorConstraint 非对称，非轴错 |

## 嫌疑2 判定：确认在 Stage3

曲线级证据（probe_curve_offset_report.json，`curve_s1_vs_s2`）：每骨 mean≈绑定姿态差且 std≈0 —— pelvis 3.84°/std 0.00、clavicle_l 23.85°/std 0.05、upperarm_l 23.65°/std 0.31。这是 FK 直传未做 pose 对齐的典型签名：Stage3 的 `RTG_Manny_to_ALS` 仅 auto_map_chains(EXACT)+add_default_ops()，无 retarget pose 对齐，Manny 与 ALS 绑定姿态差（clavicle/upperarm ~24°、lowerarm ~14°、spine_02 ~10.6°）作为恒定偏移进入每条动画曲线 → 手臂多下垂 ~7°、肩/髋探针 ~15°。

## 修复实施

参照 Mocara `AutoAlignLimbsOnly` 配方（MocaraRetargeter.cpp 786-885 行）修改 `tools/unreal/create_als_ikrig.py`，插入在 `rc.add_default_ops()` 之后：

1. 按重定向链枚举骨骼（`get_retarget_chain_start_bone/end_bone`），过滤竖直退化前缀（root/pelvis/spine/neck/head）。
2. `ResetRetargetPose`（清空旧偏移，防陈旧值存活）。
3. `AutoAlignBones(ChainToChain, Target)` 仅对四肢 32 骨；躯干保留 identity —— 竖直链方向对齐退化（任意 yaw 满足约束），Mocara 曾踩 90° 躯干 yaw。

证据：`assets/generated/kimodo_ue/als_regen/create_als_ikrig.json` 中 `retargetPoseAligned: true`、`alignBonesSource: retarget_chain_ends`；UE 日志 "KIMODO_ALSRIG auto-aligned 32 limb bones (torso at identity)"。

## 修复验证

- 二段批量重出：123/123 ok（`als_final_batch_report.json` total=123 ok=123 failed=0 skipped=0）。
- 旧 fbx_als 归档为 `fbx_als_before_align/`（248 files）。
- 探针复验：arm drop s2 与 s1 完全一致（L 17.5647 vs 17.5648、R 23.8242 vs 23.8243，+7° 下垂消除）；P3 端到端肩 8.27/6.47 → 0.93/0.93；P2 残余髋 ~13° 是 torso identity 的设计代价（P3 端到端髋仅 6.75/6.73）；P3 knee R 17.49° 继承一段（P1 15.61°）非二段问题。

## Godot 侧接入链

修复后的 Godot 侧链路与关键发现：

1. 树重组：`synthesize_kimodo_manifest.py`（123 替换 + 3 沿用 + 复制其余），`verify_kimodo_manifest.py` 输出 `KIMODO_MANIFEST_VERIFY OK`。
2. 六域导出：`export-kimodo-raw-sources.ps1` 六组全绿（recovery_movement 78 / aim 4 / overlay 36 / ragdoll 1 / stop 3 / overlay_prop 1，`GODOT_ALS_KIMODO_RAW_EXPORT_OK indices=6`）。
3. 关键发现（本批修正既有认知）：`ComputeDefinitionDigest` 只覆盖 manifest 结构化定义（metadata 由 synthesize 完整继承原版），不含 FBX 字节哈希。因此 FBX 重出后编译 digest 仍为 `afcd3d54af3a42e982ec013c3b30cef8546d1ddd817f4f2e22a6355efdae3279`，门 pin 与旧 requests 内嵌 digest 均依然有效，无需重钉。
4. 时序回退（Option C）：`artifacts/kimodo_als/restore_original_timing.py` 重跑成功 —— 116 unique raw、70 条重采样回原版关键帧域、46 noop、metadata 126/126 恢复、endpoint 14370 检查 0 失败、index digest 6/6 一致，`KIMODO_TIMING_RESTORE_OK`。
5. 运行时数据来源确认：姿态数据来自 `raw_sequences` JSON（`ALS_RAW_SOURCE_GENERATED_DIR` 指向，loader 校验 digest），FBX 仅是 UE→Godot 传输载体。

## 复验结果

| 验证 | 结果 |
| --- | --- |
| P2B 导入（门启用：marker/fbx/digest 五级检查） | `P2B_IMPORT_OK assets=273 files=141 skeletal=7 static=4 animations=126 textures=4 digest=afcd3d54…`，门 marker/fbx/digest 全 OK |
| P2B asset smoke | `P2B_ASSET_SMOKE_OK bones=68 compatible_clips=125 sampled_clips=6 pose_changes=6 material_checks=90` |
| P2B rig harness single×1 / parallel×1 | `GODOT_ALS_P2B_RIG_OK missing=0 stale=0`，digest 一致 `BB264E575CED5E9F` |
| P2B rig harness parallel×10 | `GODOT_ALS_P2B_RIG_OK missing=0 stale=0 replacements=1 events=29` |

日志证据（独立日志名）：`artifacts/kimodo_als/p2b_kimodo_restore_r1.log`、`p2b_asset_smoke_alignfix_r2.log`、`p2b_rig_alignfix_r2_{single,parallel}_{1,10}.log`。

## 范围与边界

- 本批修复针对 Stage3 FK 直传的绑定姿态恒定偏移；一段 Mocara 腿 IK 引起的 knee R ~15° 非对称不在本批范围。
- torso identity 的设计代价（P2 段髋 ~13°）为 Mocara 配方的既有行为，非本批回归。
- 修复后的 als_demo 交互窗口已拉起供用户目验（门双 marker 确认），但完整人工键鼠与视觉验收仍开放，以用户观感为准。
- `fbx_als_before_align/` 归档目录（248 files）保留至用户确认后清理。
