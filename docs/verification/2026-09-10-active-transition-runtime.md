# 活动过渡标量运行时

日期：2026-09-10。范围：完整性补完 A，兼作后续 Stop 状态机的共用基础。

## 已接入 Demo

- `AlsTransitionStack` 保存活动过渡的源/目标、时长、剩余时间、Alpha 和混合模式，
  使用候选帧内的值类型缓冲区；回滚不依赖重新推算历史。
- 新过渡保留旧过渡贡献；按 UE `GetStateWeight` 顺序衰减旧贡献并加入目标贡献。
- 返回已有权重的状态时缩短时长。HermiteCubic 使用原版的逆向近似多项式，
  Cubic/Custom 使用线性剩余权重，不能因两种曲线通常相似就混用这条规则。
- 更新全部活动过渡后，从最新已完成过渡向前清理；保留其后的未完成过渡。
- Cycle 方向求值不再整体等待上一过渡结束；普通方向边、偏向换髋可继续求值，
  中性换髋仍要求源状态实际权重严格等于 1，并满足 Feet_Crossing == 0。
- 从真实方向节点读取每帧最多 3 次转换、首次更新跳过过渡、重新相关时初始化。
- 身体/腿部贡献按每条过渡分别应用 WeightFactor 后组合，并缓存在候选帧中，
  供现有 Cycle 图的姿势与曲线采样消费。没有新增独立的事件调度器。
- `measure-p4-cycle-capture.ps1` 改为按 `TransitionsStartedThisFrame` 识别新捕获中的
  换髋启动，旧捕获仍读取原来的 elapsed == 0 标记；原版启动帧已经推进 delta，
  不应要求 elapsed 必须为 0。

缓冲区容量 128；溢出报错，不丢弃仍有贡献的过渡。30/60/120 Hz 的 Core 快速重入
压力测试通过，Godot 本批快速输入最多观测到 11 条活动过渡。128 是实现容量，
不是声称 UE 本身有此上限；更高频率/更大图仍需明确容量预算。

## 原生证据的边界

`AlsTransitionMathCommandlet` 直接调用 UE 的：

- `FAlphaBlend::AlphaToBlendOption`：Linear、Cubic、HermiteCubic，363 个数值样本。
- `FAnimNode_StateMachine::GetStateWeight`：65 组注入的活动栈，每组 6 个状态权重，
  覆盖 0 到 64 条过渡及返回已有状态，共 390 个输出。
- 原生反射读取 `(N) CycleBlending` 的 Directional States 节点设置。

输出为 `tests/Als.Core.Tests/Fixtures/P3/v4_transition_math.json` 和
`assets/config/v4_direction_state_machine.json`。对照容差为 1e-6。
注入的栈不是运行完整 ALS AnimBP 得到的轨迹；重入时长、删除顺序、规则求值次序
来自本机 UE `AnimNode_StateMachine.cpp` 的源码移植与单元测试，不能混称为
已通过完整原生状态机/角色姿势对照。

UE 导出前按插件技能执行完整 Editor target 构建及插件一致性审计，通过。
只读 commandlet 返回 0：`ALS_TRANSITION_MATH_OK alpha_rows=363 stacks=65 assets_saved=0`。
未运行正常编辑器 GUI 重启或打包认证，本批不作完整插件发布认证。

## 回归结果

- 相关 Core 测试：280/280，包含 13 项新过渡栈测试及 2 项新增方向门控测试。
  本批未重复执行耗时的全量 P5A Golden 套件。
- Godot 工程编译：0 错误、0 警告。
- Standing Cycle：30/60/120 Hz × 3 起始相位，9 次中性换髋、231 等待帧，
  原 9 次回滚及新增 9 次多过渡历史回滚/重试均通过，热路径 0 B。
- 新 Cycle 的 single/parallel 各 180 帧摘要相同：结果 `9481C451AD5B9D2C`，
  完整姿势 `E558DA509B549995`，角色根 `309E8D0E0BEEB2CB`。
- 新 Cycle 的 single/parallel 晚期事务失败注入均通过。
- `verify-p4-pose.ps1`：旧图、P4 姿势、两模式 Foot Placement 及晚期回滚通过。

| 捕获目录（artifacts 下） | 帧/截图 | 最大单帧脚旋转 | 最大活动过渡 |
| --- | --- | --- | --- |
| transition-stack-rapid | 720/120 | 13.502 度 | 5 |
| transition-stack-strafe | 720/120 | 14.122 度 | 1 |
| transition-stack-run | 720/120 | 12.309 度 | 2 |

已查看快速换向多个帧的实际截图，没有将无崩溃或角度门禁当作 ALS 动作质量验收。
普通横移起步低位脚位移代理为 5.156084 cm/帧；跑步捕获为 6.281955 cm/帧。
这些仍不是支撑脚接触锁定误差，也没有通过 UE 起步基线验收。

## 下一步与未完成项

当前 Cycle 图仍把各状态贡献汇总到源动画权重，再用现有 Blend2 链求姿势。
UE 则按活动过渡顺序逐层求中间姿势；四元数混合与归一化不保证可结合。因此
本批只闭合状态/标量权重这部分，不能称为完整逐层姿势求值已完成。

必须继续补齐中间姿势混合、曲线组合及其原生对照，然后接入已导出的 Stop
状态/Plant 数据，使用正确的 Mesh Space 分层顺序；不能用后置腿部修正掩盖错误顺序。
ShouldMove、Stop/QuickStop/Pivot 事件、动态分层、P5A 正式接线、Overlay 及后续阶段
均保持原范围。相机与键鼠输入未修改。
