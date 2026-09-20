# 第二十五批：UE 原生缓存生命周期对照

## 结果与边界

第二十四批缓存组件不再仅依靠源码推断：本批新增只读 UE commandlet，通过真实
`UAnimInstance::ParallelEvaluateAnimation` 入口执行缓存求值，并导出原生操作轨迹。
Core 在 30/60/120 Hz 三组中逐操作回放，初始化、骨骼缓存、更新、求值次数、四个
骨骼的位置及曲线存在性/数值全部匹配。每组再从同一已提交状态重试一次也匹配。

本批没有新增 Demo 功能、重置策略、动画时钟或事件队列；不将合成输入的原生节点
对照等同于完整 AnimBP/角色移动的视觉验收。相机和输入未变，完整计划继续保留。

## 实现

- `AlsPoseCacheCommandlet.h/.cpp`：`-run=AlsPoseCache -Output=...`。
- `v4_pose_cache_lifecycle_native.json`：原生期望数据，作为 Core 测试夹具保存。
- `AlsPoseCacheNativeTests.cs`：真实记录的操作顺序驱动既有 Core 缓存更新及求值组件。

探针使用临时 UAnimInstance 子类及原生代理，明确设置代理侧的遍历计数，不写入
SaveCachedPose 内部生命周期成员；派生缓存只读取自身 UpdateCounter 是否已同步。
源节点生成四骨骼/两条曲线的可辨识载荷，第二条曲线交替存在，输出故意被修改后
再次读取，以验证值复制和缓存隔离。输入骨架来自 ALS_N_Walk_F，源姿势是合成数据，
不是该 Walk 资源的动画轨迹，也不包含自定义属性或 Root Motion。

原生求值作用域由引擎公开入口创建，嵌套调用该入口产生真实内层作用域；没有在插件
中仿写 FCachedPoseScope。每组 51 个操作：12 次初始化调用、5 次 CacheBones、5 次
Update、17 次读取、6 次进入和 6 次退出作用域；共 153 个操作。30/60/120 Hz 只改变
Update delta，生命周期规则由显式计数/作用域驱动，不宣称这是完整实时角色轨迹。

## 已观测的关键行为

1. 同一作用域、同一计数的重复读取不重新求值，修改输出也不修改缓存。
2. 内层作用域首次读取会重新求值，返回外层后可复用外层自己的旧载荷。
3. 内层改变计数后，外层不能只因自己仍有载荷就复用；源计数不同会触发重新求值。
4. 骨骼缓存重建使姿势求值失效。仅全局帧号变化、遍历次数不变也会重新 CacheBones。
5. 初始化计数变化会转发到源，但不会单独使同作用域中的已有姿势失效。
6. 初始化仅比较遍历计数；仅代理全局帧号变化不会再次初始化。
7. Update/PostGraphUpdate 后，SaveCachedPose 自身 UpdateCounter 仍未同步。
   代理 Update 从第 0 次跳到第 7 次后重入，源初始化次数仍保持不变。
   因此第二十四批没有按该成员虚构相关性间断重置，是与当前原生行为一致的。
8. 初始化计数经过 32767 -> -32768，以及 -2 -> 0，匹配有符号 16 位回绕并跳过 -1。

Core 原有生命周期实现无需调整算法，仅把相关注释更新为已有原生证据。
URO SkipFrames、CustomAttributes、真实来源播放器初始化以及整图状态/事件轨迹
仍未被这些测试覆盖；不得据此关闭相关生产接线或 P5C。

## 可重复证据

首次和独立重复导出的 SHA-256 相同：

```text
124C25C6DD7CB2A192CCD9E6E5FD476BA8A1C7518DBA7ADE27E4BDCB06AF79B6
```

两次原生命令 exit 0，日志均为：

```text
ALS_POSE_CACHE_LIFECYCLE_OK traces=3 operations=153 assets_saved=0
Success - 0 error(s), 0 warning(s)
```

日志与重复导出在 `artifacts/unreal/pose-cache-lifecycle-*20260910.*`。
Debug/Release 缓存相关各 35/35；Core 常规 Debug 1661/1661（排除两类长耗时
P5aGolden/P5aTraceSchema）。TRX 位于 `artifacts/test-results/pose-cache-native/`。
Import 无代码或元数据变更，本批未重跑全套 Import，上一批结果仍单独保留。
Godot 构建 0 错误、0 警告，资源联合组件再次通过：

```text
GROUNDED_CACHE_OK rates=30,60,120 frames=1260 bone_checks=85680 detail_frames=1000 stop_frames=128 requests=35 pose_evaluations=3251 pose_cache=scoped sync=asset_runtime cycle_update=once upstream_pose=fixture demo=not_connected
```

首次构建有探针 FParallelEvaluationData 限定名错误，修正后完整构建通过；Core 新测试
首次有 System.Math 命名空间冲突，修正后全部原生数据匹配，但最后的读取次数断言
误写为至少 18，实际脚本为 17。改成精确 17 后通过，不改变任何数值或姿势容差。
初次失败 TRX `cache-native-debug.trx` 保留，成功记录使用新文件名。

## UE 构建与冷启动

本批遵循 ue-diagnosing-plugin-build-load：完整项目 Editor 目标构建、三个适用项目
插件审计、构建状态文件、命令行冷加载、独立 BuildPlugin、DataValidation 与非 NullRHI
Editor 重启均执行。未复制打包 DLL 到工程，未保存 UE 资产。

首轮 UBT 重建既有 NetCore 引擎模块，使选定引擎的 BuildId 改变。默认包装器随后
正确拒绝旧项目 receipt。核对路径均为项目内生成产物、无整插件链接后，使用技能
包装器将 receipt、三个插件的 manifest/DLL/已有 PDB 移至可恢复目录：
`D:/AdvancedLocomotionSystemV/Saved/BuildReceiptBackup/20260910T124001575Z`。
没有删除源码、内容或配置，没有手改 BuildId。

成功构建日志前缀：`20260910T124001803Z-4a0fe46f2ffe479cb425197b7c702b29`。
统一 BuildId：`2c97e25c-a06c-48c2-b92f-f287ca2545d5`。
状态 fingerprint：`D3DD746EFF364D7FD73294A67AE98E00A5CE214F84778F7FF041F5DE25DF995F`。
原生导出日志确认 AlsGodotExporter、AutoTestTools、BlueprintLisp 均冷加载。

独立 Win64 BuildPlugin exit 0，产物目录
`artifacts/unreal/AlsPoseCacheLifecyclePluginValidation-20260910`；随后项目审计再次通过。
DataValidation exit 0，688 资产，0 errors / 3 warnings，仍是既有 AI ActionsComp 与
Navmesh 版本问题。D3D12 Editor 初始化完成并 exit 0，但重现上一批的两条
`LogAutomationTest: Error: Condition failed`、旧 AI/Navmesh、LineSetComponentMaterial
和 MotionVectorSimulation 警告。它证明可冷启动，不能宣称干净的交互验收。

## 下一步

缓存生命周期的此次原生验证已完成。后续直接推进生产来源初始化、全图清单约束下
的正式绑定与共享 Sync，把 Standing/Detail/Stop/实际 Cycle 的姿势和曲线接入现有
Controller/Worker/P5 候选提交事务。不得继续用夹具替代 Main/Slot 上游，或把局部
37/59 来源数量冻结为最终 P5 全局布局。

随后继续动态上身分层、Overlay/道具及原定 P5C/P6/P7。普通起步与换髋的实际 Demo
回放、同输入 UE/Godot 全状态/源时间/关键骨骼对照和人工多帧视觉验收仍未完成。
