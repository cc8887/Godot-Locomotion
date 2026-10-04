# Lyra 默认 Layer 资源与调用点路由

本批把原 Main 的十四个默认 Layer 闭包纳入正式资源，并将普通生产宿主的外部 Layer 查找改为按实际调用点、绑定实例和 epoch 校验。新增执行器可在 ALS 81 骨逻辑布局中执行 self 空根和无效目标回退。普通角色完整 Main 的 Unlink、自身参数传播和按默认根停止上游遍历仍未接入，整个移植目标继续 active。

## ALS 资源与默认图

继续使用原 ALS 模型：68 根蒙皮骨、69 个 raw 通道、81 个 logical 通道。人物、材质、蒙皮和此前重定向动画未重导。本批只新增 ignored 资源 `assets/generated/lyra_als/default_layer_graphs_v1.json`；代码检出需要同时准备该资源及其依赖。

原 `ABP_Mannequin_Base` 的十四个函数虽然均为 `bImplemented=false`，仍各有有效编译根。只读导出确认它们都是 Result 没有输入连线、没有节点回调的单个 `AnimNode_Root`。三条带 Pose 参数的默认函数也不消费输入。无目标/无根时才采用第一输入或 Pose 重置回退，不能将 self 默认根统一写成输入透传。

导出中的编译节点索引与 `FAnimBlueprintFunction.OutputPoseNodeIndex` 使用不同地址空间。Main 共 103 个节点属性，AnimGraph 的导出根为 85、属性索引为 17，满足 `85 + 17 + 1 = 103`；函数输出索引属于属性地址空间。资源加载校验原函数签名、两份依赖 JSON 哈希、上述索引关系、十四个闭包和空回调。

`tools/unreal/export_lyra_default_layer_graphs.py` 使用现有只读 `AlsLyraGraphLibrary`，两个独立 UE 进程导出的 closure 字节相同。原 869 份 JSON、710 份资源包、9 份项目/Config 文件及现有探针包源码和二进制均保持哈希，无资产保存。新增资源 SHA256 为 `c4ff94e0392e5442bcdb69459cd7e01ec39ca723c59ef3393041d68e78b8536d`。

## Interface 与 Layer 执行

`LyraLinkedLayerCallRoutes` 为十四个实际 Main 调用点保存目标，并登记真实 `LyraItemLayerGraphInstance`。External 校验类、命名组、Main owner、实例身份和 epoch；Self 归 Main 所有且不分配 Linked 实例；Unbound 不创建替代 Provider。旧路由或旧实例退休后拒绝调用。普通 `LyraLinkedLayerGraphSet` 的十个移动根、其它 Layer 查找与 Main hook 绑定均使用这个登记表。

Update/Evaluate 复用 `AlsLinkedLayerExecution`，校验 Pose 参数数量与输出布局。Self 执行已导入的空 Root，不更新或求值输入；Unbound 有输入时只遍历第一个输入，无输入时只重置 Pose。普通上下文使用 ALS reference pose，加法上下文使用零位移、单位旋转、零尺度差值，保留 Curve、Attribute 和 RootMotion 数据。

新场景 `scenes/tests/lyra_default_layer_routes_smoke.tscn` 覆盖三个 Provider、四种实例布局和十四个函数，使用实际 Main owner 与真实外部实例登记。Self/Unbound 使用独立路由夹具；External 回调用于验证 owner 路由和遍历次数，完整外部图求值另外由既有 Main 原生对照验证。每构建覆盖 Self 336 次、Unbound 336 次、External 168 次及 60 次参数/外部身份/退休拒绝，覆盖普通与加法上下文、全部 81 骨及附加数据。

## 验证结果

| 检查 | 最终结果 | 范围 |
| --- | --- | --- |
| Debug / ExportRelease Optimize 构建 | 各 0 警告、0 错误 | 最终源码 |
| Core | 59 通过，0 失败/跳过 | 原生默认根、无效目标与绑定规则 |
| Godot | 两构建各 10 次，共 20 次退出 0，无 ERROR/WARNING | 新路由、通知、Reload 换类、实时通知、普通十角色、Emote、四布局完整 Main |
| 完整 Main 原生参考 | 每构建 4320 帧及逐帧取消重试 | 三 Provider、四布局、最终 Rig；沿用原精度门槛 |
| 私有字段 | single/per-call 34/47，three-groups/mixed 32/47 | 两套参考的真实覆盖；不是全字段验收 |
| 普通玩法报告 | Debug/Optimize 完整 JSON 相同，且与前批基线相同 | 十角色与 Emote |
| 完整性 | 14 份当前源码、前批 174/178 份未改源码、原资源保护通过 | 前批四份生产文件按显式 allowlist 修改 |
| 构建恢复 | 三组 Optimize 备份的六份 Debug DLL/PDB 均逐字恢复 | 不留临时程序集替换 |

证据统一位于 `artifacts/lyra-analysis/default-layer-runtime-v3-*`。独立审计 `tools/verify_lyra_default_layer_runtime.py` 输出 `LYRA_DEFAULT_LAYER_RUNTIME_AUDIT_OK processes=20 core=59 oldJson=869 newJson=1 fullMainUnlink=false`，报告为 `default-layer-runtime-v3-integrity.json`。

复现使用新的 EvidenceTag，保留既有日志和备份：

```powershell
& ./scripts/verify-lyra-default-layer-routes.ps1 -Configuration Debug -EvidenceTag <new-tag>
& ./scripts/verify-lyra-default-layer-routes.ps1 -Configuration Optimize -EvidenceTag <new-tag>
```

独立审计固定核对本批已冻结证据。Core、构建、UE 导出和原生参考的详细命令见对应脚本；运行前需要本地 ignored 资源及两套最终程序集。

## 修正与剩余工作

首次资源导出错误地把反序编译节点索引与函数属性索引等同，导出拒绝且未写资源；修正地址空间后两轮只读导出通过。首次 Debug 验证驱动向只含 single/per-call 的参考申请另两布局，single 已通过后驱动停止；保留失败摘要，再用真实包含对应布局的参考完成其它三项。最终审计显式合并这些终态成功记录，未隐藏驱动失败或放宽运行门槛。

生产 GraphSet 仍要求十四个调用全部为受支持的同类 External。默认执行器虽已验证，完整 Main 尚不能实际 Unlink/部分绑定：Main cache/source 遍历、零 Linked 实例资源候选、统一 Sync 与提交事务还需按选中的根调整；self Aiming 的两个 scalar 参数尚未传播。重复函数调用点、任意默认非空图、不同类混合及其它 Provider 仍开放。

下一步将 Main 遍历改为按已解析目标执行：self SkeletalControls 的空根须停止未遍历的上游 cache/移动源，同时保留 Main worker 与 Montage 的原时序；支持零 Linked 实例候选，再覆盖普通角色实际 Unlink、重新 Link、部分绑定和取消重试，并采集新的 UE 完整 Main 参考。当前 34/47 私有字段范围、完整物理 314/1680 差异、Manny/ALS 脊柱与握持近景验收及其余既有开放项保留；音频、道具物理、头颈专项继续暂缓。

本批未重跑 GPU、完整物理轨迹、全量 managed 测试、十分钟或性能验收；81 骨默认姿态为 ALS 布局夹具校验，原生默认根参考仍使用 Manny 164 骨，不能称为新的 ALS 默认姿态原生 oracle。
