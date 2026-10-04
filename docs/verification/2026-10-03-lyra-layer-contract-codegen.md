# Animation Layer 编译合同与生成参数接入

2026-10-03，继续在主目录推进 ALS 人物和 Lyra Interface/Layer 路线。上一批已完成普通实例归属的原生矩阵；本批将签名解析、入口枚举和参数/Pose 输入包装从手工声明迁移到原 UE 编译合同生成，并接入普通 Main。

## 实现与实际调用

`AlsAnimationLayerCatalog` 保存不可变类、函数、参数、调用点、默认类、Group、通知标志和混合配置，保留原顺序。`AlsAnimationLayerContractCompiler` 读取原 `FAnimBlueprintFunction` 导出，校验 inventory 字节哈希、重复身份、Pose 输入与参数绑定。它不限制 Lyra 名称、十四入口、十一类或单组。

函数兼容比较参数类型、名称及顺序和 Pose 输入；Group 作为实现的实例归属配置单独保留。未实现的入口不参与覆盖。当前通用参数类型支持 bool/int32/int64/float/double；其余枚举、结构和对象类型明确拒绝，尚未声称完整 UE 属性系统支持。

`AlsAnimationLayerCodeGenerator` 与 `tools/Als.LayerCodegen` 从上述模型生成入口枚举、不可变期望签名、标量参数结构和各函数的 Pose 输入结构。`tools/contracts/lyra-layer-function-ids.json` 锁定原 0–13 编号，新增名称分配新编号，删除的编号保留。字段按原类型生成，Aiming 保持 double，没有中间 float 转换。生成文件显式使用 LF，防止 Windows 检出改变精确生成校验。

当前实际输出为 `LyraGeneratedLayerContract.g.cs`：14 入口、三份 Pose 输入结构及一份双 double Aiming 参数结构。已删除原手工入口枚举和加载器中按 hook 写死的 Pose/参数分支。生产 `LyraLinkedLayerContracts` 使用通用模型校验及构造普通绑定器；Lyra 图适配仍检查原十四入口、单 `ItemAnimLayers` 组。

普通 `LyraMainLocomotionHost` 的 Aiming 更新使用生成参数，`LyraMainPoseHost` 的左手/Aiming/SkeletalControls 调用使用生成 Pose 输入包装，完整 Pose、曲线、属性及根运动仍通过原 `LyraLayerPoseInput` 传递。原入口保留为兼容包装并转入同一执行方法，初始化、Prepare/Evaluate 时序、角色候选、取消/提交、实例身份和旧 epoch 拒绝没有新增替代路径。

## 验证

最终 13 项导入测试通过：读取原 11 类/164 函数/14 Main 调用，校验原实际 double 绑定与完整实现；生成文件逐字与生成器结果相同、保留原编号；部分实现和不同 Group 的参数兼容；拒绝重复函数/调用/alias/参数、错绑定、不支持类型、错误 Pose 输入、旧 inventory、错误参数类型和 null Group。

另在独立新目录生成并实际编译运行新的 C# ABI：4 类、2 接口、两个新函数名称、双 Pose 输入、五种标量。验证 bool、int、超过 int 范围的 long、float，以及 `1.0000000000000002d` 的逐位 double；两份 Pose/曲线/属性/root 输入保持各自完整值，部分实现回到原 self，声明 Group 与实现 Group 不混淆，编号 5/19 保留。该证据是受控元数据和生成 C# 编译运行，未新建或执行同布局的原 UE AnimBP。

Debug 和实际 ExportRelease Optimize 均零错误、零警告。每构建各十一场景（原绑定、三 Provider × 三 Hz、十角色）及三 Hz × 三个原 Main 姿态边界，共 40 个最终 Godot 进程全部退出 0，无 Godot ERROR/WARNING；两构建单/多角色完整报告相同，原精度与逐帧取消重试门槛保持。九组普通角色每构建 5040 发布帧及同数重试；原动作中换层每构建 7560 参考帧分别通过惯性前、Rig 前和最终边界。

独立 `tools/verify_lyra_layer_codegen.py` 核验 13 项 TRX、生成检查、独立 ABI 的文件及运行日志、40 项实际运行、程序集和四轮六文件 Debug 恢复。869 份旧 Lyra JSON、710 个原 UE 包及项目/Config 的 9 个文件保持原字节哈希。普通绑定策略与原生矩阵源和参考保持上一批验证版本。本批没有 UE 启动/保存/重导人物、GPU/近景、完整物理重验、全量 managed 或性能验收。

## 复跑

```powershell
# 验证生成文件和稳定编号注册表，不写源资源。
& scripts/generate-lyra-layer-interface.ps1 -Check
# 原编译合同确有变更并完成资源依赖校验后重新生成。
& scripts/generate-lyra-layer-interface.ps1

# 从 .. 执行 dotnet，避开当前项目原有 SDK 固定版本。
dotnet test ./tests/Als.Import.Tests/Als.Import.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~AlsAnimationLayerContractTests
```

最终标签为 `layer-codegen-v3`；独立 ABI 目录为 `artifacts/lyra-analysis/layer-codegen-v3-probe`，结果为 `layer-codegen-v3-integrity.json`。现有标签和日志保持，不覆盖；运行 `verify_animation_layer_codegen.py` 应提供新 `--tag`。早期 V1/V2 生成、12 项测试和 ABI 证据保留，最终补 null 元数据门禁后使用 V3 的 13 项及两构建运行证据。

## 余下范围

本批关闭通用合同导入、生成代码机制和现有 Main 的生成参数接入。新多组的图执行仍需按绑定实例建立播放器、状态机和缓存宿主，新增实际 Pose/回调/混合请求的 UE 参考。共享/持久实例子系统、完整继承拓扑、结构/对象等参数，以及 Shotgun/Feminine 全图仍开放。源导出入口仍是 Lyra 资产清单，需要进一步推广到任意接口和依赖资产闭包。

ALS 人物继续使用 68 skin/69 raw/81 logical；完整 UE/Jolt 同输入物理轨迹仍有既有 314/1680 帧差异，本批没有重测或关闭。复杂地形、近景握持、材质、性能、跨平台/独立导出和整个移植目标保持开放；音频、道具物理及头颈按原要求暂缓。
