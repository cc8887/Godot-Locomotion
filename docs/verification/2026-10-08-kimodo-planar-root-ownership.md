# Kimodo 水平运动所有权处理与最终资产验证

本批实现目标空间的 root 轨迹／pelvis 残差分解、参考姿态原点依赖、IK 辅助根归一、最终 FBX 重新烘焙及原点截图。最终候选为 `assets/generated/als_nonprop_20261008_root_ownership_v5/resource-pack.json`，SHA256 `a03b1dc1ea46ca7062184c7c0bbed513d2f101bacb1a521eef30ff34f5872bba`。处理 183 个非道具身份（V4 88／Refactored 95），70 个道具身份保留。该候选没有激活；默认指针仍为有已知问题的 v8。

## 实现及来源

- `tools/kimodo_pipeline/root_ownership.py`：组件空间 pelvis 的水平轨迹已包含现有 root，复用 Kimodo 6 cm 半径 ADMM 求解，去除一个固定原点，保留重心残差、Z 起伏和身体旋转。对最终键继续求解，避免平滑器末层插值造成半帧偏差。原地循环在完整摆动不超过半径时使用常量轨迹，不以“范围为零”跳过远处原点。
- `graph.target_origin_reference`、`publish.prepare_target_closure`：gait Pose／Lean 基底沿对应方向 master，Lean／Acceleration／Look 沿原合同的 baseAsset/baseFrame 传递原点。50 个静态／参数姿态最大偏移从 351.218 cm 降到 3.240 cm。采样基底的 root 轨迹也计入参考原点；保留 additive 相对基底的差。右侧姿态不直接继承左侧原点。
- IK 容器 `ik_foot_root/ik_hand_root` 使用 root 下参考局部变换，端点根据最终身体 FK 重建。修正静态原点虽然已消除、辅助根却留在数米之外的问题。全部回执记录 `ikContainerPolicy=root-reference-local`。
- `publish stage` 为每个身份保存 authored/runtime raw 与原点、重建、依赖哈希回执。普通非 Root Motion 动作从同一 residual 锁定 root XY。输出 34 份最终 runtime 根曲线和 54 份独立 extracted-root 曲线；两者分别绑定其 raw 语义哈希，运行开关不影响离线烘焙。
- `bake_kimodo_final_tracks.py` 将最终完整物理轨道写入唯一 UE scratch 版本，再导出／导回 FBX。`publish finalize-fbx` 验证完整闭包、原始输入与最终输出哈希、所有轨道的误差及时域，随后更新 manifest。生产 PowerShell 已接入这一步。

本批使用已验证的 `artifacts/kimodo_als/non-prop-final-retarget/retarget-bundle.json` 重新处理，没有再次调用生成模型。原作者／原生重定向回执和活动包保持原字节。新的生产 DAG 将相同原点依赖固化到 `targetPlanarOrigin`；[计划](../../artifacts/tests/2026-10-08-planar-root-ownership-v3/production-plan-v4/graph.json)包含这些依赖。

稳定规则见[水平运动所有权设计](../architecture/kimodo-planar-root-ownership.md)。本批证据目录为 `artifacts/tests/2026-10-08-planar-root-ownership-v3`；目录中的 v1～v4 产物是保留的前置失败／中间验证，最终结论使用明确标记 v5 的文件。

## 管线及原生验证

