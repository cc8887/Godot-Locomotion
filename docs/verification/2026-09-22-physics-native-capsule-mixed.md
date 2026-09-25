# 实际混合胶囊重放与 .NET 运行时叉积差异

本批在 `${env:GODOT_ALS_ROOT}` / `main` 完成实际 capsule-pair/capsule-convex 重放，并修复由 .NET 8/9 运算实现差异造成的胶囊接触偏差。没有切换普通角色到实验物理后端，整链仍未整体验收。

## 实际几何和身体输入

新增 `PhysicsNativeCapsuleMixedInput=` / `PhysicsNativeCapsuleMixedOutput=`，从UE真实身体复制匹配的native leaf，而不是按端点重新构造胶囊。trace增加动态身体归属；capsule pair保留两端原次序，动态与固定归属映射到原生Dynamic/Kinematic供半径规则使用。

凸包trace增加实际scale/margin及诊断FNV-1a指纹。指纹覆盖原生float顶点、平面法向/位置、面顶点顺序和原生vertex-plane缓存；不是安全用途的加密摘要。UE对原生内层cooked数据计算同样的字序，精确核对指纹、外层scale和margin后复制实际wrapper。指纹只在opt-in trace首次需要时计算并缓存；普通查询不计算。

高速120Hz旧失败场景1140–1145帧共726查询，本批选择246次capsule pair和114次capsule-convex，另外366次明确跳过。capsule pair有30次活跃、36个点；本次窗口中的114次capsule-convex全部无接触，不能将其作为实际帧正接触证据（此前独立4320参考覆盖有接触分支）。

## 查出的真实差异

第一轮：同一份输入在默认 .NET 8 测试中Core与UE精确一致，却与Godot原始捕获存在最大点差 `6.05038530920865e-6 cm`、法向差 `2.747638347955217e-7`、Phi差 `1.9073486328125e-6 cm`。逐项检查源姿态与UE重导姿态，没有传输差异。

Godot随附runtimeconfig选择 `LatestMajor`，本机实际运行 `.NET 9.0.17`，而常规测试默认 `.NET 8.0.28`。设置 `DOTNET_ROLL_FORWARD=LatestMajor` 后，测试原样重现Godot的差异。胶囊算法内 `Vector3.Cross` 改为明确的float标量乘减顺序后，.NET 9 Core对UE差异降为0；未修改误差门槛、几何或原生输出。

修复前参考的source输入和Godot输出保持原样，测试明确保留旧captured偏差，同时要求当前Core对原生点/顺序/法向/Phi精确相等。没有刷新旧输出制造通过。Godot smoke额外在真实宿主内重放这246组输入，三个频率全部精确通过，报告记录实际runtime，证明修复已进入运行中的代码。

参考资产 `assets/config/v4_physics_native_capsule_mixed_reference.json` 为798,226 bytes，两次原生冷导字节一致，SHA256：

`C9301B7ED43BEFD02D8BA8B4D37BE952678A6106BA74885237FB7AF1A1B039EA`

## 验证与整链

- Core固定JIT Release串行2834通过，沿用两类旧P5A排除。
- Import固定JIT Release串行全量2422通过、1既有跳过、0失败，退出0，测试耗时4分59秒。
- .NET 9定向4项通过：实际混合帧、7056胶囊pair、9072退化、4320capsule-convex参考。扩大到名称含ReferenceTests的41项也全部通过；这些参考覆盖范围以各测试本身为准，不等于所有阶段逐位一致。
- Godot优化构建0errors/0warnings；30/60/120Hz各新增246组真实宿主原生回放及全部旧smoke通过，宿主报告 `.NET 9.0.17`。
- 十二项矩阵仍 **8/12**，无新增失败、无旧失败关闭。八个通过报告均有数值变化，不能称字节相同。普通60Hz Mannequin第595帧才睡，无法满足末秒保持；高速120Hz Mannequin第869帧睡、AnimMan仍未睡，末秒速度1.902490139cm/s、角速度0.649710953rad/s；平移30和旋转30仍失败。
- 篡改凸包指纹、缺少动态归属两项commandlet负例均退出48、无输出文件。实际输入匹配和缺省拒绝得到验证。
- 完整Editor构建/插件审计通过，fingerprint `66C1B4B900E26460EF33F8385CA4AD12A8CA10438C9D224DFB6CBDC6E2514D05`。主仓与UE镜像一致；DataValidation退出0、0errors/3旧warnings。
- 普通Editor PID28076加载标记成功后原生退出0xC0000005，wrapper退出1，DLL无占用；两旧Condition failed仍在。普通重启门禁失败，既往间歇退出异常未修复。

产物目录 `artifacts/physics-native-capsule-mixed-20260922/`。其中 `high-120.log` 现在是修复后矩阵日志：矩阵执行复用了文件名并覆盖本批最初的临时trace日志。受检的全部360条修复前源记录及输出完整保存在参考JSON的source字段，冷重导文件仍在repeat.json；后续trace与矩阵必须使用不同日志名，避免覆盖。

## 剩余工作

其他浮点叉积仍存在于Gather、接触质量和线性关节等代码中；本批只修复已通过跨运行时实验定位的胶囊分支，不把其他代码称为已逐位对齐。下一步用实际island阶段输入核对这些路径和持续历史，再处理四项整链问题。普通Ragdoll/Get-up/PoseRecovery、Mantle、完整Camera和十分钟预算仍未完成。
