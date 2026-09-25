# 胶囊补点零距离：原生确认与连续求解修复

本批在主目录 `${env:GODOT_ALS_ROOT}` / `main` 关闭上一批已知的补点零除问题。有效几何仍沿用原生结果；原生 NaN 补点现在省略，有效最近接触和其他支撑保留，避免单个无定义补点中断整步模拟。

## 原生证据

新增可选 `PhysicsCapsulePairDegenerateOutput=`，复用真实 `UpdateConstraint(CapsuleCapsule)`，原生算法未修改。采样两胶囊中心距离位于 2/5/7 cm 半径处及其 ±1e-5 cm 邻居，再组合原有姿态、大平移、偏心、短长轴、动态/运动学归属与夹角，得到 **9,072 例**。

直接读取原生 manifold，**140 例各有一个非有限补点**。例如半径5、两中心相距5时，原生最近点有效，第二点的 point1 和 normal 为 NaN，Phi仍为-5。导出明确标记 `finite:false` 和各字段有限性，不把非有限数值写成合法几何或用代理结果替代。

Core 旧实现对这些输入抛 `Nonfinite native capsule pair contact`，失败日志保留。修复只在 supplemental distance **恰为0** 时跳过此补点，不扩大 epsilon，不改最近点，不伪造法向。其他非法数值仍按原有原子性规则拒绝。

这是对原生无定义数值的明确稳定性处理，不能称原生NaN也被1:1执行。9,072例中的所有原生有限点，其数量（排除明确标记的NaN）、顺序、两侧位置、法向、Phi均精确一致；旧7,056例原生参考仍全部精确一致。原生正常计算与邻近非零距离分支均保留。

新参考 `assets/config/v4_physics_capsule_pair_degenerate_reference.json` 为14,333,172 bytes，冷重导字节一致：

`SHA256 78E4912CC137E9DFA588442CF357C12C92A0AACB89F0E34B50076A0F9B4DFD56`

旧输出命令重新导出仍与旧资产字节一致：`C6A94433372442906936F3AE268B4088311DBD52B34B7E6A4C374EAD6C7450F5`。没有重写旧参考。

## 验证

- Import Release 固定JIT串行全量 **2418通过、1既有跳过、0失败**，退出0，耗时4分54秒。
- Core Release 固定JIT串行 **2834通过**，沿用两类旧P5A排除。新增精确退化及±1e-5邻居测试；精确退化保留1个有效接触，邻居仍保留2点；零分配与非法输入不发布仍通过。
- Godot优化构建0errors/0warnings。30/60/120 Hz各完成 **120步新增连续求解**：动态—动态与动态—固定各60步，第一步仅有效最近点进入solver，随后状态/速度保持有限，固定身体不动，无Jolt回退。
- 三频率全部旧smoke项仍通过：551精度、9几何、68polygon、10capsule cull、2primitive、3transaction、5manifold、5sleep、30sphere-box、24capsule-box、20capsule-pair。
- 整链仍 **8/12**，八个成功JSON与上一批字节一致，没有新增失败。未完成项仍是普通60 Hz、高速120 Hz、平移30 Hz、旋转30 Hz；本批修复的是独立退化，不能称这四项稳定性问题已经解决。

产物、修复前失败、两套参考对照、连续求解、矩阵和UE日志：`artifacts/physics-capsule-degenerate-20260922/`。

## UE构建与剩余工作

全Editor target构建和所有插件审计通过，fingerprint `5BAC5D545576F840F3345433F34484577E5C57D783D3233459321DB8C1BDFFB8`；源码镜像hash一致。新冷导、重导、旧输出重导均退出0；DataValidation退出0，0errors/3旧warnings。

普通Editor PID33572加载标记成功，原生退出码0xC0000005，两条旧Condition failed保留；退出后exporter DLL无占用。本批普通重启门禁失败，旧间歇退出异常未修复。

用户P4规划修改保持原hash `78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100`。普通角色未接实验后端，继续capsule–convex/sphere混合对及实际pair trace重放、四项整链问题，再推进普通Ragdoll/Get-up/PoseRecovery、Mantle、完整Camera与十分钟预算。
