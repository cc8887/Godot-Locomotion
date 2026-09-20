# Aim 嵌套状态更新与帧事务

日期：2026-09-12，第一百零九批。工作区 `D:/GodotALS-p5a-events-actions`。
承接第 108 批原生 Aim 定义，继续 P4 完整上身执行链；没有 commit/revert。

## 已实现的运行链

`AlsAimStateMachine` 执行实际编译定义中的转换出口，维护有序转换栈、每条
转换的曲线身份、当前状态停留时间、初始化和更新序列。三个状态机共享
只读定义，各角色各机器拥有独立 scratch，不在定义对象上保存播放状态。

`AlsAimFrameRuntime` 管理 Behavior/Input/Camera 的嵌套更新及七个 evaluator
输入历史。初始化、清理来源缓存权重、来源更新形成有序操作流；每个来源
独立保存初始化代次、上次更新序列、显式输入、权重和 inactive 状态。
同资产不同状态不会共用一个历史。

Prepare 从已提交快照产生候选；ValidateCommit/Commit 用完整角色/代次/
帧身份校验，Cancel 丢弃候选。内部失败不能提交，调用方可取消后同帧重试。
最终层所有者必须在姿势、曲线和事件均成功后统一提交 BaseLayer 与 Aim。

## 根据 UE 源码保留的语义

检查当前引擎 `AnimNode_StateMachine.cpp` 与 `AnimInstanceProxy.cpp` 的
Initialize、SetState、Update、GetStateWeight、状态 getter 和权重缓冲实现：

- 首次更新仍执行转换选择，再丢弃首次混合，不是完全跳过转换规则。
  一个机器每帧最多三次转换。初始化事件与来源更新的顺序保留。
- `GetInstanceStateWeight` 读取 proxy 上一帧缓冲；
  `GetInstanceCurrentStateElapsedTime` 读取机器当前 elapsed，SetState
  后立即归零。不能统一从上一帧快照取这两类 getter。
- 尚未退休的转换依次更新来源/目标状态，每个状态每次遍历只更新一次。
  权重为零不意味着可以跳过更新。先收集更新，再清除已完成转换及更早转换。
- 目标状态仍有权重时，重入清除其完整来源缓存权重，但不重新初始化；
  失去全局更新相关性后恢复，则重建机器初态与对应子来源。
- proxy 每帧清零写侧权重。未遍历的机器本帧记录为零，不把隐藏前的状态
  权重一直反馈下去；机器自己的历史保留到重新相关时按策略重置。
- 来源权重乘上传入上下文权重，proxy 状态权重保持机器内部相对权重。
  inactive 从父状态向下传递，外层零权重/inactive 不自动禁止子图更新。
- 独立转换继续使用原生曲线和持续时间。中途回到仍有权重的目标时，
  有效转换时间会缩短，不能强制维持配置原始秒数。
- Head profile 只影响逐骨贡献，不能反过来改变状态更新的标量权重。
  当前提供逐骨贡献诊断；未来姿势必须按转换栈顺序混合，不能把所有
  quaternion 一次加权代替 UE 的顺序混合与最终归一化。

## 测试与实际运行

使用第 108 批正式导出的 ALSV4 定义与原生曲线，未改成简化测试图。
以下日志在 `artifacts/`，进程均实际退出 0：

- `aim-frame-tests-final.log`：43 项相关测试通过，其中 13 项本批运行时测试。
  覆盖首次隐藏初始化、正在淡出时反向重入、左右回看连续转换、两秒严格
  门槛、状态权重旧缓冲、逐骨权重、零权重/inactive、序列空洞和身份保护。
- 30/60/120 Hz 各十二秒，共 2,520 帧；每帧均取消候选，再从同一已提交帧
  重试，逐项比较三机器完整栈、每条曲线身份、全部记录权重、七来源历史与
  全部操作顺序。十角色 single/parallel 结果一致；热 Prepare+Commit
  10,000 次托管分配为 0。这不是 P7 十分钟整角色性能验收。
- `aim-frame-build-verified.log`：Godot C# 构建 0 警告、0 错误。
- `aim-frame-mapped.log`：在实际 BaseLayer 帧组件的输入协作夹具中运行
  3,360 帧，Aim 消费候选瞄准输入；3,058 次来源更新、63 次来源初始化、
  302 个隐藏帧、12 次晚期姿势故障，状态和有序来源操作重试一致。
  普通 Editor 的 Aim 原生定义重复编译检查也保持通过。
- `aim-frame-legacy.log`：原未映射入口 3,360 帧、271 隐藏帧、6 次晚期
  故障回归通过。已有事务行为保持。

首次测试编译的 in 参数捕获/断言分析器问题已修正，相关日志保留。
初次重入测试使用的 delta 长于原生规则缩短后的有效时间，转换已结束，
因此改用仍落在转换持续期内的输入来验证重叠曲线，未放宽断言或修改算法。

## 证据边界和下一步

本批未修改 UE 插件、配置或资产，不重复原生构建/打包。UE 证据来自当前
源码语义检查与上一批导出的真实图/曲线/Head 数值；尚未运行完整嵌套
状态机的原生逐帧探针，不能将本批轨迹测试称为 UE 全状态数值对照。

协作夹具明确传入 Aim 相关性，不表示生产 Aim 相关性等于 BaseLayer Slot。
`AimFrameSmokeChecks` 仅用于测试，没有把此关系硬接进正式 Demo。
新运行时尚未执行真实 Aim 动画采样、BlendSpace 滤波/网格采样、来源姿势/
曲线求值，也尚未接入正式 Overlay/LayerBlending。没有新截图或视觉通过结论。

下一步补真实 Aim BlendSpace 网格和原始来源、保留顺序的逐骨姿势求值与
原生状态/来源对照，再接正式 Overlay/BasePoses/LayerBlending 最终输出。
随后闭合真正最终曲线反馈、Foot IK/Lock/pelvis/平台，并用同输入/脚相位
对照 UE/Godot 上身、换髋与支撑脚滑移。原 P5A、P5B 全 Overlay/道具、P5C
Mantle/Roll/Root Motion、P6 物理恢复/完整 Camera、P7 十分钟预算继续保留，
音频暂缓。
