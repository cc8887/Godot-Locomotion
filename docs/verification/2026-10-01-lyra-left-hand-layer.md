# Lyra 左手层原图与 Main 组合边界

2026-10-01，当前主目录，安装版 UE5.8.1 / Godot4.7.2 mono。继续使用 ALS 原模型，81 logical / 68 skin。**本批接通固定 Unarmed/Pistol/Rifle 的第11个执行入口 `LeftHandPose_OverrideState`，保留原 Update 回调和上一 Main 曲线反馈。三个后续接口入口、完整 Main 和普通 Demo 生产接入继续开放。**

## 原图和资源

原闭包为117 Root → 115 LayeredBoneBlend，输入为116 LinkedInputPose 与114 SequenceEvaluator。编译索引和 property 索引分别为117/0、115/2、116/1、114/3；不能按 property 数值识别 Main 调用。

三个 Provider 的原 CDO 均为 EnableLeftHandPoseOverride=false、LeftHandPose_Override=null。原 `SetLeftHandPoseOverrideWeight` 读取 linked 实例上一 Main 最终 `DisableLeftHandPoseOverride` 曲线，以 double 计算 Clamp(enabled−curve,0,1)，然后 exposed pin 转成 float。当前移动资源曲线布局没有该控制曲线，正常输出的左手权重为0；负值受控反馈用于实际验证非零覆盖和门槛，不是新增玩法。

空 SequenceEvaluator 输出 ALS81 reference pose，没有独立时钟或 Sync 源。原 LeftFingersMask 按骨名映射到 ALS81；local rotation/scale、Override 曲线、child-before-base、根骨控制根运动权重均按原图。骨遮罩、曲线绑定和空资产配置经过独立加载检查。非空左手动画会明确拒绝，需另接同一 source owner，不能静默取参考姿态。

本批在仓库导出插件增加独立 `AlsLyraLeftHandLayerLibrary`，没有改 UE 引擎源码或旧 Cycle/Main probe。通过 external-only 构建后，commandlet 在临时 GamePreview 世界建立真实 Main 和 ItemAnimLayers；临时载体/ALS81骨架、原 four-node closure、原 Update handler/FPoseLink Evaluate 及真实 CopyCurveValues 都实际执行。源叶子和 enclosing Main 最终曲线由受控输入提供，**本 oracle 是左手层组件，不是完整 Main 连续求值**。

六条临时 Jog/ADS Start 动画使用已建立的 ALS81 转换；在采集前等待其压缩完成，并逐条校验原 compressed-root codec 数据。没有保存 UE 内容资产。只新增 ignored `left_hand_layer_v4_{requests,policy,native}.json`，字节数分别为1025535 / 1407 / 93807127；不重排或重写旧 JSON。

## 运行时接入

- `LyraLinkedCurveFeedback` 在同一组实例中持有 enclosing Main 已提交曲线及存在性/flags；额外控制曲线有显式名称绑定。缺失值读0，成功求值后的拷贝清除已消失元素；隐藏和 update-only 保留上一反馈。候选反馈随 Main 提交或取消。
- `LyraLeftHandLayerHost` 区分 Update 与 Evaluate，更新 double 权重后再采样只读输入姿态；传递完整81骨、曲线、typed属性和RootMotion。local LayeredBoneBlend 使用 UE BlendWith 的 double quaternion complement 和 A+(B−A)×alpha 向量顺序，零/满权重直接复制；没有套用 transition-stack 的 float complement。
- `LyraItemLayerGraphInstance` 增加 typed 输入姿态调用；签名、Main 编译调用节点、实例引用、epoch、source帧和左手候选引用一起检查。十个移动入口仍共用一个原 SourceScope/Sync，左手层不另建播放系统或骨架 writer。
- `LyraMainLeftHandHost` 在原 Main update 后、LocomotionSM 遍历前执行左手 Update；Evaluate 先取得真正 Main 移动输出，再执行该输入姿态入口。全依赖预校验后一起提交，晚期取消同时丢弃控制反馈与权重。

当前组合输出边界为 `LeftHandPose_OverrideState`。后续 Locomotion缓存、上下身槽、Aiming、FullBodyAdditives、主惯性化、RootYaw及最终SkeletalControls还未连接到这个新宿主。最终反馈 API 刻意接收 enclosing Main 输出；完整图接齐时应将实际调用放在最终 Main 求值后。当前组合测试在左手边界提供反馈，不冒称完整 Main 最终曲线。

