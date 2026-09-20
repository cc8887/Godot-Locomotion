# 原生 cached 角约束行对照

本批在 `D:\GodotALS` 的 `main` 实现实际角限位和 swing/twist 驱动行，补上前批
“只转换 Jolt 电机系数”未覆盖的逐端惯量响应。新增 Core 求解器和 UE 原生观测数据；
尚未接入 Godot 物理世界或普通角色，不能据此宣布整链稳定或 Ragdoll 完成。

## 实现

`AlsCachedAngularJoint` 是每关节独立持有的值类型。每物理步重新构造，清空限位与驱动
各自的 lambda；随后重复调用 `SolveLimits` / `SolveDrives`，读写外部共享的两端 DQ。
这种接口允许后续在线性约束和接触迭代之间调用，不能在 Jolt 步结束后直接当补偿力使用。

- 在预测质心姿态下计算两端世界惯量，应用关节局部质量调节，不改共享刚体质量。
- Limited 使用 twist / pyramid 轴；Locked 使用非单位四元数 Jacobian 轴及相对四元数分量。
- 限位采用完整逆惯量张量响应；驱动将响应投影到自身轴。限位和驱动的轴、误差与历史分开。
- 三轴软限制和全部激活的三轴驱动保留 native SIMD 的单精度、同一 DQ 快照累加语义；
  其他组合逐轴求解，中间每次写回 float DQ。并非仅切换执行性能。
- 阻尼来自初始与预测 connector 的旋转差和累计 DQ，支持零刚度纯阻尼。
- 限位按 dt² 缩放容差；驱动沿用原生激活与小系数门槛；Gather/构造重置累计量。
- 接口无引擎对象、无托管分配；双端静态不施加修正。明确拒绝当前 ALS 资产不使用的
  “一根 swing Free、另一根 Limited” dual-cone 配置，避免错误套用 pyramid。

当前实现范围为位置求解阶段、parent mass scale=1、无 shock propagation、零驱动速度目标、
无限扭矩 swing/twist 驱动；未实现 SLERP drive、角速度约束阶段、非零扭矩上限和上述额外模式。
输入必须采用一致的质量/长度单位及 UE 轴序 X=Twist、Y=Swing2、Z=Swing1。

## 原生参考

导出器通过 `FPBDJointConstraints::CreateSceneSolver` 获得原生 cached container solver，
显式 `SetUseLinearSolver(true)`，调用 GatherInput 和 ApplyPositionConstraints。
参考值由引擎真实求解产生，未在导出器内重新实现求解公式，也未保存内容资产。
两端 solver body 的本地逆惯量来自 fixture 自有存储；避免调用模块外未导出的 setter helper。

数据：`assets/config/v4_physics_angular_row_reference.json`，schema 1，4,764,617 字节。
SHA256：`7E6BCA4D137052D6D804D65003B96CA220E6F96F742AECEE4B3A879C3D0FD49E`。
最终两次冷导出字节一致，均退出 0，标记 `ALS_PHYSICS_ANGULAR_ROWS_OK cases=516 assets_saved=0`。

480 组主体：30/60/120 Hz × 5 种资产约束组合 × 4 种限位/驱动组合 × 父级静态/动态 ×
质量调节开关 × 两种旋转配置。角约束组合依次为 LLL、LKL、KLL、KKL、KKK
（L=Limited，K=Locked）。四种通道为仅限位、仅 75/1.5 驱动、两者并用、限位+37500/0 驱动。
旋转配置与 Force/Acceleration 模式绑定，并非二者的独立全因子覆盖。

另加每频率 12 个控制用例，共 36 组：

| controlCase | 条件 |
| --- | --- |
| 1 | 三轴 Free，纯阻尼驱动，非零预测旋转差 |
| 2 | 三轴 Free，无驱动 |
| 3 | 父子均静态 |
| 4 | 关闭 SIMD，强制逐轴限制与驱动 |
| 5 | 近零驱动误差、零阻尼，低于激活门槛 |
| 6 | 外部 DQ 将 twist 推到限位另一侧 |
| 7 | 极小刚度、零阻尼，强制 scalar 小系数门槛 |
| 8 | 两轴 Locked、单轴纯阻尼驱动 |
| 9 | 近 180° swing，混合锁定/限制退化分支 |
| 10 | 近 180° swing，三轴 SIMD 软限制分支 |
| 11 | 子级静态、父级动态 |
| 12 | 子级四元数反号 |

