# 普通相机时间缩放边界修复

## 问题与证据

原普通宿主使用 `delta / Engine.TimeScale` 实现导入设置 `ignoreTimeDilation=true`。在同一物理批次中修改倍率后，当前属性值可能不是生成回调 delta 时的倍率，导致相机图和位置阻尼错误推进。

本机 ALS `Plugins/ALS/Source/ALSCamera/Private/AlsCameraComponent.cpp:105` 同样明确处理“当前倍率尚未作用于当前 delta”的情况，但 UE 的 previous global/custom dilation 机制不能直接照搬到 Godot。

通过 agent-reach 查阅 Godot 官方提交 `ed1daf0bf` 的 [Main::iteration](https://github.com/godotengine/godot/blob/ed1daf0bf/main/main.cpp#L4577)：物理步长与有效倍率在进入整批物理循环前各读取一次，回调使用两者乘积。GitHub CLI 未登录、Jina TLS 失败，改用网页工具读取同一官方源文件；未修改账号或引擎。

新增普通 demo 测试在 PhysicsFrame 信号切换倍率，同时转动控制视角。固定 15 FPS 触发 catch-up。修复前 60 Hz 第一处切换 1 → 0.25 即失败：回调 `0.016666666666666666` 秒，相机却使用 `0.06666667` 秒，单步偏航误差 `14.204578915697937` 度。日志 `artifacts/camera-time-scale-before.log`，退出 1。

## 实现

`AlsNativeCameraHost` 在忽略时间缩放时使用固定物理步长 `1 / Engine.PhysicsTicksPerSecond`，否则仍使用传入 delta。该时间同时送入相机图及 Follow；不读取已变化的当前倍率，不累积遗漏步长。新增只读已提交 delta/RotationLag 诊断，失败时不发布 delta。

与项目既有物理回放契约一致，物理频率变更必须在 idle 边界进行；本批没有支持在一批物理回调中途修改 Hz。没有用渲染帧间隔或墙钟作为每个物理子步的相机时间。

## 验证

每次运行四秒固定物理时间，倍率序列 0.25、2、0.5、1 循环两次。逐帧确认相机提交、时间步长，并用先前已通过 native 对照的旋转阻尼函数和独立固定步长计算参考偏航。它验证宿主输入正确性，不是新 UE 时间倍率运行轨迹。

| 运行 | 有效样本 | 倍率切换 | 当前倍率与回调倍率不符的样本 | 最大偏航差 |
|---|---:|---:|---:|---:|
| Single 30 Hz，固定 15 FPS | 120 | 8 | 12 | 0° |
| Parallel 60 Hz，固定 15 FPS | 240 | 8 | 16 | 0° |
| Parallel 120 Hz，固定 15 FPS | 480 | 8 | 24 | 0° |
| Parallel 60 Hz，普通调度 | 240 | 8 | 14 | 0° |

上述三次退出 0，日志 `artifacts/camera-time-scale-after-{30,60,120}.log`。Optimize 构建零警告、零错误。首次测试编译因调用内部 Normalize 方法失败，改为公开 Math.IEEERemainder 后构建通过；与之后明确的行为回归失败分开记录。

普通调度复跑也退出 0，日志 `artifacts/camera-time-scale-normal-60.log`。普通 demo 60 Hz 并行八秒回归退出 0，共 480 次相机提交，移动/冲刺、倒地/起身、人称/肩侧切换和 control yaw 不回写检查通过，日志 `artifacts/camera-time-scale-demo-regression.log`。本批无新截图。

## 剩余范围

没有改动 Core、Import、UE 插件或冻结参考，无新 UE 启动/构建/全量测试。没有证明暂停/零倍率、UE owner custom dilation、运行中任意位置切换物理频率或视觉等价。

检查另外确认普通宿主 MeshScale 仍固定为 1，并复用只接受刚性变换的物理转换；这处缩放缺口尚未修复，不能以本批时间修复替代。复杂碰撞、完整相机视觉验收、旧静态物理 9/12 和 Flail 0/3、Mantle、最终十分钟性能预算继续保留。头颈/道具物理按用户要求暂缓。

工作仍在 `D:/GodotALS` 的 main。用户 P4 计划哈希保持 `78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100`，三份头颈诊断文件未动。
