# Lyra 完整 Main 转向与 Layer 输出边界

2026-10-02，直接在主目录实施，延续 ALS 人物与 14 个 typed Layer 入口的方案。整个 Lyra 移植目标保持进行中，本记录只覆盖下述连续轨迹。

## 资源和接口决策

继续使用现有 ALS 人物的模型、材质、蒙皮和骨架。布局保持 68 skin / 69 raw / 81 logical；Lyra 动画离线重定向到 ALS，逻辑骨与虚拟骨参与原图计算，最终仍通过现有模型发布点写入 skin。最终 FootPlant 显式绑定 ALS 参考姿态和对应腿长。

`LyraItemLayerGraphInstance` 的 14 个原接口入口共享同一角色内的 `ItemAnimLayers` 实例、source bank、Sync 和候选事务。Layer Group 决定实例共享，Sync Group 决定动画同步，BlendMask 决定逐骨混合。Main 拥有调用位置、状态组合、Lean、cache、Slots、惯性和最终 Rig。运行中更换 Layer 的既有 epoch/取消/发布合同保持；本批没有新增多 Group、self 或 Unlink 验收。

本批修复了实际接口输出边界：`LyraLayerPoseView` 返回原 Linked function 的姿态及完整数据通道，`LyraMainStateRootPoseView` 返回 Main 加 Lean 后的状态结果。两个视图读取同一候选中已求值的缓冲，受相同角色/帧/生命周期门禁保护，没有新增播放器时钟或再次求值。原 Main 独立 native 测试的 evaluatedRoots 是 Main 状态结果，因此改读后一个视图。

## 本批修正

转向会产生非零 Lean 和多个 AimOffset/Lean 样本，暴露了此前零组件旋转轨迹未覆盖的差异：

1. Linked function 输出被错误地包含 Main-owned Lean。修复视图后，原接口边界与 Main 状态输出分别比较。
2. UE BlendSpace 多样本时，`BlendPosesTogether` 和 BlendSpace 各 Normalize 一次；单样本只有后一次。Aiming 和两个 Main Lean 采样路径补齐条件归一化，原动画时间与混合权重不变。
3. RootYaw 的原 Win64 `FRotator3d::Quaternion` 调用 MSVC `_mm_sincos_pd`。标量 CRT sin/cos 有最低有效位差异；进入 SkeletalControls 和 Rig 后，接近 Euler 奇点的右脚结果可放大到四元数约 `5.45e-9`，超过原 `1e-10` 门槛。

新增 `tools/native/LyraNativeMath.cpp`，只包含上述无状态数学计算，无 UE 头文件、资产或运行时依赖。MSVC 14.44 x64、`/O2 /fp:precise /LD /MT` 构建；实际 DLL 179,712 字节，SHA256 `F9AF2EA6C276A87D5D769D5EB84DB1019B77CA780BCFD63A9C43B9DC9FA0170B`。dumpbin 仅列出 KERNEL32.dll。角色仍按原 RotateRootBone 在乘法后归一化 root。

`scripts/build-lyra-native-math.ps1 -EvidenceTag <新标签>` 生成本地 ignored 资源；项目构建复制该 DLL，运行时显式加载。现有 ALS 编辑器插件新增 Windows x64 导出复制钩子，把 DLL 放到可执行程序旁边。导出脚本和插件已通过 Godot 语法检查，实际 headless Editor 启动退出 0、无错误/警告，当前源码与 Debug 程序集哈希保持；本批未生成或运行独立导出游戏。其他平台保留标量 fallback，不能声明通过 Win64 的逐值门禁。

## 连续参考和物理输入

三 Provider 分别为 Unarmed、Pistol、Rifle，每 Provider 的转向轨迹连续 6 秒，覆盖 actor yaw、RootYaw、非零 Lean 和多样本 AimOffset。30/60/120Hz 共 540/1,080/2,160 帧。旧移动轨迹每 Provider 12 秒，共 1,080/2,160/4,320 帧。

原 Main 根、实际 Linked Layers、统一 Sync、cache、Slots、Main75、SkeletalControls 和 ALS 参考最终 Rig 共同执行。输入是预设物理观察；新增 `physicalInput` 捕获原组件在更新前实际读取的 Actor Rotator、component/relative transform、速度/加速度、ground/floor、aimPitch/gravity 和制动参数，Godot 读取相同物理边界。Actor quaternion→Rotator 往返不能用请求 yaw 直接代替。Native 状态、权重、曲线和 evaluator 时钟仅用于断言和排查，不驱动 Godot 宿主。

