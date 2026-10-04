# Rig 寄存器与编译遍历归 Core

沿用当前 locomotion 范围、ALS人物/骨架和Pistol/Rifle。此次将原Lyra FootPlant内的通用存储与控制流归入纯.NET Core，继续复用现有Core向量/四元数/精确姿态、Rig层级、双骨IK、Aim、Spring及Alpha内核。

## 实现边界

`AlsRigMemory`管理工作/只读/外部三类寄存器，类型化整值写入与嵌套字段路径、候选Clone、同布局工作内存重置。ResetWork只重置工作值，保留外部提交值；不匹配值类型与ResetWork外布局均在写入前拒绝。寄存器接受内置不可变值和`IAlsRigStructValue`的不可变扩展，拒绝数组等直接可写载荷；扩展方须遵守接口的不可变合同。opaque JsonElement独立持有文档生命周期。字段替换和根类型校验完成后才写入，失败不修改寄存器。

Lyra仍解析资源JSON和原类型名，AimTarget保留其Kind/Space并实现字段扩展；不把这个资源结构塞进Core数学Aim目标。原float/double寄存器量化点保留，候选存储由Core承担。

`AlsRigTraversal`拥有不可变的指令/分支/入口定义、九种已使用opcode、typed普通Unit/ControlFlowBranch/LazyIf调度、RunInstructions按完整operand身份去重，以及与之独立的按argument惰性缓存。每次入口调用独立创建执行缓存，故障后重试不继承残留，提供递归深度与访问数门禁。定义不固定FootPlant的436指令或入口名；Lyra适配层保留436布局/原两个入口校验、JSON解析、资源函数名到typed dispatch的映射与资产错误类型转换。

现有`AlsPoseCacheTraversal`是动画姿态缓存/更新上下文遍历，不能作为字节码控制流执行器；原Core缓存机制保持，通用Rig执行按其自身依赖语义实现。Core没有Godot、UE或Lyra程序集依赖，实际单位输入/输出仍由既有资源适配器连接，真实物理查询继续在Godot层。

当前支持运动图实际使用的单入口、无call/slice字节码；本批不是全UE RigVM实现，URO与完整UE调度仍后移。

## 已完成的验证

Core相关36项全过（新增Memory9＋Traversal10），0失败/0跳过。包括嵌套/custom struct取消隔离、literal根/子路径不可写、外部值跨ResetWork保留、外布局拒绝、非法路径/类型写入原子性、float/double量化、opaque文档生命周期、可写载荷拒绝；另包括work/lazy两个缓存的独立性、分支返回Completed、只求选中参数且每次入口/故障重试重新执行、定义防外部修改、循环/深度限制与无效定义拒绝。

最终Debug/ExportRelease v2构建均0警告/0错误。v1 Core测试结果沿用，因为Core源码没有后续变化；v2仅补齐适配层资产异常转换，并修正验证脚本的既有场景路径，未改变Core或公式。初次v1 Debug构建也成功，证据保留。

Debug与实际Optimize各自的原Rig遍历2520帧/683343访问/2520重试/6拒绝通过；输入1260帧/1058400变换/274680曲线精确差0。实际单位求解2520帧/2001solve/6552576比较，完整输出2520帧/2154output/174474骨/10279440比较通过，原位置/旋转门槛保持，取消重试与初始化覆盖。上述求解/输出使用原记录碰撞，不能等同于完整UE物理轨迹。

两构建各自的独立真实Jolt夹具2520帧/2484姿态/2520重试/30744命中/2976未命中/9初始重叠/21晚期故障通过；两份完整报告与此前Rig/Core批次逐项相同。完整Main＋Rig各7560帧/7296姿态/7560重试通过，其主图夹具采用解析碰撞。

两构建的普通ALS场景各1700帧、Lyra十角色各480帧/4800最终蒙皮发布通过，十角色完整报告与前一Alpha批次及两构建逐项相同。真实Jolt地形30/60/120Hz各450/900/1800帧、两角色，每构建6300最终蒙皮发布；实际台阶/斜坡/落差、站蹲/ADS/跳跃、两武器和取消重试通过，六份完整报告与此前地形批次及两构建逐项相同。

最终22成功Godot进程：两构建各8个Rig/普通＋3个地形，日志无ERROR/WARNING，两轮Optimize逐文件恢复六个Debug DLL/PDB。`tools/verify_rig_runtime_core.py audit`和`artifacts/lyra-analysis/rig-runtime-core-v3-audit.json`通过，9实施/验证源与其余4642基线保持，原资源JSON/配置保留。

构建和Godot运行使用v2，Core测试用v1。v3只修正审计工具读取真实物理报告的`actualGodotPhysics`字段，保留v2审计源副本/冻结哈希，并检查全部C#与runner仍匹配v2冻结源，没有为这一工具字段修订重复C#测试。每批实际运行期间实现源码冻结。

本批没有UE启动/构建/保存/新导出、资产JSON格式化或提交推送；没有重新执行前批710原包专项审计。没有新增GPU、全量managed、十分钟、性能、跨平台或实体键鼠验收；前批九张地形/蹲姿/握持样本保持为此前视觉证据。普通Demo重新启动供本机操作，Windows控制接口GetCursorPos 0x80070005和实体输入回报仍未解决。

仅关闭本批存储与遍历通用归属；当前完整目标及其余明确验收项继续由ROADMAP顶部约束。
