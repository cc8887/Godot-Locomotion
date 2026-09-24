# 原二维 BlendSpace 姿态与曲线

在主目录 `D:/GodotALS` 的 main 继续连接实际 6 个 WalkRun 和 2 个 Lean。新 `AlsRefactoredBlendPoseSource` 根据已验证的三角形 profile 自行计算有序权重，采样实际原 Sequence，再混合全部原生 79 骨和曲线。WalkRun 使用绝对源，Lean 使用局部加法源；未把旧 V4 姿态或权重作为输入。

## 行为与边界

Catalog 新增带曲线的绝对源编译入口，姿态和曲线由相同 bundle/digest 生成。原仅姿态入口不变。Triangulation profile 保存 catalog digest；姿态源拒绝跨版本目录。编译验证所有样本的 additive 类型一致、骨名和父层级一致，曲线统一为名称并集，保持 present 标志。

求值接受已过滤的二维坐标、normalized evaluator time 和上一提交的 triangle cache；时间按 UE float normalizedTime × float SequencePlayLength 计算。按原顺序做姿态 Overwrite/Accumulate，curve Scale/Accumulate，执行原生多姿态归一化以及 BlendSpace 的最后归一化。每个 worker 独立 sampler，资源可共享；失败不发布姿态、曲线或候选 cache。运行时不读取任何 native reference。

这仍是显式位置的 evaluator：不推进播放器时钟，不执行输入滤波、marker 同步、Notify 或 root motion 提取。后续玩家时钟需提供各样本同步时间，不能把这次 normalized evaluator 直接当完成的 marker player。支持实际本批 absolute/local additive，不接受 mesh additive 混入二维样本；Look 既有一维入口保持。

## 原生与回归证据

- UE 新 ReadRawBlendSpacePose2D 复用原 Look 导出的 raw 骨容器和实际 GetAnimationPose，只扩展第二坐标；旧 Look 方法签名及旧 JSON 字段保持。
- 每个资产 10 个坐标（含顶点、内部、边和轴外）×5 时间，共400姿态/31600骨/830个存在的曲线值。C# 自己计算权重，无 native 权重驱动。
- 最大位置差 `1.2079226507921703e-13 cm`，四元数分量差 `4.1633363009853064e-16`，scale 差0，curve差 `5.9604645e-8`。初始预算分别2e-5cm/1e-6/1e-6/1e-6，首轮即通过，未调整。
- 每样本失败输入不改输出、重试相同、两个独立 owner 并行相同；每个profile验证跨catalog版本拒绝。新1项综合测试，相关Import47项通过；Optimize构建0错误0警告。无全量/ Godot 场景验收。
- UE完整Editor构建5actions成功/插件审计通过，fingerprint `0D39DC55A9E169D9BBF79A257C7FDC69FB8508E7885A1AD3A2EA54B5EDF8D6DB`。
- 冷导出与普通Editor PID40064实际重启/导出/退出均0；400姿态参考字节相同，SHA256 `90A4CF2763CB22157A3BF22459F4C0B6793E56BEBA0C9A82C3A68730D13A62CF`。无UE资产保存。
- 普通Editor两条旧Condition failed、五条旧警告保留，未修复。原生插件变更继续按完整Editor构建、审计、冷导出、普通重启和DataValidation检查；无项目打包流水线，本批无打包完成声明。
- 本批DataValidation实际退出0，三项既有加载/导航警告保留。

日志与TRX：`artifacts/refactored-blend-poses/`。

## 后续

继续实际二维输入滤波和原marker/播放时间绑定，再原Locomotion/Overlay动画图与共享宿主。普通Demo尚未切换；Mantle gameplay、Ragdoll/Get-up/Pose Recovery、完整Camera及十分钟预算等仍是总目标，旧物理失败没有因动画对照通过而关闭。保留用户未提交文件及暂缓的颈部问题、道具物理、音频。
