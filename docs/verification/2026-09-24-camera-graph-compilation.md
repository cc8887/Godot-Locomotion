# Camera AnimBP 图编译与自定义混合曲线

主目录 `D:/GodotALS/main`，承接上一批 Refactored 相机原始数据。本批完成图到可消费定义的编译和富曲线求值；尚未实现相机图的逐帧运行时，也未替换普通 Demo 相机。

## 编译内容

`AlsCameraGraphCompiler` 从原始 authored T3D 读取连接，不手写相机预设表：

- 递归提取顶层、Look States 状态机、三个状态图及共享过渡规则图。检查输入输出的相互连接，沿根节点遍历，缓存对象共享同一编译实例。
- ModifyCurve 读取实际暴露的 `CurveValues_N` 引脚，并保留 Blend/Scale 语义。结构内 `CurveValues=0` 仅为占位。
- tag blends 保留输入绑定、tag顺序、每个子节点的真实BlendTime、Cubic/Linear/Custom、ResetChildOnActivate；bool blend保留右肩绑定和两支顺序。
- 保存三个观察状态、六条有向过渡、原生初始状态和每个状态出口的连接顺序。过渡读取各自持续时间/自定义曲线及条件表达式，包含共享规则。
- 条件图的 VelocityDirection 分支实际为“RotationMode等于VelocityDirection，或者RotationMode无效”。没有将其误读为 AND，也没有用动作状态代替RotationMode。
- 最终 LocomotionAction 覆盖保留 Default/Mantling/Rolling/Ragdolling 四支，暴露混合时间分别0.4/0.2/0.2/1秒。Ragdoll分支有独立ReferencePose来源。
- 当前真实图的 tag 绑定为 Gait/Stance/ViewMode/LocomotionAction，条件读取RotationMode，bool读取bRightShoulder。虽然原生 AnimInstance 更新 LocomotionMode 属性，当前相机图并没有使用它，不能凭上一批文字路线额外发明对应分支。

编译拒绝不支持的 selector/ModifyCurve/child update/状态机策略、外来条件函数、缺失自定义曲线和负混合时间。只支持本机当前图所需节点，不声称通用蓝图解释器。

`AlsCameraBlendCurve` 编译 Smooth/Quick 原生富曲线键，保留关键时间、值和切线。求值后按 UE `FAlphaBlend::AlphaToBlendOption` 截断到[0,1]。Quick 在0.94～0.99的部分采样略高于1，因此混合完成可能早于原始时长；后续运行时不能单看elapsed>=duration判完成。

## 验证

- 相机Import专项11项通过，`artifacts/camera-graph-tests/camera-graph-complete.trx`；包括真实图编译、缓存身份、六过渡、动作/换肩/Gait策略、自定义曲线202原生样本以及非法图变更拒绝。
- 旧630帧/112边界相机旋转原生对照一并回归。
- Optimize build通过，0 warning、0 error。
- 自定义曲线样本按原生Blend权重语义比较，容差2e-6。当前键值来自六位小数T3D、通用Hermite采用double中间量，因此不声明逐位一致；需要原生全图轨迹证明这些误差不会影响边界，必要时再补完整float键导出和原生算术。
- 首轮样本回归误将已截断的Blend权重与原始CurveFloat值比较，在Quick末端失败；`artifacts/camera-graph-tests/camera-graph.trx`保留。查本机 `Engine/Private/AlphaBlend.cpp` 确认原生截断后修正测试，未修改原曲线或放宽容差。
- 开发过程另修正MatchCollection枚举类型和转义tag字面量正则；不涉及生产动画路径。

本批无UE启动/重导、Core/Import全量或Godot运行时新相机验收，使用上一批原生数据。用户P4 SHA256仍为 `78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100`，三份头颈诊断文件保留。

## 下一步

基于编译定义实现Camera图的独立运行时：多子BlendList、每条状态过渡各自的CustomCurve、相关性/重入、共享cache单次更新和曲线缺失语义；以原生AnimBP连续输出核对。随后连接已提交角色socket、平台历史、场景球扫/初始穿透、完整相机视图合成和普通Demo，保留鼠标控制yaw与视觉lag分离。

完整Camera、Mantle、静态物理9/12和Flail0/3旧稳定性目标、十分钟性能验收尚未完成；头颈与道具物理继续暂缓。