所有用例线性轴 Free、connector 平移为零、无接触、无投影；每组观测八次位置迭代，
保存父子 DQ、DP、corrected quaternion，再重新 Gather 一次检查首轮重放。

## 验证

516 组 × 8 次迭代及每组重置后首轮对照通过。DQ 容差 3e-6 rad，本机观测最大差值为 **0**；
四元数绝对 dot 偏差容差 1e-10，最大 **6.661338147750939e-16**。线性 DP 均为零。
这是这些输入上的观测结果，不能外推成所有姿态、平台或完整世界轨迹逐位相同。

Core 新增五项语义测试：张量响应与轴向驱动的区别、纯阻尼、外部 DQ 与独立历史、
静态/非法输入边界、2048 次构造和八轮求解零托管分配。Release Core 全套
**2626 通过**，使用既有过滤排除 P5A Golden/TraceSchema。
Godot 优化构建通过，0 warning / 0 error。

Import 首轮全量：2333 通过、1 失败、1 跳过。失败为既有
`AlsAimFrameRuntimeTests.TenOwnersShareDefinitionsButNeverHistoryAndHotPrepareAllocatesNothing`，
观测 6304 字节；独立复跑通过。本批未修改 Aim 代码或放宽断言。
固定 JIT 配置全量复跑 **2334 通过、1 跳过、0 失败**；采用仓库既有的
`DOTNET_TieredCompilation=0` / `COMPlus_TieredCompilation=0`，仅作用于验证子进程。
跳过项为需要 `ALS_LAYERING_REPEAT_FILE` 的既有普通 Editor LayerBlending 重复导出对照。
尚未追踪首轮 6304 字节分配的具体来源，不能仅凭复跑把它归因于 JIT。

UE 按 `ue-diagnosing-plugin-build-load` 完成整个 Editor 目标构建与插件闭包审计。
引擎 5.9，BuildId `186ff094-6861-4ab2-95dd-e0889004ba00`，最终输入 fingerprint
`F5072062333ABB83DDD86A726598A2CEDD2391C77BF7772F55EB5A0B467D9E4D`。
普通 Editor 冷重启加载 exporter 反射类、输出专用成功标记并退出 0。
DataValidation 退出 0，0 error / 3 warning（旧 AI 组件及导航版本）。
Editor 日志含既有 UnifiedErrorTest/Condition failed 和材质/资源提示，与前批重启日志
同类内容一致，不能称无错误日志启动。本批只改 Editor 导出工具，未执行打包构建。
技能引用的两项 superpowers 技能本机未安装；采用构建审计、冷启动及实际测试记录验证。

首次编译发现 FRotation3 与 FQuat 条件表达式转换错误；控制用例扩展又发现四参数构造
不支持，均已在源代码修正后重建。失败日志保留于 UE Saved/Logs/PluginBuild。
Core 一项初始测试误将带阻尼软限位当成恰好停在边界，按源码和实际响应修正为允许进入限位内。

产物目录：`artifacts/physics-angular-rows-20260921/`。保留首轮失败与最终日志、两次原生导出、
Editor 重启脚本、全量回归及构建结果；`native-angular.json` 是早期 480 组版本，正式数据为
`native-angular-controls.json` / `native-angular-repeat.json`。

## 下一步与总体范围

下一步补齐线性锚点位置/速度约束和预测/回写顺序，复用现有 144 组原生轨迹验证完整无接触
关节步，再处理与接触迭代共同求解的后端接口。Jolt 公共 6DOF 电机参数不能承载本批所有行语义，
仍需后端求解接入；不能再次只转换系数或在步后写角速度，并声称实现等价。

本批没有重新运行 Godot 九项整链或 144 组 Jolt 轨迹，因为未改变它们的运行实现。
前批失败仍未关闭，默认物理与普通 demo 行为未因此改变。Ragdoll/Get-up/Pose Recovery
生产接入、Mantle、完整 ALS Camera 和最终十分钟预算继续在总清单内。
用户的 P4 规划文件保留原改动，不纳入本批提交；没有创建新项目目录或推送远端。