## 验证结果

最终 Debug 与 ExportRelease Optimize 都0警告0错误；external UE plugin UAT 正常退出0。两次独立 UE 采集均正常退出0，第二次完整 JSON 语义与首次一致且保留原字节。508原包、655此前JSON以及源码/source/package镜像SHA256一致。

UE commandlet 两次汇总均0错误/794警告，整份日志各846条实际 Warning 行，包含加载与退出阶段提示。主要为现有 GameplayTag、临时压缩依赖和 commandlet 注册提示；不能描述为 UE 零警告。

| 验证 | 最终结果 |
| --- | --- |
| 独立原左手层，三Provider×30/60/120Hz | 3780帧，3078姿态，1401次实际骨骼变化 |
| 隐藏 / update-only / Main反馈变化 | 189 / 513 / 516 |
| 输入与输出数据 | 每侧249318骨、3078曲线元素、12312 typed属性，另比较RootMotion |
| 取消重试 / 旧姿态视图拒绝 | 3780 / 6156 |
| Main→Left组合回归 | 11340帧、9762组合姿态、12595原移动根求值、207晚期取消重试 |
| typed调用/组/候选门禁 | 618次拒绝，其中新增108次输入姿态入口节点/epoch/异组/异帧/错误签名/复制候选拒绝 |
| 原十入口默认路径回归 | 11340帧、9762姿态、510次原拒绝，全部通过 |

左手层输入/输出最大位置差均8.978073696260406e-14 cm、quaternion 4.873060251453353e-16、scale 0；保持原位置1e-8 cm、quaternion1e-10、scale1e-12门槛，曲线存在性/flags、typed属性与RootMotion亦按既有门禁比较。正常三个Provider在Main组合中的权重为0，所有数据通道精确保持移动输出；非零左手回调/混合由独立原层采集验证。组合没有新采集 Main+Left 联合原生 oracle，标志明确 `nativeJointBoundary=false`。原 Main fixture 的 Marker 零值符号限制保持。

## 首轮失败与修正

失败日志和v1/v2/v3资产字节全部保留，均不计为最终有效 oracle：

1. 首编译将TSharedRef误用ToSharedRef，修复Serialize调用后重建。
2. v1未先DynamicUnlink，原LinkedInputPose触发ensure，UE进程退出1；即使Python提前写出JSON也不算通过。新probe在连接前及结束时解除受控链接。
3. v2进程退出0，但root provider读取尚未完成压缩的临时动画，与既定codec输入不同。增加等待压缩与六条codec精确检查，保留诊断日志。
4. v3初始CacheBones早于PreUpdate，代理仍缓存Manny Skeleton，虽RequiredBones已改ALS81，骨遮罩实际未正确刷新。增加InitializeObjects和Main/Linked GetSkeleton身份检查后采集v4。旧Main probe本来在PreUpdate后才首次CacheBones，源码与既有fixture不变。
5. v4首次Godot对照在部分权重出现位置8.0392e-7 cm、quaternion1.1047e-9、scale5.1619e-8差异；原因是误用transition-stack float补权重。按本机UE local BlendWith/ISPC公式修正后所有原门槛通过，没有放宽阈值。

证据位于 `artifacts/lyra-analysis/`：`left-hand-layer-ue-build-cache.log`、`left-hand-layer-ue-export-cache/repeat.log`、`left-hand-layer-debug-final.log`、`left-hand-layer-optimize-final.log`、`left-hand-layer-godot-final.log`、`left-hand-layer-main-pipeline-final.log`、`left-hand-layer-main-native-regression.log` 与 `left-hand-layer-verification-final.log`。`tools/verify_lyra_left_hand_layer.py` 同时检查实际非零骨骼变化、不可变资产、编译镜像和最终日志。

## 后续范围

继续沿原Main拓扑接缓存、上身/动作槽、Aiming的double参数和两套RotationOffsetBlendSpace、FullBodyAdditives原机器，再接主惯性化及完整SkeletalControls。统一Notify/Montage、换类/惯性请求、普通Gather/Worker/Commit、多角色、渲染、地形握持与性能仍待。当前未运行普通Demo或人工验收；没有提交、推送或保存UE原资产。用户未提交修改保留，整个移植目标保持开放。
