# Refactored 原始加法资源

在 `D:/GodotALS` 主目录实现原资源目录的 `CompileAdditivePose`：15 个 Local Space、27 个 Mesh Rotation Space，共 42 个原动画。保留原始键、retarget、虚拟骨布局、厘米单位及完整曲线，基础动画按原始绝对姿态采样，不递归计算它自己的加法结果。

## 原生规则及实现范围

本批实际资源全部使用 ABPT_AnimFrame。按本地 UE `AnimSequence.cpp` 的 GetSequencePose，基础时间为基础 Sequence 的 float PlayLength 乘以 Clamp(baseFrame / sampledKeyCount, 0, 1)，不是除以 sampledKeyCount - 1。本批包含非零基础帧。Local 使用局部差值；Mesh 先生成组件空间旋转再作差；曲线按名称并集作差并保留存在性。

新增 catalog 专用 raw target 入口，旧 Mantle 和 Look 的严格政策不变。目标启用 root motion、强制 root lock、其他基础姿态政策、transform curve 或骨属性仍拒绝；没有宣称支持任意 UE 加法资源。每个 worker 独占 sampler，资源可共享；采样没有播放时钟，结果在姿态和曲线全部成功后复制到调用方。共享播放资源 ID、BlendSpace 权重和播放生命周期仍需上层显式绑定。

## 验证

- UE 全 Editor 目标构建/插件审计成功，0 actions，fingerprint `05A9CE0F51DDC7551C7BD4BB9F38FB66FEADD9F8B6FA126D064F1ABA9512FE88`。
- 新脚本对每个原动画通过原生 GetAnimationPose 导出 0、0.137、0.5、0.773、1 倍时长的姿态和曲线，未保存任何 UE 资产。
- 冷启动导出与普通 Editor PID 36376 重启导出均退出 0，210 组结果字节完全相同，SHA256 `886DF2B38A4E5E44BDDAB532D9E2D414BEE9C9616F56AFB84A7B736AF035BE6B`。
- 210 姿态 × 79 骨、200 个曲线值对照：最大位置误差 `1.47394807675802e-13 cm`，四元数分量误差 `6.106260516756251e-16`，scale 误差 0，curve 误差 `1.1920929e-7`。预算分别为 1e-6 cm、1e-8、1e-8、1e-6；未调整预算。
- 每个资源验证失败请求不改输出、重试/重复求值一致、倒序采样以及两个独立 owner 并行结果一致。另有四项重新计算文件哈希后的非法政策拒绝测试。
- 新增 5 项；相关 Import 回归 138 项通过；Godot Optimize 构建 0 错误/0 警告。未运行全量测试或 Godot 场景验收。
- 普通 UE Editor 仍记录两项既有 Condition failed 和五项既有警告（AI、导航、材质、console、crowd）。本批没有修改或解决它们；C++ 未变，未新增 DataValidation 执行。

日志与 TRX：`artifacts/refactored-additive/`。参考文件绑定原目录哈希；导出参考只用于测试，不驱动运行时结果。

## 后续

普通 Demo 尚未切换。本批完成的是全部原加法序列的底层采样；后续继续原 Locomotion/Overlay 的 BlendSpace 与图、共享资源身份和完整宿主接入，再完成 Mantle gameplay、Ragdoll/Get-up/Pose Recovery、相机及十分钟性能验收。已有物理失败不因本批独立动画验证通过而关闭。用户暂缓的颈部拉伸、道具物理、音频保持原范围。
