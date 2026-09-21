# 完整世界最早速度分歧：AnimMan 第二步

本批在主目录main对比已有同初态普通30Hz原生世界与真实Godot逐阶段捕获，没有修改生产求解器。新增 `tools/physics/Compare-WorldCapture.ps1`，将捕获frame N的corrected阶段与原生samples[N+1]的身体速度按名称/索引对齐，拒绝重复帧、缺失阶段或越界。报告记录两份源文件SHA256，输出必须为新绝对路径。调用者须保证初态匹配；该工具不宣称轨迹等价，也不以速度相似代替位置验证。

## 证据

输入为 `v4_physics_world_low_frequency_reference.json` 与 `v4_physics_free_coupled_reference.json`，包含两模型各11个采样步。

| 角色 | 完成步 | 最大线速度差 cm/s | 最大角速度差 rad/s | 最大线速度差身体 |
| --- | --- | --- | --- | --- |
| AnimMan | 1 | 0.0001530494413021432 | 0.000007061247456747652 | lowerarm_l |
| AnimMan | 2 | 105.35580438351164 | 4.7653663963000685 | foot_r |
| AnimMan | 3 | 95.61778715673053 | 1.741448629661571 | foot_r |
| Mannequin | 1 | 0.000023816911294222928 | 0.0000016264860265581055 | lowerarm_l |
| Mannequin | 2 | 0.000036814386311483965 | 0.0000022215127685258893 | calf_l |
| Mannequin | 17 | 0.000524533557068091 | 0.000021799296806384675 | calf_r |

因此AnimMan不是只在末段休眠时缓慢漂移：从第二步就有大幅差异。Mannequin在采样的前17步仍接近原生，不能把这一现象概括成所有身体的通用积分错误。

第二步Core接触输入包含 `root → foot_r` 一点接触。实际root为半径7.505000114440918cm的球体，foot_r为凸包。检查 `AlsGodotContactQuery.Query` 确认：已有原生sphere-box与capsule-convex分支，但sphere-convex仍落入Godot/Jolt查询回退。此前coupled对照使用Gather后的这些接触行作为输入，故不会发现这类几何差异。

UE本地源 `Chaos/Collision/SphereConvexContactPoint.cpp` 和 `CollisionOneShotManifoldsMiscShapes.cpp` 明确使用sphere core point对convex的GJKDistance，深接触回退到最深平面，忽略凸包support margin；大球还有面顶点投影补点。现有Core capsule-convex使用GJKPenetration，不能未经验证把半径球伪装成零长胶囊替代。下一步应完整移植Sphere–Convex并用第二步真实输入和几何扫值验证，再接运行时与矩阵。

这仍是优先级很高的缺失路径证据，尚未独立证明它是105cm/s差异的唯一原因。禁止在尚无原生查询对照前删除root碰撞、调整脚部尺寸、强制睡眠或改阈值来掩盖差异。

## 验证与后续

比较工具执行两次，均输出22条观察，JSON逐字节一致；结果在 `artifacts/physics-first-divergence-20260922/differences.json`。本批未改Core/Godot/UE及测试程序集，未重跑全量、矩阵或UE门禁。最新矩阵仍9/12，上一批五组coupled通过不等于完整世界通过；旧编辑器异常保留。

用户P4规划未改变，SHA256 `78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100`。普通demo未接实验后端，Ragdoll/Get-up/Pose Recovery、Mantle、完整相机及最终十分钟预算等目标仍未完成。
