# 原生相机：动态基座历史对照

工作目录 `.`，`main`。

导出器扩展环境选项 `ALS_CAMERA_COMPONENT_BASE=1`，仍调用真实 UAlsCameraComponent::TickComponent。创建两个 transient、Movable、无碰撞的 UBoxComponent 基座，使用当前 FMovementBaseInterfaceData 接口 SetBase，并通过 SaveRelativeBasedMovement 设置角色的相对基座姿态。导出实际 GetBasedMovement/MovementBaseUtility 返回的相对旋转标志和世界变换。

每个 30/60/120 Hz 四秒轨迹：基座同时平移、偏航、pitch/roll 倾斜；前 1.5 秒使用基座 A，随后换 B，3.3 秒离开。继承原来的人称切换、500 cm 瞬移和 FOV 覆盖，使换基座发生在全第一人称期间，退出第一人称后继续读取此前历史。无角色动力学、骨骼基座、碰撞或穿透；角色仍是参考姿态，位置和 view 是受控输入。

回放只初始化一次历史，之后每帧比较四种世界位置、相机旋转、FOV/TraceRatio，另比对原生 PivotLagLocationMovementBaseSpace 与 CameraRotationMovementBaseSpace。保持先前 0.001 cm / 1e-7 度容差，局部 quaternion 分量允许 1e-8，处理 q 与 -q 等价。另断言 9 次基座变化、693 个相对基座帧和 6 次目标瞬移，防止测试没有真正覆盖分支。

新增数据使用单独的 `refactored_camera_base_reference.json`，不覆盖前批无基座参考。原无基座文件 SHA256 保持 `C3117D4D88ED129A8A4F90BCBD8133A40096B64C9CC4D245887A5741628EED86`。

首轮完整构建成功但出现已弃用 SetBase(UPrimitiveComponent*) 警告，保留前缀 `20260924T113659922Z-4b2b12925c6f44b0b313b033df53ab8c`。改用新接口后重建，前缀 `20260924T114126053Z-c8c9a56193134c89a3bc2d46a52dc941`，编译成功且没有该警告。继续按 UE 构建/加载技能验证全部插件，再启动导出。

最终完整插件审计通过，fingerprint `70AF50F54F92F21BBD251B2C25BF01D4539FCFA52D791D48C2B26B47590563C0`，BuildId `c9a68b99-36ee-4db4-8c8b-3aee056a528e`。主目录和 UE 镜像 CPP SHA256 均为 `592FBF472C3D676032B13F1DC73AA4EE63E75436938C2CE2AFC3C3FA3B91A9E1`。

## 验证结果

- 冷导出退出 0，0 error/0 warning，日志 `artifacts/camera-base-native.log`。
- 普通 Editor PID 28236 重导并正常退出 0，日志 `artifacts/camera-base-editor.log`；两份输出字节完全一致，SHA256 `9FB853FAF5C280BDAEC6A7CB794BB6F01A957A547546180F8703FCA1AEDD6F75`。普通启动仍有两条既有 Condition failed，未修复也未隐藏。
- 840 帧连续回放通过，最大位置误差（含局部枢轴）`7.101632223002542E-06` cm，最大相机旋转误差 `2.8421709430404007E-13` 度。局部旋转 quaternion 分量、FOV 和 trace ratio 断言通过。
- 基座变化 9 次，相对基座 693 帧；进一步确认 687 帧基座位置实际变化、683 帧实际倾斜，避免仅验证基座标志。
- 最终 Import 相机专项 42 项通过（含旧无基座840帧与图3114帧），`artifacts/camera-base-tests/base-final.trx`。首轮同42项通过；后补移动/倾斜覆盖断言后复跑，未调整容差。
- 本批无 Core/Godot 生产算法改动，没有全量 Core/Import 或新的 Godot 渲染/性能测试。
- DataValidation 退出 0，0 error/3 既有 warning，`artifacts/camera-base-data-validation.log`。Editor-only 导出插件未做游戏打包验收。

完整相机仍需原生碰撞/穿透对照与 Godot 全图到实际场景联合验收；本批原生位置回放使用观测曲线，不替代这些工作。旧物理稳定性、Flail、Mantle、十分钟性能仍未完成。头颈与道具物理暂缓，用户文件保留。
