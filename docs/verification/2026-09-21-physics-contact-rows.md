# 原生接触行与共享关节迭代

本批直接在 `${env:GODOT_ALS_ROOT}` 的 `main` 实现。新增 C# Core 接触法向、二维摩擦与速度行，
并让接触提供者在每轮迭代里读写关节使用的同一份身体 DP/DQ/速度。
采用原批准的 C# Core 架构；UE 仅用于离线参考，不修改 Godot 或 UE 引擎。

## 实现范围

- `AlsCachedContactPoint`：世界惯量有效质量、单向法向累计推离、静摩擦到动摩擦锥裁剪；
  速度阶段允许抵消位置推离造成的额外速度，摩擦预算包含位置阶段累计量除以 dt。
- `AlsCachedContactManifold`：每个流形先执行所有法向位置行，再执行所有摩擦位置行；
  速度行按接触点顺序执行。预分配双缓冲；Gather 成功才替换缓存，并重置每步累计量。
- `IAlsIslandContacts` / `AlsJointIsland`：预测之后 Gather，每轮位置/速度迭代先接触后关节，
  对齐本地 UE 默认相同优先级时的注册顺序。异常不发布半成品身体状态；禁止回调重入 Step/Reset。
- 单独接触行能够读取零逆质量身体的非零速度；岛入口仍拒绝移动 kinematic，尚未承诺它的积分。

输入是已经 Gather 的世界接触臂、法向/切线、误差和恢复速度目标。
**尚未移植几何 Gather、持续锚点匹配、窄相碰撞、body/shape 身份与碰撞过滤。**
测试中的解析平面仅验证共同迭代时的修正传播，不能充当 Godot 碰撞世界。
尚未支持 split impulse、soft shell、shock propagation、平均点恢复或一维摩擦。

## UE 原生参考

新增 `PhysicsContactOutput=<新绝对路径>` 导出入口，通过 Chaos 原生 collision container
创建 solver 并执行 Gather、8 轮位置约束、隐式速度、2 轮速度约束。
使用合成持续流形，跳过窄相/积分；最后 4 轮启用位置摩擦、最后 1 轮启用速度摩擦。
不复制原生算法来生成预期结果，不保存 UE 资产，不把预期响应回灌 Core。

正式数据 `assets/config/v4_physics_contact_reference.json`：5,605,362 字节，288 组。
覆盖 30/60/120 Hz、双动态/任一端静态、1/4 接触点、旋转惯量与预置共享修正，
以及无摩擦、持续摩擦锚点、恢复速度、分离接触最小摩擦预算、禁用位置/摩擦/速度、推离速度限制。
注意锚点、恢复与推离限制在这里由 **UE Gather** 生成输入，本批仅证明其后求解行的响应。

两次冷启动导出退出 0、均输出 `ALS_PHYSICS_CONTACT_OK cases=288 assets_saved=0`，字节一致。
SHA256：`4A1E8229395320399CA6441DF09FABFD564E30BDBE37EFF43630161E5DC03E1E`。

本地 UE 5.9 对照源码：`PBDCollisionSolver.h`、`PBDCollisionContainerSolver.cpp`、
`PBDCollisionSolverSettings.h`、`PBDRigidsEvolutionGBF.cpp` 和 `ConstraintGroupSolver.cpp`。
默认 Gauss-Seidel、二维摩擦、位置摩擦 stiffness 0.5、速度摩擦 stiffness 1；
导出器检查相关 CVar，拒绝不符配置。

## 验证结果

288 组逐轮原生对照全部通过，既有容差未放宽：

| 比较量 | 最大差值 |
| --- | ---: |
| 有效质量 | 0 |
| DP，cm | 2.2602567e-8 |
| DQ，rad | 9.543217e-9 |
| 线速度，cm/s | 9.344062e-6 |
| 角速度，rad/s | 1.9804534e-6 |
| 累计推离 | 6.668068e-8 |
| 累计冲量 | 3.4898e-5 |
| 摩擦比例 | 1.7285347e-6 |

