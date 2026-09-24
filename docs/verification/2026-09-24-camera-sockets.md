# Camera 真实插槽与 Godot 绑定

本批在 `.` 完成真实相机插槽数据与 Godot 读取适配。普通 Demo 尚未使用新相机，球扫和初始穿透恢复仍待实现。

## 原始数据

现有导入资产没有 Refactored 相机的三个命名 socket。新只读 Python 脚本从 `B_Als_Character` 默认对象实际 mesh component 取出 `SKM_Als`，再读取 `SK_Als` 和 mesh 持有的 socket，按原生 mesh socket 优先于 skeleton socket 的规则选取有效记录。

真实数据均绑定 `head`，不是 clavicle。局部 UE 厘米位置分别为：

| Socket | 位置 |
|---|---|
| FirstPersonCamera | (4, 14, 0) |
| ThirdPersonTraceShoulderLeft | (5, 0, -20) |
| ThirdPersonTraceShoulderRight | (5, 0, 20) |

三个 socket 的局部旋转均为单位旋转，scale 均为 1。`refactored_camera_sockets.json` 保留原始 UE 局部 TRS、父骨和原始对象路径。编译器要求实际源角色/mesh/skeleton，拒绝外来 owner、重复/缺失 socket、非法四元数和缩放，不凭名称虚构变换。

## Godot 适配

`AlsCameraSocketBinding` 绑定一个明确的 Skeleton3D 实例，以大小写无关骨骼名匹配 root/head 及 socket 父骨。root/head 直接读取骨骼；三个命名 socket 使用原始局部变换。

FBX **骨骼局部**轴为 `(X,-Y,Z)`，与 Godot **世界**轴 `(Y,Z,-X)` 不同。适配复用已经验证的 native→FBX 局部变换，乘最终显示骨骼世界变换后，再将世界位置返回 UE 厘米。没有直接套用 skeleton metadata 的 canonical 世界坐标转换来计算 FBX 局部 socket，也没有重新采样动画。

读取限定 Main，验证实例存活、位于场景树、骨骼数量与名称绑定未变。宿主仍须保证 visual worker 空闲，并在已提交动画/物理显示之后调用；绑定器不自行等待或强制同步 worker。当前每次 Sample 仍会检查骨骼名称，未宣称无分配或最终性能达标。

## 验证

- 全 Editor 目标构建/项目插件审计通过（ue-diagnosing-plugin-build-load 技能），0 编译动作。日志前缀 `../AdvancedLocomotionSystemV/Saved/Logs/PluginBuild/20260924T101957178Z-451526045eaf4d398a038b27474f737a`，fingerprint 保持 `2AABEBF5350E74B6E46D1E5F32785955F3F7DB1710B7EFCA7EB305D42B1CFAB5`。
- 两次冷导出退出 0，日志 `artifacts/camera-sockets-export-1.log`/`-2.log`；三记录完整、未保存资产。JSON SHA256 均为 `00386B4F5C6F6028EF82E088AC67410802A1A11630D7BAFEFE834DF8AF15B899`。没有 UE 插件源码更改、新普通 Editor 或 DataValidation 验收。
- Import 相机专项 33 项通过，`artifacts/camera-socket-tests/sockets-import.trx`，包括六项新增 socket 验证及原有图/跟随事务与独立原生曲线对照。
- Optimize 构建通过，0 警告/错误。初次 smoke 脚本编译出现 FileAccess 命名冲突，显式指定 Godot.FileAccess 后解决。
- Godot 实际加载当前 Mannequin、AnimMan 模型，各四组整体平移/旋转及 head 姿态变化。40 个位置与已验证的 native-world 骨骼桥接加原始 socket 偏移比较，最大误差 `3.092554787886328E-05` cm，小于预设 `0.0002` cm；四项工作线程读取/骨架名称变化拒绝通过。日志 `artifacts/camera-sockets-godot.log`，场景 `scenes/tests/camera_socket_smoke.tscn`，进程退出 0。
- 该 headless 场景验证实际模型坐标绑定，未做截图观感验收，也不表示 V4 两模型的所有骨骼姿态与 Refactored SKM_Als 完全相同。没有 Core/Import 全量或旧物理矩阵重跑。

## 继续工作

继续实际相机球扫、初始穿透恢复、碰撞通道与自身排除，再接普通 Demo 的已完成动画/物理阶段、真实基座/胶囊和控制输入。保持 control yaw 独立于显示镜头阻尼，完成墙边、平台、Ragdoll/起身、第一人称多帧验证。

完整 Camera、Mantle、静态 9/12 与 Flail 0/3 剩余稳定性、十分钟性能/视觉验收仍未完成。头颈和道具物理按用户要求暂缓，四个用户文件未改动或提交。
