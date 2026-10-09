# Kimodo 目标骨架的水平运动所有权

生产管线在目标 rig 和原资产采样时域确定后，执行 `target-planar-ownership-v1`。入口是 `root_ownership.decompose`，由 `publish.prepare_target_raw` 调用；上游 master、镜像与 Pose 派生仍先遵循共同原点契约。运行时不重新求解轨迹。

## 分解与保持

原生目标坐标为厘米、XY 地面、Z 向上。先通过完整 FK 得到 pelvis 的组件空间位置，其中已经包含现有 root 运动，不能再相加一次。其水平位置分成整体轨迹与残差，再一次性去掉轨迹起点的水平常量：

```text
pelvis_world_xy(t) = trajectory_xy(t) + residual_xy(t)
origin_xy = trajectory_xy(0) - root_reference_xy
root_authored_xy(t) = trajectory_xy(t) - origin_xy
```

对身体的 root 直属子骨骼，按新 root 的逆变换重算局部位置和旋转。结果必须重建 `原身体组件姿态 - 一个固定水平平移`，每个物理形变骨骼的最大坐标误差不得超过 `1e-5 cm`，旋转矩阵误差不得超过 `1e-6`。`ik_foot_root/ik_hand_root` 容器固定为 root 下的参考局部变换，不能跟着旧世界原点平移而遗留在数米之外；IK 手脚端点重新由最终身体 FK 求解，包括手枪骨父子层级。回执记录 `ikContainerPolicy=root-reference-local`。

pelvis 保留自然的水平重心摆动、Z 起伏、pitch/roll 和身体旋转；不能逐帧将其水平坐标全部清零。翻越沿用既有 root 高度所有权：按参考 root 加 pelvis 高度差直接赋值，避免重复添加水平路径。转身的 yaw 沿用原来基于作者髋部朝向的提取规则，本次不把 pelvis 的全部旋转转移到 root。

原地 gait/start 若全部水平样本位于均值周围 6 cm 内，使用常量整体轨迹，完整保留重心摆动；即使动画范围为零，远离原点的常量仍会消除。明确的短线性运动直接保留。其余连续动作复用 Kimodo 的 ADMM 轨迹平滑器，使用官方 0.06 m 的残差半径，并在最终键帧上继续求解，避免多层插值末层造成半帧偏移。生成器、模型和 GPU 推理不参与这一步。

Pose 表、gait/base 参考、Lean/Acceleration 加法参数和 Look 网格是离散状态，不按帧序列推断行进轨迹；但静态参考也不能携带母动作的远处原点。DAG 的 `targetPlanarOrigin` 固化目标阶段的原点依赖，stage 在独立拓扑顺序中处理全部目标 raw，不依赖 bundle 的作业排列。

步态 Pose 和 Lean 基底读取对应 gait master 的作者采样帧；右侧 Pose 读取右侧 master，不能直接复制左侧原点。Lean/Acceleration/Look 读取原合同指定的 `baseAsset/baseFrame`。一个依赖 Pose 的水平常量为 `parent_origin + parent_authored_root_xy(reference_time) - root_reference_xy`，所以从移动基底中抽取的参考帧也会移除该帧的行进距离。身体只做一次常量平移，保持 Pose 各槽和 additive 相对基底的平移差，不逐帧中心化。没有作者原点依赖的 canonical Stand/Crouch 和 T-Pose 保持原自然残差。

原点回执记录父输入、authored/runtime 语义哈希、作者帧及时间。缺失依赖、循环、过期声明或越界参考帧会阻断 stage。Overlay 下身随后组装自同一版 Stand/Crouch，组装后仅重建辅助骨，并保留组装前的回执与基底哈希，避免重复消除原点。重新生成 master 后仍须更新整个派生闭包。

## 两份姿态与曲线

每个非道具动画保存独立目录：

```text
root-ownership/<source SHA256>/
  authored-root.raw.json
  runtime.raw.json
  ownership.json
```

`authored-root` 保存完整轨迹分解。原合同未启用 Root Motion 的普通 ALS 动作，从同一 residual 创建固定 root XY 的 runtime 版本；其 root 不会在开启全局开关后额外驱动普通步行。合同声明 Root Motion 的动作以及翻越保留 authored 版本，关闭运行开关时仍由现有采样策略忽略 root。

曲线在 runtime 视图创建前后分别烘焙。`rootMotionCurves` 对应最终 runtime raw；`extractedRootMotionCurves` 对应 authored root，并另外记录 `displayedRuntimeRawSemanticSha256`。曲线原有 `runtimeRawSemanticSha256` 字段绑定传入烘焙器的那份完整姿态，必须结合索引中的视图种类读取。`ownership.json` 记录输入、两份输出的语义哈希、原点和重建误差。

SOMA 已在上游转原地的 gait，不会重新凭空产生行进距离。其原始行进数据继续由作者检查的 `authoringTrajectories` 独立保存。Distance Matching 的数据来源和运行开关见[根运动契约](als-root-motion-policy.md)。

## FBX 与 raw 一致

发布 stage 的 FBX 状态为 `pending-native-bake`。生产脚本随后运行 `bake_kimodo_final_tracks.py`，复制原生动画到专用版本目录，写入 runtime 的完整物理骨骼轨道，并导出 FBX。原动画只读取。只有最终 FBX 导回 UE 后通过所有物理骨骼、所有采样键的比较，`publish finalize-fbx` 才更新 manifest 和文件哈希。

比较读取原生 DataModel；普通 Pose 求值可能再次按 Skeleton 参考平移重定向或重建 virtual bones，不是 FBX 导出器读取的原轨道。没有原始 raw bank 的 V4 参考资产仅从重定向回执保留物理骨骼，virtual bones 由最终层级重建。显式 `FbxFactory` 和动画采样范围避免 Interchange 将两帧 60 FPS 姿态导成缺少轨道的 30 FPS 数据。批处理使用 UE 已注册的 `Editor.AsyncAssetCompilationFinishAll`，在写入、导入和释放 scratch 前收完异步任务；生产脚本还必须检查进程退出码，不能只凭回读成功标记接受随后崩溃的运行。

验收保留原阈值：位置 `0.01 cm`、旋转 `0.1°`、scale `1e-4`、时长 `1e-5 s`；键数和 FPS 必须匹配。不同输入、丢失轨道、不完整回执、失效哈希或未完成 FBX 烘焙均阻断正式激活。仍须通过既有完整 E2E 和原版方向质量门禁。

Viewer 的独立对照位于阶段⑧，所有资源采用固定原点和共同相机；首帧按钮同步暂停所有视口。验证入口 `python -m tools.kimodo_pipeline.probe_root_ownership` 准备当前活动 Walk/Roll 的新派生输出，不更改活动包。实测及未关闭范围见[本批验证](../verification/2026-10-08-kimodo-planar-root-ownership.md)。
