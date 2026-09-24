# Mantle RootMotionSource 计算层

本批在主目录 `D:/GodotALS`、main 实现 `AlsMantlingRootMotion`，尚未接普通 demo。

## 实现

逐项对照本机 `Plugins/ALS/Source/ALS/Private/RootMotionSources/AlsRootMotionSource_Mantling.cpp::PrepareRootMotion` 及 `AlsCharacter_Actions.cpp` 的起终点建立过程：

- CreateAnchors 去掉起始/末尾根骨 scale，按 native GetRelativeTransformReverse 的乘法顺序结合 mesh base rotation，建立角色起点和目标对应的校正锚点。相对目标先还原世界坐标，最终锚点按基座转回局部空间；存储位置/旋转，丢弃 scale。
- Prepare 输入已发布 time、SimulationDeltaTime 和 Movement DeltaTime，分别处理。先推进 source time；duration 太小、delta 太小或目标已失效时，返回无 root motion，但仍返回推进后的 time。
- 有效帧计算 montage time，并输出 `max(0, montageTime - delta)` 供宿主在动画更新前同步。速度用 movement delta，不能误用 simulation delta 或直接当成位移。
- blend-in × location/rotation warp，各自保留 Linear / Cubic / HermiteCubic 计算。未知选项拒绝，不替换为线性。当前尚未绑定真实 montage BlendIn，调用方必须提供已解析设置；不能从 T3D 字段缺失猜测 Linear，本机 UE 的默认 option 为 HermiteCubic。
- 根据当前 movement-base policy，每帧从实时基座变换重建两端世界姿态，再去 scale。使用 shortest-path FastLerp 和重力轴 twist；最后将当前绝对 root 从 mesh 转为 actor 空间，生成目标 actor、世界速度和旋转增量。
- 接口只处理不可变值，不推进动画播放器、不操作场景。宿主负责 action/target generation、Prepare 结果发布、montage seek、销毁与结束；算法不擅自把 time 夹紧到 duration，结束调度仍归宿主。

## 验证

`AlsMantlingRootMotionTests` 8 项通过：带 mesh yaw 的起终点还原，独立时钟/seek/速度，三种清空条件，移动/旋转/非均匀缩放基座，gravity twist 与反号 quaternion，非法帧不改变锚点。

Core Release 定向回归 **40/40**（本批 + 原绝对 root、montage Root Motion、起始时间），日志：`artifacts/mantle-warp/mantle-warp-regression.trx`。初次 8 项日志为 `mantle-warp.trx`，没有失败。

`dotnet build GodotALS.csproj -p:Optimize=true --no-restore`：0 warning / 0 error。本批未改 UE 插件/资产、未启动 UE，未跑 Import 全量、Godot 场景或截图。上一批 652 根骨原生对照仅证明采样层，不是本批运动轨迹的独立 oracle。

## 下一步与限制

需要导出实际 montage BlendIn 参数，并运行真实 UE Mantling RootMotionSource 获取逐帧位置、速度、旋转与 montage seek 结果，覆盖静态目标、运动基座、时钟差异和失效。当前受控数学测试不是完整原生轨迹验证；native 锚点 FRotator 存储往返、压缩/真实动作混合和宿主时序也须在该阶段核实。

随后把设置/根骨/warp 组合成受 action 身份保护的运行时，接 Main 障碍探测、移动目标、起止/中断和目标销毁转 Ragdoll，再做普通 demo 视觉验收。当前无可玩的完整 Mantle。

全目标保持未完成：旧物理稳定性 9/12、Flail 0/3、复杂相机与视觉验收、整体物理缩放、最终十分钟性能预算继续保留。头颈与道具物理按用户要求暂缓。用户 P4 文件 SHA256 仍为 `78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100`；三份头颈诊断文件未修改/未提交。
