# 第二十四批：候选姿势缓存与求值作用域

## 本批结果

继续完整性恢复计划 A/B，新增 `AlsPoseCacheEvaluation.cs`，并接入
`GroundedCacheSmoke.cs` 的真实 Detail/Stop/Plant 资源联合测试。未修改相机、输入、
UE 插件或 UE 资产，未提交或撤销已有工作区修改。

本批不是可玩 Demo 接线，也不是 UE 整图逐帧对照验收。

## 源码依据与实现

依据本机 UE 源码，而非猜测“同帧只算一次”的缓存规则：

- `Engine/Private/Animation/AnimNode_SaveCachedPose.cpp` 的 Initialize、CacheBones、Evaluate。
- `Engine/Public/Animation/AnimTypes.h` 的 `FGraphTraversalCounter`。
- `Engine/Private/Animation/AnimNodeBase.cpp` 的空基类 Initialize。

两个主要文件 SHA-256：

```text
AnimNode_SaveCachedPose.cpp
92D803B14E206EB379381D71E5C3366C3E1DB3F6FC518C8AF0CB5CE4550B8667
AnimTypes.h
31CCE827284996AD249A16C4D99C89E976CC1CDA741914E0380E144DA9722F80
```

具体规则：

1. 保留独立的初始化、骨骼缓存、姿势求值计数；遍历次数与全局帧号不能用角色 FrameId
   一个字段替代。计数为有符号 16 位，回绕跳过 -1；本批不实现通用 URO SkipFrames。
2. 初始化比较遍历计数，不要求全局帧号相同；初始化本身不清姿势求值计数。
3. 骨骼缓存同时比较计数和全局帧号；重建时清空对应源的求值计数。
4. 求值同时要求源计数匹配和当前最内层作用域存在载荷。内层不能直接读取外层载荷，
   内层结束后外层载荷仍可用；若内层改变了源计数，则外层也必须重新求值。
5. 缓存按编译来源节点区分，不按 AnimationId 合并。多个 UseCachedPose 引用共享
   一个生产节点，输出复制骨骼和曲线（含 Present），调用者修改输出不污染缓存。
6. 候选从已提交对象复制生命周期计数，不复制作用域姿势；关闭所有作用域后由调用者
   交换候选/已提交对象。异常作废候选，不能再读取或作为已提交状态使用。
7. 作用域令牌验证对象所有权、角色身份、序号和栈深度；拒绝旧令牌、跨对象读取、
   非 LIFO 关闭、容量溢出和缓存依赖递归。没有新增动画时钟或事件队列。

初始化重入存在一个需要明确的版本约束：本机 SaveCachedPose 声明了自身 UpdateCounter，
Initialize 条件也引用它，但该节点的 Update/PostGraphUpdate 没有同步此成员，基类
Initialize 为空。FPoseLinkBase 的同名调试计数是另一个成员。因此本批按该源码路径
实现初始化计数门控，不凭注释增加相关性间断重置。这里是静态源码核对结论，尚未用
新的 UE 原生生命周期探针验证；不能沿用第二十三批 Update 探针声称已验证 Evaluate。

## 集成与测试范围

联合测试现在通过导出的 Standing -> Detail -> Cycle 缓存引用惰性求值；Stop 各内容
状态也读取同一 Detail 缓存。实际 Detail 加法源、Stop/Plant、FootLock_R 曲线和下游
惯性化与原来的直接组合逐帧对比。重复读取前故意覆盖输出骨骼和曲线，验证复制隔离。
候选重试从同一已提交源时间、状态机和惯性化历史开始，缓存对象也从已提交对象重建。

Idle/Cycle 上游姿势仍明确使用 AdditiveBasePose 夹具，Main/Slot 上游为测试输入。
资源测试中的 Initialize/CacheBones 回调不是生产源播放器初始化；初始化的门控与
失效规则由 Core 测试覆盖，生产状态重入、来源初始化和 P5 事务接线仍未完成。

当前载荷只有固定布局的骨骼和曲线，不支持 UE CustomAttributes/Root Motion；不得
在属性携带路径中将此组件当作完整 SaveCachedPose 使用，也不能据此关闭 P5C。

## 验证结果

- Godot 工程构建：0 错误、0 警告。
- 新增 Core 缓存求值测试：16/16。
- Core 常规 Debug：1658/1658，排除两类长耗时 P5aGolden/P5aTraceSchema 测试。
- Import Debug：692/692。
- Release 缓存更新与求值：32/32，包含候选复制、多次读取与嵌套作用域零分配测试。
- 新联合资源测试：30/60/120 Hz，共 1260 帧、85680 个骨骼检查，3251 次缓存源求值。
- 原 Stop/Plant 组件回归通过；相机和输入 SHA-256 与第二十三批一致。
- `git diff --check` 通过，只有已有 LF/CRLF 提示。

```text
GROUNDED_CACHE_OK rates=30,60,120 frames=1260 bone_checks=85680 detail_frames=1000 stop_frames=128 requests=35 pose_evaluations=3251 pose_cache=scoped sync=asset_runtime cycle_update=once upstream_pose=fixture demo=not_connected
STOP_PLANT_OK rates=30,60,120 fixed_sources=12 sample_components=4608 bone_checks=2448 selectors=246 curves=72 machine_bones=71400 alloc=0B stop_machine=connected outer_state_machine=not_connected demo=not_connected
```

测试结果文件在 `artifacts/test-results/pose-cache-lifecycle/`。首轮 Core 命令误写长测试
排除名称，运行期间旧 `AlsPoseCacheTraversalTests.UpdateAndRetryHotPathAllocateNothing`
报告 2520 B 而非 0 B；随后停止该轮，纠正筛选后常规回归通过，Release 缓存测试也通过。
未修改该旧测试或放宽阈值。首次失败原因尚未确定，不将被停止的一轮记为通过。

## 后续必做

先验证真实 UE 缓存初始化/骨骼缓存/求值生命周期，再把 Main/Slot 上游、实际 Cycle、
Standing/Detail/Stop/惯性化、完整源布局和事件接入现有 Controller/Worker/P5 事务。
不直接把状态权重替换进旧 Idle/Cycle 混合，也不另造一套时钟或通知队列。

之后按恢复计划 C 完成动态上身 Layering/Add/LS/Mask/HandIK/YawOffset，再推进原定
P5B Overlay/道具、P5C Mantle/Roll/Root Motion、P6 恢复/完整相机、P7 十分钟性能门禁。
同输入 UE/Godot 的源时间、状态、曲线、关键骨骼和多帧截图验收仍需执行；本批没有
新的 Demo 滑移改善或上身修复结论。
