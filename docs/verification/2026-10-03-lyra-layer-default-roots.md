# Lyra 默认 Layer 根和无效目标回退

本批完成原始 Main 十四个默认根的实际 UE 求值，以及可复用的 managed 调用路由基础。普通 Godot 生产宿主的 self/default/部分绑定/Unlink 接入仍开放；ALS 人物资源与当前三 Provider 执行路径未在本批重导或运行验证。

## 原生结果

`ABP_Mannequin_Base` 的十四个接口函数均为 `bImplemented=false`，但每个函数仍有有效的编译输出根属性。因此，不能用“未实现”判断根不存在。只读读取实际 CDO 接线后，十四个默认闭包均为单个 `AnimNode_Root`，其 Result Pose 没有输入连线。

对 Main 的真实 LinkedAnimLayer 节点调用 UE 的 Update/Evaluate，分别覆盖初始 self 和实际 `LinkAnimClassLayers(Unarmed)` → `UnlinkAnimClassLayers(Unarmed)` 后的 self。两阶段各十四个函数完全相同：输出参考姿态，不更新或求值任何输入。`FullBody_Aiming`、`FullBody_SkeletalControls`、`LeftHandPose_OverrideState` 虽有一个 Pose 参数，其默认根仍不消费该参数。

另用真实 `FAnimNode_LinkedAnimLayer` 的无效目标覆盖 0/1/2 输入、普通/加法上下文、空/已有输出的十二种组合。无有效目标或根时，有输入只更新并求值第一个输入；第二输入不遍历。无输入只重置 Pose，已有 Curve、标志及自定义 Attribute 保留。加法重置为零位移、单位旋转、零尺度差值，不能套普通参考骨骼尺度。

这两条路径须区分：self 有真实空根时执行该根；无有效目标或根时才采用 first-input/reference 回退。将 self 统一实现为输入透传，会改变原 Lyra 的默认行为。

## 实现和验证

- 新增 `src/Als.Core/Animation/AlsLinkedLayerExecution.cs`，按目标和根的有效性路由 Update/Evaluate；输入使用惰性回调，输出上下文整体交给选中的根或输入，重置回调只负责 Pose 并保留加法上下文。绑定归属、参数传播、惯性请求仍由调用宿主管理。
- 新增独立可选 UE 探针 `tools/unreal/LyraLayerFallbackOracle`。反射输出函数根及完整十四个默认闭包，直接调用原生 Update/Evaluate；在临时实例中替换入参 PoseLink 为可计数的受控输入，之后恢复，不修改 CDO 或编译函数元数据。
- 最终 `package-layer-fallback-v4` 编译成功。两个独立 UE 进程均退出 0，无 Error/Fatal/Ensure；40 个场景、164 个骨骼/场景，request/native/closure 三份 JSON 各自字节完全相同。
- Release Core 构建 0 警告/0 错误；新 41 项默认根/全 Pose、Curve、Attribute、遍历对照，加既有 18 项绑定规则/原生矩阵，共 59 项通过，0 失败/跳过。
- 独立审计通过；869 份原生成 JSON、710 份原 UE 包、9 份项目/Config 文件哈希保持。三份原始 UE 源文件逐字复制至 `artifacts/lyra-analysis/layer-fallback-v4-engine-source/` 并校验当前引擎和复制件一致。

最终证据位于 `artifacts/lyra-analysis/layer-fallback-v4-{native,closure,integrity}.json`、`layer-fallback-v4-repeat-*`、`layer-fallback-v4-core-final.trx`。可用以下脚本复现：

```powershell
& ./scripts/build-lyra-layer-fallback-oracle.ps1 -EngineRoot '../UE_5.8' -UnrealProject '../GASP58/GASP58.uproject' -PackageName <new-package-name>
& ./scripts/capture-lyra-layer-fallback.ps1 -PackageName <new-package-name> -RunTag <new-run-tag>
```

复现使用新的包和日志名称，保留已有证据。测试当前固定引用已验收的 v4 捕获；独立审计入口为 `tools/verify_lyra_layer_fallback.py`。

## 修正过程和边界

首轮探针未传入 UE 要求的 `FAnimationUpdateSharedContext`，触发 Ensure/Assertion；第二轮补齐后成功。新增闭包反射时一次局部变量遮蔽导致编译失败，修正后 v4 成功。首轮 managed 模型错误地假设带 Pose 输入的默认根会透传，导致六项失败；读取真实闭包后改为执行空根，最终全部通过。原失败包、日志和 TRX 均保留，未放宽门槛。

原生使用原始 Manny 164 骨载体；本批未在 ALS 81 骨逻辑布局重跑姿态，也未跑 Godot、完整 Main、GPU、十分钟/性能或完整物理轨迹。新 managed 路由目前是执行基础，未接普通生产 `LyraLinkedLayerGraphSet`；该宿主仍要求十四个调用全部绑定到受支持的外部 Provider。任意默认图、多个相同函数调用点、不同类混合、部分绑定退休后的空目标、共享/持久实例及跨帧重绑定事务仍需继续。

下一步把 Main 的十四个默认闭包纳入资源执行描述，按真实调用点目标创建 external/self/unbound 路由；在 self 空根下保留 Main 状态、cache 与参数语义并停止未遍历的子源，统一登记后一次跨 owner Sync。再覆盖普通 Godot 上实际 Unlink/重新 Link、部分绑定及连续取消重试，并与新原生整链参考比较。此前真实 NotifyState、三 Provider、多组图和物理开放项及音频/道具物理/头颈暂缓项保留，整个移植目标继续 active。
