# Animation Layer 普通实例归属：原生矩阵

2026-10-03，在主目录继续 ALS 人物与 Lyra Interface/Layer 路线。此批补齐普通 Layer 绑定的真实 UE 运行参考，并据此修正 Core。整个移植目标仍在进行中。

## 人物与资源

继续使用 ALS Mannequin 的网格、材质、蒙皮权重和骨架：68 skin、69 raw、81 logical。Lyra 动画通过 UE 离线 IK Retarget 到 ALS；目标遮罩、参考姿态、腿长和控制骨映射使用现有 ALS profile。本批没有重导或替换人物、动画、材质及配置。

两次捕获分别保护 869 份既有 Lyra JSON、710 个原 UE 资产包及 GASP58 项目/Config 的 9 个文件，结束后逐项核对 SHA256。资源仍位于 ignored 本地目录，只有代码的检出不能代替可运行资产。

## 捕获方式及范围

新增自有 `LyraLayerBindingMatrix.cpp`，使用现有原 Main、Unarmed、Pistol、Rifle 编译类和原 Manny Mesh。每个布局在独立临时 GamePreview World 中初始化真实 SkeletalMeshComponent，执行原 `InitializeGroupedLayers`、`LinkAnimClassLayers` 和 `UnlinkAnimClassLayers`；逐调用点读取真实 `GetTargetInstance`、类、实例身份和通知标志。

夹具仅在进程内修改已有编译函数的 Group/Implemented、调用点默认类/接口/通知标志和类 CDO 标志，保留原编译函数根及签名。保存完整原值并恢复，不保存资产。修改 CDO 后显式刷新 Blueprint 的 `UpdateCustomPropertyListForPostConstruction`，恢复后再次刷新；否则 NewObject 会使用旧构造属性缓存，不能正确测试新默认值。

10 个布局包括：三命名组、命名/None 混合、无组重复函数、部分实现、全命名/全无组默认类、不同默认类、default/self 混合、无接口但有默认类，以及指定 Main 时重建默认外部实例。共 93 步、1302 次调用点观察。

两次独立 UE 命令进程均退出 0，请求、native、closure 三份 JSON 各自字节相同。`metadataRestored=true`、`assetsSaved=0`、`posesEvaluated=false`。这是原实例归属操作的运行参考；没有新编译任意多组 AnimBP，也没有对这些新布局进行 Pose 求值或验证回调与混合请求。每次日志均有 744 条警告（GameplayTags 733、ToolsetRegistry 5、LogTemp 4、ModelContextProtocol 2），保留原加载警告；没有绑定失败、Error 或 Ensure。

## 原生参考发现的修正

1. `PrepareUnlink` 接受空类，保留原 UE 的默认选择和分桶行为。
2. 普通共享 Linked 实例被替换时，原 Teardown 执行 Uninitialize 和 MarkAsGarbage。`GetTargetInstance` 使用 IsValid，因此其他未被覆盖的调用点也可能读到空目标。Core 在候选中退休该实例的全部调用点，取消不会影响已提交绑定或消耗实例身份。
3. 命名组同类复用仍按本组首调用点判断，不能改为合并全部同类目标。不同配置默认类可以形成首调用点同类、其他调用点不同类的有效状态；这与部分覆盖后旧共享实例失效的情况不同。

此前 [普通绑定策略](2026-10-03-lyra-linked-layer-binding-policy.md) 把部分实现的旧共享目标描述为始终保留，这一结论已被本批原生参考纠正。早期 V2 捕获和 managed 6 失败/4 通过保留；V3 修正后原生 10 项及原策略 8 项均通过。没有修改原观察值或放宽门槛。

## 验证

`AlsLinkedLayerBindingNativeTests` 从实际 native 合同构造独立 Core 绑定器，逐步执行、取消、重试并提交。每个调用点比较目标类型、类、通知标志；原生与 Core 实例身份做双向映射，拒绝不当合并或身份复用。最终 18 项 managed 测试通过。

Debug 和实际 ExportRelease Optimize 构建均零错误、零警告。每构建验证原绑定场景、三 Provider × 三 Hz 普通场景、十角色场景，以及三 Hz × 三个原 Main 姿态边界，共 40 个最终 Godot 进程；全部退出 0，无 Godot ERROR/WARNING。单/多角色完整报告跨构建相同，普通九组每构建 5040 发布帧/重试、54 换类、54 同类复用、51 帧中重绑拒绝。三 Hz 原动作中换层每构建 7560 参考帧，在惯性前、Rig 前、最终姿态三个边界逐帧重试并保持原误差门槛。

独立 `tools/verify_lyra_layer_binding_matrix.py` 核验原生双捕获、18 项 TRX、40 次实际运行、程序集哈希、四轮六文件 Debug 恢复以及所有受保护资源与原包。结果为 `linked-layer-native-v3-integrity.json`。没有重测完整物理轨迹、GPU/近景、全量 managed 或性能。

## 下一步与开放范围

普通实例归属的命名多组、无组逐调用点、default/self/Unlink 和标志组合已有上述受控原生参考。生产 Main 的图执行仍限定原十四入口、单个 `ItemAnimLayers` 组。

接下来从真实 `FAnimBlueprintFunction` 生成签名与 typed 调用包装，再实现按实际实例归属的图宿主：每实例持有自己的播放器、状态机、缓存和回调历史，调用传递 Pose/曲线/属性/root 完整值。Layer Group、Sync Group 和骨骼遮罩继续分别处理。需要新增多组图求值、默认/self/Unlink 的实际回调与混合请求参考；共享/持久实例子系统、完整继承拓扑及 Shotgun/Feminine 全图仍开放。

完整 UE/Jolt 同输入轨迹仍有既有 314/1680 帧失败；本批绑定验收不关闭该项。复杂地形、近景握持、材质、性能、跨平台/独立导出继续开放，音频、道具物理、头颈专项按原要求暂缓。

## 证据与复跑

原生最终标签是 `layer-binding-matrix-v3` 和 `layer-binding-matrix-v3-repeat`，插件包是 `artifacts/unreal/lyra-whole-main-oracle/package-layer-binding-matrix-v3`。早期编译错误、原包保护别名错误、V2 构造属性缓存问题及 managed 失败均保留，不覆盖。

```powershell
# 新捕获使用新标签；现有标签均已使用。
python tools/build_lyra_layer_binding_matrix_request.py --output artifacts/lyra-analysis/<new-tag>-request.json
& scripts/capture-lyra-layer-binding-matrix.ps1 -RunTag <new-tag> -PackageName package-layer-binding-matrix-v3

# 从 .. 执行，避开仓库旧 SDK 固定版本。
dotnet test ./tests/Als.Core.Tests/Als.Core.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~AlsLinkedLayerBindingsTests|FullyQualifiedName~AlsLinkedLayerBindingNativeTests'

# 每次验证使用新 EvidenceTag；与原三个姿态边界配合。
& scripts/verify-lyra-linked-layer-bindings.ps1 -Configuration Debug -EvidenceTag <new-evidence>
& scripts/verify-lyra-linked-layer-bindings.ps1 -Configuration Optimize -EvidenceTag <new-evidence>
```

原生 theory 明确读取冻结的 V3 原生参考。新增捕获应经独立核查后再更换参考，不自动接收任意新输出。
