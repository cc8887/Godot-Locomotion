# Refactored 脚部查询的 Godot 物理边界

日期：2026-09-13，第一百七十一批，原 P4 完整脚部链路。

## 改动与来源

新增 `src/Als.Godot/Locomotion/AlsRefactoredFootQueryGather.cs`，消费
`AlsRefactoredFootRigFrame.Prepare` 产生的当前请求，返回纯值观察结果。
请求中的帧身份、序号、Rig 世界变换和两条射线完整回传，由脚部所有者拒绝
取消候选的旧响应。桥接不读取上一帧骨骼、不重新计算锁定目标、不提交动画。

对应本地 ALS 的 `Private/Nodes/AlsRigUnit_FootOffsetTrace.cpp:25`：当前目标
XY、Rig 原点上下距离、忽略 OwningActor、Visibility 查询、命中后回到 VM
空间判断坡度。厘米/世界轴只在 Godot 边界转换；法线只换轴、不乘 100，
不提前归一化或按 Godot Up 过滤。逆 Rig 缩放和坡度修正仍由已有原生对照
过的纯计算消费者处理。

Visibility 显式映射为构造参数 collision mask，不复用 Motor 地面掩码。
调用者传入角色所有应忽略碰撞体的 RID（包括道具）；只查物理 Body，忽略
Area。使用场景实际碰撞形状，支持 concave 三角形；不宣称已完成所有 UE
complex collision 资产/背面规则的转换。Godot 射线设置为忽略背面与内部起点。

构造/销毁限定主线程，查询还限定主物理阶段。两条射线先一起验证，再访问
物理世界。禁用脚不查询、不会留下旧命中。Godot Dictionary 查询有托管开销，
本批没有声明零分配或通过最终性能预算。

## 实际验证

新增 `scenes/tests/refactored_foot_query_smoke.tscn`，在 Godot 4.7.2 实际物理
世界连接“Worker Prepare → 主线程 Gather → Worker Evaluate → 验证/提交”。
这是脚部切片集成场景，骨架为 10 骨骼受控夹具，曲线受控；未运行 Demo
完整动画图，也未把 Task.Run 测试调度替换为生产调度器。

30/60/120 Hz 全部退出零，每档 13 项几何/边界检查、10 对独立实例，分别
运行 30/60/120 帧。查询数 1099/2079/4019，单线程与并行候选状态/逐骨骼
姿势完全相同。覆盖平地、30°/70° 坡、三角形、空查询、层过滤、自身/道具
排除、Area 排除、平台在下个物理帧的位置变化、非均匀 Rig 缩放、禁用脚、
工作线程与非物理阶段拒绝，以及晚期取消/同帧重试/旧响应拒绝。

日志：`artifacts/refactored-foot-query-171-{30,60,120}-verified.log`。
Godot 优化 Debug 构建零警告、零错误；`git diff --check` 通过。

保留首错：构建时 typed Godot Array 不支持 IDisposable，改为普通包装对象；
首轮场景的三角形绕序错误导致查询遗漏，修正为 Godot 正面绕序后，新增检查
同时确认命中和向上法线。未放宽射线过滤或坡度判断来通过测试。

## 尚未完成

本批未修改默认 Demo。新曲线生产器仍需安放到状态/Movement 缓存的正确
混合位置，空中预测需明确 V4/Refactored 规则，生产调度器需在当前前脚部
姿态与锁定目标产生后增加查询阶段，并保持脊柱→脚部→手部及完整根事务。
之后进行真实角色移动、接触、平台与多帧截图对照。第 616 帧旧失败未关闭。
P3/P4 整角色验收、P5A 剩余功能、P5B/P5C/P6/P7 继续开放，音频暂缓。
