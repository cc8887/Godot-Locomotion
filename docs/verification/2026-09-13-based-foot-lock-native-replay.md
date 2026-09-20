# 基座脚锁实际输入的原生回放（第一百六十一批）

## 结论与范围

本批补齐生产输入捕获、实际编译的 ALS-Refactored C++ 函数回放，以及逐帧
比较工具。1440 个案例数值配对通过，但第 616 帧的视觉失败仍保留。该结果
证明脚锁函数在这些输入下相符，不证明 V4 动画图与 Refactored 输入契约相符，
也不证明完整角色已经 1:1 移植。

默认入口仍为 BaseLayer。`--als-cycle --foot-ik-frame --based-foot-lock`
保留显式测试状态，新增 `--based-foot-lock-trace` 仅开启诊断分配。音频继续暂缓。

## 实现

- `AlsBasedFootLockFrame` 捕获左右脚的上一状态、实际输入与候选结果，跟随
  全局 Commit/Cancel 发布；诊断数据经正式 Worker/Character 到渲染夹具。
- `AlsBasedFootLockProbe.cpp` 使用参考项目已有 private-member accessor，
  调用实际编译的 ProcessFootLockTeleport、ProcessFootLockBaseChange 和
  RefreshFootLock。瞬态组件/实例提供真正的 AnimInstanceProxy 组件变换，
  不复制函数算法，不保存 UE 资产。
- 插件描述及 Build.cs 分别声明 ALS 插件和 ALS/GameplayTags 模块依赖。
- `compare_based_foot_lock_trace.mjs` 比较四组姿势与锁定量，并输出停止区间
  的约束隔离结果及捕获几何关系。几何解释是诊断计算；数值通过依据仍是 C++。

原生每例重置为捕获的 Godot 上一状态，属于独立逐帧函数配对，不是原生
历史连续累积，更不是 UE 物理场景或完整 AnimBP 回放。

## 证据

渲染 `artifacts/movement-visual-161-input/`：720 帧，24 张截图，进程 exit 1，
仍为第 616 帧左脚 41.100044°，未改变 30° 门槛。

原生输出 `artifacts/based-foot-native-161.json` 与普通 Editor 重启输出
`artifacts/based-foot-editor-161.json` 均 1440 例、66 个隔离变体，进程 exit 0，
两文件 SHA-256 相同：
`203A842085F91BA6DBC065C5D49DD3394C4DBB553B532B3407CBAF09B8CC10AF`。

`artifacts/based-foot-comparison-161.json`：

| 项 | 最大差异 | 门槛 |
| --- | --- | --- |
| 位置 | 0.0000305176 cm | 0.001 cm |
| 旋转 | 0.0000456589° | 0.02° |
| 锁定量 | 2.88788e-8 | 1e-6 |

第 616 帧左脚原生单帧旋转为 41.100008°。隔离大腿限制时为 67.961114°；
仅保留脚掌限制时为 0°；两项均不限制时为 0°。这些变体仅用于诊断，
生产没有关闭限制或提高角度阈值。

该帧上一 Amount=0、输入 LockAmount=1。骨盆旋转后的大腿参考轴约为
(0.966769, 0.051615, -0.250387)。上一 Final 的组件位置约为
(-1.752296, -0.820647, 14.050055) cm，与大腿轴 XY 夹角 -157.961129°；
当前 Target 约为 (1.304528, 0.034392, 13.668929) cm，夹角 -1.545915°。
两位置相距 3.196955 cm，却位于参考方向的不同侧。Refactored 按原规则捕获
上一 Final，随后立即执行 90° 大腿约束，因此产生大角度旋转。

导入编译器明确将原生参考姿势转换为 FBX 骨骼空间，`ReferencePose` 与
`PreciseReferencePose` 使用同一空间；不是前者为 Godot、后者为 FBX。
当前轴转换与导出的 Skeleton 参考平移一致。因此此前“参考姿势被当成
另一种坐标空间”的猜测不能作为已确认原因。原生初始化使用 Mesh 的参考
骨架，仍须独立核对 Mesh/Skeleton 参考一致性及运行时目标/骨盆采样来源。

## 检查与限制

52 项 Foot IK Import 专项通过，含新增的诊断提交/取消/重试身份测试；
记录 `artifacts/tests/foot-ik-frame-161.trx`。诊断脚本语法及普通 git diff
空白检查通过。此前本批优化 Debug 构建及实际渲染捕获已完成。

UE 完整 Editor-target 构建、依赖审计、冷启动原生回放、普通 Editor 重启、
DataValidation、含 ALS 依赖的隔离 BuildPlugin 和打包后审计均 exit 0。
完整构建日志前缀为项目 Saved/Logs/PluginBuild 下
`20260913T083804766Z-e453f91463ff436e8b8ee967a7915111`，
BuildId 为 `37729e27-e1dd-445a-8356-98dafb52a4b5`。

普通 Editor 日志保留启动阶段两条 Condition failed，以及旧 AI
PawnActionsComponent、旧 NavMesh、材质和渲染变量警告；原生输出一致且进程
正常退出，不把它描述为全日志零错误。DataValidation 也保留旧 AI/NavMesh
资产兼容警告。日志均在 `artifacts/unreal/based-foot-*-161.log`。

首次打包的 PowerShell 参数被批处理按字面量解析，输出落到
`D:/UnrealEngine/$packagePath`；编译成功后检查插件描述和两个绝对路径，
将整份生成包移到预定的
`artifacts/unreal/AlsBasedFootPluginValidation-20260913-161`，没有部署 DLL。
日志保留原实际路径，没有删除源文件或用户资产。

## 下一步

1. 核对 Mesh 初始化参考轴，以及原生 RefreshFeetOnGameThread 的目标骨骼、
   骨盆和 Final 历史与 V4 输出链的对应关系，补真实输入来源配对。
2. 回放相同停止/换向输入，追踪 V4 满锁曲线、源动画相位和 Final 捕获时序，
   明确跨版本适配规则。不能把当前 Target 直接替换旧 Final 或任意延迟满锁
   当作未经对照的修复。
3. 同一 720 帧渲染与平台/换基座/传送验证通过后，继续默认完整根、P3/P4
   人工与接触相位验收，再按原计划推进 P5A 剩余分发、P5B Overlay/道具、
   P5C Mantle/Roll/Root Motion、P6 物理恢复/完整 Camera、P7 十分钟预算。
