# Look 独立样本权重

实际 BS_Als_Look 使用两段一维 BlendSpaceData，原始段为 (Down,Forward)[0,.5] 与 (Forward,Up)[.5,1]，不是旧 V4 的五点网格。新增 Core `AlsRefactoredLookBlendSpace`，按本地 UE GetNormalizedBlendInput/GetSamples1D/GetSamplesFromBlendInput 实现 double 归一化→float段坐标→段内权重→原生小数组排序（含等权重反转）→低权重裁剪→归一化。

Look 编译器校验实际原生文本中的类型、轴、两段索引/顶点、三个样本顺序和其他非默认属性。ObjectExporter 省略的策略依照本地 BlendSpace1D 原生类默认值解释：bInterpolateUsingGrid=false、无轴/样本权重平滑等。未知非默认属性直接拒绝，不猜测兼容行为。支持范围限定于当前原资产，不是通用 BlendSpace 编译器。

姿态采样新增 Evaluate(pitch,normalizedTime)，自行生成权重再调用上一批 raw/additive 混合；不再以原生参考权重驱动 C# 姿态结果。

## 验证

- 同一组35个原生参考：样本数量、顺序、float权重逐值一致；最终2765个骨骼结果通过既有容差，最大位置约6.43424e-14cm、旋转分量4.44089e-16、scale差0。
- 一项Core边界测试覆盖±90、相邻float、零附近低权重裁剪、重新归一化、非法输入。该细边界测试没有新增UE采样对照，原生姿态参考仍是7个pitch×5个time。
- 新增四个导入拒绝用例：开启网格、修改段顶点、修改轴范围、原生样本引用与绑定不一致。
- Import Look/Head/BasePose/Mantling定向123通过；追加原类型校验后Look11项复跑通过（重叠）。Core定向1项通过；Optimize构建0 warning、0 error。
- 证据在 `artifacts/refactored-look-weights/`。没有新UE导出、Godot场景或全量测试。

下一步Head实际图回调/相关性/求值整合及连续View/Spine/Head状态原生对照。普通Demo尚未切换新Head；完整宿主/Mantle/物理稳定性/Flail/最终预算等旧缺口及用户暂缓项保持。
