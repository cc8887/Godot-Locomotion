# 粒子初始姿态的存储精度

本批在 . 补齐创建、重置和外部姿态输入时的原生四元数存储边界。两模型的初始姿态现在与独立 UE 世界逐值一致；完整矩阵仍 **8/12**，四项休眠失败未解决，普通角色尚未接入 Core 刚体后端。

## 缺口与修正

UE 的粒子 SetR/SetQ 先将 actor 四元数存为 float，再进行首步 COM 计算和预测。原 Core 初始化保存传入 double 四元数，只在后续积分 StoreActor 时才舍入；重置和直接提供的外部目标也缺少此边界。

当前普通 120 Hz 原生完整世界的初态观测：AnimMan 82 个、Mannequin 74 个四元数分量发生舍入，最大分别 2.9426034497959108e-8、2.7841497218794586e-8。每个分量转 float 后再提升为 double，与原生 sample 0 精确一致；位置仍为原 double。

AlsJointIsland 现在对自有的初始状态、成功 Reset 的状态、暂存 external target 统一保存 float 四元数；不在舍入后归一化，不舍入位置，不修改调用方数组。Reset 先验证全部输入再发布，external target 保持暂存/失败回滚语义。

复用独立导出的 v4_physics_world_contact_window.json，新增 Import 回归遍历 40 身体的创建和 Reset，共 80 次状态比较；姿态和速度逐值相等，80 次旋转都确实经过舍入。未重写任何原生 golden。Core 新增非平凡旋转外部目标的存储与拒绝/重试回归。

## 实际回放影响

普通 120 Hz 新采集 74 个步骤样本，与前批的独立完整 UE 世界比较。时间步长在本批保持不变，单独测量旋转存储修正的影响。

| 模型 / 完成步 | 前批最大速度差 cm/s | 本批最大速度差 cm/s |
| --- | ---: | ---: |
| Mannequin / 1 | 0.0000370864 | 0.00000794604 |
| Mannequin / 147 | 0.172008286 | 0.161465413 |
| Mannequin / 150 | 1.566225456 | 1.550897098 |
| Mannequin / 160 | 1.253298183 | 1.603835046 |
| AnimMan / 1 | 0.000575327 | 0.000588093 |
| AnimMan / 160 | 0.350294552 | 0.145112012 |

早期和后期均有改善及退化，不能把初态逐值一致称为整段轨迹一致。第 143..152 步的 20 帧、416 对接触仍与原生的两端顺序、图内顺序、集合全部一致。

普通 120 Hz AnimMan 第 604 步睡眠（前批 675）；Mannequin 到 1200 步仍未睡。末秒最大速度 2.266960620880127 cm/s、角速度 0.2738077938556671 rad/s，最大锚点误差 0.5033167852807052 cm，累计接触点 92836。休眠阈值未改。

矩阵 30 Hz 只有高速通过；60 Hz 四项全过；120 Hz 高速、平移、旋转通过，普通休眠失败。八个成功报告均相对前批变化，没有新增或关闭失败项。

## 验证

所有日志位于 artifacts/physics-particle-input-20260922/：

- Core Release 固定 JIT、串行全量 2872 通过，沿用排除 AlsP5aGoldenTests / AlsP5aTraceSchemaTests 的既有过滤；相关定向 28 项在 .NET 8.0.28 / 9.0.17 均通过。
- Import Release 固定 JIT、串行全量 **2453 通过 / 1 既有跳过**（NormalEditorRepeatsTheConsumedGraphAndInputSemantics）。新初态与旧关节参考定向 5 项在 .NET 8 通过；新初态对照在 .NET 9 通过。
- Godot Optimize 构建 0 warning / 0 error；30/60/120 Hz 接触 smoke 全过。
- 60 Hz 真实场景接触：3 场景、13 几何、9 生命周期检查通过。
- 世界姿态回归：8 场景、160 身体交接、192 捕获、32 拒绝输入通过；最大位置误差 5.8788432e-6 m、basis 误差 6.493369e-7、速度误差 1.1920929e-7 m/s。
- 本批无 UE 插件改动、无新导出或 Editor 重启。沿用前批有效构建；既有普通 Editor 退出 0xC0000005 和两条 Condition failed 未解决，不能称完整 UE 门禁通过。

## 下一边界

已确认当前 UE 实际 dtUsed=0.008333333767950535，而 Godot capture dt=0.008333333333333333，名义上都为 120 Hz，实际秒数不同。Compare-WorldCapture.ps1 新增 nativeDt/capturedDt/sameStepDuration，接受同名义频率的旧 double 或原生 float 秒数并明确记录，不把两者静默视为相同输入。新报告 74 条全部 sameStepDuration=false，原有速度差字段逐值未变。

下一步应明确主机层到 Core、kinematic velocity、接触 Gather 和睡眠使用同一个步长，再单独测量这个边界，继续首步残差及后期放大定位；不能只在某一个公式中强制 float 或只修改比较参考。四项稳定性失败、普通 Ragdoll/Get-up/Pose Recovery、Mantle、完整相机和十分钟性能预算仍待完成。

用户 P4 规划文件不在本批提交范围，SHA256 保持 78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100。
