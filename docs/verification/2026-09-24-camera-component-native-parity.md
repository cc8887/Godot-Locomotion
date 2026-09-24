# 原生 Camera 组件：无碰撞连续位置对照

工作主目录 `.`，`main`。本批新增原生 Editor-only 导出接口、冻结参考和连续回放测试，没有修改 Godot 生产算法。

## 独立来源与范围

导出器创建真实 B_Als_Character 与 B_Als_CameraComponent，禁角色移动更新，使用参考姿态；调用原组件的 TickComponent，原相机动画实例正常更新，读取真实 GetCurveValue。没有复制 TickCamera 的公式来生成期望值，没有替换原组件。

为隔离空间计算，角色位于高处无碰撞区域；记录实际 root/head/第一人称/肩部世界插槽、mesh 旋转/比例、原生 view、FOV 覆盖、曲线，以及组件内部 PivotTarget/PivotLag/Pivot/CameraLocation、旋转、FOV、TraceRatio。角色位置/view 是受控输入，不是原生 CharacterMovement 的结果；没有动画运动、Ragdoll、移动基座、碰撞/穿透或 time dilation 验收。

30/60/120 Hz 各四秒，共 840 帧。覆盖连续平移与转向、人称切换、换肩、500 cm 瞬移、105 度 FOV 覆盖。实际曲线中 full FirstPerson 192 帧、部分混合 36 帧；三次瞬移、210 帧覆盖均有断言。

Python 驱动核对实际设置与回放设置，写入来源类、设置 SHA256 和引擎版本。测试检查来源与摘要；每条轨迹仅用 UE 初始状态启动，后续连续推进自己的状态，不逐帧重置到 UE 输出。

## 数值结果

- 840 帧、四种位置输出最大误差 `6.872798536897762E-06` cm；预设容差 0.001 cm。
- 旋转最大角差 0 度；预设容差 1e-7 度。
- FOV 通过 1e-5 容差；原生与回放的 TraceRatio 均为 1，证明此参考没有混入阻挡。
- Import 相机专项 41 项通过，包含既有 3114 帧图参考及 FOV/事务回归，`artifacts/camera-component-tests/component-final.trx`。测试 Release 构建通过；没有 Core/Import 全量或新的 Godot 渲染/性能测试。

两次冷导出均退出 0、0 error/0 warning，日志 `camera-component-native-1.log`、`-2.log`。轨迹内容字节一致；第二份增加 engine 元数据，因此整文件摘要不同，不能称两文件字节相同。

正式参考 `assets/config/refactored_camera_component_reference.json` 取第二份，SHA256 `C3117D4D88ED129A8A4F90BCBD8133A40096B64C9CC4D245887A5741628EED86`。

普通 Editor PID 3688 实际加载新插件，导出后正常退出 0；`artifacts/camera-component-editor.log`。其 JSON 与正式参考整文件字节相同。启动仍有两条既有 `LogAutomationTest: Error: Condition failed`，不声称整个 Editor 日志无错误。

## UE 构建

按照 ue-diagnosing-plugin-build-load 技能执行完整项目 Editor-target 构建和四项目插件审计，未复制 DLL、修改 BuildId 或依赖 Live Coding。新增 ALSCamera 模块依赖，其插件所有者 ALS 原已声明。

首次因系统 dotnet 缺 .NET 10 失败，日志前缀 `20260924T111752172Z-0a4544ea1fd94644af8b5c51542ebe12`；指定引擎自带 DotNet 10 后完整构建与审计通过，前缀 `20260924T111928372Z-b7600d34fd434b82aaafca1ae9c7acc5`。fingerprint `66EB7E32057D2F034396EFB7CE2F749AF75D939455F96141589E0307F9BDEAE1`，BuildId `c9a68b99-36ee-4db4-8c8b-3aee056a528e`。导出器 CPP 主仓库与 UE 镜像 SHA256 同为 `C6F08D26372482079E7AE3F160A3EFE561C1701996A4F9F98191F78E739AA472`。

DataValidation 退出 0，0 error / 3 既有 warning（旧 PawnActionsComponent 与导航网格版本），日志 `artifacts/camera-component-data-validation.log`。本插件为 Editor-only 模块，未做游戏打包验收。

## 下一步

继续原生碰撞/穿透与动态基座轨迹，然后使用 Godot 自己的曲线图/实际场景联合验收；本批的位置层使用观测的原生曲线，不等于完整 Godot 图到镜头链路已原生等价。完整 Camera、Mantle、旧物理稳定性、Flail 与十分钟性能目标仍未完成。头颈及道具物理继续暂缓，用户 P4 规划及头颈诊断文件保留。
