# Refactored 姿态曲线生产位置与 Rig 输入读取

日期：2026-09-13，第一百七十批。继续原 P4 完整脚部输入补完，尚未切换 Demo。

## 原资产证据

只读导出 7 个实际 linked AnimBP：Grounded、Locomotion、Standing、Crouching、
Layering、Head、Ragdolling。原生文本位于 `artifacts/refactored-pose-graphs-170/`。
检查脚本保留完整 ExportPath，区分源图节点和 ExecuteUbergraph 的编译副本，
并读取嵌套 AnimGraphNodeBinding，不能只看界面引脚的 DefaultValue。

`assets/config/refactored_pose_curve_inputs.json` 保存 8 个实际曲线写入节点：

| 写入位置 | 原图写入 | Alpha / 后续消费 |
| --- | --- | --- |
| Grounded 顶层 | PoseGrounded、FootLeftIk、FootRightIk = 1 | Alpha 1；之后继续父图混合 |
| Standing 的 Movement 缓存内 | PoseMoving = 1 | 后续状态/缓存/惯性化继续混合 |
| Crouching 的 Movement 缓存内 | PoseMoving = 1 | 同样不能在最终姿势强制写 1 |
| Fall 的脚曲线节点 | FootLeftIk、FootRightIk 目标值 1 | Alpha 绑定 GetParent.InAirState.GroundPredictionAmount |
| Jump 的脚曲线节点 | FootLeftIk、FootRightIk 目标值 1 | 同上，为 WorkerThread_Unbatched 属性读取 |
| Fall 的姿态节点 | PoseStanding、PoseInAir = 1 | 在脚曲线节点之后，Alpha 1 |
| Jump 的姿态节点 | PoseStanding、PoseInAir = 1 | 同上 |
| Land 节点 | 两脚 IK/Lock、PoseStanding、PoseGrounded = 1 | Alpha 1，继续参与状态混合 |

这些节点的序列化 Node.CurveValues 可为零，而实际暴露引脚为 1；空中脚节点
Alpha 引脚显示 1，但嵌套属性绑定覆盖它。只复制显示默认值会产生错误行为。

Fall/Jump 的包裹 CallFunction 节点调用 RefreshInAir。原 UE CallFunction
默认 OnUpdate 在 SourcePose.Update 前调用，之后才遍历子节点的动态输入；
不能把该预测数据无条件视为上一帧或静态 Alpha。

## 本批代码

`AlsRefactoredPoseCurveCompiler` 严格编译上述 8 个写入点，校验原始路径、节点
数据、动态绑定、引脚值和相邻连接。`AlsRefactoredPoseCurveRuntime` 按指定
写入点执行已有原生对照过的 ModifyCurve/Blend，保持 clamp Alpha 和缺失
曲线插入语义。它是节点生产器，尚未安放进当前 V4 的状态与缓存遍历。

`AlsRefactoredPoseCurveReader` 从明确的已提交曲线布局读取 Grounded/InAir/Moving，
返回带帧身份的值快照；布局缺失不能以别的曲线名替代，已知名字未出现时读零。
`AlsRefactoredPoseCurveHistory.Apply` 验证 PreviousIdentity，再计算原生
Clamp01(Grounded + InAir * GroundPrediction) 并填充脚部输入的 PelvisAmount。

两个读取时点必须分开：RefreshPose 在 NativeThreadSafeUpdateAnimation 内
读取父实例 proxy 曲线缓存，构成 PoseState；Control Rig 在本帧实际求值时
直接读取其输入姿势曲线。前者的 MovingAmount 与后者本帧 PoseMoving 不是
可以任意互换的参数。当前代码分别保留缓存快照和 Rig 的当前曲线参数。

共用 T3D 对象解析提取为 `read_native_objects.mjs`；原脚部环境和腿部配置
重新生成后逐字节相同，SHA256 分别为
`F0A52840D6471C9405AB8C3D94826602E01349EE9FE27EDCF732C7AD84AA7222`、
`E2FD40F2F2FBF43C5C2366A5DD23185153E82A8DFF1DDD1566C655C524231CA4`。

## 验证

通过 Python 在临时 SkeletalMeshComponent/AlsAnimationInstance 的嵌套状态中
设置受控参数，逐项读回确认，再调用实际 GetControlRigInput。没有修改资产
或 CDO，也没有把 C# 公式生成的数据当原生真值。

294 组 Grounded/InAir/Prediction 组合（含超界值）与 C# 结果逐 float 相等，
最大误差 0。冷启动、普通 Editor、正式夹具三份数据一致，SHA256：
`91E8A22A72722924CAD81A59430871544C906FEB464166A1D1C494A96CF9C27F`。
夹具为 `tests/Als.Core.Tests/Fixtures/FootIk/native_rig_pose_input.json`。
该探针验证实际 getter 计算，不等于执行整个动画 Update/Evaluate 调度。

新增 10 项与相关回归合计 36/36 通过，覆盖原始配置、绑定/位置/曲线改动拒绝、
动态 Alpha 钳制、缺失曲线、缓存与当前曲线隔离、帧身份及原生 getter；
同时包含前批 1440 帧腿部组合和完整脚部帧事务。Godot Debug 优化构建
零警告、零错误。记录为 `artifacts/pose-curves-native-170-tests.log`、
`artifacts/tests/pose-curves-native-170.trx`、`artifacts/pose-curves-170-godot-build.log`。

完整 Editor 目标构建及审计通过，BuildId
`922a5b3d-4d0f-46e9-875e-c17bc57ca9ee`，输入 fingerprint 与第 168 批一致：
`2B4F92D47E20F9190D620351F4A7ADA7DE082C011D9DF36F4CB48B668B34D979`。
7 个蓝图冷导出退出零，getter 冷/普通 Editor 导出与退出零。普通 Editor
仍有两条既有 AutomationTest Condition failed，未宣称全日志无错误。
本批未修改 UE 插件、加载配置或资产；未重复第 168 批的隔离插件打包。

保留两次 Python 首错：第一次 InAirAmount 的 snake_case 名不能解析；改用
原始反射名称后，第二次因替换只读 PoseState 父属性失败。最终只修改临时
实例的嵌套结构引用，并显式读回验证，再成功采样。未修改属性权限或绕过
资产只读边界。相关日志为 `rig-pose-input-170-cold`、`-fixed`、`-final` 前缀。

## 后续接入

下一步把新写入点映射到当前移动图的对应缓存/状态边界，使新曲线跟随原有
状态、Overlay/Slot 和惯性化实际混合；不能在最终输出补一个运动布尔值。
同时核对 Refactored 落地预测与当前 V4 预测的差异，再将缓存姿态输入、新
脚部帧所有者、真实主线程查询、Worker 阶段、空间转换和手部顺序接到完整根。

新组件尚未生产接入，未执行新的移动截图或关闭第 616 帧约 41.100025°
失败。原 P3/P4 整角色与默认完整入口验收、P5A–P7 范围保持，音频仍暂缓。
