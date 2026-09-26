# 原 Refactored Grounded 状态机组件

日期：2026-09-26。主目录实施，基于普通 Demo stance 接入 `9a24597`，期间用户另提交许可证 `8a7ac98`；用户 README、漫游角色、HUD、场景、项目配置及 LayerBlending 修改保留，不纳入本批。

## 实现范围

新增 `AlsRefactoredGroundedResources`、规则编译器及 `AlsRefactoredGroundedRuntime`。读取已有 `refactored_locomotion_machines.json` 与按哈希验证的原始资源目录，不改导出数据。Core 状态机新增独立规则域；旧 V4 与其他 Refactored 输入域仍隔离。

- 原 Grounded node41：入口 Conduit、Standing、Crouching、StandToCrouch、CrouchToStand、Roll 六状态，二十条烘焙转换。最大每更新一次转换，首次更新丢弃过渡混合，入口 Conduit 不初始化可执行姿态。
- 十二条编辑器转换中四条通过两个状态别名各展开三次。使用 nativeText 的 AliasedStateNodes 建立真实成员关系，同时核对烘焙出口、规则图、源属性身份、优先级及展开完整性。生成 delegate 19–38 与源 RulePropertyIndex 分别保存，不假设所有生成节点都存在编辑器 debug 记录。
- 原 GameplayTag、MovingSmooth、左右 Rotate、FromRoll、Pose Standing/Crouching 阈值解析为不可变类型化表达式；无效 stance 与未知但有效 stance 分开处理。
- 静止站蹲切换的 .3 秒惯性请求、移动/旋转时 .5 秒自定义混合、翻滚退出 .6 秒 Cubic、专用序列结束的零时长自动转换按原数据执行。自转换禁止重入和惯性请求，保持原状态。
- 原 StanceChange 曲线核对 201 个 UE 导出样本（误差门槛 2e-6）；实际机器混合权重也验证经过该曲线。两条非循环站蹲序列的身份、长度、1.2f rate 与 DoNotSync 配置已绑定。
- 原状态入口停止 Transition/Turn、Roll 退出播放过渡作为候选回调返回。Prepare/Commit/Cancel、帧身份、相关性重入、异常恢复保持事务边界，尚不消费这些副作用。自动规则读取上一提交播放器观察；任意严格正缓存权重均可相关，不能用动画权重裁剪阈值替代。

本次只读核对本机 UE `AnimNode_StateMachine.cpp` 的 Conduit、首次更新、自转换行为；无 UE 源修改、构建、运行、新导出或 DataValidation。

## 验证与失败记录

测试均在主目录、Release、`DOTNET_TieredCompilation=0` 执行，TRX 位于忽略目录 `artifacts/tests/refactored-grounded/`。

| 记录 | 结果与覆盖 |
|---|---|
| grounded-first / resource-v2 / v3 / v4 / v5 | 初轮失败保留：回调对象缺 className、标签正则转义、误用生成 delegate 查编辑节点、别名短对象路径未补资产前缀、float rate 序列化为短十进制导致全精度策略校验不符。已逐项修正；未放宽原生动画阈值 |
| grounded-v6 | 首批 6 项通过 |
| grounded-expanded | 9 项通过，全部二十边与规则真值表 |
| grounded-final | 10 项通过，增加有效哈希下的别名/阈值/播放器/回调变异拒绝 |
| grounded-regression | 最终 Import 56 通过、0 失败、0 跳过；包含本批 10 项、Standing/Crouching 状态机和姿态宿主、既有 Standing 独立/共享原生六组 |
| grounded-core | Core 状态机、TransitionStack、缓存遍历 57 通过、0 失败、0 跳过 |
| Godot Optimize 构建 | `dotnet build GodotALS.csproj -p:Optimize=true --no-restore` 成功，0 警告、0 错误 |

新测试覆盖全部二十条出口选择（包括两条不得执行的自转换）、入口 pose 阈值与 Roll 优先级、四种 stance × 十六种移动/旋转/Roll 组合 × 四个阈值样本、自动退出优先于当前 stance、极小正缓存权重、回调/惯性候选撤销、非法身份/时间和重复提交。30/60/120 Hz 各八秒，共 1680 帧，逐帧取消重试与干净机器对照，包含站蹲反向中断及 traversal counter 间断。

这 1680 帧使用受控输入和按已验证长度/rate 推进的测试时钟，**不是实际序列播放器/骨骼姿态整链或 Grounded UE 连续 oracle**。本批未运行 Godot 场景、人工观感测试、全量测试或十分钟性能验收。

## 下一步边界

普通 Demo 保持上一批新 Standing/Crouching + 共享 Transition 的迁移桥接实现，本批没有改变普通入口输出。继续实现 Grounded 实际姿态宿主：原专用站蹲播放器、Roll 固定帧基底、两 stance 缓存与相关性、Parent 更新/回调消费、node43 惯性、node42 曲线，再与共享 bank/队列/最终曲线统一提交并接入 Demo；随后接原 Locomotion/Jump/Fall/Land。

完整 Crouching/Grounded 原生姿态轨迹、真实脚目标/足锁反馈、源 Notify→Pivot、上身与最终脚部、新全链 Mantle/Roll/Ragdoll/Camera、人工与最终性能仍未关闭。音频、道具物理、头颈拉长专项继续按用户要求暂缓。
