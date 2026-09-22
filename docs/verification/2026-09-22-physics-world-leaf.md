# 完整世界实际形状与右手首次接触

本批在主目录 `.`、`main` 完成只读原生观测和回归。未改 Core 公式、几何资产、睡眠门槛或普通 demo 后端；完整目标未完成。

## 新证据

`AlsPhysicsWorldReference.cpp` 的既有接触诊断窗口现在同时记录求解线程每个 shape 的实际 leaf 类型、margin、局部 bounds 和 leaf-local 变换。直接读取 `FShapeInstance`；不从 authored FKShapeElem 推测，也不使用 Godot 代理尺寸。未开启接触诊断窗口时不增加这些字段。

新参考 `assets/config/v4_physics_world_leaf_window.json` 包含两模型普通120 Hz完成70..80步：第70步作为基线，71..80为完整接触观测。测试按模型、身体、native shape index 对照现有 RuntimeShapeCompiler 产物：43个形状×10帧=430次，类型、margin、bounds、局部位置/旋转/缩放全部逐值一致。故当前窗口不能把接触点差异归因于“完整 UE 世界和资产导出使用了不同形状”。

两模型原生世界全程48040个身体样本的位置、旋转、线/角速度及每帧 awake 数与上批未加字段的 `native-normal120.json` 一致，证明本次观测没有改变该场景的轨迹。原生仍 AnimMan 第635步睡眠、Mannequin第1004步睡眠。

## 右手窗口

新 Godot 捕获覆盖 frame61..78（完成62..79），位置 `artifacts/physics-world-leaf-20260922/captures/`。结合已有更早捕获，Core 的 Mannequin 右手与地面首次接触是完成第77步。原生窗口71..76无该对，第77步出现；77..79双方均有4点，原生这三帧每帧重建（restored=false）。原生更早的逐对历史未在本批连续记录，因此这里不证明它在整段历史中的首次接触也恰为77步。

| 完成步 | 手局部点最大差 cm | 地板局部点最大差 cm | 法向最大差 |
| ---: | ---: | ---: | ---: |
| 77 | 4.76837158203125e-7 | 8.631674575031098e-5 | 6.206335383118183e-17 |
| 78 | 0 | 0.000152587890625 | 2.220446049250313e-16 |
| 79 | 4.76837158203125e-7 | 0.00012969970703125 | 1.0007415106216802e-16 |

点按原顺序、先恢复实际 float 存储后比较；这里是两条自由运行轨迹，进入窄相前姿态不保证相同。不能据此断言某一几何公式错误，也不能用“全部是舍入”解释十秒休眠差距。上一批147..150的差距仍需追踪；现在可从首次接触77步开始做同姿态/同GJK缓存的生成对照，而非重复查资产尺寸。

## 验证与产物

- UE 完整 Editor 目标构建、插件审计通过，fingerprint `907095CB43EAFEED85BBDADBE5D62ABBB1BAE98BC6BE534FD1FC0CDC0F54D127`。
- 主仓库与 UE 项目镜像源文件 SHA256 均为 `B685A6FDC3B352DDD7BC1697930DE34E1CDA78007F1B492886B0415F5A07C869`。
- 使用上一批 `setup120`；`PhysicsWorldContactStart=71`、`PhysicsWorldContactFrames=10`。完整导出和独立冷重导均退出0，SHA256均 `D1969040AE8F2338D3F487D8FD22CF6332BC6A6202785D171B4D26E40F53FBF4`。
- 用既有 `Export-WorldContactWindow.ps1 -StartFrame 70 -Frames 10` 提取上述资产，1035291字节，SHA256 `1ADA7BFC5C58D0939A56ACE1C00046A078A8FA57CBBCB09AB71F0214F4AE5E07`；保留 sourceSha256。
- RuntimeShape、PrimitiveGeometry、旧落地设置窗口和新实际leaf测试共18项，在.NET8.0.28/.NET9.0.17均通过。日志 `targeted8.log`、`targeted9.log`。未改生产C#，未新跑全量Core/Import或Godot构建。
- DataValidation退出0，0 error / 3既有warning。
- 普通Editor PID18932成功加载导出器并输出 `ALS_WORLD_LEAF_EDITOR_RESTART_OK`，但原生退出码3221225477（0xC0000005），进程已消失。两条旧Condition failed仍存在；本批重启门禁失败，不称全部验证通过，不通过反复重启掩盖失败。
- Godot首次命令误用 `--capture-dir`，启动时被参数校验拒绝；日志 `core.log`。改为既有 `--capture-directory` 后完成捕获，最终仍在1200步因Mannequin未休眠而退出1（`core-capture.log`），不是成功验收。

所有新过程产物位于 `artifacts/physics-world-leaf-20260922/`。用户P4规划文件哈希仍为 `78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100`。

## 剩余工作

最新完整矩阵仍8/12，本批未重跑十二项，不声称修复稳定性。下一步在第77步真实输入上让UE独立生成接触，并保留GJK缓存和流形恢复边界；与已验证的历史/Gather/共同求解连接起来。之后继续30/120 Hz差距及普通Ragdoll、Get-up、Pose Recovery、Mantle、完整相机和最终十分钟预算。
