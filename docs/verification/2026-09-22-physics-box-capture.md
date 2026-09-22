# 真实世界姿态下的 polygon 反向变换修复

## 修复

主目录 `D:/GodotALS`、`main`。原生 `CollisionOneShotManifolds.cpp` 在选择 shape1 为参考面时，调用 `Convex1Transform.GetRelativeTransformNoScale(Convex2Transform)`，从两个世界姿态直接生成反向相对变换。
此前 Core 只接收 shape1To0，然后求其逆来构造 shape0To1。经过 float 存储的粒子 quaternion 不是数学上严格单位四元数；对已经组合的变换再取逆，与从世界姿态独立求相对变换不等价，世界位置较大时还会放大平移舍入。

现在 Godot polygon 查询计算两个方向，经过事务式 `AlsPolygonQueryCache` 传给 `AlsPolygonManifold`，参考面在shape1时使用直接计算的反向变换。仅有相对姿态的旧调用仍按shape0为identity的语义工作；未统一归一化四元数或修改接触/睡眠阈值。

## 复现与独立原生参考

新增 `Export-BoxCaptureInputs.ps1`：从原始raw Gather捕获读取世界姿态，按模型/hand_r以及setup中第一个环境body配对；用已验证的runtime shape盒体bounds和margin、场景floor尺寸准备输入，记录源哈希。Godot环境名是全局body下标，例如environment_19，而原生world对外名是environment_0，脚本显式映射，不按文本混用。

新增原生 `PhysicsBoxCaptureInput/Output`，调用实际 BoxBox `UpdateConstraint`，不复制几何公式：每姿态先冷缓存，再保留本次生成的GJK缓存重查相同姿态，不调用流形恢复。cull固定6 cm，以隔离几何计算；不是运行时实际cull/历史缓存的完整重放。

输入来自两模型完成77..79附近和141..160窗口的45个实际有右手地面接触的姿态，共90次查询/360点。修复前新测试失败：最大点差 `7.197556e-5 cm`、Phi差 `8.583069e-6 cm`、法向差0；日志 `first-test.log`。修复后360点的位置、法向、Phi全部逐值一致，未放宽断言。

新资产 `assets/config/v4_physics_box_capture_reference.json` 为321693字节，SHA256 `CEF5150770906353E2587907AFC0E69BDC5C910D7F9243AAE5D27ECD019AF4C4`。独立冷重导退出0、字节一致。新测试同时锁定runtimeShapes文件哈希，旧polygon四套参考仍按原精度通过。

## 完整运行结果

十二项完整场景重新运行：**8/12**，没有新增失败，也未关闭四个旧失败。

| 频率 | 普通落地 | 高速落地 | 平移平台 | 旋转平台 |
| --- | --- | --- | --- | --- |
| 30 Hz | 失败 | 通过 | 失败 | 失败 |
| 60 Hz | 通过 | 通过 | 通过 | 通过 |
| 120 Hz | 失败 | 通过 | 通过 | 通过 |

普通120 AnimMan从687步睡眠提前到658步，Mannequin仍1200步未睡；末秒最大线速度2.242934226989746 cm/s、角速度0.27109295129776 rad/s，未优于旧2.22707/.269835。不能把局部计算对齐当整链稳定性完成。

新捕获18帧与既有独立native-normal120世界比较（单位cm/s，整帧各身体最大线速度差）：

| 模型/完成步 | 修复前 | 修复后 |
| --- | ---: | ---: |
| Mannequin147 | 0.11575455 | 0.04826135 |
| Mannequin150 | 1.53549594 | 1.49292400 |
| Mannequin160 | 1.6324 | 0.09951573 |
| AnimMan159 | 0.06778954 | 0.14303362 |
| AnimMan160 | 0.29288135 | 0.24826801 |

改善并非每帧单调，特别是AnimMan159退化；新最大差身体也可能变化。继续保留M150及A159附近问题，不宣称整段轨迹等价。

## 验证

- 新盒体捕获+旧四套polygon参考，在.NET8/.NET9均通过（5项）；.NET9新参考maxPoint/maxNormal/maxPhi均0。
- Core Release固定JIT串行按既有命令运行2860项通过，退出0；命令保留对AlsP5aGoldenTests/AlsP5aTraceSchemaTests的既有排除。
- Godot Optimize构建0警告/0错误；30/60/120 Hz接触smoke全部通过（各76原生polygon检查，其他精度/几何/历史/睡眠检查保留）。
- UE完整Editor目标构建与插件审计通过，fingerprint `38D5FF8813E423C3E9DE2942C11F51C1DE05F6A98B1395406B098EDCEC461E5D`。新exporter cpp主仓库/UE镜像SHA256均 `66F8BF08CA015371F18F3400D14EEF52A7A868EF74F0162CD25F7D82C8EBEA91`。
- DataValidation退出0，0 error / 3既有warning。普通Editor PID11668加载标记成功、原生退出0，进程已结束；两条旧Condition failed保留，上批退出访问冲突不因这次成功而视为修复。
- Import Release固定JIT串行全量2448通过/1既有跳过，退出0，5分10秒；日志 `import-full.log`。

日志和捕获均在 `artifacts/physics-box-capture-20260922/`，新输入/原生重导/修复前失败/修复后定向/矩阵/`after-differences.json` 均保留。输入准备期间发现并修正PowerShell自动变量 `$input` 和环境全局下标命名；未修改原始捕获。用户P4规划文件保持原哈希。

## 后续

本批完成了一处有独立同输入证据的生产移植修复。下一步捕获真实跨帧GJK缓存及流形恢复决策，对比M147..150和A159之前的接触重建/恢复；再处理剩余30/120稳定性，继续普通Ragdoll/Get-up/Pose Recovery、Mantle、完整相机和最终十分钟预算。普通demo仍未接实验Core刚体后端，完整目标未完成。
