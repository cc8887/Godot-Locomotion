# Refactored 原生编译图清单

在 `.` 的 `main` 继续分层图移植。本批补齐实际编译节点身份、反射默认值和原生缓存更新顺序，尚未把完整骨骼图接入普通 Demo。

## 导出与编译输入

新增 `ReadCompiledAnimationGraph` 只读 UE 接口。遍历原 Blueprint 图，用运行时结构类型识别动画节点，包含 `AlsAnimGraphNode_CurvesBlend`，不使用 `AnimGraphNode_` 类名前缀筛掉自定义节点。记录编辑图路径/GUID、compiledNodeIndex、propertyIndex、编辑节点的反射结构、生成类 CDO 的运行时结构和 `GetOrderedSavedPoseNodeIndicesMap()`。不编译或保存 Blueprint 资产。

`export_refactored_layering_inventory.py` 读取已提交的四图导出，记录其文件 SHA256，生成 `assets/config/refactored_layering_inventory.json`。四个 Blueprint 都有编辑节点清单；本批 Import 的完整属性覆盖校验仅针对 Layering，不宣称 Locomotion 编辑节点覆盖编译器生成的全部内部属性。

Layering 的实测结果：

- 编辑动画节点 106，编译属性 94；12 个编辑节点没有编译身份，保留为 index=-1，不能作为运行时分支执行。
- 2 个自定义曲线节点、2 个 SequenceEvaluator、1 个 MultiWayBlend、7 个 Slot。
- 11 个缓存的原生顺序如下。索引是 compiledNodeIndex，不是反向的 propertyIndex。

| 索引 | 缓存 |
| --- | --- |
| 90 | Legs |
| 3 | Pelvis |
| 71 | Spine |
| 70 | Head |
| 56 | Locomotion Additive Mesh Space |
| 69 | Arm Left |
| 68 | Arm Right |
| 57 | Locomotion Additive Local Space |
| 36 | Base Poses |
| 51 | Locomotion |
| 52 | Overlay |

`AlsRefactoredLayeringInventoryCompiler` 交叉校验四图来源与文件哈希、Layering 节点名称/类型/GUID、编译索引反向映射、完整属性覆盖、每个 UseCache 的原生 linkId/sourceLinkId 与编辑图保存节点、缓存顺序成员闭包。输出只读清单，JSON 元素独立克隆，不依赖已释放的文档。它仍是完整图编译的输入阶段，不是骨骼算子编译器。

## 验证证据

- 使用 `ue-diagnosing-plugin-build-load` 的完整 Editor-target wrapper 构建 6 actions 成功，插件审计通过。记录前缀 `20260924T165147945Z-2c3821145b7348eb8878b0b68b7da32a`，fingerprint `6A36CA669CAA388864B0C247DC956056B6CD5200FB0EBDCD1191CE13D3DB27F1`，BuildId `7fb8adce-a7f2-4be3-9d02-f8b2ae766ac2`。
- 冷命令行导出实际退出 0，无 Error/Warning，标记 `ALS_REFACTORED_LAYERING_INVENTORY_OK assets=4 assets_saved=0`。
- 普通 Editor PID 23396，实际进程句柄等待退出 0，同一标记。冷/普通导出字节一致，SHA256 `AB57FE0014CAA905CBE4334B055809C1DE91C40FDA6C975C4C96DDB9B32EC1AE`。
- 普通 Editor 仍报告两条既有 `Condition failed`；另有旧 AI PawnActionsComponent、旧导航版本、LineSetComponentMaterial、r.MotionVectorSimulation、CrowdManager 警告，完整日志保留，未在本批解决。没有将退出 0 描述成日志全绿。
- DataValidation 实际退出 0：0 error、3 warning（旧 AI 组件/导航资产）；无打包构建或新 Godot 场景验收。
- Import Release `FullyQualifiedName~AlsRefactoredLayering`：14 通过、0 失败，含新增 8 项。检查真实清单以及哈希/GUID/索引/缓存指向/重复顺序/缺节点/漏自定义节点拒绝。首次测试编译触发 xUnit2031，改用 Assert.Single 的 predicate 重载后通过；生产逻辑未为测试改动。
- `dotnet build GodotALS.csproj -p:Optimize=true --no-restore`：0 warning、0 error。未运行 Core/Import 全量。
- 日志、重复 JSON、TRX 在 `artifacts/refactored-layer-inventory/`；用户 P4 规划哈希保持 `78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100`，project.godot/诊断文件/uid 未纳入提交。

## 下一步

依据真实 94 节点清单编译运行分支，补齐 MultiWay 的原生归一化与站立/蹲姿资产采样，处理 Refactored 虚拟骨与区域 Slot，再接 Head/View 和普通 Mantle 宿主。完整图需原生运行对照与 Godot 视觉验证，不能以本批元数据校验代替。

既有物理稳定性 9/12、Flail 0/3、复杂相机碰撞、Mantle 探测/motion 生命周期和最终十分钟预算继续保留。头颈拉伸、道具物理、音频仍按用户要求暂缓。
