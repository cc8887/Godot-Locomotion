# Standing 原生连续对照：时钟修正，姿态验收未通过

本批直接在 `D:/GodotALS` 的 main 推进。新增原始 Standing 完整子图与真实 Parent/Montage 的连续轨迹，发现并修正宿主的两处接线错误。本批不是完整角色或普通 Demo 验收完成。

## 已修正

1. Standing 转移条件改为独立的 `MovingSmooth` 输入。UE 的 `bMovingSmooth` 和 Rest 使用的 `bMoving` 不是同一个字段；轨迹加入松开输入后速度仍为 170 的滑行片段，不能继续用 raw Moving 代替二者。
2. 无 Movement 分支更新时仍准备、预校验和提交空播放器批次。UE 每帧轮换同步组；旧宿主跳过整个批次，导致再起步时已重置的 RunStart 被旧组时钟覆盖。30 Hz 第 69 帧原先为 0.349999994 秒，原生为 0.141666666 秒，修正后此差异消除。隐藏 BlendSpace 自身的历史仍由原播放器运行时保存，没有将整组清理误作全实例重置。

新增专项回归分别保护 MovingSmooth 独立性、四帧隐藏后的 RunStart 重入；既有整帧取消重试、跳过求值和后处理故障测试继续执行。

## 原生证据

导出器创建临时 GamePreview 世界，使用原 AB_Als Parent、原 Standing 生成类和 79 骨骼布局。工作线程执行原 Standing 根图和缓存遍历；主线程执行 NativePostUpdateAnimation，再调用真实 StopQuick 生成通知。没有改原动画资源，没有保存资产。

- 30/60/120 Hz，各 11 秒，总计 2310 帧；五种 Standing 状态、12 次 QuickStop、真实 Turn/Transition 实例、Pivot 输入及周期性连续三帧 update-only。
- 最后完整 Editor 构建记录：`20260925T090603078Z-432f2f7082c14ac08af503f925cda1f7`，4 actions，UBT 0，4 个项目插件审计通过。引擎 5.9，BuildId `b4127720-ddfd-475f-a955-59a24fb7ace6`，输入指纹 `173CD9BE7773184C3C321FF981F9A60E642CC56B08B290250FC1E1F47AE8B94C`。
- 冷启动 PID 40584、正常 Editor PID 18296 均实际退出 0。最终两份输出 28,559,445 字节，SHA256 均为 `2C6FC40160E046D71EB9BB38A62850CEA65BC0F7E99E80170752C889CEEA6CF3`。冷输出为 `assets/config/refactored_standing_host_trace.json`，普通输出为 `artifacts/standing-host-native/normal-v3.json`。
- DataValidation PID 27668 实际退出 0，0 errors / 3 既有 warnings。普通 Editor 的既有 Condition failed、PawnActions/Navmesh 等警告未修复；不将其隐藏为无警告启动。
- 两处 exporter C++ 源镜像 SHA256 均为 `CC0C80259CAFC69064F2BEFF9678F0ECD9FBDC63CABF289D1FD58ADC0C502B78`。未编辑引擎、复制 DLL 或调整 BuildId。

保留中间失败：最初受保护函数直接调用编译失败，改为派生类成员访问；早期导出拒绝已终止的 null Montage 实例，改为遵循原有导出器的有效实例过滤；v2 刺激缺少 MovingSmooth，只有三状态，已由 v3 替代。对应 PluginBuild 和 standing-host-cold/normal 日志仍在 UE Saved/Logs 中。不能把旧 v2 的冷输出当最终数据。

## 当前严格对照结果

`artifacts/tests/standing-host/standing-host-all-frames.trx`：三组均完整执行，但三组最终数值门禁均失败，没有跳过或放宽预算。测试累积数值超限再在结尾失败，避免首个误差遮蔽后续状态/动作错误。

| Hz | 求值骨骼次数 | 最大位置差（cm） | 最大曲线差 | 最大时钟差（s） |
|---|---:|---:|---:|---:|
| 30 | 19,908 | 3.8687163e-5 | 3.8146973e-6 | 4.7683716e-7 |
| 60 | 40,053 | 3.2808883e-5 | 3.8146973e-6 | 3.5762787e-7 |
| 120 | 80,106 | 2.1368271e-5 | 7.6293945e-6 | 2.6822090e-7 |

全部 2310 帧状态、Parent 已比较字段、QuickStop 数量、Montage 数量/来源/slot/playing/rate、活动移动播放器时钟/权重通过当前检查。最大 Parent 字段差 1.1920929e-7、权重差 5.9604645e-8。剩余超限为 258 个骨骼位置分量及 FootPlanted、RotationYawSpeed 曲线；位置预算仍 2e-5 cm，旋转/缩放/曲线预算仍 2e-6。其他数值检查没有超限。未证明逐位相同，也未确定所有剩余差异的来源；不能据差值很小便宣称完整对齐。

首轮原生状态失败、源时钟失败、修正后姿态失败的 TRX 均保留。专项回归首次编译错误为测试误用资源的 PlayerId 属性，改用原资源数组索引，无生产算法变更。

修正后的专项与相关回归 62 项通过、0 失败、0 跳过，见 `artifacts/tests/standing-host/standing-host-native-related.trx`。Godot 工程 `dotnet build GodotALS.csproj -c ExportRelease` 通过，0 警告/0 错误；导出 Python 语法检查通过。此构建不是打包或实际场景运行；上述三项原生严格对照仍失败，没有用相关回归的通过替代它们。

## 后续

继续分离源采样、状态混合与惯性历史中的误差，再处理严格姿态/曲线门禁。外层 Transition Slot、Crouching、完整角色共享 bank、源 Notify/曲线反馈、Overlay、Godot 适配与普通 Demo 切换仍未完成。本导出仅验证受控输入的 Standing 子图；不含完整 Character/world/root motion 模拟，也不自动派发通用源序列通知。

本批没有 Godot 运行、多帧画面或十分钟性能验收。Ragdoll/Get-up/Pose Recovery 等原目标继续保留；道具物理、音频、头颈问题仍按用户要求暂缓。用户修改及未跟踪诊断文件不纳入提交。
