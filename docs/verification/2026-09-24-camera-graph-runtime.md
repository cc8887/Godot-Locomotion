# Camera 动画图逐帧运行时

本批在 `D:/GodotALS` 的 `main` 实现相机曲线图运行时，尚未替换普通 Demo 相机。承接图编译与基础相机数学；不把单元回归等同于完整原生相机验收。

## 实现

- `AlsCameraBlendList` 支持任意数量子分支，按原生当前目标权重缩放切换时长，保留中断后的各通道状态、归一化、零权重旧分支更新及 ResetChildOnActivate。自定义曲线提前达到终值时停止推进。
- `AlsCameraGraphRuntime` 消费实际编译图，输入 RotationMode、Stance、Gait、ViewMode、LocomotionAction 与左右肩。实现共享缓存延迟更新、每帧一次更新/求值、三 Look States 的六条过渡及每条过渡自己的曲线。
- 状态机首次更新先选择请求状态，再跳过初始过渡；从独占 Ragdoll 分支返回时重新初始化不再相关的 Look States。中断保留活动过渡栈，按原生顺序更新未完成过渡的两端，曲线按 Override/Accumulate 顺序合成。
- Prepare 在私有历史副本上生成候选；Commit 才发布曲线和推进帧号，Discard 不推进已提交时间。运行时要求独占 owner，未宣称同实例可并发调用。
- 当前类型依赖编译图，因此暂放在 `Als.Import/Runtime`，不令 Core 反向依赖 Import。尚未优化字典、委托和历史复制分配，不能据此宣布十分钟性能预算达标。

## 修复上一批默认值遗漏

原始 T3D 会省略默认 BlendType。此前编译器把省略值当作 Linear，而本机 UE 5.9 的 BlendList 默认为 HermiteCubic。本批没有只凭 C++ 默认值修改常量，而是只读反射实际资产的全部 12 个选择节点：10 个 CUBIC、1 个 CUSTOM、ViewMode 为 HERMITE_CUBIC。

导出 JSON 新增 blendNodes；编译器要求每个可达选择节点均有反射记录，并拒绝记录缺失、外来节点或显式 T3D 值与反射冲突。第一人称切换 0.025/0.1 秒时权重验证为 0.15625，而非 Linear 的 0.25。

首次使用 graph.nodes 被 Python 保护属性访问拒绝，日志 `artifacts/camera-runtime-reflection.log` 保留。改用 `ObjectIterator(EdGraphNode)` 后读取节点结构体成功，不需要改 UE 插件或保存资产。

## 验证与证据

- Core 相机专项 11 项通过：`artifacts/camera-runtime-tests/core-final.trx`。含四分支中断、复制隔离，以及三帧率下与既有二分支混合的逐值对照。
- Import 相机专项 23 项通过：`artifacts/camera-runtime-tests/import-final.trx`。含 30/60/120 Hz 共 1890 帧组合切换，每帧丢弃再试与正常提交精确一致；共享节点单次更新；Ragdoll 独占后恢复；480 帧逐帧 Look 中断；默认混合值和非法元数据拒绝。旧 630 帧/112 边界旋转参考、202 曲线采样一并通过。
- `dotnet build GodotALS.csproj -p:Optimize=true --no-restore` 通过，0 警告、0 错误。
- 按 ue-diagnosing-plugin-build-load 完成全 Editor 构建及全部项目插件审计：`D:/AdvancedLocomotionSystemV/Saved/Logs/PluginBuild/20260924T092822206Z-cfb467b4fce24996be421712ca7768c2`；ALS、AlsGodotExporter、AutoTestTools、BlueprintLisp 通过。fingerprint 为 `1CFC5657A618E30F849577D7EA003062B54DB04B41EF728389B91D449D0DF5E3`，BuildId 为 `186ff094-6861-4ab2-95dd-e0889004ba00`。没有修改插件源码、普通 Editor 重启或新的 DataValidation 验收。
- 两次成功冷导日志为 `artifacts/camera-runtime-reflection-2.log`、`-3.log`，退出 0。assets/settings/blendNodes/rotationReference/rotationBoundaries 逐字段相同；原始图仍仅三个未连接 ErrorTolerance PinId 不同，不声称整个文件字节一致。
- 没有重跑 Core/Import 全量、Godot 渲染或旧物理稳定性矩阵。用户 P4 文档修改和头颈诊断文件保持未提交。

## 下一步与边界

先增加原生 Camera AnimBP 的连续轨迹对照，验证中断、缓存与重入；当前组合测试证明本实现的事务一致性，不是独立原生全图 oracle。富曲线仍受 T3D 六位小数与求值精度限制，沿用 2e-6 采样容差，不声称 bit exact。

随后接入真实 socket/pivot、平台/瞬移、视角阻尼、第一人称/FOV、相机碰撞与初始穿透，并替换普通 Demo 输出。保持控制输入 yaw 独立于显示镜头阻尼。最终还需普通模式视觉验收及十分钟性能测试。

旧静态稳定性 9/12、Flail 0/3、Mantle 等未完成项不因本批关闭；头颈问题与道具物理按用户要求暂缓。