| 检查 | 最终结果 | 证据 |
| --- | --- | --- |
| Python 管线回归 | 113 项通过，含原点、残差／旋转保持、镜像来源、依赖顺序、移动基底参考帧、曲线及 FBX 哈希／FPS 拒绝测试 | [日志](../../artifacts/tests/2026-10-08-planar-root-ownership-v3/pipeline-tests-closure-v5.log) |
| 身体组件姿态重建 | 183 身份最大位置误差 3.1264e-13 cm；最终 raw pelvis-root 水平距离最大 6.000004 cm | [资产审计](../../artifacts/tests/2026-10-08-planar-root-ownership-v3/payload-audit-v5.json) |
| UE 5.8 最终 FBX | V4 88/88，全部物理骨骼／全部采样键通过；进程正常退出 | [完整回执](../../artifacts/tests/2026-10-08-planar-root-ownership-v3/full-pack-fbx-receipt-v5.json)、[日志](../../artifacts/tests/2026-10-08-planar-root-ownership-v3/full-pack-fbx-console-v5.log) |
| Godot 4.7.2 优化构建 | 0 错误／0 警告；后续改动均为离线 Python／Viewer，无新增 C# 改动 | [构建](../../artifacts/tests/2026-10-08-planar-root-ownership-v3/optimize-build.log) |
| 最终包导入 | 真实导入及 P2B 通过，273 资产／126 动画 | [导入](../../artifacts/tests/2026-10-08-planar-root-ownership-v3/godot-import-v5.log)、[P2B](../../artifacts/tests/2026-10-08-planar-root-ownership-v3/p2b-v5.log) |
| 六域实际请求 | recovery／aim／overlay／ragdoll／stop／overlay_prop 均生成并钉入包 | [请求](../../artifacts/tests/2026-10-08-planar-root-ownership-v3/requests-v5)、[finalize](../../artifacts/tests/2026-10-08-planar-root-ownership-v3/finalize-requests-v5.log) |
| 根运动开关 | 各 360 帧通过；关闭提取 0、位移 0；开启提取 73 帧 | [关闭](../../artifacts/tests/2026-10-08-planar-root-ownership-v3/root-policy-disabled-v5.log)、[开启](../../artifacts/tests/2026-10-08-planar-root-ownership-v3/root-policy-enabled-v5.log) |
| 最终 payload／原输入 | 1509 文件逐字哈希通过；原输入、活动指针不变 | [审计](../../artifacts/tests/2026-10-08-planar-root-ownership-v3/payload-audit-v5.json) |

FBX 验收沿用位置 0.01 cm、旋转 0.1°、scale 1e-4、时长 1e-5 s，以及严格键数／FPS 门限。完整数值读取 DataModel，不能将经过 Skeleton 平移重定向或虚拟骨重算的 Pose 求值冒充原轨道。原 T-Pose 两键、60 FPS、1/60 s 由显式 FbxFactory 和 SET_RANGE 保持；默认 Interchange 曾将其转成没有骨轨道的 30 FPS，失败日志保留于 [首次完整导出](../../artifacts/tests/2026-10-08-planar-root-ownership-v3/full-pack-fbx-console.log)。

另一次 88 个回读均通过后，UE 在退出时因压缩 hash 仍有在途任务而断言：[失败运行](../../artifacts/tests/2026-10-08-planar-root-ownership-v3/full-pack-fbx-console-v3.log)。该轮未 finalize。修复使用原生已注册的 `Editor.AsyncAssetCompilationFinishAll` 在修改／导入／释放 scratch 前收完任务；v4、v5 重跑均正常退出，没有仅凭成功 marker 接受随后崩溃的进程。

## 从 idle 开始的真实移动及截图

最终版按普通输入覆盖 walk 前／后／左／右／左前，每方向原版与候选各一进程，每进程从冷启动到松键后 idle 观察 600 帧，共 6000 帧／380 张 GPU 截图。第 30 帧按下、第 210 帧松开，命令在下一普通提交帧读取。逐帧保留物理、最终骨骼、足底和 root 消费记录；截图请求帧、实际帧、提交帧、显示帧、输入和骨骼身份全部一致。全部 10 个结构／穿地／身体脱离检查通过，0 violations。

| 候选方向 | pelvis-root 最大水平距离 | 起步区间最大相对单帧位移 | 稳态胶囊速度 | 足底质量对照 |
| --- | --- | --- | --- | --- |
| 前 | 5.92 cm | 0.59 cm | 1.75 m/s | 通过既有门限 |
| 后 | 6.83 cm | 0.92 cm | 1.75 m/s | 起步／持续移动失败 |
| 左 | 4.46 cm | 0.62 cm | 1.75 m/s | 起步／持续移动失败 |
| 右 | 5.66 cm | 0.69 cm | 1.75 m/s | 起步／持续移动失败 |
| 左前 | 5.89 cm | 0.56 cm | 1.75 m/s | 通过既有相对原版门限 |

