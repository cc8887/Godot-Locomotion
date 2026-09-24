# 普通 Demo：ALS 相机接入

工作目录 `D:/GodotALS`，分支 `main`。普通入口 `scenes/demo/als_demo.tscn` 默认启用；旧 P4 场景保留诊断行为。

## 实现

- Main Observe 阶段在动画提交、物理姿态展示之后读取实际 Skeleton 插槽；Worker 空闲且显示与运行时提交帧一致才更新。
- 由已提交角色状态选择原生 Camera 图的旋转模式、步态、姿态、动作；结合真实胶囊底、移动基座、鼠标控制旋转运行图与位置事务。
- 接入球扫、起点穿透恢复和当前胶囊 RID 排除；原生 trace enum 不直接作为 Godot 碰撞 mask。
- 原 Orbit 节点继续采集鼠标与提供控制 yaw/pitch。Camera3D 使用独立世界变换，关闭旧 SpringArm 处理，采用水平 FOV；相机阻尼不反写移动控制方向。
- B 切换第一/第三人称，T 换肩，HUD 提供提示。第一人称状态传给下一次 Motor Gather。
- 当前 owner 改变时重新绑定骨架、重建相机历史和碰撞排除。该分支本批未做换代故障验证。

## 验证

`dotnet build GodotALS.csproj -p:Optimize=true --no-restore`：0 警告、0 错误。

`native_camera_demo_smoke.tscn` 使用普通 AlsDemoEntry，时间轴固定为八秒：移动/冲刺、鼠标旋转、T 换肩、B 两次切换、Ragdoll 进入、退出及起身完成。按键经 Viewport.PushInput 分发，鼠标运动调用现有 ApplyMouseMotion；未模拟操作系统硬件输入。

| 运行 | 相机提交 | 结果 / 日志 |
| --- | ---: | --- |
| 单线程 30 Hz | 240 | 通过，`artifacts/native-camera-integration-30.log` |
| 并行 60 Hz | 480 | 通过，`artifacts/native-camera-integration-60.log` |
| 并行 120 Hz | 960 | 通过，`artifacts/native-camera-integration-120.log` |
| 渲染并行 60 Hz | 480 | 通过，`artifacts/native-camera-integration-render.log` |

各组检查输出有限、无宿主故障、换肩生效、进入第一人称、回到第三人称、倒地/起身完成；控制 yaw 始终保持预期 0.49999976 rad，不随相机跟随变化。

最终五张截图位于 `artifacts/native-camera-integration-images/`，阶段 80/140/180/240/360 分别对应运动、换肩、第一人称、倒地、起身。人工检查换肩、第一人称及起身图；此前首轮五图也检查了运动和倒地。未见明显镜头方向或构图异常，截图不是连续轨迹等价证明。

## 未完成 / 边界

- 本批没有新的 UE 相机组件位置轨迹 oracle、连续贴墙/复杂穿透、移动平台或角色换代专项；之前相机图的原生对照不能替代这些测试。
- 当前普通宿主按单位比例角色工作；缩放角色未接入。查询异常会丢弃候选并冻结相机、记录错误，尚无宿主自动恢复策略。
- 图与 socket 检查仍有逐帧分配，未做十分钟性能验收；截图 HUD FPS 不作性能结论。
- 本批未修改 Core/Import/UE，也未重新运行其全量测试。旧物理稳定性 9/12、Flail 0/3 未关闭；Mantle 及全项目联合验收继续。头颈和道具物理按用户要求暂缓。
- 用户原有 P4 规划和头颈诊断文件保留，不纳入提交。
