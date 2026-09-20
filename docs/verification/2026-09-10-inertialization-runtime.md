# 第十批：惯性化运行时与 UE 原生节点轨迹

日期：2026-09-10。工作区：`D:\GodotALS-p5a-events-actions`。
范围：完整性补完 A 中 Detail 所需的下游惯性化组件。尚未接入可玩 Demo。

## 实现

- `AlsInertialization` 保存最近两次输出姿势、曲线、组件变换、附着父身份和时间间隔。
  历史必须是惯性化之后的输出，不能继续记录未经修正的源动画。
- 显式分离 Update、Request、Evaluate；支持多次 Update 后一次求值、同帧多个请求取
  最短时长、没有历史时丢弃请求、首次消费请求时先推进时间、连续中断的 deficit。
- 对位置、旋转轴角、缩放差量和曲线使用 UE 的五次多项式衰减；保留投影速度估计、
  背离目标速度截断、过大趋近速度缩短时长。不用普通交叉混合或指数平滑替代。
- 根骨处理区分局部空间、组件旋转变化和附着父变化；瞬移取消待初始化请求，但不
  直接取消已经激活的惯性化。后者是当前 UE 实现行为，不是自行设计的重置策略。
- UE 厘米阈值通过 unitsPerCentimeter 适配 Godot 米制；缩放使用差量加法，而不是
  Detail 加法动画的乘法缩放公式。两类节点不能共用同一个缩放合成操作。
- 曲线存在性与数值分开，支持新出现和退出的曲线。当前 UE 曲线导数中的
  `Delta - Value - Prev2Value` 表达式按源码保留，原生轨迹对照通过；没有擅自改为
  看起来更自然的相邻两帧直接相减。
- 候选与已提交实例使用预分配 CopyFrom 完整复制历史和待消费请求，支持重试/回滚。
  参数和输出布局在消费请求前验证，支持完全原地输出，拒绝错位重叠。
  Reset 是组件重新初始化接口，不宣称它对应 UE 当前为空实现的 ResetDynamics。

## 原生参考

依据：本地 UE 5.9 的 `AnimNode_Inertialization.cpp`，包括 CalcInertialFloat、
Update_AnyThread、Evaluate_AnyThread、InitFrom、ApplyTo、Deactivate。

新增 `AlsInertializationCommandlet`，通过源节点 GUID 找到 ALS_AnimBP BaseLayer 的
编译后 Inertialization 节点模板。复制模板保留有效 NodeData，再将输入接为合成姿势。
实际调用原生节点 Update/Evaluate，不把 C# 运算写入参考文件。

15 组轨迹 = 30/60/120 Hz × 5 个场景，每组 60 帧、4 个骨骼、3 条曲线，共 900 帧。
覆盖单帧历史、连续中断及同帧多请求、组件旋转、零时间步、多次 Update 和瞬移请求。
位置/缩放误差限 0.00003，四元数正负等价后的分量距离限 0.0001，曲线误差限
0.00003。每组要求存在非零原生位置修正，防止节点透传输入也被算作有效参考。

遵循 `ue-diagnosing-plugin-build-load`：每次改动原生插件后完整构建 Editor target，
审计适用项目插件，再冷启动 commandlet。未复制 DLL、修改引擎或保存 UE 资产。
最终构建 fingerprint：
`786E10134C968E0838576CFFFD6BA2B4B8C8BCD7B2C5B277942641DFE3F4E167`。
构建记录位于 UE 项目 Saved/Logs/PluginBuild，前缀为
`20260910T052711860Z-f90b5c3b641141e89a423acca263b019`。

首次原生运行因独立构造节点缺少 NodeData 断言退出；第二次通过类型遍历查模板退出 4。
最终改为源 GUID 定位后成功。失败日志保留，不以成功记录覆盖；没有绕过引擎断言。
最终日志：`artifacts/inertialization-native-guid-20260910.log`，退出码 0，0 errors / 0 warnings。

```text
ALS_INERTIALIZATION_OK traces=15 frames=900 bones=4 assets_saved=0
```

fixture：`tests/Als.Core.Tests/Fixtures/P3/v4_inertialization_native.json`。
SHA256：`EBD69941758F93EE75F53AFEC4094BF17A0644133A258502E95980EBB0900DE8`。
没有进行 GUI 重启、项目数据验证或打包，不作为插件发布验收。

## 验证

- Core Locomotion：343/343，其中惯性化 11 项，含 900 帧原生对照及逐帧候选复制一致性。
- Import 全量：581/581。Godot 构建：0 warnings / 0 errors。
- 新增 `inertial_detail_smoke.tscn`，使用真实导入 Detail 动画与基准进行联合求值。
  30/60/120 Hz 下累计 28560 次骨骼检查、420 次候选重试；结束后输入精确透传。
  预热后的 1000 次循环中，采样、曲线、候选复制及惯性化求值的托管分配为 0 B。
  状态切换和四个源时间由测试显式提供，不是完整状态机或 Sync Runtime 的验证。

```text
INERTIAL_DETAIL_OK rates=30,60,120 bone_checks=28560 retries=420 max_translation_correction=0.158560 alloc=0B state_machine=not_connected
```

最终联合测试日志：`artifacts/inertial-detail-smoke-final-20260910.log`。
Detail Pose 和 Stop Plant 回归通过，计数与第九批一致；对应日志为
`artifacts/inertial-detail-regression-20260910.log` 和
`artifacts/inertial-stop-regression-20260910.log`。
Standing Cycle 回归通过：9 组频率/相位、9 次普通回滚、9 次中断回滚、63 次姿势检查，
空闲/活动路径均为 0 B，摘要与第九批一致；日志为
`artifacts/inertial-cycle-regression-20260910.log`。
Camera/Input SHA256 与第九批相同。没有重新录制可玩 Demo 截图，没有运行全量 Core/P5A/P7。

## 边界与下一步

当前仅支持固定完整骨骼/曲线布局，以及本 ALS BaseLayer 节点使用的默认过滤和
无 per-bone blend profile 的请求。未实现通用过滤/tag、跳过缓存姿势时的请求转发、
骨骼 LOD 布局变化、曲线标志和 root-motion attributes。后者留给原定 Root Motion 阶段。
附着父变化已有组件单测，但尚无原生轨迹；非均匀/负组件缩放也未做原生一致性验收。
原生探针是四骨骼合成输入，不是整张 ALS AnimBP 的真实角色动作回放。

接下来补 Detail 状态时钟、相关性/重置和源时间，再接 ShouldMove、Not Moving /
Moving / Stop 及 Plant，统一接入 P5A 播放身份、Sync 和事件事务。七条 Detail 惯性化
转换应向下游共享节点发请求，不是给每个加法源各放一个独立惯性化历史。
之后继续动态 Layering、Overlay/道具及既定 P5C/P6/P7。
本批没有修改可玩 Demo 输出，不能据此宣称滑步、交错步或换髋观感已修复。