五方向每帧均禁用 root 采样／消费；原版与候选胶囊净位移均约 5.275 m。物理速度配置没有修改。旧 v8 数米级骨盆起步偏移的原因是资产原点／轨迹和混合参考，不是关闭状态下消费了 root motion。本批最终前向最大水平偏移已降到 5.92 cm；只修循环而漏掉参考姿态的 v2 在同条件仍有 34.03 cm，相关中间证据保持。

足底中位滑移（起步／持续，cm/s）分别为：前 21.217／22.251，后 72.381／55.389，左 156.871／156.721，右 164.488／160.884，左前 58.148／21.588。门限保持原 `max(25, 2 × 同条件原版中位数)`，左前起步通过相对原版门限，不意味着所有滑移都低于 25 cm/s。峰值与完整采样仍保留在报告中。

[最终总结果](../../artifacts/tests/2026-10-08-planar-root-ownership-v3/godot-walk-five-directions-v5/summary.json)是失败，进程退出码 1；[原版／候选逐窗口比较](../../artifacts/tests/2026-10-08-planar-root-ownership-v3/godot-walk-five-directions-v5/comparisons.json)和[根／身体审计](../../artifacts/tests/2026-10-08-planar-root-ownership-v3/movement-root-audit-v5.json)区分结构通过与滑步失败，没有放宽门限。

已实际检查[前向首个按住帧 31](../../artifacts/tests/2026-10-08-planar-root-ownership-v3/godot-walk-five-directions-v5/candidate-walk-scheduled/forward-31.png)、[前向松键后帧 450](../../artifacts/tests/2026-10-08-planar-root-ownership-v3/godot-walk-five-directions-v5/candidate-walk-scheduled/forward-450.png)、[左前持续移动帧 150](../../artifacts/tests/2026-10-08-planar-root-ownership-v3/godot-walk-five-directions-v5/candidate-walk-scheduled-left-forward/left-forward-150.png)。HUD 明确标记 Root Motion OFF、候选版本、HELD／RELEASED 状态及 tick。

## Pipeline Viewer 原点对照

阶段⑧区分独立探针与活动包。新增首帧按钮，A/B 的同步以活动视口为主，修复 B seek 后立即被 A 覆盖的问题；导出标明 mode，仅链路模式附带链路资产名，避免 A/B 报告残留旧链路名称。Viewer Python 1 项、Node 3 项回归通过，并在真实浏览器操作及导出截图／坐标。

最终包 FBX 复制到独立、逐文件哈希记录的 `kimodo_ue/root_ownership/20261008_v5` 预览目录，保持两侧固定原点、相同相机和 time=0：

- [Walk 修复前后首帧](../../artifacts/tests/2026-10-08-planar-root-ownership-v3/walk-before-after-v5-frame0.png)：pelvis-root 148.6 → 1.1 cm；[坐标／相机报告](../../artifacts/tests/2026-10-08-planar-root-ownership-v3/walk-before-after-v5-frame0.json)。
- [Lean 基底修复前后首帧](../../artifacts/tests/2026-10-08-planar-root-ownership-v3/lean-base-before-after-v5-frame0.png)：350.3 → 0.8 cm，辅助根连线不再延伸到旧原点；[坐标／相机报告](../../artifacts/tests/2026-10-08-planar-root-ownership-v3/lean-base-before-after-v5-frame0.json)。

UE FBX 导出器可能附带一个重复尾键，Three.js 显示时域比原采样域长一帧；原生回读使用合同的 SET_RANGE，严格保持原键数／时长。Viewer 的首帧与绝对原点对照有效，同步仍按归一时间，不等于支撑相位对齐。

## 验收边界

已关闭本批 root／pelvis 水平所有权、静态原点依赖、IK 容器和 raw／最终 FBX 不一致的问题。全 183 身份的完整 71 项矩阵、Run／Crouch／翻越／全动作视觉验收本批没有重跑，不能引用旧证书宣称完成。Distance Matching 仍为独立曲线数据，没有接入播放控制。

后退和纯侧移母动作的支撑／方向／步幅质量仍需继续修复并重出整个闭包。按照既有发布门禁，本次不激活失败候选；这不表示默认 v8 的已知异常已经在普通启动中消失。没有提交或推送。