这些是指定输入、平台与配置上的阶段比较，不等于完整碰撞世界或整链落地轨迹等价。
Core 新增 9 项语义测试：单向释放、推离速度抵消、摩擦预算、运动零质量端响应、
失败 Gather 与重置、2048 次零分配、接触/关节共享修正、异常恢复、重入拒绝。

Release 回归使用仓库既有固定 JIT 配置：

- Core **2647 通过**，沿用过滤排除 `AlsP5aGoldenTests` / `AlsP5aTraceSchemaTests`。
- Import **2357 通过、1 既有跳过**（普通 Editor LayerBlending 重复导出需环境变量）。
- Godot 优化构建 **0 warning / 0 error**。
- Godot 144 组无接触对子、每组 12 帧回放通过，退出 0，无 ERROR/FAILED/leaked 标记。
  最大位置 1.020660e-6 cm、角度 3.576279e-7 rad、线速度 4.722374e-5 cm/s、
  角速度 8.397188e-6 rad/s；与前批相同。该回放保护原有关节路径，不含接触。

全部日志、TRX、重复导出和 Godot 报告在 `artifacts/physics-contact-rows-20260921/`。

## 导出插件构建与失败记录

按 `ue-diagnosing-plugin-build-load` 执行完整 Editor 目标构建和插件闭包审计，未复制 DLL。
最终 BuildId `186ff094-6861-4ab2-95dd-e0889004ba00`，输入 fingerprint
`8A65BE0119814CFA1D0DF9C7C61B6A5EC6E510BCE4F78B1DF77AE48E91A8B77E`。
构建/审计日志在 UE `Saved/Logs/PluginBuild/20260920T203515956Z-d606200947ab4ea99f0b96ab30f89022-*`。
技能引用的两项 superpowers 技能本机不可用，采用直接源码、构建审计和实际运行记录验证。

普通 Editor 冷重启输出 `ALS_CONTACT_EDITOR_RESTART_OK exporter_class_loaded=true assets_saved=0`，
退出 0。仍有既有 UnifiedErrorTest/Condition failed 与引擎材质/资源缺失提示，不能称无错误日志。
DataValidation 退出 0，**0 error / 3 warning**，为旧 AI PawnActionsComponent 与导航版本提示。
本批仅改 Editor 导出工具，仓库无适用的打包构建要求，未执行打包。

保留并修复以下失败：

1. 直接调用 inline Reset 引用未导出的 partition helper，导致链接失败。
   改用原生容器 AddConstraints 内部 Reset，不改变引擎导出符号。
2. 首次冷导出缺少真实 shape instance，在 `CalculateImplicitBoundsTestFlags` 崩溃。
   从真实 particle geometry 创建的 shape 数组传入实例。
3. 第二次冷导出缺少 auxiliary material 数组容量，激活约束时越界。
   初始化数组，并将合成材料/标志设置放在原生 Activate 重置之后。
4. 新 Core 测试初版 target-typed new 与 with 表达式编译失败，改成显式值构造。

每次 exporter 修正均同时更新主仓库与 UE 项目本地源码，重新完成全目标构建/审计后才启动。
上述崩溃发生在离线测试导出器的初始化，不是 Godot 角色运行时。

## 后续工作

1. 接触几何 Gather、稳定 body/shape 身份、持续流形/摩擦锚点和真实碰撞查询接入。
2. 重力/外力、移动 kinematic、动态物体双向响应、连续碰撞与物理岛发现。
3. 原生睡眠/唤醒，再恢复带睡眠参考和整链落地、高速、多频率验收。
4. 接普通角色 Ragdoll/Get-up/Pose Recovery；继续 Mantle、完整 ALS Camera 与最终十分钟预算。

旧 Jolt 九项失败和整链观感问题未关闭，普通入口尚未切换到新接触后端。
用户未提交的 P4 规划保持原字节，不纳入提交；所有实现保留在主目录，没有创建新项目副本。