60Hz 四次独立 UE 进程实际退出 0。`physical` 与 `evaluators-repeat` 在去掉新增 evaluator 观察后，全部 1,080 帧逐字段相同；新增 Main75 输出 tap 后，去掉新增观察记录也全部相同；一次 Linked input tap 被初始化重绑覆盖，未产出该边界数据，后续已移除，保留其 capture 与有效 capture 完全相同的证明。有效 tap 的原 Main75 输出在排查帧逐位相同，把最后误差定位到 RootYaw。

所有 UE 采集保护 863 个旧 JSON、709 个原资产包及项目描述/8 份配置。不保存 transient ALS81 Skeleton、245 Sequence 和 BlendSpace，不改原资产、蓝图或引擎源码。UE 临时序列压缩和依赖警告保留。

## 验证

位置 `1e-8 cm`、四元数 `1e-10`、缩放 `1e-12` 门槛保持；曲线 float 位值/存在性/flags、integer attributes 和 RootMotion 同时比较。每条轨迹分别检查 Main75 前、Rig 前和完整 Main 最终输出；所有实际访问的原 Linked Layer 输出也比较。

Debug 与实际 ExportRelease Optimize 的最终矩阵全部通过。每格均含三个边界，36 个实际 Godot 比较进程退出 0、无错误/警告；每构建覆盖 11,340 帧参考，各边界分别连续执行。

| 轨迹 | Hz | 三 Provider 帧数 | Debug | 实际 Optimize |
| --- | --- | ---: | --- | --- |
| 转向 | 30 | 540 | 三边界通过 | 三边界通过 |
| 转向 | 60 | 1,080 | 三边界通过 | 三边界通过 |
| 转向 | 120 | 2,160 | 三边界通过 | 三边界通过 |
| 旧移动 | 30 | 1,080 | 三边界通过 | 三边界通过 |
| 旧移动 | 60 | 2,160 | 三边界通过 | 三边界通过 |
| 旧移动 | 120 | 4,320 | 三边界通过 | 三边界通过 |

两构建各六项回归也全部通过：Aiming 7,560 帧、Aiming Scope 11,340 帧、Lean Composition 2,100 帧、Main/Linked Additives 11,340 帧、惯性宿主 40,320 帧和普通十角色换层/重试/武器。12 个回归进程退出 0、无错误/警告；普通十角色两构建完整报告精确相同，每角色 480 次实际发布/移动。Debug 和 Optimize 构建均 0 警告/0 错误。优化测试实际复制加载六个 ExportRelease DLL/PDB，最终六个 Debug 文件恢复并与最终 Debug 报告的 SHA256 匹配；数学 DLL 在资源、Debug 和 ExportRelease 目录中 SHA256 相同。

最终审计 `artifacts/lyra-analysis/whole-main-turning-final-integrity.json` 核对 12 份报告、48 个最终 Godot 进程、7 个本批 UE 采集进程退出码、三组独立 native 等价、863 个旧 JSON、709 个原 UE 包、9 个项目/配置文件和 21 份当前源码哈希。当前可选探针源码与 `package-inertia-output` 编译源相同。审计保持 `completeAcceptance=false / goalComplete=false`。

实际证据位于 `artifacts/lyra-analysis/whole-main-{debug,optimize}-{turn30-final,turn60-final,turn120-final,movement30-turn-final,movement60-turn-final,movement120-turn-final}-verification.json` 及各边界日志。60Hz 报告附完整六项回归。独立 native 等价见 `whole-main-turn-independent-native-equivalence.json`；原保护清单在各次 capture 的 `closure.json` 中。

## 保留的失败证据

`turn60-first` 的第 72 帧 Layer 语义错误、`linked-boundary` 的第 38 帧 AimOffset、`multisample` 的第 113 帧 Lean、`lean-multisample` 的 Rifle226 最终 Rig，以及 `turn30-first` 的 Rifle173 最终 Rig 失败均保留。没有放宽门槛、截断轨迹或跳过骨骼。

`root-rotation` 临时误读 RotateRootBone 的 Normalize 导致移除一次归一化，完整最终输出仍失败；核对源码后已恢复原归一化。该失败不作验收依据。首个数学桥进程因 Godot 按字节加载程序集、普通 DLL 探测未找到库而失败；显式加载修复后的记录使用新标签，旧报告未覆盖。

## 范围

本批 UE 为 GamePreview 静态平面，位置/速度是预设观察，没有 CharacterMovement 真实移动仿真；Godot 对照使用解析平面，没有本批新的 Jolt/GPU或近景视觉验收。没有 Montage 动作与换层联合原生矩阵、复杂地形、Shotgun/Feminine 全图、多 Group/self/Unlink、性能或全量签收。音频、道具物理、头颈专项仍按原要求暂缓。

下一步扩大原 Main 的动作/换层联合连续轨迹，再核验真实物理和近景观感。本批精确对照通过不等于整个迁移目标完成。
