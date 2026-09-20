# 地面步幅/播放倍率、宏与状态默认值（第八十七批）

承接地面五项输入函数，补齐剩余步幅和站立播放倍率所依赖的真实宏，复用已
实现的蹲伏倍率。此批重点是正式来源与计算对照；UpdateMovementValues 的
全局候选所有者、最终地面曲线反馈和完整 Demo 输出仍未连接。

## 正式来源

导出器通过反射 FGraphReference 解析宏引用，递归收集以下三张宏图及实际连接：

- ALS_AnimBP:GetAnimCurve_Clamped：GetCurveValue(Name) + Bias，最后 Clamp。
- ALS_MacroLibrary:ML_DoWhile(TrueFalse)：UpdateGraph 的条件执行/切换通知。
- Engine StandardMacros:DoOnce：上述条件宏的两个独立实例所依赖的宏本体。

在 `-IncludeRuntimeInputs` 模式下新增宏引用信息和七项带类型的 CDO 状态默认值。
保留原 15 个输入图、13 个数值默认值、6 个绑定/5 条曲线与 1005 个曲线样本。
本次完整导出包含 179 张图；收集宏节点/连接不等于条件宏运行状态已实现。
后续编译条件门控时仍需核对其临时变量及初始化语义，不能仅凭图名跳过。

| 状态 | 实际 CDO |
| --- | --- |
| VelocityBlend | F/B/L/R 全零 |
| LeanAmount | LR/FB 全零 |
| RelativeAccelerationAmount | XYZ 全零 |
| DiagonalScaleAmount | 0 |
| StandingPlayRate | 1 |
| Speed | 0 |
| ShouldMove | false |

`AlsMovementInputStateCompiler` 检查结构路径、字段、数值类型与有限值，映射 UE
相对加速度轴到 Godot，Speed 转为 m/s。共享定义持有这些值，BaseLayer 初始
全局 Lean/Speed 改为从它读取，关闭此前“Lean 初始零未有 CDO 证明”的缺口。

正式文件 `assets/config/v4_movement_runtime_inputs.json` 与重复导出完全一致，
SHA256：`AFAF6B0CD52390CC82C750874812804B9E30A7F69583FA4C4EE10C1CF30D860A`。
导出不保存或修改 UE 动画资产。

## 算法与直接 UE 对照

`AlsGroundedRateCompiler` 验证 GetAnimCurve_Clamped 的 5 个节点，以及步幅/站立
倍率图的 14/20 个节点、宏引用路径/GUID、连接、曲线名和执行链。

Stride 先按 clamp(Weight_Gait-1,0,1) 混合 Walk/Run 曲线，再按 BasePose_CLF
混合到 Crouch 曲线；BasePose_CLF 不额外钳制。StandingRate 将 Speed/各动画
参考速度按两个偏移步态权重混合，除以新 Stride 和组件竖直缩放，限制 0..3。
CrouchingRate 复用原函数，限制 0..2。保留原除零返回零行为。

新增原生 oracle：创建瞬态骨骼组件/实际生成 AnimBP 实例，设置 Speed、组件
缩放和受控 Weight_Gait/BasePose_CLF，使用 ProcessEvent 执行原 Blueprint 的
CalculateStrideBlend，将结果写入该瞬态实例的 StrideBlend，再分别执行两种
播放倍率函数。没有用复制到 C++ 的公式生成期望值，也没有保存这个临时实例。

720 组 = 6 个速度 × 8 个步态权重 × 5 个蹲伏权重 × 3 个竖直缩放，产生
2160 个函数结果。速度包括静止与高速，权重覆盖偏移门槛/中间值/范围外，
组件 X/Y 固定 2/3 而 Z 为 0.5/1/2，以检查实际使用的缩放轴。
导入逐一验证全部结果（绝对误差 <=2e-6），同时检查覆盖全集和重复案例。
这些是有真实 UE 执行来源的函数级对照，不是完整角色输入/姿势回放。

## 验证与修正

