# Mantle 最终 typed event 分发与配置解析

本批将既有 Mantle 队列验证延伸到正式 AlsP5Runtime.TryPrepareSourceEvents 输出的 AlsEventBuffer，并增加最终事件到原生脚步配置的校验接口。普通角色动画图仍未接 Mantle，不能把这个集成测试当作场景攀爬完成。

## 实现

AlsMantlingHostNotifyResources 现在是不可变配置对象，复制脚步字典，在构造时验证每个脚步事件都有对应的 timeline，且为瞬时 Footstep、左右脚 payload 相符。事件查询使用 EventId、SourceActionId、SourceAnimationId、OccurrenceHandleId 四项作为键，并要求 Trigger 阶段、正播放 epoch、完整 payload 相符。

TryResolveFootstep 不产生副作用、不推进时钟、不维护第二个事件队列。调用者仍须只在帧提交成功后消费事件；该接口不自行证明帧已提交，也不代替角色身份或帧新旧校验。音频、粒子、贴花尚未执行。

## 验证

- 真实完整宿主 source binding/9 个 Roll/Get-up 动作资源、合并 Mantle binding 进入现有 P5 分发路径。没有新写一套事件调度器。
- 30/60/120 Hz × 正常/替换/隐藏/Ragdoll 四种情况 × 六个 Montage，各运行五秒，共 25200 个提交帧；每帧丢弃并重做一次候选。逐事件值、事件数、proxy RNG、notify instance allocator、active count、Montage queue 状态和全部物理播放实例一致。
- Roll 和 Mantle 同时存在时，Roll 状态事件不解析成 Mantle 脚步；正常结束每个频率总计 18 个 Mantle 脚步，替换后的新物理 epoch 有事件，隐藏与 Ragdoll 减少事件，结束后 bank 和 active state 清空。这是底层共存测试，不表示玩法允许同时 Roll 和 Mantle。
- 最终 Mantle 事件保持动作/动画/handle/播放 epoch/当前动画时间及左右脚，能够解析原生对象和设置路径。故意改动作、动画、handle、phase 或 payload 被拒绝。
- 另一项用实际动画图 sequence source tick 和 Mantle queue 进入同一次 proxy 分发，源事件数量保留，Mantle 事件正确追加，资源身份不混用。
- 新增 13 项通过；Import Release Mantling/MontageNotify/RecoveryActionProfile 最终 127 通过、0 失败、0 跳过，TRX：`artifacts/mantle-typed-events/import-final.trx`。Optimize 构建 0 警告、0 错误。
- 新组合测试初次编译因 lambda 捕获 ref struct bindings 及错误枚举 namespace 失败，改为复制所需元数据数组并使用 Contracts 枚举；生产分发算法未改，门槛未放宽。

本批没有新 UE 导出、原生完整图 oracle、Core 全量或 Godot 场景/渲染验收。实际普通 demo 图 relevance 尚未验证，本批以输入 mask 覆盖隐藏场景。

## 下一步

继续普通宿主完整曲线布局及控制消费、PostLocomotion 图位置与合并资源实例的接入，再连接障碍探测、移动基座和 Mantling motion 生命周期。上一批 Montage 曲线仍必须显式绑定，省略参数仅得到 sequence 曲线。

物理稳定性 9/12、Flail 0/3、复杂相机、非恒等 OrientAndScale 原生对照、旧移动 oracle 闭包、完整视觉与十分钟性能预算仍未完成；头颈、道具物理和音频暂缓。用户 plan 哈希保持 78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100，project.godot、头颈诊断及外部 uid 未修改/提交。
