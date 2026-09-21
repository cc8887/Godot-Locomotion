# 运动平台启动前的原生基线

主目录 main 本批补齐剩余两个 30 Hz 失败场景的原生对照。生产 Core 求解器、睡眠阈值及十二项验收门槛均未修改，矩阵仍记作 9/12。

## 实现

Godot setup 捕获现在支持平台场景，但明确只输出启动前十秒的 `platform-pre-motion` 阶段（300 步），不把完整 24 秒平台流程误交给固定世界 exporter。文件名包含 translate/rotate-settle，原生输入保留 phase/platformMode。

环境捕获新增 `kinematic`：来自实际 AnimatableBody3D。场景包含 TranslatingPlatform 和 RotatingPlatform 两个运动学体，不只是当前测试选中的一个。原生初始化区分 static/kinematic，并在每一帧从 Chaos particle 读取 ObjectState 验证，类型不符即拒绝导出。两者本阶段均不移动，尚未实现原生平台运动目标重放。

旧 schema1 输入没有 kinematic 时仍按旧静态体基线解释；旧普通 30 Hz 冷重导与上批参考字节一致。旧参考中的远处平台曾被静态化，因此不能称旧环境类型全部对齐；本批新平台参考已保留实际类型。

## 原生结果

四组均在第 300 帧仍未入睡（AnimMan 20、Mannequin 18 个动态身体唤醒）：

| 角色 | 场景初态 | 末秒最大线速度 cm/s | 末秒最大角速度 rad/s |
| --- | --- | --- | --- |
| AnimMan | 旋转平台 | 8.53991636518212 | 0.385664442687521 |
| AnimMan | 平移平台 | 8.52314142898641 | 0.391382246775429 |
| Mannequin | 旋转平台 | 8.9710238371824 | 0.509380889307606 |
| Mannequin | 平移平台 | 8.44986524071256 | 0.510334230415909 |

Core 两次真实场景复跑仍于运动前失败：Mannequin 已睡，AnimMan 未睡。原生和 Core 的睡眠结果不相同，不能宣称轨迹等价；但十秒必须全部休眠的要求在原生四组同样不成立。普通 30 Hz 亦有原生失败，见前批低频记录。不能仅凭剩余三个验收失败继续断言存在对应算法遗漏，也不能因此自动关闭验收要求。

## 验证

- 新参考 `assets/config/v4_physics_world_platform_settle_reference.json` 为 24,529,743 字节，1204 个完整世界采样；首次与冷重复导出均退出0、SHA256 同为 `65F016D8492979C7E753859E2C179B95E03F5A67A88B0BA301B39DBF725B701C`。
- 新旧三组世界参考测试默认和 LatestMajor roll-forward 各3项通过。保留原生失败，独立从逐帧数据重算睡眠和末秒速度；验证两个具体 kinematic 名称、完整身体/关节数和接触。
- 首次测试错误假设只有一个 kinematic，实际为两个；检查场景后修正断言，不修改捕获或原生结果。
- Godot 优化构建0警告0错误，60Hz contact smoke通过。没有修改 Core/Import 生产公式，未重复全量或其余十个矩阵场景；全量基线仍 Core2837、Import2427+1既有skip，不能将新定向用例计入已运行全量。
- UE完整Editor目标构建及全插件审计通过，fingerprint `7E2055DF742C07F6D1FC7E907B5057879C55F9ABC0A47449B5A8B1D652B23AB0`。DataValidation退出0（3旧警告），普通Editor PID28676加载成功、原生退出0、无DLL残留。两旧Condition failed仍在，既往间歇0xC0000005未修。
- canonical与UE插件源码相同；用户P4规划哈希保持 `78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100`，不纳入提交。

日志、setup、重复导出及编辑器记录位于 `artifacts/physics-platform-baseline-20260922/`。

## 下一步

以这三类原生失败为依据，补更长时段独立运行，分开评估原生行为对齐和产品稳定性。继续最早分歧定位（原生float dt、初始particle存储、conditioned inertia和岛调度边界仍存在），并适配含自由关节的coupled重放。不得通过增大阈值或强制定时睡眠把结果涂绿。

普通Ragdoll/Get-up/Pose Recovery、Mantle、完整ALS相机和最终十分钟性能验收等总目标继续保留；普通demo本批未切换实验后端。