- 93 项相关 Import 用例覆盖：新倍率/默认值 15，原地面函数、曲线、空中和
  落地预测 78。首轮 92 通过，修正新测试夹具后重新运行新 15 项全部通过。
  原生 720 组对照自首轮起通过。
- 外推性质测试最初选 350 cm/s，此处 Walk/Run 曲线都为 1，无法区分外推；
  改用原生样本确认有差异的 150 cm/s。随后发现用已舍入 float 重算期望导致
  一 ULP 差异，改为直接原生值对照加外推方向检查，未放宽原生误差门槛。
- Godot 构建零警告/零错误。生产 single/parallel 各 180 帧退出零，结果摘要
  `21E164D829153157`、完整姿势摘要 `CF9225D4DE9B2C8B`，来源事件各 10 个。
  日志列出新 rate_functions=2、native_rate_cases=720、state_defaults=7，仍明确
  标记 frame_adapter=not_connected。
- BaseLayer 映射回归 3360 帧、12 次最终 Slot 故障、24 次守卫与同帧重试通过；
  隐藏期间全局更新 145 帧、上一已提交掩码消费 14 帧。物理/地面/Slot 仍为
  受控夹具，不能作为完整地面输入接通的证据。

UE 使用技能要求的完整项目 Editor target 和全插件审计。第一次在启动 UBT 前
因系统缺 .NET 10 失败，随后按引擎 GetDotnetPath 使用其自带运行时；第二次
发现 JSON 反射 API 要求非 const FProperty*，修正属性指针声明后编译通过。
首个错误日志保留在项目 Saved/Logs/PluginBuild。没有绕过门禁启动旧插件。
最终来源指纹 `075B073AD82905F74259F3D9C2CF397369A679200FBD88B18DE6EEA590DB2564`。
源码仓库与项目内 exporter SHA256 均为
`3156C652E5A81DECE65AFCCE6F84BA5645C00E66E07F695B9D815F222259F235`。

两次原生导出均退出零，零错误/一条既有 AI 组件警告。DataValidation 退出零，
零错误/三条既有 AI 与导航警告。独立 BuildPlugin 包
`artifacts/unreal/AlsGroundedInputsValidation-20260912` 退出零，未部署包内 DLL。
打包后完整项目 wrapper 重建 NetCore 并再次审计通过，输入指纹未变，最终
BuildId 为 `0c423ffb-b3f5-4fb8-9fc9-5db49727eb0b`。

普通 Editor 使用 D3D12 冷启动，既有只读 Python 审计导出 15 图/5 曲线/13 默认值，
正常关闭、退出零。保留既有两条 AutomationTest Condition failed，以及 AI/导航、
默认材质和渲染线程变量警告；没有本批插件加载/导出错误，不把整个日志称为
零错误。普通 Editor 审计不是再次运行 720 组函数 oracle；后者由前述两次
完整 commandlet 导出验证。

主要日志：`artifacts/ue-grounded-rates-native.log`、`ue-grounded-rates-repeat.log`、
`ue-grounded-inputs-validation.log`、`ue-grounded-inputs-editor-restart.log`、`ground-rates-worker-single.log`、
`ground-rates-worker-parallel.log`、`ground-rates-base-layer.log`。

## 下一项与边界

继续编译 UpdateMovementValues 完整顺序和 UpdateGraph/ShouldMove 门控，建立
与空中输入共用的候选历史，接入上一已提交 Weight_Gait/BasePose_CLF 和同帧
实际速度、加速度、组件缩放。Standing/Crouching 必须读取同一候选结果，删除
旧路径中竞争的局部输入推进和二次归一化，再接 Worker/Demo 完整基础姿势。
JumpPlayRate 的事件更新也仍待连接。

随后闭合最终 YawOffset、动态上身与 IK/脚锁，并对起步、左右反向、换髋和
上身进行实际移动多帧验收。原 P5A–P7 范围保持：完整 Overlay/道具、特殊动作、
物理恢复、完整相机、人工与十分钟性能验收。音频暂缓。
本批未提交、回退或合并工作树，未进行人工视觉或完整性能验收。
