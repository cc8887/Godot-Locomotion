# 原二维 BlendSpace 三角形求值

本批在 `.` / `main` 新增原始二维三角形求值及资源编译。8 个原资产包括 6 个 WalkRun 和 Ground/Air 两个 Lean；Look 的一维实现保持原状。

## 原生差异和实现

实际 Refactored 资产使用三角形，而已有 V4 WalkRun/Lean 使用 grid。不能直接把 V4 的 bilinear/grid 权重用于这些源。新 `AlsTriangulatedBlendSpace` 按本地 UE `BlendSpace.cpp` 的 GetSamples2D / GetSamplesFromBlendInput 和 UnrealMath.cpp 的 GetBaryCentric2D 实现：double 归一化坐标、float 外侧距离和投影比例、1e-4 边界容差、缓存三角形的邻边遍历、凸外缘投影和转向回退、严格大于 1e-5 的初筛、原生小数组排序和 float 总权重归一化。

cache 由调用者提供上次提交值，求值返回候选值，没有内部推进状态；输出只在找到有效采样后发布。此 API 输入应为滤波后的坐标，不负责输入滤波、播放时间、marker、Notify 或姿态求值。仅支持非退化多三角形布局，未声称任意 UE BlendSpace 功能齐全。

导出插件读取实际 FBlendSpaceData 的完整精度顶点、法线、邻居及外缘链接。原始结构独立保存在 inputs，原生查询结果单独保存在 reference；运行时编译不读取 reference。inputs 绑定现有 catalog 字节哈希；compiler 校验全部 8 个原资源闭包、sample 原文和结构绑定、轴范围/默认政策、三角形顶点、法线、邻接、外缘连接。profile 保存源样本路径、loop 与原轴滤波窗口（WalkRun .2/.4 秒 cubic，Lean 无滤波）。这些元数据尚未组成完整播放器。

## 验证结果

- 每个资产 512 次连续坐标查询：轴内外、角点、外缘、三角形边、接近零权重阈值以及反向路径；共 4096 次。C# 用自己的上一帧 cache 推进，不拿 native next cache 驱动。
- 采样数量、顺序、索引、每步 cache 与原生一致；最大权重误差 `5.9604645e-8`，预算 1e-7，未放宽预算。不是逐位权重一致，精确舍入来源未进一步定位。
- 每步从提交 cache 重试结果一致；五项损坏 hash/闭包/法线/顶点/邻居的拒绝测试。新增 6 项，相关 Import 回归 28 项通过。Godot Optimize 构建 0 错误/0 警告；没有全量或 Godot 运行场景测试。
- 首轮 1 项失败：校验器误要求每个外缘端点都链接另一个三角形。实际两三角形矩形在同一三角形角落保留 -1/-1；按 UE 原始布局允许该情形，同时核对相邻边也是外缘。修复后通过，原失败 TRX 保留。
- UE 完整 Editor 目标构建 5 actions 成功，插件审计通过，fingerprint `E1F521401C418FAD6E1009934A55E71F8C18B8594B176BC7156D04255BC36D45`。源码从主仓库复制到 UE 项目 Source，不复制 DLL。
- 冷导出退出 0；普通 Editor PID 27020 真正重启/导出/退出 0。两次 inputs 和 reference 均逐字节一致。inputs SHA256 `F57F65EFED525B3ED02EF7BEAC974C1C352ABCBCB880F4B1C90B52D6FF82179C`；reference `19616B577CEE1E4C9D6E28FC6952FE33CA1FCA1A8289D3CD17FDB98E7C3B165F`。
- 普通 UE Editor 仍有两条旧 Condition failed 和五条旧警告，本批未修复。DataValidation 退出 0，保留三项既有加载/导航警告。没有现成打包流水线，本批未作打包验收声明。

所有日志、初次失败和修复后 TRX 均在 `artifacts/refactored-triangulation/`。

## 后续范围

下一步是把滤波、原样本姿态/曲线混合、归一化播放时间和 Sync Runtime 接到这些原二维资源，验证连续运动，再接实际 Locomotion/Overlay 图和普通宿主。普通 Demo 本批未切换；Ragdoll/Get-up/Pose Recovery、Mantle gameplay、完整相机和十分钟预算等总目标仍未完成，旧物理失败保留。用户原有修改及暂缓的颈部问题、道具物理、音频未动。
