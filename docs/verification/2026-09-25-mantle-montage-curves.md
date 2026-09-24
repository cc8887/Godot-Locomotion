# Mantle Montage 自有曲线补齐

在准备宿主图接入时发现：既有 Mantle sampler 只读取序列的 PoseStanding/PoseGrounded，缺少 Montage 自有的 Foot IK、Foot Lock、Layer*、ViewBlock 曲线。不能把此前序列采样正确视为完整 Montage 曲线已移植。本批补齐导出、编译和宿主采样合成；普通 Demo 的完整 Mantle 仍未接通。

## 原生语义与实现

本机 UE `AnimInstanceProxy.cpp` 的 SlotEvaluatePose 在 AnimTrack 采样之后调用 Montage->EvaluateCurveData，然后 NewPose.Curve.Combine(MontageCurve)，再参与 Slot 混合。`AnimCurveTypes.h` 的 Combine 对输入中存在的同名值执行覆盖，而非相加；缺失值不覆盖。Montage 曲线使用 Montage 时间，不使用序列采样量化后的时间。

只读插件接口 ReadSourceFloatCurves 扩展为 UAnimSequenceBase，可读取序列或 Montage 数据模型的原始 RichCurve 键。新增 ReadAssetFloatCurveValues 调用引擎 EvaluateCurveData，提供独立逐时刻参考；未保存 UE 资产。新 Python 脚本导出六个 Montage 的原始键与 121 个时刻的原生求值。

CompileMontages 复用已有 RichCurve evaluator，并验证完整 Montage/曲线名字清单、输入哈希、非加权键、常量外推和键序。文本元数据仅用于名字闭包，不读取其中截断的关键帧小数。六个 Montage 分别有 13、11、9、9、10、10 条曲线，共 62 条。

AlsMantlingHostPoseProfile 新增显式 montageCurves 参数。完整采样按 ActionDefinitionId 选择 Montage 曲线，同时核对 AnimationId，避免共用同一序列的不同 Montage 串用控制曲线；序列曲线之后覆盖 Montage 自有值，再交给共享 Slot 混合。每个 owner 使用独立预分配 scratch，非法身份不部分写输出。

现有六资产 ClipStart=0、ClipRate=1，因此 evaluation 的序列时间就是 Montage 时间；明确拒绝其他映射段，不能通过浮点逆除重建原时间。省略 montageCurves 的旧接口仍仅采样序列，保留基础回归用途；未来普通宿主必须传入完整曲线资源，并将所有原生名字纳入布局。Layer*/ViewBlock 在普通 V4/Refactored 混合图中的消费语义还需要接入，不能只添加曲线名就声称功能完成。

## 验证

- UE 整项目 Editor 构建成功，插件审计通过。BuildId `7fb8adce-a7f2-4be3-9d02-f8b2ae766ac2`；fingerprint `7AE5DC05C3174F83E4519A0610CB08CAFEF05B06884EB2B33766F58F4DDC282A`。日志前缀 `20260924T160649367Z-15e94896aefd4d29b6c8462c092c3fab`。
- 冷启动导出退出码 0，0 错误/0 警告；普通 Editor PID 35136 实际进程句柄退出码 0，两输出逐字节一致。普通启动仍有两条既有 Condition failed，日志保留，未宣称普通编辑器全无错误。
- DataValidation 退出码 0，0 错误/3 条既有警告（旧 PawnActionsComponent 和 Navmesh），未改 UE 资产。未做打包验收。
- 726 个时刻、7502 个原生曲线值（2686 个非整数值）通过，最大绝对差 `1.1920929e-7`，门槛 2e-6。
- 六动作各 240 帧，真实共享 Slot 的 Montage 自有曲线权重混合通过。缺失宿主曲线、错误动作身份、缺失 Montage 清单、缺失曲线及错误哈希被拒绝。
- 首轮 Import 110 过/1 失败：名字正则同时读到数据模型和 RawCurveData，造成重复名字。修为只读取唯一数据模型 CurveData 行；没有放宽数值门槛。随后五项姿态定向通过，最终 Import Release 定向 114 通过/0 失败/0 跳过；Optimize 构建 0 警告/0 错误。
- 产物和首次失败记录均在 `artifacts/mantle-montage-curves`，最终 TRX 为 `import-final.trx`。无新 Godot 场景/渲染验收、Core 全量或完整图 UE oracle。

原始键文件 `refactored_mantle_montage_curves.json` SHA256：`C59AB3344BCA178C7EEF6896FB0EA77CFCB026A4D03F43F993A4EAF4619EB979`。
原生参考 `refactored_mantle_montage_curve_reference.json` SHA256：`4C264B35765D4DEF832DBC877B016E85227A51DD5FD952BF18932E8F740B3135`。

## 后续与保留项

下一步继续普通宿主完整曲线布局/控制消费、正确 PostLocomotion 图位置与 typed notify dispatch，然后障碍探测、移动基座、Mantling motion 进入/中断/销毁/Ragdoll 生命周期。

旧缺口保留：物理稳定性 9/12、Flail 0/3、复杂相机、非恒等 OrientAndScale 原生对照、旧移动 oracle 闭包、完整视觉与十分钟性能验收。头颈、道具物理、音频暂缓，脚步粒子/贴花未执行。用户 plan 哈希仍为 78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100；project.godot、诊断文件和 uid 不纳入提交。
