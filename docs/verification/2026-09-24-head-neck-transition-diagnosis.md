# 跳跃/冲刺切换时头颈拉长：诊断记录

用户明确：同时普通鼠标转向，没有 Alt 或右键；表现为脖子拉长。道具物理工作暂停。本批仅增加独立诊断场景/记录，没有修改生产动画、物理或导入逻辑，也没有提交修复。

## 测试

主目录 .，基于 main d288f36，优化构建通过。使用当前 Godot 4.7.2 Mono、OpenGL Compatibility、默认 Mannequin/Default overlay、60 Hz。

诊断场景 scenes/tests/head_neck_transition_diagnostic.tscn 使用真实 W/Space/Shift/Ctrl/R 输入：30帧跳跃，50帧按冲刺，140帧释放冲刺，180帧重新冲刺，240帧释放冲刺并蹲伏，280帧切回站立再冲刺，340帧请求翻滚，400帧停止。总480帧。实际状态记录覆盖 InAir、Grounded、Walking/Running/Sprinting、Standing/Crouching；输入请求本身不等于每个动作均成功执行。

五组均退出0、无生产错误，日志及独立目录均为 artifacts/head-neck-{名称}：

- parallel：完整图分阶段记录、无鼠标运动、实际渲染截图。
- look：普通连续鼠标转向，不读取骨骼世界变换、不启用大体积动画图捕获，实际渲染截图。
- stress：较大幅度鼠标横纵运动，完整图分阶段记录、世界变换、实际渲染截图。
- palette：较大鼠标运动、不主动读取世界变换，每次渲染后比较实际蒙皮矩阵与从局部姿态独立累乘的结果。
- single：单线程、较大鼠标运动、完整图分阶段记录，headless对照。

每组 trace.jsonl 保存 spine_03、neck_01、head 的局部位置/旋转/缩放，非 render-only 模式还保存世界位置。开启 --production-graph-capture 时保存 MainMovement、BaseLayer、PostLayering、PostAim、PreFoot 的对应骨骼和曲线。连续截图按渲染时机采样，可能跳过物理帧，不是每个物理帧都有截图。render-only 的 trace.world 是占位 identity，不可用于世界位置结论。

## 观察与结论边界

五组共2400帧，未稳定复现用户描述的明显拉长。头部局部偏移长度0.0929075142～0.0929075342 m，颈部0.1656259275～0.1656259576 m；误差为浮点量级。未观察到明显缩放异常。palette 组969条渲染矩阵对照：位置误差最大4.4802715e-7 m，基向量误差最大1.8128046e-7。不能据此宣称所有运行情况下无渲染同步问题。

这些测试全程 RagdollSimulation=null，因此本次复现序列没有运行新接的物理骨架回写。已检查若干切换前后截图，尚未发现与用户反馈一致的明显头颈拉长；不把正常关节外观或大角度低头直接等同于该缺陷。

当前不能确定根因，也不能宣称问题已修好。下一步需要用户实际异常视频/准确操作时机，以对齐鼠标方向幅度、切换帧及运行场景；随后针对异常帧比较分层/瞄准结果、蒙皮和绑定姿态。未运行 UE 对照、完整资产蒙皮审计或长时间随机操作，不能排除这些路径的异常。

## 复跑

Godot参数示例（选择新的输出目录保留旧证据）：

```text
--path . --rendering-method gl_compatibility res://scenes/tests/head_neck_transition_diagnostic.tscn -- --output=./artifacts/head-neck-new --capture --look --stress --render-only
```

去掉 --render-only 并加入 --production-graph-capture 可记录动画图阶段；加入 --single 可对照单线程。无 --capture 时支持 headless。诊断场景不接入正常入口。用户原 P4 文件 SHA256仍为78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100。
