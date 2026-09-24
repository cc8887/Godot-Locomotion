# Refactored 四武器过渡条件

本批直接在 `.` 的 `main` 推进，承接武器 baked machine 资源。未修改用户的项目配置、旧计划、分层文件和头颈诊断文件。

## 实现

- `AlsNativeNestedGraph` 从已校验哈希的 Blueprint nativeText 按完整 outer 路径提取声明和定义；同名 `AnimationTransitionGraph_0` 不会串到其他 transition 或生成函数。缺失、重复、外部路径、未闭合对象树拒绝。
- `AlsRefactoredWeaponRuleCompiler` 根据 baked edge 的 compiled rule index 找实际图，验证原始双向连线、父实例属性路径、函数所属类、标签常量、状态时钟所属机器及六条边的闭包。拒绝多余节点/操作数。
- `AlsRefactoredWeaponRule` 独立于旧 V4 条件。输入来自父实例刷新后的状态，状态时间保持原生 float，在与 double 常量比较时提升精度。标签使用相等比较，子标签不当作相等。

四种武器原图一致，按 baked edge 顺序：

| 边 | 状态 | 条件 |
| --- | --- | --- |
| 0 | Relaxed → Ready | RotationMode 等于 Aiming |
| 1 | Aiming → Ready | RotationMode 不等于 Aiming |
| 2 | Ready → Relaxed | TransitionsState.bTransitionsAllowed 且状态时间 ≥ 3 秒 |
| 3 | Ready → Relaxed | LocomotionState.bMoving 且状态时间 ≥ 3 秒 |
| 4 | Ready → Aiming | RotationMode 等于 Aiming |
| 5 | Ready → Relaxed | LocomotionMode 等于 InAir，或 Gait 等于 Sprinting |

Ready 的出口顺序仍为 2、3、4、5；不能将三条退回边合并。旧 V4 的 `elapsed > threshold` 和曲线比较未复用。资源中的 QuickFeet、通知局部索引、Aim 曲线和 Bow 特殊混合时长均保持独立。

## 验证

- 新增 11 项测试通过：4 个真实武器图编译/行为测试，6 类错误图拒绝，1 项完整路径身份拒绝。
- 4 个武器共 2,880 组输入、17,280 次条件判断，包含零时间、3 秒前一个 float、恰好 3 秒、后一个 float、10 秒，独立门控组合、空标签和子标签，以及原出口优先级。
- 拒绝测试覆盖旧严格大于、错误父属性、错误机器时钟、错误时间常量、断裂连线、多余操作数。
- 相关 Import 回归 91 通过、0 失败、0 跳过。
- `dotnet build GodotALS.csproj -p:Optimize=true --no-restore -v quiet`：0 警告、0 错误。
- 结果：`artifacts/refactored-weapon-rules/weapon-rules.trx` 和 `weapon-related.trx`。本批未发生测试失败。

这些是原始图结构编译与托管条件边界证据，不是新 UE 连续运行 oracle。复用既有已导出的资源，未修改 UE 插件、启动 UE/Godot、运行全量套件或性能/打包验收。

## 后续

下一步实现武器状态机连续 tick、原始通知及消费，再接四种武器的 state pose/source 更新与连续原生对照。完整 Overlay 姿态证据仍为 9/13；本批不增加已完成整图数量。

普通 Demo 尚未切换到新 Refactored 图。实际移动状态机、统一角色宿主、Notify/root motion、Ragdoll/Flail/Get-up 整体验收和十分钟预算等既有缺口仍在。道具物理、音频和头颈拉伸诊断按用户要求暂缓。
