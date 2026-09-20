# Refactored 独立落地预测与原生对照

日期：2026-09-13，第一百七十三批。归属原 P4 脚部/骨盆控制的输入补完。

## 实现与边界

正式导出 AB_Als_C 实际 Settings.InAir、碰撞 channel/responses 与
CF_Als_GroundPredictionAmount 的原生键/切线，保留 1001 个 GetFloatValue
采样。严格编译器检查资产来源、曲线域、插值、响应通道和数值一致性。

新增独立 AlsRefactoredGroundPrediction，将原 RefreshGroundPrediction 分成
查询准备和观测消费。输入使用 UE 世界轴、厘米、世界胶囊尺寸及当前实例
缓存的 GroundPredictionBlock；请求保留帧身份和序号，拒绝其他候选响应。
垂直速度等于 -200 cm/s 时开始预测，距离按 [-200,-4000] → [150,2000]
映射后乘角色缩放。屏蔽值先钳制，allowance <= 1e-4 时不查询；初始穿透
只要 blocking 且法线可行走仍接受。响应曲线输出不额外钳制。

这是与 V4 LandPrediction 分开的合同，尚未接生产帧。Godot 胶囊扫掠、
碰撞通道映射和初始穿透法线的真实几何验证仍待实现；本批原生物理结果
作为计算对照输入，不代表 Godot 与 Chaos 碰撞求解已经相同。

## 原生验证

新增导出器接口创建临时 GamePreview 物理世界，在 AB_Als_C 临时实例上
调用原 RefreshInAir。保持原函数的游戏世界检查，不修改 ALS 源码，
不保存 UE 资产。平地胶囊场景覆盖五种高度、七种垂直速度、两种水平
速度、三种缩放和六种屏蔽值，共 1260 组；另记录同世界的原生扫掠。

41 项相关 Import 测试通过，原生对照中 462 组预测为正、798 组为零，
含 216 组接受初始穿透。最大预测误差 1.1920929e-7，最大扫掠向量误差
4.263256414560601e-14 cm，保留原 1e-6 cm 检查门槛。
结果：`artifacts/tests/ground-prediction-173-reciprocal.trx`。

原生 Win64 Development 使用 /fp:fast，常量范围除法被优化为乘 float
倒数。直接 C# 除法在 -1500 cm/s 案例产生不同舍入；按锁定构建的运算
顺序修正后全组通过。这是该构建数值基线，不声称所有编译器逐位相同。

冷启动与普通 Editor 均退出 0，输出完成标记及 1260/1001 条有效数据。
二者与正式原生夹具 SHA256 均为
AF01524BBD4731C3BC4AFA18417E5D26727169584E9FD2E3AC317DF8A0024F3C。
设置导出也字节一致：506CA7589662717E5CF660617BFA89747AED151E36BF2FFE90776A126B40A88F。
日志为 `artifacts/unreal/ground-prediction-173-native.log` 和
`ground-prediction-173-native-editor.log`。普通 Editor 仍有两条既有
LogAutomationTest Condition failed，不能称为全日志零错误；本次采样
无 Python 异常、断言或退出失败。

Godot 优化 Debug 构建零警告/零错误，Python/Node 语法检查通过。新模型
尚未被生产调用，因此没有重复上一批的生产回放来冒充新功能接入证据。

## UE 插件门禁与首错保留

使用 ue-diagnosing-plugin-build-load 技能的完整 Editor 目标构建与审计，
不是单独替换 DLL。成功构建日志前缀
20260913T130722504Z-1d108601e79941dc825e0177ad6a59ef，
BuildId 为 922a5b3d-4d0f-46e9-875e-c17bc57ca9ee，输入指纹
40DBFFC0CDCD195581A79C618854FCB83EE7118457CFFC49845306EDC1F90742。
项目 DataValidation 退出 0，0 errors/3 warnings；日志
`artifacts/unreal/ground-prediction-173-validation.log`。
三条警告来自原 V4 AI 的 PawnActionsComponent 缺失及旧 NavMesh 版本，
未修改这些资产。隔离插件 BuildPlugin 成功退出 0；UBA 内存压力触发一次
编译重试后完成，没有重启构建。日志
`artifacts/unreal/ground-prediction-173-package.log`，产物
`artifacts/unreal/ground-prediction-173-package/`。打包后项目审计退出 0，
ALS、AlsGodotExporter、AutoTestTools、BlueprintLisp 的清单、DLL、
BuildId 和 receipt 均一致。源文件、公共头和 Build.cs 的两份镜像相同。

此次 PowerShell 传给批处理的未加引号参数使临时包先输出到
`D:/UnrealEngine/$predictionPackage`。构建完成后核实该目录由本次创建、
包含预期插件和 DLL、目标目录不存在，再整体移入上述 artifacts 路径。
没有删除文件，也未替换项目或引擎 DLL；后续命令应使用明确的完整路径参数。

保留失败证据：初次 Editor 世界探针被原游戏世界检查跳过，210 行默认值
无效，未作为正式夹具；Python PIE 尝试因无 LevelEditorSubsystem 失败。
最终临时游戏世界接口避免这些前提。首次 C++ 构建因 protected getter
失败，改用反射读取实例 Settings 后完整目标重新构建通过。
首次 C# 原生对照失败在 sweep 舍入，日志 ground-prediction-173-native.trx
保留，未放宽误差门槛。

## 继续顺序

接真实 Godot 胶囊查询与明确碰撞映射，补 Grounded/Jump/Fall/Land 的其余
六个曲线生产点和缓存 PoseState 的读取时点，再接生产 Worker 分阶段查询
及完整脚部 Rig。之后按实际脊柱→脚→手顺序提交整个根，执行移动接触、
平台、多帧截图及人工对照。第 616 帧旧失败仍开放；P3/P4 整角色验收和
P5A–P7 未完成，音频继续暂缓。
